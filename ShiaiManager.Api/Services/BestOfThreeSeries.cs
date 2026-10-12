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

    /// <summary>Describes the position of <paramref name="bout"/> and the current Serienstand.</summary>
    public static FightSeries Describe(FightRecord bout, IReadOnlyCollection<FightRecord> bouts) =>
        new(bout.Round,
            bout.Round == DecidingRound,
            WinsOf(bouts, bout.WhiteAthleteId),
            WinsOf(bouts, bout.BlueAthleteId));
}
