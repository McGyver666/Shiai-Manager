namespace ShiaiManager.Api.Models;

/// <summary>
/// Position of a fight within a Kampfserie and the current Serienstand (won bouts per side).
/// </summary>
/// <param name="BoutNumber">1-based bout number within the series (3 = Entscheidungskampf).</param>
/// <param name="IsDecidingFight">True for the Entscheidungskampf.</param>
/// <param name="WhiteWins">Completed bouts of the series won by the White athlete.</param>
/// <param name="BlueWins">Completed bouts of the series won by the accent-side athlete.</param>
public sealed record FightSeries(int BoutNumber, bool IsDecidingFight, int WhiteWins, int BlueWins);
