using ShiaiManager.Api.Data;
using ShiaiManager.Api.Hubs;
using ShiaiManager.Api.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ShiaiManager.Api.Services;

/// <summary>
/// Persists and applies the per-tatami automatic Freikampf mode.
/// </summary>
public sealed class FreeFightModeService : IFreeFightModeService
{
    private static readonly string Pending = FightStatus.Pending.ToString();
    private static readonly string InProgress = FightStatus.InProgress.ToString();
    private static readonly string Paused = FightStatus.Paused.ToString();

    private readonly AppDbContext _dbContext;
    private readonly IAuditLogService _auditLog;
    private readonly IHubContext<TournamentHub> _hub;

    /// <summary>Initializes a new service instance.</summary>
    public FreeFightModeService(
        AppDbContext dbContext,
        IAuditLogService auditLog,
        IHubContext<TournamentHub> hub)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(auditLog);
        ArgumentNullException.ThrowIfNull(hub);
        _dbContext = dbContext;
        _auditLog = auditLog;
        _hub = hub;
    }

    /// <inheritdoc />
    public async Task<FreeFightModeResult> SetEnabledAsync(
        Guid tournamentId,
        Guid tatamiId,
        bool enabled,
        string user,
        CancellationToken cancellationToken)
    {
        var tatami = await _dbContext.Tatamis
            .FirstOrDefaultAsync(t => t.Id == tatamiId && t.TournamentId == tournamentId, cancellationToken);
        if (tatami is null) return new(FreeFightModeResultStatus.TatamiNotFound);
        if (enabled && !tatami.IsActive) return new(FreeFightModeResultStatus.TatamiInactive);

        var changed = tatami.IsFreeFightModeEnabled != enabled;
        if (changed)
        {
            tatami.IsFreeFightModeEnabled = enabled;
            tatami.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        var generated = enabled && await EnsureNextCoreAsync(tournamentId, tatami, cancellationToken);
        if (changed || generated)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        if (changed)
        {
            var action = enabled ? "FreeFightModeEnabled" : "FreeFightModeDisabled";
            await _auditLog.LogAsync(
                tournamentId,
                user,
                action,
                "Tatami",
                tatami.Id,
                $"TatamiId={tatami.Id}; Enabled={enabled}",
                cancellationToken);
        }

        if (changed || generated)
        {
            await NotifyQueueChangedAsync(tournamentId, tatamiId);
        }

        return new(FreeFightModeResultStatus.Success, MapToModel(tatami));
    }

    /// <inheritdoc />
    public async Task<bool> EnsureNextAsync(
        Guid tournamentId,
        Guid tatamiId,
        CancellationToken cancellationToken)
    {
        var tatami = await _dbContext.Tatamis
            .FirstOrDefaultAsync(t => t.Id == tatamiId && t.TournamentId == tournamentId, cancellationToken);
        if (tatami is null) return false;

        var generated = await EnsureNextCoreAsync(tournamentId, tatami, cancellationToken);
        if (!generated) return false;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await NotifyQueueChangedAsync(tournamentId, tatamiId);
        return true;
    }

    private async Task<bool> EnsureNextCoreAsync(
        Guid tournamentId,
        TatamiRecord tatami,
        CancellationToken cancellationToken)
    {
        if (!tatami.IsActive || !tatami.IsFreeFightModeEnabled) return false;

        var pending = await TatamiQueueRules.PendingOnTatami(
                _dbContext.Fights,
                tournamentId,
                tatami.Id)
            .ToListAsync(cancellationToken);
        var ordered = TatamiQueueRules.InQueueOrder(pending);
        var running = await _dbContext.Fights.AnyAsync(
            f => f.TournamentId == tournamentId
                && f.TatamiId == tatami.Id
                && (f.Status == InProgress || f.Status == Paused),
            cancellationToken);
        var placeNextIndex = TatamiQueueRules.PlaceNextIndex(ordered, running);

        if (TatamiQueueRules.AtPlaceNext(ordered, running)?.IsFree == true)
        {
            return false;
        }

        var tournament = await _dbContext.Tournaments
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tournamentId, cancellationToken);
        if (tournament is null) return false;

        var startedFreeFights = await _dbContext.Fights
            .AsNoTracking()
            .Where(f => f.TournamentId == tournamentId
                && f.TatamiId == tatami.Id
                && f.Kind == FreeFightRules.FreeKind
                && f.StartedAtUtc != null)
            .ToListAsync(cancellationToken);
        var lastStartedFreeFight = startedFreeFights
            .OrderByDescending(f => f.StartedAtUtc)
            .FirstOrDefault();
        var timing = lastStartedFreeFight is null
            ? new FightTiming(
                FightTiming.DefaultFreeFightMatchDurationSeconds,
                false,
                FightTiming.DefaultGoldenScoreDurationSeconds)
            : FightTiming.ForFreeFight(lastStartedFreeFight);
        var now = DateTimeOffset.UtcNow;
        var fight = new FightRecord
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            Kind = FreeFightRules.FreeKind,
            CategoryId = null,
            BracketType = nameof(FightBracketType.Main),
            Status = Pending,
            TatamiId = tatami.Id,
            WhiteName = "Weiß",
            BlueName = string.Equals(tournament.AccentSideColor, "Red", StringComparison.OrdinalIgnoreCase)
                ? "Rot"
                : "Blau",
            MatchDurationSeconds = timing.MatchDurationSeconds,
            GoldenScoreEnabled = timing.GoldenScoreEnabled,
            GoldenScoreDurationSeconds = timing.GoldenScoreDurationSeconds,
            QueueOrder = placeNextIndex,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        ordered.Insert(placeNextIndex, fight);
        TatamiQueueRules.Renumber(ordered, now);
        _dbContext.Fights.Add(fight);
        return true;
    }

    private Task NotifyQueueChangedAsync(Guid tournamentId, Guid tatamiId) =>
        _hub.Clients.Group(tournamentId.ToString())
            .SendAsync(
                "TatamiQueueChanged",
                new { tournamentId, tatamiId },
                CancellationToken.None);

    private static Tatami MapToModel(TatamiRecord record) =>
        new(
            record.Id,
            record.TournamentId,
            record.Name,
            record.DisplayOrder,
            record.IsActive,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.IsFreeFightModeEnabled);
}
