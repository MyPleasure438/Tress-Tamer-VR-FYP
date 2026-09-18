using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// HANDOFF NOTE:
// Generic "return to home position" behaviour for VR-grabbable tools (Scissor, Clipper, Comb, Brush, ...).
// Add this component alongside the tool's own script and its XRGrabInteractable - the instant the player
// lets go of the tool (releases the grip, whether in mid-air or after setting it down), it snaps straight
// back to a fixed home position/rotation instead of staying wherever it was dropped or falling under
// physics.
//
// SETTING THE HOME SPOT:
// - The very first time this component is added to a GameObject in the Editor, Reset() automatically
//   fills Return Position/Return Rotation with wherever the tool is sitting right now (e.g. its spot on
//   the tool tray/rack) - so the default just works with nothing to type.
// - Don't like where it snaps back to? Either hand-edit the Return Position/Return Rotation fields
//   directly in the Inspector, or move the tool to the spot you want in the Scene view and right-click
//   this component's header -> "Capture Current Transform As Return Point" to re-capture it, or drag a
//   separate empty "Tool Home" marker Transform into Use Return Point below to follow THAT Transform's
//   position/rotation instead (handy if you want to see/move the home spot as its own Scene object, or
//   reuse one marker for several tools).
[DisallowMultipleComponent]
public class ToolReturnToOrigin : MonoBehaviour
{
    [Header("Grab State")]
    [Tooltip("Auto-found on Awake if left empty.")]
    [SerializeField] private XRGrabInteractable grabInteractable;

    [Header("Return Point")]
    [Tooltip("Optional. If assigned, the tool returns to THIS Transform's live position/rotation instead of the fixed fields below - lets you move/see the home spot as its own object in the Scene view rather than typing numbers.")]
    [SerializeField] private Transform useReturnPoint;

    [Tooltip("World-space position the tool snaps back to on release. Auto-filled with the tool's starting spot the first time this component is added - edit freely in the Inspector to move it (ignored if Use Return Point is assigned).")]
    [SerializeField] private Vector3 returnPosition;

    [Tooltip("World-space rotation (Euler angles) the tool snaps back to on release. Auto-filled with the tool's starting rotation the first time this component is added - edit freely in the Inspector to change it (ignored if Use Return Point is assigned).")]
    [SerializeField] private Vector3 returnRotationEuler;

    [Header("Physics")]
    [Tooltip("Zero out the Rigidbody's velocity/angular velocity on return (if this tool has one), so it doesn't keep sliding/spinning from momentum right after snapping back.")]
    [SerializeField] private bool resetPhysicsVelocity = true;

    [Header("Return Sound")]
    [Tooltip("Optional. Drag a sound clip here to play it once, the instant this tool snaps back to its Return Position - leave empty for silence.")]
    [SerializeField] private AudioClip returnSoundClip;
    [Tooltip("Auto-found (AudioSource on this GameObject) or auto-created if left empty and Return Sound Clip is assigned.")]
    [SerializeField] private AudioSource returnAudioSource;
    [Range(0f, 1f)]
    [SerializeField] private float returnSoundVolume = 1f;

    private Rigidbody cachedRigidbody;

    private void Awake()
    {
        if (grabInteractable == null)
            grabInteractable = GetComponent<XRGrabInteractable>();

        cachedRigidbody = GetComponent<Rigidbody>();

        if (returnSoundClip != null && returnAudioSource == null)
        {
            returnAudioSource = GetComponent<AudioSource>();

            if (returnAudioSource == null)
                returnAudioSource = gameObject.AddComponent<AudioSource>();
        }

        // Fallback for tools that somehow reached Play with both fields still at the exact Vector3.zero
        // default (e.g. the component was added purely via script, bypassing Reset()) - capture wherever
        // the tool currently is rather than snapping it to the world origin.
        if (returnPosition == Vector3.zero && returnRotationEuler == Vector3.zero)
            CaptureCurrentTransformAsReturnPoint();
    }

    // Runs once, automatically, the moment this component is first added to a GameObject in the Editor -
    // pre-fills the Inspector fields with the tool's current placement so there's nothing to type by
    // default; only override afterwards if you actually want a different home spot.
    private void Reset()
    {
        CaptureCurrentTransformAsReturnPoint();
    }

    // Public (and exposed via the right-click context menu) so the home spot can be re-captured either
    // from the Inspector or from an Editor script after moving the tool to a new spot.
    [ContextMenu("Capture Current Transform As Return Point")]
    public void CaptureCurrentTransformAsReturnPoint()
    {
        returnPosition = transform.position;
        returnRotationEuler = transform.eulerAngles;
    }

    private void OnEnable()
    {
        if (grabInteractable != null)
            grabInteractable.selectExited.AddListener(OnSelectExited);
    }

    private void OnDisable()
    {
        if (grabInteractable != null)
            grabInteractable.selectExited.RemoveListener(OnSelectExited);
    }

    // Fires the instant the player's hand lets go of the tool (release/drop), whether it was let go in
    // mid-air or set down on a surface first.
    private void OnSelectExited(SelectExitEventArgs args)
    {
        Vector3 targetPosition = useReturnPoint != null ? useReturnPoint.position : returnPosition;
        Quaternion targetRotation = useReturnPoint != null ? useReturnPoint.rotation : Quaternion.Euler(returnRotationEuler);

        transform.SetPositionAndRotation(targetPosition, targetRotation);

        if (resetPhysicsVelocity && cachedRigidbody != null)
        {
#if UNITY_6000_0_OR_NEWER
            cachedRigidbody.linearVelocity = Vector3.zero;
#else
            cachedRigidbody.velocity = Vector3.zero;
#endif
            cachedRigidbody.angularVelocity = Vector3.zero;
        }

        PlayReturnSound();
    }

    private void PlayReturnSound()
    {
        if (returnSoundClip == null || returnAudioSource == null)
            return;

        returnAudioSource.PlayOneShot(returnSoundClip, returnSoundVolume);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 gizmoPosition = useReturnPoint != null ? useReturnPoint.position : returnPosition;

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(gizmoPosition, 0.03f);
        Gizmos.DrawLine(transform.position, gizmoPosition);
    }
}
