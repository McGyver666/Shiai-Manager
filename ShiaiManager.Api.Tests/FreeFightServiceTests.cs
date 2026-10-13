using ShiaiManager.Api.Contracts;
using ShiaiManager.Api.Data;
using ShiaiManager.Api.Hubs;
using ShiaiManager.Api.Models;
using ShiaiManager.Api.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ShiaiManager.Api.Tests;

/// <summary>
/// Unit tests for <see cref="FreeFightService"/>: creating, editing and deleting Freikämpfe on a tatami (ADR-0013).
/// </summary>
[Trait("Category", "UnitTest")]
public sealed class FreeFightServiceTests
{
    private static readonly FreeFightSideInput WhiteGuest = new(null, "Weiß");
    private static readonly FreeFightSideInput BlueGuest = new(null, "Blau");

    private static FreeFightInput Input(
        FreeFightSideInput? white = null,
        FreeFightSideInput? blue = null,
        int matchDurationSeconds = 240,
        bool goldenScoreEnabled = false,
        int goldenScoreDurationSeconds = 180) =>
        new(white ?? WhiteGuest, blue ?? BlueGuest, matchDurationSeconds, goldenScoreEnabled, goldenScoreDurationSeconds);

    private sealed record Seeded(string Db, Guid TournamentId, Guid CategoryId, Guid TatamiId, List<Guid> AthleteIds, List<FightRecord> RoundOne);

