// Shared between HaircutManager (developer-configured per-zone requirement, see
// HaircutManager.TryGetRequiredStubbleStyle) and EvaluationSystem (compares the requirement against the
// actual achieved state from StubbleAreaSwapController.GetAchievedStubbleStyle). Kept as its own small
// top-level file, matching the project's existing pattern for small shared enums
// (Stage1HairLengthState.cs, Stage1EvaluationZone.cs, etc.).
public enum RequiredStubbleStyle
{
    // No requirement configured for this zone - old behavior applies (any fully-shaved result, regardless
    // of which stubble finish it left behind, counts as Correct).
    None,
    CrewCut,
    ButchCut,
    InductionCut
}
