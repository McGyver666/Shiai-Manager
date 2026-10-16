using ShiaiManager.Api.Data;
using ShiaiManager.Api.Hubs;
using ShiaiManager.Api.Models;
using ShiaiManager.Api.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ShiaiManager.Api.Tests;

[Trait("Category", "UnitTest")]
public sealed class FreeFightModeServiceTests
{
    [Fact]
    public async Task EnsureNextAsync_WhenModeIsEnabled_CreatesReadyFightWithDefaultTiming()
    {
        await using var context = CreateContext();
        var tournamentId = Guid.NewGuid();
        var tatamiId = Guid.NewGuid();
        context.Tournaments.Add(new TournamentRecord
        {
            Id = tournamentId,
            Name = "Testturnier",
            Date = new DateOnly(2026, 10, 16),
            Venue = "Halle",
            Organizer = "Verein",
            AccentSideColor = "Red",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        context.Tatamis.Add(new TatamiRecord
        {
            Id = tatamiId,
            TournamentId = tournamentId,
            Name = "Tatami 1",
            IsActive = true,
            IsFreeFightModeEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        var service = CreateService(context);

        Assert.True(await service.EnsureNextAsync(tournamentId, tatamiId, CancellationToken.None));

        var fight = await context.Fights.SingleAsync();
        Assert.Equal(FreeFightRules.FreeKind, fight.Kind);
        Assert.Equal("Weiß", fight.WhiteName);
        Assert.Equal("Rot", fight.BlueName);
        Assert.Equal(240, fight.MatchDurationSeconds);
        Assert.False(fight.GoldenScoreEnabled);
        Assert.Equal(180, fight.GoldenScoreDurationSeconds);
        Assert.Equal(FightStatus.Pending.ToString(), fight.Status);
    }

    [Fact]
    public async Task EnsureNextAsync_WhenPositionOneAlreadyContainsFreeFight_DoesNotCreateAnother()
    {
        await using var context = CreateContext();
        var tournamentId = Guid.NewGuid();
        var tatamiId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        context.Tournaments.Add(new TournamentRecord
        {
            Id = tournamentId,
            Name = "Testturnier",
            Date = new DateOnly(2026, 10, 16),
            Venue = "Halle",
            Organizer = "Verein",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        context.Tatamis.Add(new TatamiRecord
        {
            Id = tatamiId,
            TournamentId = tournamentId,
            Name = "Tatami 1",
            IsActive = true,
            IsFreeFightModeEnabled = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        context.Fights.Add(new FightRecord
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            Kind = FreeFightRules.FreeKind,
            BracketType = "Main",
            Status = FightStatus.Pending.ToString(),
            TatamiId = tatamiId,
            WhiteName = "Weiß",
            BlueName = "Blau",
            QueueOrder = 0,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await context.SaveChangesAsync();

        var service = CreateService(context);

        Assert.False(await service.EnsureNextAsync(tournamentId, tatamiId, CancellationToken.None));
        Assert.Equal(1, await context.Fights.CountAsync());
    }

    [Fact]
    public async Task StartingARegularFight_WhenModeIsEnabled_CreatesTheNextFreeFight()
    {
        await using var context = CreateContext();
        var tournamentId = Guid.NewGuid();
        var tatamiId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        context.Tournaments.Add(new TournamentRecord
        {
            Id = tournamentId,
            Name = "Testturnier",
            Date = new DateOnly(2026, 10, 16),
            Venue = "Halle",
            Organizer = "Verein",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        context.Tatamis.Add(new TatamiRecord
        {
            Id = tatamiId,
            TournamentId = tournamentId,
            Name = "Tatami 1",
            IsActive = true,
            IsFreeFightModeEnabled = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        var regularFightId = Guid.NewGuid();
        context.Fights.Add(new FightRecord
        {
            Id = regularFightId,
            TournamentId = tournamentId,
            BracketType = "Main",
            Status = FightStatus.Pending.ToString(),
            TatamiId = tatamiId,
            WhiteAthleteId = Guid.NewGuid(),
            BlueAthleteId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await context.SaveChangesAsync();

        var modeService = CreateService(context);
        var match = CreateMatchService(context, modeService);

        Assert.Equal(
            MatchActionResult.Success,
            await match.StartAsync(regularFightId, "Tisch 1", CancellationToken.None));

        var queue = await context.Fights
            .OrderBy(f => f.QueueOrder)
            .ToListAsync();
        Assert.Equal(2, queue.Count);
        Assert.Equal(FightStatus.InProgress.ToString(), queue.Single(f => f.Id == regularFightId).Status);
        var generated = queue.Single(f => f.Id != regularFightId);
        Assert.Equal(FreeFightRules.FreeKind, generated.Kind);
        Assert.Equal(0, generated.QueueOrder);
    }

    [Fact]
    public async Task StartingAFreeFight_UsesItsEditedTimingForTheFollowingGeneratedFight()
    {
        await using var context = CreateContext();
        var (tournamentId, tatamiId) = await SeedEmptyModeAsync(context);
        var modeService = CreateService(context);
        await modeService.EnsureNextAsync(tournamentId, tatamiId, CancellationToken.None);

        var first = await context.Fights.SingleAsync();
        first.MatchDurationSeconds = 120;
        first.GoldenScoreEnabled = true;
        first.GoldenScoreDurationSeconds = 60;
        await context.SaveChangesAsync();

        var match = CreateMatchService(context, modeService);
        Assert.Equal(
            MatchActionResult.Success,
            await match.StartAsync(first.Id, "Tisch 1", CancellationToken.None));

        var generated = await context.Fights
            .Where(f => f.Id != first.Id)
            .SingleAsync();
        Assert.Equal(120, generated.MatchDurationSeconds);
        Assert.True(generated.GoldenScoreEnabled);
        Assert.Equal(60, generated.GoldenScoreDurationSeconds);
    }

    [Fact]
    public async Task ResumingAPausedFight_DoesNotGenerateAnotherFreeFight()
    {
        await using var context = CreateContext();
        var (tournamentId, tatamiId) = await SeedEmptyModeAsync(context);
        var now = DateTimeOffset.UtcNow;
        var regularId = Guid.NewGuid();
        context.Fights.Add(new FightRecord
        {
            Id = regularId,
            TournamentId = tournamentId,
            BracketType = "Main",
            Status = FightStatus.Pending.ToString(),
            TatamiId = tatamiId,
            WhiteAthleteId = Guid.NewGuid(),
            BlueAthleteId = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await context.SaveChangesAsync();

        var modeService = CreateService(context);
        var match = CreateMatchService(context, modeService);
        Assert.Equal(MatchActionResult.Success, await match.StartAsync(regularId, "Tisch 1", CancellationToken.None));
        Assert.Equal(MatchActionResult.Success, await match.PauseAsync(regularId, "Tisch 1", CancellationToken.None));
        Assert.Equal(MatchActionResult.Success, await match.ResumeAsync(regularId, "Tisch 1", CancellationToken.None));

        Assert.Equal(1, await context.Fights.CountAsync(f => f.Kind == FreeFightRules.FreeKind));
    }

    [Fact]
    public async Task DisablingTheMode_LeavesExistingGeneratedFightAndStopsReplenishment()
    {
        await using var context = CreateContext();
        var (tournamentId, tatamiId) = await SeedEmptyModeAsync(context);
        var modeService = CreateService(context);
        await modeService.EnsureNextAsync(tournamentId, tatamiId, CancellationToken.None);

        var disabled = await modeService.SetEnabledAsync(
            tournamentId,
            tatamiId,
            false,
            "Tisch 1",
            CancellationToken.None);
        Assert.Equal(FreeFightModeResultStatus.Success, disabled.Status);
        Assert.False(disabled.Tatami!.FreeFightModeEnabled);

        var fight = await context.Fights.SingleAsync();
        var match = CreateMatchService(context, modeService);
        Assert.Equal(MatchActionResult.Success, await match.StartAsync(fight.Id, "Tisch 1", CancellationToken.None));
        Assert.Equal(1, await context.Fights.CountAsync(f => f.Kind == FreeFightRules.FreeKind));
    }

    private static async Task<(Guid TournamentId, Guid TatamiId)> SeedEmptyModeAsync(AppDbContext context)
    {
        var now = DateTimeOffset.UtcNow;
        var tournamentId = Guid.NewGuid();
        var tatamiId = Guid.NewGuid();
        context.Tournaments.Add(new TournamentRecord
        {
            Id = tournamentId,
            Name = "Testturnier",
            Date = new DateOnly(2026, 10, 16),
            Venue = "Halle",
            Organizer = "Verein",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        context.Tatamis.Add(new TatamiRecord
        {
            Id = tatamiId,
            TournamentId = tournamentId,
            Name = "Tatami 1",
            IsActive = true,
            IsFreeFightModeEnabled = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await context.SaveChangesAsync();
        return (tournamentId, tatamiId);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var context = new AppDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        return context;
    }

    private static FreeFightModeService CreateService(AppDbContext context)
    {
        var hub = new Mock<IHubContext<TournamentHub>>();
        var clients = new Mock<IHubClients>();
        var proxy = new Mock<IClientProxy>();
        hub.Setup(x => x.Clients).Returns(clients.Object);
        clients.Setup(x => x.Group(It.IsAny<string>())).Returns(proxy.Object);
        proxy.Setup(x => x.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new FreeFightModeService(
            context,
            new AuditLogService(context, NullLogger<AuditLogService>.Instance),
            hub.Object);
    }

    private static MatchService CreateMatchService(AppDbContext context, IFreeFightModeService modeService)
    {
        var hub = new Mock<IHubContext<TournamentHub>>();
        var clients = new Mock<IHubClients>();
        var proxy = new Mock<IClientProxy>();
        hub.Setup(x => x.Clients).Returns(clients.Object);
        clients.Setup(x => x.Group(It.IsAny<string>())).Returns(proxy.Object);
        proxy.Setup(x => x.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new MatchService(
            context,
            new AuditLogService(context, NullLogger<AuditLogService>.Instance),
            hub.Object,
            new Mock<IBracketService>().Object,
            new Mock<IRankingService>().Object,
            NullLogger<MatchService>.Instance,
            modeService);
    }
}
