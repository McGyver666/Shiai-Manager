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

        Fight Map(FightRecord r) =>
            FightMapper.ToModel(r, GoldenScoreRules.IsActive(r, r.Category, DateTimeOffset.UtcNow))
                with { Series = BestOfThreeSeries.DescribeOrNull(r, seriesBouts) };

        // Running fights first, then the pending fights in manual queue order (TatamiQueueRules).
        var running = records
            .Where(f => f.Status == InProgress || f.Status == Paused)
            .OrderBy(f => f.Status == InProgress ? 0 : 1)
            .ThenBy(f => f.QueueOrder ?? int.MaxValue)
            .ThenBy(f => f.Round)
            .ThenBy(f => f.FightNumber);
        var pending = TatamiQueueRules.InQueueOrder(records.Where(f => f.Status == Pending && !f.IsBye));
        var playable = running.Concat(pending).Select(Map).ToList();

        return new TatamiQueue(
            tatami.Id,
            tatami.Name,
            playable.ElementAtOrDefault(0),
            playable.ElementAtOrDefault(1),
            playable.ElementAtOrDefault(2),
            playable);
    }
}
