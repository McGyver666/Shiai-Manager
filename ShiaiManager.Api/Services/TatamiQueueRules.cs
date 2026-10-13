using ShiaiManager.Api.Data;
using ShiaiManager.Api.Models;

namespace ShiaiManager.Api.Services;

/// <summary>
/// Shared ordering of the manual tatami queue, used by queue reordering and Freikampf placement.
/// </summary>
internal static class TatamiQueueRules
{
    private static readonly string Pending = FightStatus.Pending.ToString();

    /// <summary>Pending, non-bye fights on the tatami whose both sides are known (athlete or Freikampf name).</summary>
    public static IQueryable<FightRecord> ReadyPending(IQueryable<FightRecord> fights, Guid tournamentId, Guid tatamiId) =>
        fights.Where(f => f.TournamentId == tournamentId
            && f.TatamiId == tatamiId
            && f.Status == Pending
            && !f.IsBye
            && (f.WhiteAthleteId != null || f.WhiteName != null)
            && (f.BlueAthleteId != null || f.BlueName != null));

    /// <summary>True when both sides of the fight are known (athlete or Freikampf name).</summary>
    public static bool HasBothSides(FightRecord fight) =>
        (fight.WhiteAthleteId is not null || fight.WhiteName is not null)
        && (fight.BlueAthleteId is not null || fight.BlueName is not null);

    /// <summary>Orders pending fights by manual queue order, falling back to bracket order.</summary>
    public static List<FightRecord> InQueueOrder(IEnumerable<FightRecord> pending) =>
        pending
            .OrderBy(f => f.QueueOrder ?? int.MaxValue)
            .ThenBy(f => f.Round)
            .ThenBy(f => f.FightNumber)
            .ToList();

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
