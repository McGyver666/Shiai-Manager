using ShiaiManager.Api.Data;
using ShiaiManager.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ShiaiManager.Api.Services;

/// <summary>
/// SQLite-backed fight queue for a tatami (F-01).
/// Includes all active/assigned fights for the tatami, including pending fights with unresolved
/// participants (n.n.), while keeping start-ready fights ahead of unresolved ones.
/// </summary>
public sealed class TatamiQueueService : ITatamiQueueService
{
    private readonly AppDbContext _dbContext;

    private static readonly string InProgress = FightStatus.InProgress.ToString();
    private static readonly string Paused = FightStatus.Paused.ToString();
    private static readonly string Pending = FightStatus.Pending.ToString();

    /// <summary>Initializes a new service instance.</summary>
    public TatamiQueueService(AppDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<TatamiQueue?> GetQueueAsync(
        Guid tournamentId,
        Guid tatamiId,
        CancellationToken cancellationToken)
    {
        var tatami = await _dbContext.Tatamis
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tatamiId && t.TournamentId == tournamentId, cancellationToken);
        if (tatami is null) return null;

        var records = await _dbContext.Fights
            .AsNoTracking()
            .Include(f => f.Category)
            .Where(f => f.TournamentId == tournamentId && f.TatamiId == tatamiId)
            .ToListAsync(cancellationToken);

        var seriesBouts = await BestOfThreeSeries.LoadBoutsAsync(
            _dbContext, records.Select(f => f.CategoryId).OfType<Guid>(), cancellationToken);

        Fight Map(FightRecord r) => MapToModel(r) with { Series = BestOfThreeSeries.DescribeOrNull(r, seriesBouts) };

        // In-progress fights first, then ready pending fights. Pending fights honor a manual
        // queue order (QueueOrder) when set, falling back to bracket order (Round, FightNumber).
        var playable = records
            .Where(IsQueueVisible)
            .OrderBy(f => f.Status == InProgress ? 0 : f.Status == Paused ? 1 : 2)
            .ThenBy(f => f.Status == Pending && !IsReadyToStart(f) ? 1 : 0)
            .ThenBy(f => f.QueueOrder ?? int.MaxValue)
            .ThenBy(f => f.Round)
            .ThenBy(f => f.FightNumber)
            .Select(Map)
            .ToList();

        return new TatamiQueue(
            tatami.Id,
            tatami.Name,
            playable.ElementAtOrDefault(0),
            playable.ElementAtOrDefault(1),
            playable.ElementAtOrDefault(2),
            playable);
    }

    private static bool IsQueueVisible(FightRecord f) =>
        f.Status == InProgress
        || f.Status == Paused
        || (f.Status == Pending && !f.IsBye);

    private static bool IsReadyToStart(FightRecord f) =>
        f.Status == Pending
        && !f.IsBye
        && TatamiQueueRules.HasBothSides(f);

    private static Fight MapToModel(FightRecord r) =>
        new(r.Id,
            r.TournamentId,
            r.CategoryId,
            Enum.Parse<FightBracketType>(r.BracketType),
            r.Round,
            r.FightNumber,
            r.PoolNumber,
            r.WhiteSourceFightId,
            r.WhiteSourceOutcome is null ? null : Enum.Parse<FightSlotSourceOutcome>(r.WhiteSourceOutcome),
            r.BlueSourceFightId,
            r.BlueSourceOutcome is null ? null : Enum.Parse<FightSlotSourceOutcome>(r.BlueSourceOutcome),
            r.WhiteAthleteId,
            r.BlueAthleteId,
            r.WinnerId,
            r.IsBye,
            Enum.Parse<FightStatus>(r.Status),
            r.TatamiId,
            r.QueueOrder,
            r.WhiteScore,
            r.BlueScore,
            r.WhitePenalties,
            r.BluePenalties,
            r.WhiteIpponCount,
            r.WhiteWazaAriCount,
            r.WhiteYukoCount,
            r.BlueIpponCount,
            r.BlueWazaAriCount,
            r.BlueYukoCount,
            r.PausedAtUtc,
            r.OsaeKomiSide,
            r.OsaeKomiStartedAtUtc,
            r.OsaeKomiPausedAtUtc,
            r.OsaeKomiElapsedMilliseconds,
            r.StartedAtUtc,
            r.CompletedAtUtc,
            r.CreatedAtUtc,
            r.UpdatedAtUtc,
            IsGoldenScore: GoldenScoreRules.IsActive(r, r.Category, DateTimeOffset.UtcNow),
            FreeFight: FreeFightRules.DescribeOrNull(r));
}