    private static string CreateDatabasePath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ShiaiManagerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "freefight.db");
    }

    private static AppDbContext CreateDbContext(string path) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path}").Options);

    private static MatchService CreateMatchService(AppDbContext ctx)
    {
        var mockHub = new Mock<IHubContext<TournamentHub>>();
        var mockClients = new Mock<IHubClients>();
        var mockProxy = new Mock<IClientProxy>();
        mockHub.Setup(h => h.Clients).Returns(mockClients.Object);
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockProxy.Object);
        mockProxy.Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return new MatchService(
            ctx, new AuditLogService(ctx, NullLogger<AuditLogService>.Instance), mockHub.Object,
            new Mock<IBracketService>().Object, new Mock<IRankingService>().Object, NullLogger<MatchService>.Instance);
    }

    private static FreeFightService CreateService(AppDbContext ctx)
    {
        var mockHub = new Mock<IHubContext<TournamentHub>>();
        var mockClients = new Mock<IHubClients>();
        var mockProxy = new Mock<IClientProxy>();
        mockHub.Setup(h => h.Clients).Returns(mockClients.Object);
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockProxy.Object);
        mockProxy.Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return new FreeFightService(
            ctx, new AuditLogService(ctx, NullLogger<AuditLogService>.Instance), CreateMatchService(ctx),
            new SqliteFightsStore(ctx, NullLogger<SqliteFightsStore>.Instance), mockHub.Object);
    }

    /// <summary>Seeds 4 athletes in one category and puts both round-one fights on one tatami.</summary>
    private static async Task<Seeded> SeedAsync(CompetitionMode mode = CompetitionMode.Individual)
    {
        var db = CreateDatabasePath();
        await using var ctx = CreateDbContext(db);
        await ctx.Database.EnsureCreatedAsync();

        var presets = new Mock<ICategoryPresetsStore>();
        presets.Setup(p => p.SeedDefaultsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var t = await new SqliteTournamentStore(ctx, NullLogger<SqliteTournamentStore>.Instance, presets.Object)
            .CreateAsync("T", new DateOnly(2026, 1, 1), "V", "O", CancellationToken.None);
        if (mode != CompetitionMode.Individual)
        {
            var tournament = await ctx.Tournaments.SingleAsync(x => x.Id == t.Id);
            tournament.CompetitionMode = mode.ToString();
            await ctx.SaveChangesAsync();
        }

        var club = await new SqliteClubsStore(ctx, NullLogger<SqliteClubsStore>.Instance)
            .CreateAsync(t.Id, "JC Test", null, null, null, CancellationToken.None);
        var athleteStore = new SqliteAthletesStore(ctx, NullLogger<SqliteAthletesStore>.Instance);
        var athleteIds = new List<Guid>();
        for (var i = 0; i < 4; i++)
        {
            var athlete = await athleteStore.CreateAsync(
                t.Id, club!.Id, $"A{i:D2}", "Tester", 2000 + i, Gender.Male, null, null, 1, true, CancellationToken.None);
            athleteIds.Add(athlete!.Id);
        }

        var category = await new SqliteCategoriesStore(ctx, NullLogger<SqliteCategoriesStore>.Instance)
            .CreateAsync(t.Id, "U18 M -73", "U18", Gender.Male, 73m, null, null, null, 300, false, 180, CancellationToken.None);
        var registrations = new SqliteRegistrationsStore(ctx, NullLogger<SqliteRegistrationsStore>.Instance);
        foreach (var athleteId in athleteIds)
        {
            var registration = await registrations.CreateAsync(t.Id, athleteId, 70m, null, false, CancellationToken.None);
            await registrations.AssignCategoryAsync(registration!.Id, category!.Id, CancellationToken.None);
        }

        await new BracketService(ctx, NullLogger<BracketService>.Instance)
            .GenerateAsync(t.Id, category!.Id, BracketFormat.SingleElimination, CancellationToken.None);
        var tatami = await new SqliteTatamisStore(ctx, NullLogger<SqliteTatamisStore>.Instance)
            .CreateAsync(t.Id, "Tatami 1", 0, CancellationToken.None);

        var roundOne = await ctx.Fights.AsNoTracking()
            .Where(f => f.CategoryId == category.Id && f.Round == 1)
            .OrderBy(f => f.FightNumber)
            .ToListAsync();
        var match = CreateMatchService(ctx);
        foreach (var fight in roundOne)
        {
            await match.AssignTatamiAsync(fight.Id, tatami.Id, "Admin", CancellationToken.None);
        }

        return new Seeded(db, t.Id, category.Id, tatami.Id, athleteIds, roundOne);
    }

    private static async Task<TatamiQueue> QueueAsync(Seeded seeded)
    {
        await using var ctx = CreateDbContext(seeded.Db);
        return (await new TatamiQueueService(ctx).GetQueueAsync(seeded.TournamentId, seeded.TatamiId, CancellationToken.None))!;
    }

    private static async Task<FreeFightResult> CreateAsync(Seeded seeded, FreeFightInput input, bool placeNext = false)
    {
        await using var ctx = CreateDbContext(seeded.Db);
        return await CreateService(ctx).CreateAsync(
            seeded.TournamentId, seeded.TatamiId, input, placeNext, "Tisch1", CancellationToken.None);
    }

    // ─── Create ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_AppendsFreikampfAtTheEndOfTheTatamiQueue()
    {
        var seeded = await SeedAsync();

        var result = await CreateAsync(seeded, Input());

        Assert.Equal(FreeFightResultStatus.Success, result.Status);
        var queue = await QueueAsync(seeded);
        Assert.Equal(
            [seeded.RoundOne[0].Id, seeded.RoundOne[1].Id, result.Fight!.Id],
            queue.Upcoming.Select(f => f.Id));
        var created = queue.Upcoming[^1];
        Assert.Null(created.CategoryId);
        Assert.Equal(FightStatus.Pending, created.Status);
        Assert.Equal(new FreeFightDetails("Weiß", "Blau", 240, false, 180, null), created.FreeFight);
    }

    [Fact]
    public async Task Create_PlaceNext_PutsFreikampfDirectlyBehindTheCurrentFight()
    {
        var seeded = await SeedAsync();

        var result = await CreateAsync(seeded, Input(), placeNext: true);

        var queue = await QueueAsync(seeded);
        Assert.Equal(
            [seeded.RoundOne[0].Id, result.Fight!.Id, seeded.RoundOne[1].Id],
            queue.Upcoming.Select(f => f.Id));
    }

    [Fact]
    public async Task Create_PlaceNext_WhileAFightIsRunning_PutsFreikampfFirstAmongPendingFights()
    {
        var seeded = await SeedAsync();
        await using (var ctx = CreateDbContext(seeded.Db))
        {
            await CreateMatchService(ctx).StartAsync(seeded.RoundOne[1].Id, "Tisch1", CancellationToken.None);
        }

        var result = await CreateAsync(seeded, Input(), placeNext: true);

        var queue = await QueueAsync(seeded);
        Assert.Equal(
            [seeded.RoundOne[1].Id, result.Fight!.Id, seeded.RoundOne[0].Id],
            queue.Upcoming.Select(f => f.Id));
    }

    [Fact]
    public async Task Create_WithAthleteOfTheTournament_StoresAthleteInsteadOfName()
    {
        var seeded = await SeedAsync();

        var result = await CreateAsync(
            seeded,
            Input(white: new FreeFightSideInput(seeded.AthleteIds[0], null), matchDurationSeconds: 180,
                goldenScoreEnabled: true, goldenScoreDurationSeconds: 60));

        Assert.Equal(FreeFightResultStatus.Success, result.Status);
        Assert.Equal(seeded.AthleteIds[0], result.Fight!.WhiteAthleteId);
        Assert.Null(result.Fight.BlueAthleteId);
        Assert.Equal(new FreeFightDetails(null, "Blau", 180, true, 60, null), result.Fight.FreeFight);
    }

    [Fact]
    public async Task Create_TrimsFreeTextNames()
    {
        var seeded = await SeedAsync();

        var result = await CreateAsync(seeded, Input(white: new FreeFightSideInput(null, "  Gast 1  ")));

        Assert.Equal("Gast 1", result.Fight!.FreeFight!.WhiteName);
    }

    [Fact]
    public async Task Create_SideWithoutAthleteAndName_IsRejected()
    {
        var seeded = await SeedAsync();

        var result = await CreateAsync(seeded, Input(blue: new FreeFightSideInput(null, "   ")));

        Assert.Equal(FreeFightResultStatus.NameRequired, result.Status);
        Assert.Equal(2, (await QueueAsync(seeded)).Upcoming.Count);
    }

    [Fact]
    public async Task Create_AthleteOfAnotherTournament_IsRejected()
    {
        var seeded = await SeedAsync();
        var other = await SeedAsync();

        var result = await CreateAsync(seeded, Input(white: new FreeFightSideInput(other.AthleteIds[0], null)));

        Assert.Equal(FreeFightResultStatus.AthleteNotFound, result.Status);
    }

    [Fact]
    public async Task Create_SameAthleteOnBothSides_IsRejected()
    {
        var seeded = await SeedAsync();
        var side = new FreeFightSideInput(seeded.AthleteIds[0], null);

        var result = await CreateAsync(seeded, Input(white: side, blue: side));

        Assert.Equal(FreeFightResultStatus.SameAthleteOnBothSides, result.Status);
    }

    [Fact]
    public async Task Create_TatamiOfAnotherTournament_IsRejected()
    {
        var seeded = await SeedAsync();
        var other = await SeedAsync();

        await using var ctx = CreateDbContext(seeded.Db);
        var result = await CreateService(ctx).CreateAsync(
            seeded.TournamentId, other.TatamiId, Input(), false, "Tisch1", CancellationToken.None);

        Assert.Equal(FreeFightResultStatus.TatamiNotFound, result.Status);
    }

    [Fact]
    public async Task Create_InTeamMatchdayMode_IsAllowed()
    {
        var seeded = await SeedAsync(CompetitionMode.TeamMatchday);

        var result = await CreateAsync(seeded, Input());

        Assert.Equal(FreeFightResultStatus.Success, result.Status);
    }

    [Fact]
    public async Task Create_WritesAuditEntry()
    {
        var seeded = await SeedAsync();

        var result = await CreateAsync(seeded, Input());

        await using var ctx = CreateDbContext(seeded.Db);
        var entry = await ctx.AuditLogs.SingleAsync(a => a.Action == "FreeFightCreated");
        Assert.Equal(result.Fight!.Id, entry.EntityId);
        Assert.Equal("Tisch1", entry.User);
    }

    // ─── Update ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_PendingFreikampf_ChangesSidesAndTimes()
    {
        var seeded = await SeedAsync();
        var created = (await CreateAsync(seeded, Input())).Fight!;

        await using var ctx = CreateDbContext(seeded.Db);
        var result = await CreateService(ctx).UpdateAsync(
            seeded.TournamentId, created.Id,
            Input(blue: new FreeFightSideInput(seeded.AthleteIds[2], null), matchDurationSeconds: 120, goldenScoreEnabled: true),
            "Tisch1", CancellationToken.None);

        Assert.Equal(FreeFightResultStatus.Success, result.Status);
        Assert.Equal(seeded.AthleteIds[2], result.Fight!.BlueAthleteId);
        Assert.Equal(new FreeFightDetails("Weiß", null, 120, true, 180, null), result.Fight.FreeFight);
    }

    [Fact]
    public async Task Update_StartedFreikampf_IsRejected()
    {
        var seeded = await SeedAsync();
        var created = (await CreateAsync(seeded, Input())).Fight!;
        await using (var ctx = CreateDbContext(seeded.Db))
        {
            Assert.Equal(MatchActionResult.Success,
                await CreateMatchService(ctx).StartAsync(created.Id, "Tisch1", CancellationToken.None));
        }

        await using var verifyCtx = CreateDbContext(seeded.Db);
        var result = await CreateService(verifyCtx).UpdateAsync(
            seeded.TournamentId, created.Id, Input(matchDurationSeconds: 60), "Tisch1", CancellationToken.None);

        Assert.Equal(FreeFightResultStatus.AlreadyStarted, result.Status);
    }

    [Fact]
    public async Task Update_RegularFight_IsNotFound()
    {
        var seeded = await SeedAsync();

        await using var ctx = CreateDbContext(seeded.Db);
        var result = await CreateService(ctx).UpdateAsync(
            seeded.TournamentId, seeded.RoundOne[0].Id, Input(), "Tisch1", CancellationToken.None);

        Assert.Equal(FreeFightResultStatus.FightNotFound, result.Status);
    }

    // ─── Delete ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_PendingFreikampf_RemovesItFromTheQueue()
    {
        var seeded = await SeedAsync();
        var created = (await CreateAsync(seeded, Input())).Fight!;

        await using (var ctx = CreateDbContext(seeded.Db))
        {
            var result = await CreateService(ctx).DeleteAsync(seeded.TournamentId, created.Id, "Tisch1", CancellationToken.None);
            Assert.Equal(FreeFightResultStatus.Success, result.Status);
        }

        var queue = await QueueAsync(seeded);
        Assert.DoesNotContain(queue.Upcoming, f => f.Id == created.Id);
        await using var verifyCtx = CreateDbContext(seeded.Db);
        Assert.True(await verifyCtx.AuditLogs.AnyAsync(a => a.Action == "FreeFightDeleted" && a.EntityId == created.Id));
    }

    [Fact]
    public async Task Delete_StartedFreikampf_IsRejected()
    {
        var seeded = await SeedAsync();
        var created = (await CreateAsync(seeded, Input())).Fight!;
        await using (var ctx = CreateDbContext(seeded.Db))
        {
            await CreateMatchService(ctx).StartAsync(created.Id, "Tisch1", CancellationToken.None);
        }

        await using var verifyCtx = CreateDbContext(seeded.Db);
        var result = await CreateService(verifyCtx).DeleteAsync(seeded.TournamentId, created.Id, "Tisch1", CancellationToken.None);

        Assert.Equal(FreeFightResultStatus.AlreadyStarted, result.Status);
        Assert.True(await verifyCtx.Fights.AnyAsync(f => f.Id == created.Id));
    }

    [Fact]
    public async Task Delete_RegularFight_IsNotFound()
    {
        var seeded = await SeedAsync();

        await using var ctx = CreateDbContext(seeded.Db);
        var result = await CreateService(ctx).DeleteAsync(seeded.TournamentId, seeded.RoundOne[0].Id, "Tisch1", CancellationToken.None);

        Assert.Equal(FreeFightResultStatus.FightNotFound, result.Status);
        Assert.True(await ctx.Fights.AnyAsync(f => f.Id == seeded.RoundOne[0].Id));
    }

    // ─── Match control ───────────────────────────────────────────────────────

    [Fact]
    public async Task MoveInQueue_FreikampfWithFreeTextSides_CanBeMovedUp()
    {
        var seeded = await SeedAsync();
        var created = (await CreateAsync(seeded, Input())).Fight!;

        await using (var ctx = CreateDbContext(seeded.Db))
        {
            Assert.Equal(MatchActionResult.Success,
                await CreateMatchService(ctx).MoveInQueueAsync(created.Id, QueueMoveDirection.Up, "Tisch1", CancellationToken.None));
        }

        var queue = await QueueAsync(seeded);
        Assert.Equal(
            [seeded.RoundOne[0].Id, created.Id, seeded.RoundOne[1].Id],
            queue.Upcoming.Select(f => f.Id));
    }

    private static async Task<Fight> CreateAndStartAsync(Seeded seeded, FreeFightInput input)
    {
        var created = (await CreateAsync(seeded, input)).Fight!;
        await using var ctx = CreateDbContext(seeded.Db);
        Assert.Equal(MatchActionResult.Success, await CreateMatchService(ctx).StartAsync(created.Id, "Tisch1", CancellationToken.None));
        return created;
    }

    private static async Task<FightRecord> ReadFightAsync(Seeded seeded, Guid fightId)
    {
        await using var ctx = CreateDbContext(seeded.Db);
        return await ctx.Fights.AsNoTracking().SingleAsync(f => f.Id == fightId);
    }

    [Fact]
    public async Task Start_FreikampfWithFreeTextSides_DoesNotLockAnyCategory()
    {
        var seeded = await SeedAsync();

        var created = await CreateAndStartAsync(seeded, Input(white: new FreeFightSideInput(seeded.AthleteIds[0], null)));

        Assert.Equal(FightStatus.InProgress.ToString(), (await ReadFightAsync(seeded, created.Id)).Status);
        await using var ctx = CreateDbContext(seeded.Db);
        Assert.False((await ctx.Categories.SingleAsync(c => c.Id == seeded.CategoryId)).IsLocked);
    }

    [Theory]
    [InlineData("white", "White")]
    [InlineData("Blue", "Blue")]
    public async Task ConfirmBySide_CompletesFreikampfWithWinningSide(string side, string expectedSide)
    {
        var seeded = await SeedAsync();
        var created = await CreateAndStartAsync(seeded, Input());

        await using (var ctx = CreateDbContext(seeded.Db))
        {
            Assert.Equal(MatchActionResult.Success,
                await CreateMatchService(ctx).ConfirmResultBySideAsync(created.Id, side, "Tisch1", CancellationToken.None));
        }

        var completed = await ReadFightAsync(seeded, created.Id);
        Assert.Equal(FightStatus.Completed.ToString(), completed.Status);
        Assert.Equal(expectedSide, completed.WinnerSide);
        Assert.Null(completed.WinnerId);
    }

    [Fact]
    public async Task ConfirmBySide_WithoutSide_RecordsHikiwakeInIndividualTournament()
    {
        var seeded = await SeedAsync();
        var created = await CreateAndStartAsync(seeded, Input());

        await using (var ctx = CreateDbContext(seeded.Db))
        {
            Assert.Equal(MatchActionResult.Success,
                await CreateMatchService(ctx).ConfirmResultBySideAsync(created.Id, null, "Tisch1", CancellationToken.None));
        }

        var completed = await ReadFightAsync(seeded, created.Id);
        Assert.Equal(FightStatus.Completed.ToString(), completed.Status);
        Assert.Null(completed.WinnerSide);
    }

    [Fact]
    public async Task ConfirmBySide_UnknownSide_IsRejected()
    {
        var seeded = await SeedAsync();
        var created = await CreateAndStartAsync(seeded, Input());

        await using var ctx = CreateDbContext(seeded.Db);
        Assert.Equal(MatchActionResult.WinnerNotParticipant,
            await CreateMatchService(ctx).ConfirmResultBySideAsync(created.Id, "red", "Tisch1", CancellationToken.None));
    }

    [Fact]
    public async Task ConfirmBySide_RegularFight_IsRejected()
    {
        var seeded = await SeedAsync();
        await using var ctx = CreateDbContext(seeded.Db);
        var match = CreateMatchService(ctx);
        await match.StartAsync(seeded.RoundOne[0].Id, "Tisch1", CancellationToken.None);

        Assert.Equal(MatchActionResult.InvalidState,
            await match.ConfirmResultBySideAsync(seeded.RoundOne[0].Id, "white", "Tisch1", CancellationToken.None));
    }

    [Fact]
    public async Task ConfirmByAthlete_FreikampfWithSelectedAthlete_RecordsTheAthletesSide()
    {
        var seeded = await SeedAsync();
        var created = await CreateAndStartAsync(seeded, Input(blue: new FreeFightSideInput(seeded.AthleteIds[1], null)));

        await using (var ctx = CreateDbContext(seeded.Db))
        {
            Assert.Equal(MatchActionResult.Success,
                await CreateMatchService(ctx).ConfirmResultAsync(created.Id, seeded.AthleteIds[1], "Tisch1", CancellationToken.None));
        }

        var completed = await ReadFightAsync(seeded, created.Id);
        Assert.Equal("Blue", completed.WinnerSide);
        Assert.Null(completed.WinnerId);
    }

    [Fact]
    public async Task Confirm_FreikampfWithAthlete_UpdatesTheAthletesLastFight()
    {
        var seeded = await SeedAsync();
        var created = await CreateAndStartAsync(seeded, Input(white: new FreeFightSideInput(seeded.AthleteIds[0], null)));

        await using (var ctx = CreateDbContext(seeded.Db))
        {
            await CreateMatchService(ctx).ConfirmResultBySideAsync(created.Id, "blue", "Tisch1", CancellationToken.None);
        }

        await using var verifyCtx = CreateDbContext(seeded.Db);
        var athlete = await verifyCtx.Athletes.SingleAsync(a => a.Id == seeded.AthleteIds[0]);
        Assert.NotNull(athlete.LastFightEndedAtUtc);
        Assert.NotNull(athlete.LastFightDurationSeconds);
    }

    [Fact]
    public async Task Confirm_Freikampf_LeavesOfficialFightsUnchanged()
    {
        var seeded = await SeedAsync();
        var created = await CreateAndStartAsync(seeded, Input(
            white: new FreeFightSideInput(seeded.AthleteIds[0], null),
            blue: new FreeFightSideInput(seeded.AthleteIds[1], null)));
        List<FightRecord> before;
        await using (var ctx = CreateDbContext(seeded.Db))
        {
            before = await ctx.Fights.AsNoTracking().Where(f => f.CategoryId == seeded.CategoryId).OrderBy(f => f.Id).ToListAsync();
        }

        await using (var ctx = CreateDbContext(seeded.Db))
        {
            await CreateMatchService(ctx).ConfirmResultAsync(created.Id, seeded.AthleteIds[0], "Tisch1", CancellationToken.None);
        }

        await using var verifyCtx = CreateDbContext(seeded.Db);
        var after = await verifyCtx.Fights.AsNoTracking().Where(f => f.CategoryId == seeded.CategoryId).OrderBy(f => f.Id).ToListAsync();
        Assert.Equivalent(before, after, strict: true);
    }

    [Fact]
    public async Task EditResult_CompletedFreikampf_IsRejected()
    {
        var seeded = await SeedAsync();
        var created = await CreateAndStartAsync(seeded, Input(
            white: new FreeFightSideInput(seeded.AthleteIds[0], null),
            blue: new FreeFightSideInput(seeded.AthleteIds[1], null)));
        await using var ctx = CreateDbContext(seeded.Db);
        var match = CreateMatchService(ctx);
        await match.ConfirmResultBySideAsync(created.Id, "white", "Tisch1", CancellationToken.None);

        var result = await match.EditResultAsync(
            created.Id,
            new EditFightResultRequest { WinnerId = seeded.AthleteIds[1], Confirmed = true },
            "admin",
            CancellationToken.None);

        Assert.Equal(EditResultStatus.InvalidState, result.Status);
    }
}
