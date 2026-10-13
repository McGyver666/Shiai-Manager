namespace ShiaiManager.Api.Services;

/// <summary>
/// API messages for Freikämpfe (ADR-0013).
/// </summary>
public static class FreeFightMessages
{
    /// <summary>A side has neither an athlete nor a name.</summary>
    public static readonly LocalizedMessage NameRequired = new(
        "errors.freeFight.nameRequired",
        "Jede Seite braucht einen Namen oder einen Athleten.");

    /// <summary>The same athlete was selected for both sides.</summary>
    public static readonly LocalizedMessage SameAthleteOnBothSides = new(
        "errors.freeFight.sameAthlete",
        "Ein Athlet kann nicht gegen sich selbst kämpfen.");

    /// <summary>The Freikampf has already started.</summary>
    public static readonly LocalizedMessage AlreadyStarted = new(
        "errors.freeFight.alreadyStarted",
        "Ein gestarteter Freikampf kann nicht mehr geändert oder gelöscht werden.");
}
