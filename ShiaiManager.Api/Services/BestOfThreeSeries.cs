using Microsoft.EntityFrameworkCore;
using ShiaiManager.Api.Data;
using ShiaiManager.Api.Models;

namespace ShiaiManager.Api.Services;

/// <summary>
/// Rules of a Best-of-Three Kampfserie (ADR-0012), shared by draw, match control, rankings and DTO mapping.
/// All bouts of a series are main-bracket fights 1/1, 2/1 and 3/1 between the same White and Blue athlete.
/// </summary>
internal static class BestOfThreeSeries
{
    /// <summary>Round of the Entscheidungskampf.</summary>
    public const int DecidingRound = 3;

    private const int WinsNeeded = 2;
    private static readonly string FormatName = BracketFormat.BestOfThree.ToString();
    private static readonly string Completed = FightStatus.Completed.ToString();

    /// <summary>True when the stored draw format of a category is Best of Three.</summary>
    public static bool IsSeries(string? drawFormat) => drawFormat == FormatName;

    /// <summary>Counts the completed bouts won by <paramref name="athleteId"/>.</summary>
    public static int WinsOf(IEnumerable<FightRecord> bouts, Guid? athleteId) =>
        athleteId is null ? 0 : bouts.Count(b => b.Status == Completed && b.WinnerId == athleteId);

    /// <summary>True when bouts 1 and 2 are completed with different winners (Serienstand 1:1).</summary>
    public static bool NeedsDecidingFight(IReadOnlyCollection<FightRecord> bouts)
    {
        var first = bouts.FirstOrDefault(b => b.Round == 1);
        var second = bouts.FirstOrDefault(b => b.Round == 2);
        return first is { WinnerId: not null } && first.Status == Completed
            && second is { WinnerId: not null } && second.Status == Completed
            && first.WinnerId != second.WinnerId;
    }

    /// <summary>Returns the athlete who has won two bouts, or null while the series is open.</summary>
    public static Guid? WinnerOf(IReadOnlyCollection<FightRecord> bouts)
    {
        var reference = bouts.FirstOrDefault();
        if (reference is null) return null;
        if (WinsOf(bouts, reference.WhiteAthleteId) >= WinsNeeded) return reference.WhiteAthleteId;
        if (WinsOf(bouts, reference.BlueAthleteId) >= WinsNeeded) return reference.BlueAthleteId;
        return null;
    }

    /// <summary>Returns the athlete who lost the series, or null while the series is open.</summary>
    public static Guid? LoserOf(IReadOnlyCollection<FightRecord> bouts)
    {
        if (WinnerOf(bouts) is not { } winner) return null;
        var reference = bouts.First();
        return reference.WhiteAthleteId == winner ? reference.BlueAthleteId : reference.WhiteAthleteId;
    }

    /// <summary>Describes the position of <paramref name="bout"/> and the current Serienstand.</summary>
    public static FightSeries Describe(FightRecord bout, IReadOnlyCollection<FightRecord> bouts) =>
        new(bout.Round,
            bout.Round == DecidingRound,
            WinsOf(bouts, bout.WhiteAthleteId),
            WinsOf(bouts, bout.BlueAthleteId));

    /// <summary>Describes <paramref name="bout"/> when its category is a Kampfserie, otherwise null.</summary>
    public static FightSeries? DescribeOrNull(FightRecord bout, IReadOnlyDictionary<Guid, List<FightRecord>> boutsByCategory) =>
        bout.CategoryId is { } categoryId && boutsByCategory.TryGetValue(categoryId, out var bouts)
            ? Describe(bout, bouts)
            : null;

    /// <summary>
    /// Loads all bouts of the Best-of-Three categories among <paramref name="categoryIds"/>, keyed by category;
    /// categories with another draw format are absent.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, List<FightRecord>>> LoadBoutsAsync(
        AppDbContext dbContext,
        IEnumerable<Guid> categoryIds,
        CancellationToken cancellationToken)
    {
        var ids = categoryIds.Distinct().ToList();
        var seriesCategoryIds = await dbContext.Categories
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id) && c.DrawFormat == FormatName)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
        if (seriesCategoryIds.Count == 0) return new Dictionary<Guid, List<FightRecord>>();

        var bouts = await dbContext.Fights
            .AsNoTracking()
            .Where(f => f.CategoryId != null && seriesCategoryIds.Contains(f.CategoryId.Value))
            .ToListAsync(cancellationToken);
        return bouts.WithCategory()
            .GroupBy(x => x.CategoryId, x => x.Fight)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>Creates a pending bout of a Kampfserie between the series' White and Blue athlete.</summary>
    public static FightRecord CreateBout(
        Guid tournamentId,
        Guid categoryId,
        int round,
        Guid? whiteAthleteId,
        Guid? blueAthleteId,
        Guid? tatamiId,
        DateTimeOffset utcNow) =>
        new()
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            CategoryId = categoryId,
            BracketType = FightBracketType.Main.ToString(),
            Round = round,
            FightNumber = 1,
            WhiteAthleteId = whiteAthleteId,
            BlueAthleteId = blueAthleteId,
            TatamiId = tatamiId,
            Status = FightStatus.Pending.ToString(),
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow
        };
}
