namespace ShiaiManager.Api.Contracts;

/// <summary>Requested automatic Freikampf mode state for a tatami.</summary>
public sealed record UpdateFreeFightModeRequest(bool Enabled);
