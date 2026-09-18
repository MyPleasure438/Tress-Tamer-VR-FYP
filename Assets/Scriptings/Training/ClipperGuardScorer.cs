using UnityEngine;
using UnityEngine.Events;

// HANDOFF NOTE (Phase 2 - Clipper Guard Training):
// New, self-contained script. Compares the player's currently selected guard (ClipperGuardController)
// against the recommended guard for the current training step (TrainingGuideManager). Teaching/scoring
// check only - does not touch Clipper.cs, CuttingManager.cs, or what actually gets cut.
//
// HOW TO WIRE THIS UP IN UNITY:
// 1. Add this component anywhere (e.g. same GameObject as your Training Guide Panel).
// 2. Drag your TrainingGuideManager and ClipperGuardController into the matching fields.
// 3. Call CheckGuardCorrectness() from a button (e.g. add it alongside 'Check This Zone').
[DisallowMultipleComponent]
public class ClipperGuardScorer : MonoBehaviour
{
    [SerializeField] private TrainingGuideManager trainingGuideManager;
    [SerializeField] private ClipperGuardController guardController;

    [Tooltip("How close (in cm) the selected guard must be to the recommended guard to count as correct. Small tolerance only covers float rounding - guards are discrete sizes, not a range.")]
    [SerializeField] private float matchToleranceCm = 0.01f;

    [Tooltip("Fires after CheckGuardCorrectness() runs, with a short human-readable result.")]
    [SerializeField] private UnityEvent<string> onGuardCheckResult;

    // Returns true if the currently selected guard matches the recommended guard for the current
    // step. summary is always set to something displayable, even when the check does not apply
    // (e.g. current step recommends Scissor, not Clipper).
    public bool CheckGuardCorrectness(out string summary)
    {
        if (trainingGuideManager == null || guardController == null)
        {
            summary = "Guard check unavailable - missing TrainingGuideManager or ClipperGuardController reference.";
            onGuardCheckResult?.Invoke(summary);
            return false;
        }

        float recommendedGuardCm = trainingGuideManager.GetRecommendedGuardForCurrentStep(out bool isClipperStep);

        if (!isClipperStep)
        {
            summary = "This zone recommends Scissor, not Clipper - guard check does not apply here.";
            onGuardCheckResult?.Invoke(summary);
            return false;
        }

        float selectedGuardCm = guardController.CurrentGuardCm;
        bool isCorrect = Mathf.Abs(selectedGuardCm - recommendedGuardCm) <= matchToleranceCm;

        summary = isCorrect
            ? $"Correct guard - {selectedGuardCm:0.#} cm matches the recommended {recommendedGuardCm:0.#} cm for this zone."
            : $"Guard mismatch - you have {selectedGuardCm:0.#} cm attached, recommended is {recommendedGuardCm:0.#} cm for this zone.";

        onGuardCheckResult?.Invoke(summary);
        return isCorrect;
    }
}
