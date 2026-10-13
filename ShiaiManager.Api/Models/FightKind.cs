namespace ShiaiManager.Api.Models;

/// <summary>
/// Distinguishes official tournament fights from Freikämpfe (ADR-0013).
/// </summary>
public enum FightKind
{
    /// <summary>Official fight of a category's draw or a team encounter.</summary>
    Regular,

    /// <summary>Freikampf: spontaneous fight outside the official schedule without a category.</summary>
    Free
}
