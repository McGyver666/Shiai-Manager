using ShiaiManager.Api.Data;
using ShiaiManager.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ShiaiManager.Api.Services;

/// <summary>
/// SQLite-backed read access to fight records.
/// </summary>
public sealed class SqliteFightsStore : IFightsStore
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<SqliteFightsStore> _logger;

    /// <summary>Initializes a new store instance.</summary>
    public SqliteFightsStore(AppDbContext dbContext, ILogger<SqliteFightsStore> logger)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(logger);
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Fight>> GetAllAsync(
        Guid tournamentId,
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        var records = await _dbContext.Fights
            .AsNoTracking()
            .Where(f => f.TournamentId == tournamentId && f.CategoryId == categoryId)
            .OrderBy(f => f.BracketType)
            .ThenBy(f => f.Round)
            .ThenBy(f => f.FightNumber)
            .ToListAsync(cancellationToken);

        var isSeries = await IsSeriesCategoryAsync(categoryId, cancellationToken);
        return records
            .Select(r => MapToModel(r) with { Series = isSeries ? BestOfThreeSeries.Describe(r, records) : null })
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<Fight?> GetByIdAsync(Guid fightId, CancellationToken cancellationToken)
    {
        var record = await _dbContext.Fights
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fightId, cancellationToken);
        if (record is null) return null;

        if (!await IsSeriesCategoryAsync(record.CategoryId, cancellationToken)) return MapToModel(record);

        var bouts = await _dbContext.Fights
            .AsNoTracking()
            .Where(f => f.CategoryId == record.CategoryId)
            .ToListAsync(cancellationToken);
        return MapToModel(record) with { Series = BestOfThreeSeries.Describe(record, bouts) };
    }

    private async Task<bool> IsSeriesCategoryAsync(Guid categoryId, CancellationToken cancellationToken) =>
        BestOfThreeSeries.IsSeries(await _dbContext.Categories
            .AsNoTracking()
            .Where(c => c.Id == categoryId)
            .Select(c => c.DrawFormat)
            .FirstOrDefaultAsync(cancellationToken));

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
            IsGoldenScore: false);
}
