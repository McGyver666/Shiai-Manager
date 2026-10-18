namespace ShiaiManager.Api.Models;

/// <summary>
/// Snapshot of the fight queue for a single tatami.
/// </summary>
/// <param name="TatamiId">Tatami identifier.</param>
/// <param name="TatamiName">Display name of the tatami.</param>
/// <param name="Current">Fight currently in progress, or the next ready fight when none is running.</param>
/// <param name="Next">The fight after the current one.</param>
/// <param name="OnDeck">The fight after the next one.</param>
/// <param name="Upcoming">All playable fights assigned to this tatami, in queue order.</param>
/// <param name="FreeFightModeEnabled">Whether automatic Freikämpfe are enabled for this tatami.</param>
public sealed record TatamiQueue(
    Guid TatamiId,
    string TatamiName,
    Fight? Current,
    Fight? Next,
    Fight? OnDeck,
    IReadOnlyList<Fight> Upcoming,
    bool FreeFightModeEnabled = false);
