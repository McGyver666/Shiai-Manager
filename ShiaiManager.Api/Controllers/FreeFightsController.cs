using ShiaiManager.Api.Contracts;
using ShiaiManager.Api.Models;
using ShiaiManager.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ShiaiManager.Api.Controllers;

/// <summary>
/// API endpoints for adding, editing and deleting Freikämpfe at the Mattentisch (ADR-0013).
/// Running a Freikampf uses the regular match endpoints.
/// </summary>
[ApiController]
[Authorize(Policy = "LiveFightOperation")]
[Route("api/tournaments/{tournamentId:guid}")]
public sealed class FreeFightsController : ControllerBase
{
    private readonly IFreeFightService _freeFightService;

    /// <summary>Initializes a new controller instance.</summary>
    public FreeFightsController(IFreeFightService freeFightService)
    {
        ArgumentNullException.ThrowIfNull(freeFightService);
        _freeFightService = freeFightService;
    }

    /// <summary>
    /// Adds a Freikampf to the tatami queue, at its end or directly behind the running or current fight.
    /// </summary>
    [HttpPost("tatamis/{tatamiId:guid}/free-fights")]
    [ProducesResponseType(typeof(Fight), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Fight>> CreateAsync(
        Guid tournamentId,
        Guid tatamiId,
        [FromBody] CreateFreeFightRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _freeFightService.CreateAsync(
            tournamentId, tatamiId, request.ToInput(), request.PlaceNext, CurrentUser(), cancellationToken);
        return result.Status == FreeFightResultStatus.Success
            ? Created($"/api/tournaments/{tournamentId}/free-fights/{result.Fight!.Id}", result.Fight)
            : MapFailure(result.Status);
    }

    /// <summary>
    /// Changes the sides and clock settings of a Freikampf that has not started yet.
    /// </summary>
    [HttpPut("free-fights/{fightId:guid}")]
    [ProducesResponseType(typeof(Fight), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Fight>> UpdateAsync(
        Guid tournamentId,
        Guid fightId,
        [FromBody] FreeFightRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _freeFightService.UpdateAsync(
            tournamentId, fightId, request.ToInput(), CurrentUser(), cancellationToken);
        return result.Status == FreeFightResultStatus.Success ? Ok(result.Fight) : MapFailure(result.Status);
    }

    /// <summary>
    /// Deletes a Freikampf that has not started yet.
    /// </summary>
    [HttpDelete("free-fights/{fightId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(
        Guid tournamentId,
        Guid fightId,
        CancellationToken cancellationToken)
    {
        var result = await _freeFightService.DeleteAsync(tournamentId, fightId, CurrentUser(), cancellationToken);
        return result.Status == FreeFightResultStatus.Success ? NoContent() : MapFailure(result.Status);
    }

    private ActionResult MapFailure(FreeFightResultStatus status) => status switch
    {
        FreeFightResultStatus.TatamiNotFound
            or FreeFightResultStatus.FightNotFound
            or FreeFightResultStatus.AthleteNotFound => NotFound(),
        FreeFightResultStatus.NameRequired =>
            this.LocalizedValidationProblem(nameof(FreeFightRequest.White), FreeFightMessages.NameRequired),
        FreeFightResultStatus.SameAthleteOnBothSides =>
            this.LocalizedValidationProblem(nameof(FreeFightRequest.Blue), FreeFightMessages.SameAthleteOnBothSides),
        FreeFightResultStatus.AlreadyStarted =>
            this.LocalizedConflict("Freikampf bereits gestartet.", FreeFightMessages.AlreadyStarted),
        _ => StatusCode(StatusCodes.Status500InternalServerError)
    };

    private string CurrentUser()
    {
        var user = User.Identity?.Name;
        return string.IsNullOrWhiteSpace(user) ? "unbekannt" : user;
    }
}
