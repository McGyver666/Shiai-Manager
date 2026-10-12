namespace ShiaiManager.Api.Services;

/// <summary>
/// API messages for draw generation.
/// </summary>
public static class DrawMessages
{
    /// <summary>Best of Three requested for a category without exactly two athletes.</summary>
    public static readonly LocalizedMessage BestOfThreeRequiresTwoAthletes = new(
        "errors.draw.bestOfThreeRequiresTwoAthletes",
        "Best of Three ist nur bei genau zwei Athleten in der Gewichtsklasse möglich.");

    /// <summary>Best of Three requested outside the individual competition mode.</summary>
    public static readonly LocalizedMessage BestOfThreeRequiresIndividualMode = new(
        "errors.draw.bestOfThreeRequiresIndividualMode",
        "Best of Three ist nur im Wettbewerbsmodus Einzelturnier möglich.");
}
