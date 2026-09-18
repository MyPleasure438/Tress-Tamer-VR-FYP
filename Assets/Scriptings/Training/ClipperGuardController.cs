using UnityEngine;
using UnityEngine.Events;

// HANDOFF NOTE (Phase 2 - Clipper Guard Training):
// New, self-contained script. Does NOT modify Clipper.cs, CuttingManager.cs, or any cutting logic -
// this only tracks which guard size the player currently has 'attached', for teaching/scoring purposes.
// Actual cut depth is still governed entirely by HaircutManager/CuttingManager exactly as before -
// selecting a guard here does not change what the clipper physically cuts.
//
// HOW TO WIRE THIS UP IN UNITY:
// 1. Add this component to your Clipper tool's GameObject (or anywhere convenient - it doesn't need
//    a reference to Clipper itself).
// 2. Default guards are 0.5 / 1 / 1.5 / 2 / 3 cm - edit Available Guards Cm in the Inspector if your
//    real guard set is different.
// 3. Call CycleNextGuard() / CyclePreviousGuard() from a VR controller button binding (e.g. a
//    thumbstick click or a physical guard-swap gesture), or SetGuardIndex()/SetGuardLengthCm()
//    from a menu button.
[DisallowMultipleComponent]
public class ClipperGuardController : MonoBehaviour
{
    [Header("Available Guards")]
    [Tooltip("Common real clipper guard sizes, in centimeters. Order matters for cycling.")]
    [SerializeField] private float[] availableGuardsCm = { 0.5f, 1f, 1.5f, 2f, 3f };

    [Tooltip("Which guard is attached at Start (index into Available Guards Cm).")]
    [SerializeField] private int currentGuardIndex = 2;

    [Header("Events")]
    [Tooltip("Fires whenever the selected guard changes, with the new guard size in cm. Hook a UI label's refresh here.")]
    [SerializeField] private UnityEvent<float> onGuardChanged;

    public float CurrentGuardCm => availableGuardsCm != null && availableGuardsCm.Length > 0
        ? availableGuardsCm[Mathf.Clamp(currentGuardIndex, 0, availableGuardsCm.Length - 1)]
        : 0f;

    public int CurrentGuardIndex => currentGuardIndex;
    public int GuardCount => availableGuardsCm != null ? availableGuardsCm.Length : 0;

    // Exposes the guard-changed event for runtime subscription (e.g. ClipperTrainingHud keeping its
    // label in sync no matter what triggers a guard change - UI buttons, VR stick input, training-step
    // auto-recommendations, etc). Fixes the bug where the label only refreshed on the on-screen
    // </> buttons and silently went stale for every other caller.
    public UnityEvent<float> OnGuardChanged => onGuardChanged;

    public void CycleNextGuard()
    {
        if (availableGuardsCm == null || availableGuardsCm.Length == 0)
            return;

        currentGuardIndex = (currentGuardIndex + 1) % availableGuardsCm.Length;
        onGuardChanged?.Invoke(CurrentGuardCm);
    }

    public void CyclePreviousGuard()
    {
        if (availableGuardsCm == null || availableGuardsCm.Length == 0)
            return;

        currentGuardIndex = (currentGuardIndex - 1 + availableGuardsCm.Length) % availableGuardsCm.Length;
        onGuardChanged?.Invoke(CurrentGuardCm);
    }

    public void SetGuardIndex(int index)
    {
        if (availableGuardsCm == null || availableGuardsCm.Length == 0)
            return;

        currentGuardIndex = Mathf.Clamp(index, 0, availableGuardsCm.Length - 1);
        onGuardChanged?.Invoke(CurrentGuardCm);
    }

    // Picks the closest available guard to the given cm value - handy for snapping to whatever
    // TrainingGuideManager.GetRecommendedGuardForCurrentStep() reports.
    public void SetGuardLengthCm(float cm)
    {
        if (availableGuardsCm == null || availableGuardsCm.Length == 0)
            return;

        int bestIndex = 0;
        float bestDiff = Mathf.Abs(availableGuardsCm[0] - cm);

        for (int i = 1; i < availableGuardsCm.Length; i++)
        {
            float diff = Mathf.Abs(availableGuardsCm[i] - cm);

            if (diff < bestDiff)
            {
                bestDiff = diff;
                bestIndex = i;
            }
        }

        SetGuardIndex(bestIndex);
    }
}
