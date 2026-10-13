using System.ComponentModel.DataAnnotations;

namespace ShiaiManager.Api.Contracts;

/// <summary>
/// Request payload for confirming the winner of a fight.
/// </summary>
public sealed record ConfirmResultRequest
{
    /// <summary>Identifier of the winning athlete; null records a Hiki-wake for team-matchday fights and Freikämpfe.</summary>
    public Guid? WinnerId { get; init; }

    /// <summary>Winning side of a Freikampf ("white" or "blue"); takes precedence over <see cref="WinnerId"/>.</summary>
    [MaxLength(10)]
    public string? WinnerSide { get; init; }
}
