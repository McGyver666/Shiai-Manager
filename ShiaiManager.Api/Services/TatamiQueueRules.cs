using ShiaiManager.Api.Data;
using ShiaiManager.Api.Models;

namespace ShiaiManager.Api.Services;

/// <summary>
/// Shared ordering of the manual tatami queue, used by the queue view, queue reordering and Freikampf placement.
/// Start-ready fights come first; pending fights whose sides are still open (n.n.) follow.
/// </summary>
internal static class TatamiQueueRules
{
    private static readonly string Pending = FightStatus.Pending.ToString();

    /// <summary>Pending, non-bye fights on the tatami, including fights whose sides are still open.</summary>
    public static IQueryable<FightRecord> PendingOnTatami(IQueryable<FightRecord> fights, Guid tournamentId, Guid tatamiId) =>
        fights.Where(f => f.TournamentId == tournamentId
            && f.TatamiId == tatamiId
            && f.Status == Pending
            && !f.IsBye);

    /// <summary>True when both sides of the fight are known (athlete or Freikampf name).</summary>
    public static bool HasBothSides(FightRecord fight) =>
        (fight.WhiteAthleteId is not null || fight.WhiteName is not null)
        && (fight.BlueAthleteId is not null || fight.BlueName is not null);

    /// <summary>
    /// Orders pending fights: start-ready before open ones, then by manual queue order, falling back to bracket order.
    /// </summary>
    public static List<FightRecord> InQueueOrder(IEnumerable<FightRecord> pending) =>
        pending
            .OrderBy(f => HasBothSides(f) ? 0 : 1)
            .ThenBy(f => f.QueueOrder ?? int.MaxValue)
            .ThenBy(f => f.Round)
            .ThenBy(f => f.FightNumber)
            .ToList();

    /// <summary>
    /// Returns the insertion index used by the existing "place next" behavior.
    /// A running fight is followed by the first pending fight; without one, the current
    /// start-ready fight keeps its place and the new fight follows it.
    /// </summary>
    public static int PlaceNextIndex(IReadOnlyList<FightRecord> ordered, bool fightIsRunning) =>
        fightIsRunning ? 0 : Math.Min(1, ordered.Count(HasBothSides));

    /// <summary>
    /// Returns the fight occupying the automatic next slot. If no fight is running and the
    /// current fight is already a Freikampf, it is the slot maintained by the mode.
    /// </summary>
    public static FightRecord? AtPlaceNext(IReadOnlyList<FightRecord> ordered, bool fightIsRunning)
    {
        if (!fightIsRunning && ordered.ElementAtOrDefault(0)?.IsFree == true)
        {
            return ordered[0];
        }

        return ordered.ElementAtOrDefault(PlaceNextIndex(ordered, fightIsRunning));
    }

    /// <summary>Normalizes the manual queue order to 0..n-1 so the order is stable and gap-free.</summary>
    public static void Renumber(IReadOnlyList<FightRecord> ordered, DateTimeOffset now)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].QueueOrder != i)
            {
                ordered[i].QueueOrder = i;
                ordered[i].UpdatedAtUtc = now;
            }
        }
    }
}
