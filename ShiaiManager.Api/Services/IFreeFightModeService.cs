using ShiaiManager.Api.Models;

namespace ShiaiManager.Api.Services;

/// <summary>Outcome of changing the automatic Freikampf mode of a tatami.</summary>
public enum FreeFightModeResultStatus
{
    /// <summary>The requested state was applied.</summary>
    Success,

    /// <summary>The tatami does not belong to the requested tournament.</summary>
    TatamiNotFound,

    /// <summary>The mode cannot be enabled while the tatami is inactive.</summary>
    TatamiInactive
}

/// <summary>Result of a Freikampf mode update.</summary>
/// <param name="Status">Operation status.</param>
/// <param name="Tatami">The updated tatami on success.</param>
public sealed record FreeFightModeResult(FreeFightModeResultStatus Status, Tatami? Tatami = null);

/// <summary>
/// Maintains the persisted per-tatami Freikampf mode and its automatically generated queue entry.
/// </summary>
public interface IFreeFightModeService
{
    /// <summary>
    /// Enables or disables automatic Freikampf generation for a tatami.
    /// Enabling also ensures that the next queue position contains a ready Freikampf.
    /// </summary>
    Task<FreeFightModeResult> SetEnabledAsync(
        Guid tournamentId,
        Guid tatamiId,
        bool enabled,
        string user,
        CancellationToken cancellationToken);

    /// <summary>
    /// Ensures the next queue position contains a generated Freikampf when the mode is active.
    /// Returns true when a new fight was created.
    /// </summary>
    Task<bool> EnsureNextAsync(
        Guid tournamentId,
        Guid tatamiId,
        CancellationToken cancellationToken);
}
