using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShiaiManager.Api.Contracts;
using ShiaiManager.Api.Models;

namespace ShiaiManager.Api.Tests;

/// <summary>
/// Exercises the Freikampf API end to end: create, validation, result by side and the start guard (ADR-0013).
/// </summary>
[Trait("Category", "UnitTest")]
public sealed class FreeFightsApiTests : IClassFixture<TournamentFlowSmokeTests.ApiFactory>
{
    private readonly TournamentFlowSmokeTests.ApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Initializes the test class.</summary>
    public FreeFightsApiTests(TournamentFlowSmokeTests.ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Freikampf_CanBeCreatedRunAndWonBySide()
    {
        using var client = _factory.CreateClient();
        var (tournamentId, tatamiId) = await SeedTournamentWithTatamiAsync(client);

        var created = await client.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/tatamis/{tatamiId}/free-fights",
            new { white = new { name = "Gast 1" }, blue = new { name = "Gast 2" }, matchDurationSeconds = 120 });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var fight = (await created.Content.ReadFromJsonAsync<Fight>(JsonOptions))!;
        Assert.Null(fight.CategoryId);
        Assert.Equal(new FreeFightDetails("Gast 1", "Gast 2", 120, false, 180, null), fight.FreeFight);
        var location = await client.GetFromJsonAsync<Fight>(created.Headers.Location, JsonOptions);
        Assert.Equal(fight.Id, location!.Id);

        var queue = await client.GetFromJsonAsync<TatamiQueue>(
            $"/api/tournaments/{tournamentId}/tatamis/{tatamiId}/queue", JsonOptions);
        Assert.Equal(fight.Id, queue!.Current!.Id);

        var fightPath = $"/api/tournaments/{tournamentId}/fights/{fight.Id}";
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"{fightPath}/start", null)).StatusCode);

        var deleteStarted = await client.DeleteAsync($"/api/tournaments/{tournamentId}/free-fights/{fight.Id}");
        Assert.Equal(HttpStatusCode.Conflict, deleteStarted.StatusCode);

        var byAthlete = await client.PostAsJsonAsync($"{fightPath}/result", new { winnerId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, byAthlete.StatusCode);
        var unknownSide = await client.PostAsJsonAsync($"{fightPath}/result", new { winnerSide = "red" });
        Assert.Equal(HttpStatusCode.BadRequest, unknownSide.StatusCode);

        var result = await client.PostAsJsonAsync($"{fightPath}/result", new { winnerSide = "Blue" });
        Assert.Equal(HttpStatusCode.NoContent, result.StatusCode);

        var after = await client.GetFromJsonAsync<TatamiQueue>(
            $"/api/tournaments/{tournamentId}/tatamis/{tatamiId}/queue", JsonOptions);
        Assert.Null(after!.Current);
        var completed = await client.GetFromJsonAsync<Fight>(created.Headers.Location, JsonOptions);
        Assert.Equal(FightSide.Blue, completed!.FreeFight!.WinnerSide);
    }

    [Fact]
    public async Task FreikampfMode_CreatesOneReadyFightAndReplenishesAfterStart()
    {
        using var client = _factory.CreateClient();
        var (tournamentId, tatamiId) = await SeedTournamentWithTatamiAsync(client);

        var enabled = await client.PutAsJsonAsync(
            $"/api/tournaments/{tournamentId}/tatamis/{tatamiId}/free-fight-mode",
            new { enabled = true });

        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        var tatami = await enabled.Content.ReadFromJsonAsync<Tatami>(JsonOptions);
        Assert.True(tatami!.FreeFightModeEnabled);

        var queue = await client.GetFromJsonAsync<TatamiQueue>(
            $"/api/tournaments/{tournamentId}/tatamis/{tatamiId}/queue", JsonOptions);
        Assert.True(queue!.FreeFightModeEnabled);
        var first = Assert.Single(queue.Upcoming);
        Assert.Equal("Weiß", first.FreeFight!.WhiteName);
        Assert.Equal("Blau", first.FreeFight.BlueName);

        var start = await client.PostAsync(
            $"/api/tournaments/{tournamentId}/fights/{first.Id}/start",
            null);
        Assert.Equal(HttpStatusCode.NoContent, start.StatusCode);

        var afterStart = await client.GetFromJsonAsync<TatamiQueue>(
            $"/api/tournaments/{tournamentId}/tatamis/{tatamiId}/queue", JsonOptions);
        Assert.Equal(first.Id, afterStart!.Current!.Id);
        Assert.True(afterStart.Next!.FreeFight is not null);
        Assert.Equal(2, afterStart.Upcoming.Count);

        var reenabled = await client.PutAsJsonAsync(
            $"/api/tournaments/{tournamentId}/tatamis/{tatamiId}/free-fight-mode",
            new { enabled = true });
        Assert.Equal(HttpStatusCode.OK, reenabled.StatusCode);
        var stillOnePending = await client.GetFromJsonAsync<TatamiQueue>(
            $"/api/tournaments/{tournamentId}/tatamis/{tatamiId}/queue", JsonOptions);
        Assert.Equal(2, stillOnePending!.Upcoming.Count);
    }

    [Fact]
    public async Task DeactivatingTatami_DisablesModeAndAuditsTheChange()
    {
        using var client = _factory.CreateClient();
        var (tournamentId, tatamiId) = await SeedTournamentWithTatamiAsync(client);

        var enabled = await client.PutAsJsonAsync(
            $"/api/tournaments/{tournamentId}/tatamis/{tatamiId}/free-fight-mode",
            new { enabled = true });
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);

        var deactivated = await client.PutAsJsonAsync(
            $"/api/tournaments/{tournamentId}/tatamis/{tatamiId}",
            new UpdateTatamiRequest
            {
                Name = "Matte 1",
                DisplayOrder = 0,
                IsActive = false
            });
        Assert.Equal(HttpStatusCode.NoContent, deactivated.StatusCode);

        var tatami = await client.GetFromJsonAsync<Tatami>(
            $"/api/tournaments/{tournamentId}/tatamis/{tatamiId}", JsonOptions);
        Assert.False(tatami!.IsActive);
        Assert.False(tatami.FreeFightModeEnabled);

        var backup = await client.GetFromJsonAsync<TournamentBackup>(
            $"/api/tournaments/{tournamentId}/backup", JsonOptions);
        Assert.Contains(backup!.AuditLogs, entry =>
            entry.Action == "FreeFightModeEnabled" && entry.EntityId == tatamiId);
        Assert.Contains(backup.AuditLogs, entry =>
            entry.Action == "FreeFightModeDisabled" && entry.EntityId == tatamiId);
    }

    [Theory]
    [InlineData("Gast\u0007")]
    [InlineData("Gast\u202E1")]
    public async Task Freikampf_NameWithControlCharacters_Returns400(string name)
    {
        using var client = _factory.CreateClient();
        var (tournamentId, tatamiId) = await SeedTournamentWithTatamiAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/tatamis/{tatamiId}/free-fights",
            new { white = new { name }, blue = new { name = "Blau" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Freikampf_SideWithoutNameOrAthlete_Returns400WithMessageKey()
    {
        using var client = _factory.CreateClient();
        var (tournamentId, tatamiId) = await SeedTournamentWithTatamiAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/tatamis/{tatamiId}/free-fights",
            new { white = new { name = " " }, blue = new { name = "Blau" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("errors.freeFight.nameRequired", problem.RootElement.GetProperty("messageKey").GetString());
    }

    [Theory]
    [InlineData(10, 180, 5)]
    [InlineData(240, 10, 5)]
    [InlineData(240, 180, 61)]
    public async Task Freikampf_InvalidTimesOrTooLongName_Returns400(int matchDurationSeconds, int goldenScoreDurationSeconds, int nameLength)
    {
        using var client = _factory.CreateClient();
        var (tournamentId, tatamiId) = await SeedTournamentWithTatamiAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/tatamis/{tatamiId}/free-fights",
            new
            {
                white = new { name = new string('W', nameLength) },
                blue = new { name = "Blau" },
                matchDurationSeconds,
                goldenScoreEnabled = true,
                goldenScoreDurationSeconds,
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<(Guid TournamentId, Guid TatamiId)> SeedTournamentWithTatamiAsync(HttpClient client)
    {
        await AuthenticateAsync(client);
        var tournament = await CreateAsync<Tournament>(client, "/api/tournaments", new CreateTournamentRequest
        {
            Name = "Freikampf-Turnier",
            Date = new DateOnly(2026, 10, 13),
            Venue = "Sporthalle",
            Organizer = "JC Test",
        });
        var tatami = await CreateAsync<Tatami>(client, $"/api/tournaments/{tournament.Id}/tatamis", new CreateTatamiRequest
        {
            Name = "Matte 1",
        });
        return (tournament.Id, tatami.Id);
    }

    private static async Task<T> CreateAsync<T>(HttpClient client, string path, object request)
    {
        var response = await client.PostAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private static async Task AuthenticateAsync(HttpClient client)
    {
        var bootstrap = await client.PostAsJsonAsync("/api/auth/bootstrap-admin", new BootstrapAdminRequest
        {
            UserName = "admin",
            Password = "Admin!123456"
        });
        Assert.True(bootstrap.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict);

        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            UserName = "admin",
            Password = "Admin!123456"
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var payload = await login.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.AccessToken);
    }
}
