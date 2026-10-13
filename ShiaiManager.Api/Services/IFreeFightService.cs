using ShiaiManager.Api.Models;

namespace ShiaiManager.Api.Services;

/// <summary>One side of a Freikampf: an athlete of the tournament or a free-text name.</summary>
/// <param name="AthleteId">Selected athlete; null for a free-text side.</param>
/// <param name="Name">Free-text name; ignored when an athlete is selected.</param>
public sealed record FreeFightSideInput(Guid? AthleteId, string? Name);

/// <summary>Editable settings of a Freikampf.</summary>
/// <param name="White">White side.</param>
/// <param name="Blue">Accent (blue) side.</param>
/// <param name="Timing">Fight time and golden score settings.</param>
public sealed record FreeFightInput(FreeFightSideInput White, FreeFightSideInput Blue, FightTiming Timing);

/// <summary>Outcome status of a Freikampf operation.</summary>
public enum FreeFightResultStatus
{
    /// <summary>The operation completed successfully.</summary>
    Success,

    /// <summary>The tatami does not belong to the tournament.</summary>
    TatamiNotFound,

    /// <summary>No Freikampf with this id exists in the tournament.</summary>
    FightNotFound,

    /// <summary>A selected athlete does not belong to the tournament.</summary>
    AthleteNotFound,

    /// <summary>The same athlete was selected for both sides.</summary>
    SameAthleteOnBothSides,

    /// <summary>A side has neither an athlete nor a name.</summary>
    NameRequired,

    /// <summary>The Freikampf has already started and can no longer be edited or deleted.</summary>
    AlreadyStarted
}

/// <summary>Result of a Freikampf operation.</summary>
/// <param name="Status">Outcome status.</param>
/// <param name="Fight">The created or updated Freikampf on success; null otherwise and for deletions.</param>
public sealed record FreeFightResult(FreeFightResultStatus Status, Fight? Fight = null);

/// <summary>
/// Creates, edits and deletes Freikämpfe on a tatami (ADR-0013). Running and completing a Freikampf
/// uses the regular match control (<see cref="IMatchService"/>).
/// </summary>
public interface IFreeFightService
{
    /// <summary>Returns the Freikampf of the tournament; null when no Freikampf with this id exists.</summary>
    Task<Fight?> GetAsync(Guid tournamentId, Guid fightId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a pending Freikampf to the tatami queue, at its end or, with <paramref name="placeNext"/>,
    /// directly behind the running or current fight.
    /// </summary>
    Task<FreeFightResult> CreateAsync(
        Guid tournamentId,
        Guid tatamiId,
        FreeFightInput input,
        bool placeNext,
        string user,
        CancellationToken cancellationToken);

    /// <summary>Changes the sides and clock settings of a Freikampf that has not started yet.</summary>
    Task<FreeFightResult> UpdateAsync(
        Guid tournamentId,
        Guid fightId,
        FreeFightInput input,
        string user,
        CancellationToken cancellationToken);

    /// <summary>Deletes a Freikampf that has not started yet.</summary>
    Task<FreeFightResult> DeleteAsync(
        Guid tournamentId,
        Guid fightId,
        string user,
        CancellationToken cancellationToken);
}
