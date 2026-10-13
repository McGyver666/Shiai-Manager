using ShiaiManager.Api.Data;
using ShiaiManager.Api.Models;

namespace ShiaiManager.Api.Services;

/// <summary>
/// Shared Freikampf rules (ADR-0013): kind markers, limits and the mapping of Freikampf data.
/// </summary>
public static class FreeFightRules
{
    /// <summary>Maximum length of a free-text side name.</summary>
    public const int MaxNameLength = 60;

    /// <summary>Stored kind of a regular fight.</summary>
    public static readonly string RegularKind = nameof(FightKind.Regular);

    /// <summary>Stored kind of a Freikampf.</summary>
    public static readonly string FreeKind = nameof(FightKind.Free);

    /// <summary>True when the fight is a Freikampf.</summary>
    public static bool IsFree(FightRecord fight) => fight.Kind == FreeKind;

    /// <summary>Freikampf data of the fight; null for regular fights.</summary>
    public static FreeFightDetails? DescribeOrNull(FightRecord fight)
    {
        if (!IsFree(fight)) return null;

        var timing = FightTiming.Resolve(fight, category: null)!.Value;
        return new FreeFightDetails(
            fight.WhiteName,
            fight.BlueName,
            timing.MatchDurationSeconds,
            timing.GoldenScoreEnabled,
            timing.GoldenScoreDurationSeconds,
            fight.WinnerSide);
    }
}
