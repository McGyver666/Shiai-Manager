namespace ShiaiManager.Api.Models;

/// <summary>
/// Represents a ranked placement within a competition category.
/// </summary>
/// <param name="Place">Placement (1 = gold, 2 = silver, 3 = bronze).</param>
/// <param name="AthleteId">Athlete identifier.</param>
/// <param name="AthleteName">Full name of the athlete (last name, first name).</param>
/// <param name="ClubName">Name of the athlete's club.</param>
/// <param name="SeriesWins">Won bouts of the athlete in a Best-of-Three Kampfserie; null for other formats.</param>
/// <param name="SeriesLosses">Lost bouts of the athlete in a Best-of-Three Kampfserie; null for other formats.</param>
public sealed record RankingEntry(
    int Place,
    Guid AthleteId,
    string AthleteName,
    string ClubName,
    int? SeriesWins = null,
    int? SeriesLosses = null);
