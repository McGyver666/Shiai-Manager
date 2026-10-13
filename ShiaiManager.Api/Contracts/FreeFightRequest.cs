using System.ComponentModel.DataAnnotations;
using ShiaiManager.Api.Models;
using ShiaiManager.Api.Services;

namespace ShiaiManager.Api.Contracts;

/// <summary>
/// One side of a Freikampf: an athlete of the tournament or a free-text name.
/// </summary>
public sealed record FreeFightSideRequest
{
    /// <summary>Selected athlete of the tournament; null for a free-text side.</summary>
    public Guid? AthleteId { get; init; }

    /// <summary>Free-text name, e.g. "Weiß" or "Gast 1"; required when no athlete is selected.</summary>
    [MaxLength(FreeFightDetails.MaxNameLength, ErrorMessage = "Der Name darf maximal {1} Zeichen lang sein.")]
    // Control and invisible format characters (e.g. bidi overrides) would garble the scoreboard.
    [RegularExpression(@"^[^\p{Cc}\p{Cf}]*$", ErrorMessage = "Der Name enthält unzulässige Steuerzeichen.")]
    public string? Name { get; init; }

    /// <summary>Maps the request to the service input.</summary>
    public FreeFightSideInput ToInput() => new(AthleteId, Name);
}

/// <summary>
/// Request payload for editing a Freikampf (ADR-0013).
/// </summary>
public record FreeFightRequest
{
    /// <summary>White side.</summary>
    [Required(ErrorMessage = "Die weiße Seite ist erforderlich.")]
    public FreeFightSideRequest? White { get; init; }

    /// <summary>Accent (blue) side.</summary>
    [Required(ErrorMessage = "Die zweite Seite ist erforderlich.")]
    public FreeFightSideRequest? Blue { get; init; }

    /// <summary>Regular fight time in seconds (default 240 = 4 minutes).</summary>
    [Range(30, 3600, ErrorMessage = "Die Kampfdauer muss zwischen 30 und 3600 Sekunden liegen.")]
    public int MatchDurationSeconds { get; init; } = FightTiming.DefaultFreeFightMatchDurationSeconds;

    /// <summary>Whether a golden score follows a tie at the end of regular time.</summary>
    public bool GoldenScoreEnabled { get; init; }

    /// <summary>Golden score duration in seconds (default 180 = 3 minutes).</summary>
    [Range(30, 3600, ErrorMessage = "Die Golden-Score-Dauer muss zwischen 30 und 3600 Sekunden liegen.")]
    public int GoldenScoreDurationSeconds { get; init; } = FightTiming.DefaultGoldenScoreDurationSeconds;

    /// <summary>Maps the request to the service input.</summary>
    public FreeFightInput ToInput() =>
        new(White!.ToInput(), Blue!.ToInput(), new FightTiming(MatchDurationSeconds, GoldenScoreEnabled, GoldenScoreDurationSeconds));
}

/// <summary>
/// Request payload for adding a Freikampf to a tatami queue (ADR-0013).
/// </summary>
public sealed record CreateFreeFightRequest : FreeFightRequest
{
    /// <summary>
    /// True places the Freikampf directly behind the running or current fight; false appends it to the queue.
    /// </summary>
    public bool PlaceNext { get; init; }
}
