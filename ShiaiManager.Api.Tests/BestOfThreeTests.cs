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
/// Tests for the Best of Three draw format: draw, Kampfserie, Entscheidungskampf, result
/// correction, re-draw after assignment changes, rankings and series DTO data (ADR-0012).
/// </summary>
[Trait("Category", "UnitTest")]
public sealed class BestOfThreeTests
{
    private static readonly string Completed = FightStatus.Completed.ToString();

    private sealed record Scenario(
        string Db,
        Guid TournamentId,
        Guid CategoryId,
        Guid TatamiId,
        List<Guid> AthleteIds,
        List<Guid> RegistrationIds,
        List<Guid> ClubIds);

    private static AppDbContext CreateDbContext(string path) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path}").Options);

    private static BracketService CreateBracketService(AppDbContext ctx) =>
        new(ctx, NullLogger<BracketService>.Instance);

    private static MatchService CreateMatchService(AppDbContext ctx)
    {
        var hub = new Mock<IHubContext<TournamentHub>>();
        var clients = new Mock<IHubClients>();
        var proxy = new Mock<IClientProxy>();
        hub.Setup(h => h.Clients).Returns(clients.Object);
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(proxy.Object);
        proxy.Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return new MatchService(ctx,
            new AuditLogService(ctx, NullLogger<AuditLogService>.Instance),
            hub.Object,
            new Mock<IBracketService>().Object,
            new Mock<IRankingService>().Object,
            NullLogger<MatchService>.Instance,
            new Mock<IFreeFightModeService>().Object);
    }

    /// <summary>Seeds a tournament with one club per athlete, one category and one tatami; no draw yet.</summary>
    private static async Task<Scenario> SeedAsync(int athleteCount, CompetitionMode mode = CompetitionMode.Individual)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ShiaiManagerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var db = Path.Combine(dir, "bestofthree.db");

        await using var ctx = CreateDbContext(db);
        await ctx.Database.EnsureCreatedAsync();

        var presets = new Mock<ICategoryPresetsStore>();
        presets.Setup(p => p.SeedDefaultsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var t = await new SqliteTournamentStore(ctx, NullLogger<SqliteTournamentStore>.Instance, presets.Object)
            .CreateAsync("T", new DateOnly(2026, 1, 1), "V", "O", CancellationToken.None);
        if (mode != CompetitionMode.Individual)
        {
            var record = await ctx.Tournaments.FirstAsync(x => x.Id == t.Id);
            record.CompetitionMode = mode.ToString();
            await ctx.SaveChangesAsync();
        }

        var clubStore = new SqliteClubsStore(ctx, NullLogger<SqliteClubsStore>.Instance);
        var athleteStore = new SqliteAthletesStore(ctx, NullLogger<SqliteAthletesStore>.Instance);
        var category = await new SqliteCategoriesStore(ctx, NullLogger<SqliteCategoriesStore>.Instance)
            .CreateAsync(t.Id, "U18 M -73", "U18", Gender.Male, 73m, null, null, null, 300, false, 180, CancellationToken.None);
        var regStore = new SqliteRegistrationsStore(ctx, NullLogger<SqliteRegistrationsStore>.Instance);

        var athleteIds = new List<Guid>();
        var registrationIds = new List<Guid>();
        var clubIds = new List<Guid>();
        for (var i = 0; i < athleteCount; i++)
        {
            var club = await clubStore.CreateAsync(t.Id, $"JC {i}", null, null, null, CancellationToken.None);
            var athlete = await athleteStore.CreateAsync(
                t.Id, club!.Id, $"A{i:D2}", "Tester", 2000, Gender.Male, null, null, 1, true, CancellationToken.None);
            var reg = await regStore.CreateAsync(t.Id, athlete!.Id, 70m, null, false, CancellationToken.None);
            await regStore.AssignCategoryAsync(reg!.Id, category!.Id, CancellationToken.None);
            clubIds.Add(club.Id);
            athleteIds.Add(athlete.Id);
            registrationIds.Add(reg.Id);
        }

        var tatami = await new SqliteTatamisStore(ctx, NullLogger<SqliteTatamisStore>.Instance)
            .CreateAsync(t.Id, "Tatami 1", 0, CancellationToken.None);

        return new Scenario(db, t.Id, category!.Id, tatami!.Id, athleteIds, registrationIds, clubIds);
    }

    /// <summary>Seeds two athletes, draws Best of Three and assigns both fights to the tatami.</summary>
    private static async Task<Scenario> SeedDrawnSeriesAsync()
    {
        var s = await SeedAsync(2);
        await using var ctx = CreateDbContext(s.Db);
        await CreateBracketService(ctx).GenerateAsync(s.TournamentId, s.CategoryId, BracketFormat.BestOfThree, CancellationToken.None);
        var match = CreateMatchService(ctx);
        foreach (var fight in await ctx.Fights.AsNoTracking().Where(f => f.CategoryId == s.CategoryId).ToListAsync())
        {
            await match.AssignTatamiAsync(fight.Id, s.TatamiId, "admin", CancellationToken.None);
        }

        return s;
    }

    private static async Task<List<FightRecord>> FightsAsync(Scenario s)
    {
        await using var ctx = CreateDbContext(s.Db);
        return await ctx.Fights.AsNoTracking()
            .Where(f => f.CategoryId == s.CategoryId)
            .OrderBy(f => f.Round)
            .ToListAsync();
    }

    private static async Task<FightRecord> BoutAsync(Scenario s, int round) =>
        (await FightsAsync(s)).Single(f => f.Round == round);

    /// <summary>Starts and confirms the bout of the given round; <paramref name="whiteWins"/> picks the winner.</summary>
    private static async Task PlayAsync(Scenario s, int round, bool whiteWins)
    {
        var bout = await BoutAsync(s, round);
        await using var ctx = CreateDbContext(s.Db);
        var match = CreateMatchService(ctx);
        Assert.Equal(MatchActionResult.Success, await match.StartAsync(bout.Id, "admin", CancellationToken.None));
        var winner = whiteWins ? bout.WhiteAthleteId : bout.BlueAthleteId;
        Assert.Equal(MatchActionResult.Success, await match.ConfirmResultAsync(bout.Id, winner, null, "admin", CancellationToken.None));
    }

    private static async Task<EditFightResultResponse> EditWinnerAsync(Scenario s, int round, bool whiteWins, bool confirmed = false)
    {
        var bout = await BoutAsync(s, round);
        await using var ctx = CreateDbContext(s.Db);
        var request = new EditFightResultRequest
        {
            WinnerId = (whiteWins ? bout.WhiteAthleteId : bout.BlueAthleteId)!.Value,
            WhiteIpponCount = whiteWins ? 1 : 0,
            BlueIpponCount = whiteWins ? 0 : 1,
            Confirmed = confirmed
        };
        return await CreateMatchService(ctx).EditResultAsync(bout.Id, request, "admin", CancellationToken.None);
    }

    private static async Task<IReadOnlyList<RankingEntry>> RankingsAsync(Scenario s)
    {
        await using var ctx = CreateDbContext(s.Db);
        return await new RankingService(ctx).GetCategoryRankingsAsync(s.TournamentId, s.CategoryId, CancellationToken.None);
    }

    // ─── Draw ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Generate_TwoAthletes_CreatesFirstAndSecondBoutWithSameColors()
    {
        var s = await SeedAsync(2);
        await using var ctx = CreateDbContext(s.Db);

        var fights = await CreateBracketService(ctx)
            .GenerateAsync(s.TournamentId, s.CategoryId, BracketFormat.BestOfThree, CancellationToken.None);

        Assert.Equal(2, fights.Count);
        Assert.Equal([1, 2], fights.Select(f => f.Round).OrderBy(r => r));
        Assert.All(fights, f =>
        {
            Assert.Equal(FightBracketType.Main, f.BracketType);
            Assert.Equal(1, f.FightNumber);
            Assert.Equal(FightStatus.Pending, f.Status);
            Assert.False(f.IsBye);
        });
        Assert.Equal(fights[0].WhiteAthleteId, fights[1].WhiteAthleteId);
        Assert.Equal(fights[0].BlueAthleteId, fights[1].BlueAthleteId);
        Assert.Equal(s.AthleteIds.Order(), new[] { fights[0].WhiteAthleteId!.Value, fights[0].BlueAthleteId!.Value }.Order());

        var category = await ctx.Categories.AsNoTracking().SingleAsync(c => c.Id == s.CategoryId);
        Assert.Equal(BracketFormat.BestOfThree.ToString(), category.DrawFormat);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Generate_NotExactlyTwoAthletes_IsRejected(int athleteCount)
    {
        var s = await SeedAsync(athleteCount);
        await using var ctx = CreateDbContext(s.Db);

        var ex = await Assert.ThrowsAsync<LocalizedOperationException>(() => CreateBracketService(ctx)
            .GenerateAsync(s.TournamentId, s.CategoryId, BracketFormat.BestOfThree, CancellationToken.None));

        Assert.Equal(DrawMessages.BestOfThreeRequiresTwoAthletes.Key, ex.Localized.Key);
        Assert.Empty(await ctx.Fights.Where(f => f.CategoryId == s.CategoryId).ToListAsync());
    }

    [Fact]
    public async Task Generate_TeamMatchday_IsRejected()
    {
        var s = await SeedAsync(2, CompetitionMode.TeamMatchday);
        await using var ctx = CreateDbContext(s.Db);

        var ex = await Assert.ThrowsAsync<LocalizedOperationException>(() => CreateBracketService(ctx)
            .GenerateAsync(s.TournamentId, s.CategoryId, BracketFormat.BestOfThree, CancellationToken.None));

        Assert.Equal(DrawMessages.BestOfThreeRequiresIndividualMode.Key, ex.Localized.Key);
    }

    // ─── Kampfserie & Entscheidungskampf ─────────────────────────────────────

    [Fact]
    public async Task TwoNil_CreatesNoDecidingFight_AndRanksSeriesWinnerFirst()
    {
        var s = await SeedDrawnSeriesAsync();

        await PlayAsync(s, 1, whiteWins: true);
        await PlayAsync(s, 2, whiteWins: true);

        var fights = await FightsAsync(s);
        Assert.Equal(2, fights.Count);
        Assert.All(fights, f => Assert.Equal(Completed, f.Status));

        var white = fights[0].WhiteAthleteId!.Value;
        var blue = fights[0].BlueAthleteId!.Value;
        var rankings = await RankingsAsync(s);
        Assert.Equal(2, rankings.Count);
        Assert.Equal((1, white), (rankings[0].Place, rankings[0].AthleteId));
        Assert.Equal((2, blue), (rankings[1].Place, rankings[1].AthleteId));
        Assert.Equal((2, 0), (rankings[0].SeriesWins, rankings[0].SeriesLosses));
        Assert.Equal((0, 2), (rankings[1].SeriesWins, rankings[1].SeriesLosses));
    }

    [Fact]
    public async Task OneOne_CreatesDecidingFightOnSameTatami_VisibleInQueue()
    {
        var s = await SeedDrawnSeriesAsync();

        await PlayAsync(s, 1, whiteWins: true);
        await PlayAsync(s, 2, whiteWins: false);

        var fights = await FightsAsync(s);
        Assert.Equal(3, fights.Count);
        var decider = fights.Single(f => f.Round == 3);
        Assert.Equal(FightStatus.Pending.ToString(), decider.Status);
        Assert.Equal(FightBracketType.Main.ToString(), decider.BracketType);
        Assert.Equal(1, decider.FightNumber);
        Assert.Equal(fights[1].TatamiId, decider.TatamiId);
        Assert.Equal(fights[0].WhiteAthleteId, decider.WhiteAthleteId);
        Assert.Equal(fights[0].BlueAthleteId, decider.BlueAthleteId);
        Assert.Empty(await RankingsAsync(s));

        await using var ctx = CreateDbContext(s.Db);
        var queue = await new TatamiQueueService(ctx).GetQueueAsync(s.TournamentId, s.TatamiId, CancellationToken.None);
        Assert.Equal(decider.Id, queue!.Current!.Id);
        Assert.Equal(new FightSeries(3, true, 1, 1), queue.Current.Series);
    }

    [Fact]
    public async Task DecidingFightCompleted_RanksSeriesWinner_WithTwoToOne()
    {
        var s = await SeedDrawnSeriesAsync();
        await PlayAsync(s, 1, whiteWins: true);
        await PlayAsync(s, 2, whiteWins: false);

        await PlayAsync(s, 3, whiteWins: false);

        var fights = await FightsAsync(s);
        Assert.Equal(3, fights.Count);
        var rankings = await RankingsAsync(s);
        Assert.Equal((1, fights[0].BlueAthleteId!.Value), (rankings[0].Place, rankings[0].AthleteId));
        Assert.Equal((2, fights[0].WhiteAthleteId!.Value), (rankings[1].Place, rankings[1].AthleteId));
        Assert.Equal((2, 1), (rankings[0].SeriesWins, rankings[0].SeriesLosses));
        Assert.Equal((1, 2), (rankings[1].SeriesWins, rankings[1].SeriesLosses));
    }

    [Fact]
    public async Task SecondBoutPlayedFirst_DoesNotRankBeforeSeriesIsDecided()
    {
        var s = await SeedDrawnSeriesAsync();

        await PlayAsync(s, 2, whiteWins: true);

        Assert.Empty(await RankingsAsync(s));
        Assert.Equal(2, (await FightsAsync(s)).Count);
    }

    // ─── Ergebniskorrektur ───────────────────────────────────────────────────

    [Fact]
    public async Task Edit_OneOneToTwoNil_RemovesUnstartedDecidingFight()
    {
        var s = await SeedDrawnSeriesAsync();
        await PlayAsync(s, 1, whiteWins: true);
        await PlayAsync(s, 2, whiteWins: false);

        var result = await EditWinnerAsync(s, 2, whiteWins: true);

        Assert.Equal(EditResultStatus.Success, result.Status);
        Assert.Equal(2, (await FightsAsync(s)).Count);
        Assert.Equal(1, (await RankingsAsync(s))[0].Place);
    }

    [Fact]
    public async Task Edit_OneOneToTwoNil_StartedDecidingFight_RequiresConfirmation_ThenRemovesAndAudits()
    {
        var s = await SeedDrawnSeriesAsync();
        await PlayAsync(s, 1, whiteWins: true);
        await PlayAsync(s, 2, whiteWins: false);
        var decider = await BoutAsync(s, 3);
        await using (var ctx = CreateDbContext(s.Db))
        {
            await CreateMatchService(ctx).StartAsync(decider.Id, "admin", CancellationToken.None);
        }

        var pending = await EditWinnerAsync(s, 2, whiteWins: true);

        Assert.Equal(EditResultStatus.ConfirmationRequired, pending.Status);
        Assert.Equal(decider.Id, Assert.Single(pending.AffectedFights!).FightId);
        Assert.Equal(3, (await FightsAsync(s)).Count);

        var confirmed = await EditWinnerAsync(s, 2, whiteWins: true, confirmed: true);

        Assert.Equal(EditResultStatus.Success, confirmed.Status);
        Assert.Equal(2, (await FightsAsync(s)).Count);
        await using var read = CreateDbContext(s.Db);
        var audit = await read.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "DecidingFightRemoved")
            .ToListAsync();
        Assert.Equal(decider.Id, Assert.Single(audit).EntityId);
    }

    [Fact]
    public async Task Edit_TwoNilToOneOne_CreatesDecidingFight_AndScoringIsProvisionalAgain()
    {
        var s = await SeedDrawnSeriesAsync();
        await PlayAsync(s, 1, whiteWins: true);
        await PlayAsync(s, 2, whiteWins: true);

        var result = await EditWinnerAsync(s, 2, whiteWins: false);

        Assert.Equal(EditResultStatus.Success, result.Status);
        var fights = await FightsAsync(s);
        var decider = fights.Single(f => f.Round == 3);
        Assert.Equal(fights[1].TatamiId, decider.TatamiId);
        Assert.Empty(await RankingsAsync(s));

        await using var ctx = CreateDbContext(s.Db);
        var scoring = await new RankingService(ctx).GetGlobalClubScoringAsync(s.TournamentId, CancellationToken.None);
        Assert.Equal("Provisional", scoring.Status);
    }

    [Fact]
    public async Task Edit_DecidingFight_OnlyChangesSeriesWinner()
    {
        var s = await SeedDrawnSeriesAsync();
        await PlayAsync(s, 1, whiteWins: true);
        await PlayAsync(s, 2, whiteWins: false);
        await PlayAsync(s, 3, whiteWins: true);

        var result = await EditWinnerAsync(s, 3, whiteWins: false);

        Assert.Equal(EditResultStatus.Success, result.Status);
        var fights = await FightsAsync(s);
        Assert.Equal(3, fights.Count);
        Assert.All(fights, f => Assert.Equal(Completed, f.Status));
        Assert.Equal(fights[0].BlueAthleteId, (await RankingsAsync(s))[0].AthleteId);
    }

    // ─── Neu-Auslosung nach Zuweisungsänderung ───────────────────────────────

    [Fact]
    public async Task RefreshDraw_ThirdAthleteAssigned_DiscardsBestOfThreeDraw()
    {
        var s = await SeedDrawnSeriesAsync();
        await using (var ctx = CreateDbContext(s.Db))
        {
            var athlete = await new SqliteAthletesStore(ctx, NullLogger<SqliteAthletesStore>.Instance).CreateAsync(
                s.TournamentId, s.ClubIds[0], "A99", "Tester", 2000, Gender.Male, null, null, 1, true, CancellationToken.None);
            var regStore = new SqliteRegistrationsStore(ctx, NullLogger<SqliteRegistrationsStore>.Instance);
            var reg = await regStore.CreateAsync(s.TournamentId, athlete!.Id, 70m, null, false, CancellationToken.None);
            await regStore.AssignCategoryAsync(reg!.Id, s.CategoryId, CancellationToken.None);
        }

        await using (var ctx = CreateDbContext(s.Db))
        {
            await CreateBracketService(ctx).RefreshDrawAsync(s.TournamentId, s.CategoryId, CancellationToken.None);
        }

        Assert.Empty(await FightsAsync(s));
        await using var read = CreateDbContext(s.Db);
        Assert.Null((await read.Categories.AsNoTracking().SingleAsync(c => c.Id == s.CategoryId)).DrawFormat);
    }

    [Fact]
    public async Task RefreshDraw_AthleteRemoved_DiscardsBestOfThreeDraw()
    {
        var s = await SeedDrawnSeriesAsync();
        await using (var ctx = CreateDbContext(s.Db))
        {
            await new SqliteRegistrationsStore(ctx, NullLogger<SqliteRegistrationsStore>.Instance)
                .DeleteAsync(s.RegistrationIds[1], CancellationToken.None);
        }

        await using (var ctx = CreateDbContext(s.Db))
        {
            await CreateBracketService(ctx).RefreshDrawAsync(s.TournamentId, s.CategoryId, CancellationToken.None);
        }

        Assert.Empty(await FightsAsync(s));
        await using var read = CreateDbContext(s.Db);
        Assert.Null((await read.Categories.AsNoTracking().SingleAsync(c => c.Id == s.CategoryId)).DrawFormat);
    }

    [Fact]
    public async Task RefreshDraw_StillTwoAthletes_RegeneratesBestOfThree()
    {
        var s = await SeedDrawnSeriesAsync();

        await using (var ctx = CreateDbContext(s.Db))
        {
            await CreateBracketService(ctx).RefreshDrawAsync(s.TournamentId, s.CategoryId, CancellationToken.None);
        }

        Assert.Equal(2, (await FightsAsync(s)).Count);
    }

    // ─── Wertung & DTOs ──────────────────────────────────────────────────────

    [Fact]
    public async Task ClubScoring_CountsEverySeriesBoutIndividually()
    {
        var s = await SeedDrawnSeriesAsync();
        await PlayAsync(s, 1, whiteWins: true);
        await PlayAsync(s, 2, whiteWins: false);
        await PlayAsync(s, 3, whiteWins: true);
        var white = (await BoutAsync(s, 1)).WhiteAthleteId!.Value;
        var whiteClub = s.ClubIds[s.AthleteIds.IndexOf(white)];

        await using var ctx = CreateDbContext(s.Db);
        var scoring = await new RankingService(ctx).GetGlobalClubScoringAsync(s.TournamentId, CancellationToken.None);

        var winnerClub = scoring.Clubs.Single(e => e.ClubId == whiteClub);
        var loserClub = scoring.Clubs.Single(e => e.ClubId != whiteClub);
        Assert.Equal((2, 3), (winnerClub.Wins, winnerClub.Fights));
        Assert.Equal((1, 3), (loserClub.Wins, loserClub.Fights));
        Assert.Equal("Final", scoring.Status);
    }

    [Fact]
    public async Task FightsStore_ReportsSeriesPositionAndScore()
    {
        var s = await SeedDrawnSeriesAsync();
        await PlayAsync(s, 1, whiteWins: false);

        await using var ctx = CreateDbContext(s.Db);
        var fights = await new SqliteFightsStore(ctx, NullLogger<SqliteFightsStore>.Instance)
            .GetAllAsync(s.TournamentId, s.CategoryId, CancellationToken.None);

        Assert.Equal(new FightSeries(1, false, 0, 1), fights.Single(f => f.Round == 1).Series);
        Assert.Equal(new FightSeries(2, false, 0, 1), fights.Single(f => f.Round == 2).Series);
    }

    [Fact]
    public async Task FightsStore_NonSeriesCategory_HasNoSeries()
    {
        var s = await SeedAsync(2);
        await using var ctx = CreateDbContext(s.Db);
        await CreateBracketService(ctx).GenerateAsync(s.TournamentId, s.CategoryId, BracketFormat.SingleElimination, CancellationToken.None);

        var fights = await new SqliteFightsStore(ctx, NullLogger<SqliteFightsStore>.Instance)
            .GetAllAsync(s.TournamentId, s.CategoryId, CancellationToken.None);

        Assert.Null(Assert.Single(fights).Series);
    }

    [Fact]
    public async Task CompletedFights_ReportSeriesPosition()
    {
        var s = await SeedDrawnSeriesAsync();
        await PlayAsync(s, 1, whiteWins: true);
        await PlayAsync(s, 2, whiteWins: false);
        await PlayAsync(s, 3, whiteWins: true);

        await using var ctx = CreateDbContext(s.Db);
        var summaries = await new CompletedFightsService(ctx).GetCompletedFightsAsync(s.TournamentId, CancellationToken.None);

        Assert.Equal(new FightSeries(3, true, 2, 1), summaries.Single(f => f.Round == 3).Series);
        Assert.Equal(new FightSeries(1, false, 2, 1), summaries.Single(f => f.Round == 1).Series);
    }
}
