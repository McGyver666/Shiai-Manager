using ShiaiManager.Api.Data;
using ShiaiManager.Api.Models;

namespace ShiaiManager.Api.Services;

/// <summary>
/// Server-authoritative golden-score rule shared by the tatami queue, fight broadcasts and the clock evaluator.
/// </summary>
internal static class GoldenScoreRules
{
    private static readonly string InProgress = FightStatus.InProgress.ToString();
    private static readonly string Paused = FightStatus.Paused.ToString();

    /// <summary>
    /// True only when the fight is active (InProgress or Paused), golden score is enabled for the fight
    /// (category or Freikampf settings), the elapsed fight time has reached the regular match duration,
    /// and waza-ari and yuko counts are tied.
    /// </summary>
    public static bool IsActive(FightRecord fight, CategoryRecord? category, DateTimeOffset now)
    {
        if (fight.Status != InProgress && fight.Status != Paused) return false;
        if (fight.StartedAtUtc is null) return false;
        if (FightTiming.Resolve(fight, category) is not { GoldenScoreEnabled: true } timing) return false;

        var referenceTime = fight.OsaeKomiPausedAtUtc is not null
            ? fight.OsaeKomiPausedAtUtc.Value
            : fight.Status == Paused && fight.PausedAtUtc is not null
                ? fight.PausedAtUtc.Value
                : now;
        var elapsedSeconds = (referenceTime - fight.StartedAtUtc.Value).TotalSeconds;
        if (elapsedSeconds < timing.MatchDurationSeconds) return false;

        return IsTied(fight);
    }

    /// <summary>Golden score only applies when both sides have equal waza-ari and yuko counts.</summary>
    public static bool IsTied(FightRecord fight) =>
        fight.WhiteWazaAriCount == fight.BlueWazaAriCount
        && fight.WhiteYukoCount == fight.BlueYukoCount;
}
