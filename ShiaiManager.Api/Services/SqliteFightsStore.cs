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

        var seriesBouts = await BestOfThreeSeries.LoadBoutsAsync(_dbContext, [categoryId], cancellationToken);
        return records
            .Select(r => FightMapper.ToModel(r) with { Series = BestOfThreeSeries.DescribeOrNull(r, seriesBouts) })
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<Fight?> GetByIdAsync(Guid fightId, CancellationToken cancellationToken)
    {
        var record = await _dbContext.Fights
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fightId, cancellationToken);
        if (record is null) return null;

        var seriesBouts = await BestOfThreeSeries.LoadBoutsAsync(
            _dbContext, record.CategoryId is { } categoryId ? [categoryId] : [], cancellationToken);
        return FightMapper.ToModel(record) with { Series = BestOfThreeSeries.DescribeOrNull(record, seriesBouts) };
    }
}
