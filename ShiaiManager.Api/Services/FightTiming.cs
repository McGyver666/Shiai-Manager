using ShiaiManager.Api.Data;

namespace ShiaiManager.Api.Services;

/// <summary>
/// Clock settings of a fight: a Freikampf carries its own, a regular fight uses its category's (ADR-0013).
/// </summary>
/// <param name="MatchDurationSeconds">Regular fight time in seconds.</param>
/// <param name="GoldenScoreEnabled">Whether a golden score follows a tie at the end of regular time.</param>
/// <param name="GoldenScoreDurationSeconds">Golden score duration in seconds.</param>
public readonly record struct FightTiming(int MatchDurationSeconds, bool GoldenScoreEnabled, int GoldenScoreDurationSeconds)
{
    /// <summary>Default regular fight time of a new Freikampf.</summary>
    public const int DefaultFreeFightMatchDurationSeconds = 240;

    /// <summary>Default golden score duration of a new Freikampf.</summary>
    public const int DefaultGoldenScoreDurationSeconds = 180;

    /// <summary>Clock settings stored on a Freikampf, falling back to the defaults for missing values.</summary>
    public static FightTiming ForFreeFight(FightRecord fight) =>
        new(
            fight.MatchDurationSeconds ?? DefaultFreeFightMatchDurationSeconds,
            fight.GoldenScoreEnabled ?? false,
            fight.GoldenScoreDurationSeconds ?? DefaultGoldenScoreDurationSeconds);

    /// <summary>Clock settings of the fight; null for a regular fight whose category is unknown.</summary>
    public static FightTiming? Resolve(FightRecord fight, CategoryRecord? category)
    {
        if (fight.IsFree) return ForFreeFight(fight);

        return category is null
            ? null
            : new FightTiming(category.MatchDurationSeconds, category.GoldenScoreEnabled, category.GoldenScoreDurationSeconds);
    }
}
