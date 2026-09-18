using UnityEngine;
using UnityEngine.Events;

// HANDOFF NOTE (Phase 2 - Clipper Guard Training):
// New, self-contained script. Optional movement guidance only - does not gate, block, or alter
// cutting in any way, and does NOT modify Clipper.cs. It watches the clipper tool's own Transform
// from the outside (every GameObject's transform is already public), which is enough to tell if the
// player is sweeping the tool upward - the real technique for blending sides/nape into a fade.
//
// HOW TO WIRE THIS UP IN UNITY:
// 1. Add this component anywhere (e.g. same GameObject as your Training Guide Panel).
// 2. Drag your Clipper tool's Transform (the GameObject the Clipper component lives on, or its
//    handle/grip point) into Clipper Transform.
// 3. Read LatestFeedback from your UI, or hook On Motion Feedback (UnityEvent<string>) to a label.
[DisallowMultipleComponent]
public class ClipperMotionGuide : MonoBehaviour
{
    [Tooltip("Drag the clipper tool's Transform here (the GameObject the Clipper component is on, or its handle).")]
    [SerializeField] private Transform clipperTransform;

    [Tooltip("Minimum movement per frame (meters) before a direction is evaluated - filters out hand jitter when the tool is nearly still.")]
    [SerializeField] private float minimumMovementToEvaluate = 0.001f;

    [Tooltip("How strongly the motion has to point upward to count as good (1 = straight up only, lower = more lenient).")]
    [Range(0f, 1f)]
    [SerializeField] private float upwardDotThreshold = 0.5f;

    [Tooltip("Fires whenever a new feedback message is produced (only when there is enough movement to evaluate).")]
    [SerializeField] private UnityEvent<string> onMotionFeedback;

    private Vector3 lastPosition;
    private bool hasLastPosition;

    public string LatestFeedback { get; private set; } = "";

    private void Update()
    {
        if (clipperTransform == null)
            return;

        Vector3 currentPosition = clipperTransform.position;

        if (!hasLastPosition)
        {
            lastPosition = currentPosition;
            hasLastPosition = true;
            return;
        }

        Vector3 delta = currentPosition - lastPosition;
        lastPosition = currentPosition;

        if (delta.magnitude < minimumMovementToEvaluate)
            return;

        float upwardDot = Vector3.Dot(delta.normalized, Vector3.up);
        string feedback = upwardDot >= upwardDotThreshold
            ? "Good - moving upward for a clean blend."
            : (upwardDot <= -upwardDotThreshold
                ? "Moving downward - for fades/blends, try sweeping upward instead."
                : "Try moving more upward along the zone for a smoother blend.");

        LatestFeedback = feedback;
        onMotionFeedback?.Invoke(feedback);
    }
}
