using System.ComponentModel.DataAnnotations;
using ShiaiManager.Api.Models;

namespace ShiaiManager.Api.Contracts;

/// <summary>
/// Request payload for confirming the winner of a fight.
/// </summary>
public sealed record ConfirmResultRequest
{
    /// <summary>
    /// Winning athlete of a regular fight; null records a Hiki-wake for team-matchday fights. Not accepted for a Freikampf.
    /// </summary>
    public Guid? WinnerId { get; init; }

    /// <summary>Winning side of a Freikampf; null records a Hiki-wake. Only accepted for a Freikampf.</summary>
    [EnumDataType(typeof(FightSide), ErrorMessage = "Die Seite muss \"White\" oder \"Blue\" sein.")]
    public FightSide? WinnerSide { get; init; }
}
