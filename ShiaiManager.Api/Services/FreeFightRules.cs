using ShiaiManager.Api.Data;
using ShiaiManager.Api.Models;

namespace ShiaiManager.Api.Services;

/// <summary>
/// Shared Freikampf rules (ADR-0013): kind markers, the exclusion from official results and the mapping of Freikampf data.
/// </summary>
public static class FreeFightRules
{
    /// <summary>Stored kind of a regular fight.</summary>
    public static readonly string RegularKind = nameof(FightKind.Regular);

    /// <summary>Stored kind of a Freikampf.</summary>
    public static readonly string FreeKind = nameof(FightKind.Free);

    /// <summary>
    /// Official fights only. Every consumer of official results (rankings, club scoring, medals, statistics,
    /// fight overview) must use this filter so that Freikämpfe never count.
    /// </summary>
    public static IQueryable<FightRecord> OfficialOnly(this IQueryable<FightRecord> fights) =>
        fights.Where(f => f.Kind == RegularKind);

    /// <summary>Pairs each fight with its category; fights without one (Freikämpfe) are skipped.</summary>
    public static IEnumerable<(FightRecord Fight, Guid CategoryId)> WithCategory(this IEnumerable<FightRecord> fights)
    {
        foreach (var fight in fights)
        {
            if (fight.CategoryId is { } categoryId) yield return (fight, categoryId);
        }
    }

    /// <summary>Freikampf data of the fight; null for regular fights.</summary>
    public static FreeFightDetails? DescribeOrNull(FightRecord fight)
    {
        if (!fight.IsFree) return null;

        var timing = FightTiming.ForFreeFight(fight);
        return new FreeFightDetails(
            fight.WhiteName,
            fight.BlueName,
            timing.MatchDurationSeconds,
            timing.GoldenScoreEnabled,
            timing.GoldenScoreDurationSeconds,
            Enum.TryParse<FightSide>(fight.WinnerSide, out var winnerSide) ? winnerSide : null);
    }
}
