namespace ShiaiManager.Api.Models;

/// <summary>
/// Freikampf-specific data of a fight (ADR-0013); null for regular fights.
/// </summary>
/// <param name="WhiteName">Free-text name of the White side; null when an athlete is selected.</param>
/// <param name="BlueName">Free-text name of the accent side; null when an athlete is selected.</param>
/// <param name="MatchDurationSeconds">Regular fight time in seconds.</param>
/// <param name="GoldenScoreEnabled">Whether a golden score follows a tie at the end of regular time.</param>
/// <param name="GoldenScoreDurationSeconds">Golden score duration in seconds.</param>
/// <param name="WinnerSide">Winning side ("White" or "Blue"); null until completed or for Hikiwake.</param>
public sealed record FreeFightDetails(
    string? WhiteName,
    string? BlueName,
    int MatchDurationSeconds,
    bool GoldenScoreEnabled,
    int GoldenScoreDurationSeconds,
    string? WinnerSide);
