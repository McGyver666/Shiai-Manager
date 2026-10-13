using System.Text.Json.Serialization;

namespace ShiaiManager.Api.Data;

/// <summary>
/// Persistence model for a single fight within a bracket.
/// </summary>
public sealed class FightRecord
{
    /// <summary>Unique fight identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning tournament.</summary>
    public Guid TournamentId { get; set; }

    /// <summary>Fight kind stored as <see cref="Models.FightKind"/> member name.</summary>
    public string Kind { get; set; } = nameof(Models.FightKind.Regular);

    /// <summary>Category this fight belongs to; null for a Freikampf (ADR-0013).</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>Bracket type stored as enum member name ("Main" or "Repechage").</summary>
    public string BracketType { get; set; } = string.Empty;

    /// <summary>Round number (1 = first round, highest = final).</summary>
    public int Round { get; set; }

    /// <summary>1-based position within the round.</summary>
    public int FightNumber { get; set; }

    /// <summary>Pool/group number for group-stage fights in a round-robin-with-knockout bracket; null for all other fight types.</summary>
    public int? PoolNumber { get; set; }

    /// <summary>Source fight for the White slot; null when the slot is assigned directly.</summary>
    public Guid? WhiteSourceFightId { get; set; }

    /// <summary>Outcome selected from the White slot source fight; null when no source exists.</summary>
    public string? WhiteSourceOutcome { get; set; }

    /// <summary>Source fight for the Blue slot; null when the slot is assigned directly.</summary>
    public Guid? BlueSourceFightId { get; set; }

    /// <summary>Outcome selected from the Blue slot source fight; null when no source exists.</summary>
    public string? BlueSourceOutcome { get; set; }

    /// <summary>Athlete in the White role; null when the slot is TBD.</summary>
    public Guid? WhiteAthleteId { get; set; }

    /// <summary>Athlete in the Blue role; null for a bye or when TBD.</summary>
    public Guid? BlueAthleteId { get; set; }

    /// <summary>Winner of this fight; null until the fight is completed.</summary>
    public Guid? WinnerId { get; set; }

    /// <summary>Free-text name of the White side of a Freikampf; null when an athlete is selected.</summary>
    public string? WhiteName { get; set; }

    /// <summary>Free-text name of the Blue side of a Freikampf; null when an athlete is selected.</summary>
    public string? BlueName { get; set; }

    /// <summary>Regular fight time of a Freikampf in seconds; null for regular fights.</summary>
    public int? MatchDurationSeconds { get; set; }

    /// <summary>Whether a Freikampf has golden score; null for regular fights.</summary>
    public bool? GoldenScoreEnabled { get; set; }

    /// <summary>Golden score duration of a Freikampf in seconds; null for regular fights.</summary>
    public int? GoldenScoreDurationSeconds { get; set; }

    /// <summary>Winning side of a Freikampf ("White" or "Blue"); null until completed or for Hikiwake.</summary>
    public string? WinnerSide { get; set; }

    /// <summary>True when one slot is a bye; the other athlete auto-advances.</summary>
    public bool IsBye { get; set; }

    /// <summary>Fight status stored as enum member name.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Tatami this fight is assigned to; null when unassigned.</summary>
    public Guid? TatamiId { get; set; }

    /// <summary>
    /// Manual position of the fight within its tatami's pending queue; null when the fight
    /// has not been manually reordered and falls back to bracket order (Round, FightNumber).
    /// </summary>
    public int? QueueOrder { get; set; }

    /// <summary>Accumulated score for the White athlete.</summary>
    public int WhiteScore { get; set; }

    /// <summary>Accumulated score for the Blue athlete.</summary>
    public int BlueScore { get; set; }

    /// <summary>Number of penalties (Shido) recorded for the White athlete.</summary>
    public int WhitePenalties { get; set; }

    /// <summary>Number of penalties (Shido) recorded for the Blue athlete.</summary>
    public int BluePenalties { get; set; }

    /// <summary>Number of Ippon scores recorded for the White athlete.</summary>
    public int WhiteIpponCount { get; set; }

    /// <summary>Number of Waza-ari scores recorded for the White athlete.</summary>
    public int WhiteWazaAriCount { get; set; }

    /// <summary>Number of Yuko scores recorded for the White athlete.</summary>
    public int WhiteYukoCount { get; set; }

    /// <summary>Number of Ippon scores recorded for the Blue athlete.</summary>
    public int BlueIpponCount { get; set; }

    /// <summary>Number of Waza-ari scores recorded for the Blue athlete.</summary>
    public int BlueWazaAriCount { get; set; }

    /// <summary>Number of Yuko scores recorded for the Blue athlete.</summary>
    public int BlueYukoCount { get; set; }

    /// <summary>When the fight was paused, if applicable.</summary>
    public DateTimeOffset? PausedAtUtc { get; set; }

    /// <summary>Which side currently has an active osae-komi hold, if any.</summary>
    public string? OsaeKomiSide { get; set; }

    /// <summary>Timestamp when the active osae-komi hold started, if any.</summary>
    public DateTimeOffset? OsaeKomiStartedAtUtc { get; set; }

    /// <summary>Timestamp when the osae-komi hold was paused, if any.</summary>
    public DateTimeOffset? OsaeKomiPausedAtUtc { get; set; }

    /// <summary>Accumulated osae-komi duration in milliseconds before the current segment.</summary>
    public long OsaeKomiElapsedMilliseconds { get; set; }

    /// <summary>Timestamp when the fight was started; null while pending.</summary>
    public DateTimeOffset? StartedAtUtc { get; set; }

    /// <summary>Timestamp when the fight was completed; null until a winner is confirmed.</summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>Creation timestamp in UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Last update timestamp in UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>Navigation property to the owning tournament.</summary>
    [JsonIgnore]
    public TournamentRecord? Tournament { get; set; }

    /// <summary>Navigation property to the category.</summary>
    [JsonIgnore]
    public CategoryRecord? Category { get; set; }
}
