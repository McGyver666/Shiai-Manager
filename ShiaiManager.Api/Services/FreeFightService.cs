using ShiaiManager.Api.Data;
using ShiaiManager.Api.Hubs;
using ShiaiManager.Api.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ShiaiManager.Api.Services;

/// <summary>
/// SQLite-backed management of Freikämpfe (ADR-0013).
/// </summary>
public sealed class FreeFightService : IFreeFightService
{
    private static readonly string Pending = FightStatus.Pending.ToString();
    private static readonly string InProgress = FightStatus.InProgress.ToString();
    private static readonly string Paused = FightStatus.Paused.ToString();

    private readonly AppDbContext _dbContext;
    private readonly IAuditLogService _auditLog;
    private readonly IMatchService _matchService;
    private readonly IFightsStore _fightsStore;
    private readonly IHubContext<TournamentHub> _hub;

    /// <summary>Initializes a new service instance.</summary>
    public FreeFightService(
        AppDbContext dbContext,
        IAuditLogService auditLog,
        IMatchService matchService,
        IFightsStore fightsStore,
        IHubContext<TournamentHub> hub)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(auditLog);
        ArgumentNullException.ThrowIfNull(matchService);
        ArgumentNullException.ThrowIfNull(fightsStore);
        ArgumentNullException.ThrowIfNull(hub);
        _dbContext = dbContext;
        _auditLog = auditLog;
        _matchService = matchService;
        _fightsStore = fightsStore;
        _hub = hub;
    }

    /// <inheritdoc />
    public async Task<FreeFightResult> CreateAsync(
        Guid tournamentId,
        Guid tatamiId,
        FreeFightInput input,
        bool placeNext,
        string user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var tatamiExists = await _dbContext.Tatamis
            .AnyAsync(t => t.Id == tatamiId && t.TournamentId == tournamentId, cancellationToken);
        if (!tatamiExists) return new(FreeFightResultStatus.TatamiNotFound);

        var validation = await ValidateAsync(tournamentId, input, cancellationToken);
        if (validation != FreeFightResultStatus.Success) return new(validation);

        var now = DateTimeOffset.UtcNow;
        var fight = new FightRecord
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            Kind = FreeFightRules.FreeKind,
            CategoryId = null,
            BracketType = nameof(FightBracketType.Main),
            Status = Pending,
            TatamiId = tatamiId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        Apply(fight, input);

        var ordered = TatamiQueueRules.InQueueOrder(
            await TatamiQueueRules.ReadyPending(_dbContext.Fights, tournamentId, tatamiId).ToListAsync(cancellationToken));
        var insertIndex = ordered.Count;
        if (placeNext)
        {
            var fightIsRunning = await _dbContext.Fights.AnyAsync(
                f => f.TournamentId == tournamentId && f.TatamiId == tatamiId
                    && (f.Status == InProgress || f.Status == Paused),
                cancellationToken);
            // Without a running fight the first pending fight is the current one and keeps its place.
            insertIndex = fightIsRunning ? 0 : Math.Min(1, ordered.Count);
        }

        ordered.Insert(insertIndex, fight);
        TatamiQueueRules.Renumber(ordered, now);

        _dbContext.Fights.Add(fight);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLog.LogAsync(
            tournamentId, user, "FreeFightCreated", "Fight", fight.Id, AuditDetails(fight, placeNext), cancellationToken);
        await _matchService.NotifyFightUpdatedAsync(fight.Id, cancellationToken);

        return new(FreeFightResultStatus.Success, await _fightsStore.GetByIdAsync(fight.Id, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<FreeFightResult> UpdateAsync(
        Guid tournamentId,
        Guid fightId,
        FreeFightInput input,
        string user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var fight = await FindFreeFightAsync(tournamentId, fightId, cancellationToken);
        if (fight is null) return new(FreeFightResultStatus.FightNotFound);
        if (fight.Status != Pending) return new(FreeFightResultStatus.AlreadyStarted);

        var validation = await ValidateAsync(tournamentId, input, cancellationToken);
        if (validation != FreeFightResultStatus.Success) return new(validation);

        Apply(fight, input);
        fight.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLog.LogAsync(
            tournamentId, user, "FreeFightUpdated", "Fight", fight.Id, AuditDetails(fight, placeNext: null), cancellationToken);
        await _matchService.NotifyFightUpdatedAsync(fight.Id, cancellationToken);

        return new(FreeFightResultStatus.Success, await _fightsStore.GetByIdAsync(fight.Id, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<FreeFightResult> DeleteAsync(
        Guid tournamentId,
        Guid fightId,
        string user,
        CancellationToken cancellationToken)
    {
        var fight = await FindFreeFightAsync(tournamentId, fightId, cancellationToken);
        if (fight is null) return new(FreeFightResultStatus.FightNotFound);
        if (fight.Status != Pending) return new(FreeFightResultStatus.AlreadyStarted);

        _dbContext.Fights.Remove(fight);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLog.LogAsync(
            tournamentId, user, "FreeFightDeleted", "Fight", fight.Id, $"TatamiId={fight.TatamiId}", cancellationToken);

        // The fight no longer exists, so clients are told to reload their tatami queues.
        _ = _hub.Clients.Group(tournamentId.ToString())
            .SendAsync("CategoryFightsUpdated", new { tournamentId, categoryId = (Guid?)null }, CancellationToken.None);

        return new(FreeFightResultStatus.Success);
    }

    private Task<FightRecord?> FindFreeFightAsync(Guid tournamentId, Guid fightId, CancellationToken cancellationToken) =>
        _dbContext.Fights.FirstOrDefaultAsync(
            f => f.Id == fightId && f.TournamentId == tournamentId && f.Kind == FreeFightRules.FreeKind,
            cancellationToken);

    private async Task<FreeFightResultStatus> ValidateAsync(
        Guid tournamentId,
        FreeFightInput input,
        CancellationToken cancellationToken)
    {
        if (!HasParticipant(input.White) || !HasParticipant(input.Blue)) return FreeFightResultStatus.NameRequired;

        var athleteIds = new[] { input.White.AthleteId, input.Blue.AthleteId }.OfType<Guid>().ToArray();
        if (athleteIds.Length == 2 && athleteIds[0] == athleteIds[1]) return FreeFightResultStatus.SameAthleteOnBothSides;
        if (athleteIds.Length == 0) return FreeFightResultStatus.Success;

        var known = await _dbContext.Athletes
            .CountAsync(a => a.TournamentId == tournamentId && athleteIds.Contains(a.Id), cancellationToken);
        return known == athleteIds.Length ? FreeFightResultStatus.Success : FreeFightResultStatus.AthleteNotFound;
    }

    private static bool HasParticipant(FreeFightSideInput side) =>
        side.AthleteId is not null || !string.IsNullOrWhiteSpace(side.Name);

    private static void Apply(FightRecord fight, FreeFightInput input)
    {
        fight.WhiteAthleteId = input.White.AthleteId;
        fight.WhiteName = input.White.AthleteId is null ? input.White.Name!.Trim() : null;
        fight.BlueAthleteId = input.Blue.AthleteId;
        fight.BlueName = input.Blue.AthleteId is null ? input.Blue.Name!.Trim() : null;
        fight.MatchDurationSeconds = input.MatchDurationSeconds;
        fight.GoldenScoreEnabled = input.GoldenScoreEnabled;
        fight.GoldenScoreDurationSeconds = input.GoldenScoreDurationSeconds;
    }

    // Free-text names are not logged: they may contain personal data.
    private static string AuditDetails(FightRecord fight, bool? placeNext) =>
        $"TatamiId={fight.TatamiId}; WhiteAthleteId={fight.WhiteAthleteId?.ToString() ?? "-"}; " +
        $"BlueAthleteId={fight.BlueAthleteId?.ToString() ?? "-"}; MatchDurationSeconds={fight.MatchDurationSeconds}; " +
        $"GoldenScore={fight.GoldenScoreEnabled}/{fight.GoldenScoreDurationSeconds}" +
        (placeNext is null ? string.Empty : $"; PlaceNext={placeNext}");
}
