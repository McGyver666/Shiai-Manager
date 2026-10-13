namespace ShiaiManager.Api.Models;

/// <summary>
/// Side of a fight; a Freikampf records its winner per side (ADR-0013).
/// </summary>
public enum FightSide
{
    /// <summary>White side.</summary>
    White,

    /// <summary>Accent side (blue or red, depending on the tournament's accent colour).</summary>
    Blue
}
