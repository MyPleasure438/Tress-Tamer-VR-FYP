using UnityEngine;
using UnityEngine.InputSystem;

// ============================================================================
// HANDOFF NOTE - VR Tool Control Bindings
// ============================================================================
// Wires physical Meta Quest 2 controller buttons/stick to existing gameplay
// systems, without changing how those systems work internally.
//
// Control scheme:
//   Left controller,  Primary Button (X)  -> Undo last haircut change
//   Right controller, Primary Button (A)  -> Toggle headzone overview
//   Either controller, Secondary Button (Y/B), while holding the Clipper
//                                          -> Toggle Clipper Training HUD
//   Analog stick on the hand holding the Clipper, while the HUD is open,
//   left/right                            -> Cycle clipper guard length
//
// While the HUD is open and the clipper-holding hand's stick is being used
// for guard cycling, that same hand's locomotion input action is disabled
// so it can't also drive movement/turning at the same time. It's restored
// the instant the HUD closes or the Clipper is let go.
//
// WHY THIS VERSION IS DIFFERENT FROM THE PREVIOUS ONE:
// The previous version read controller buttons via the legacy
// UnityEngine.XR.InputDevices API (GetDeviceAtXRNode + CommonUsages).
// That API ONLY sees devices coming from a real XR runtime (an actual
// headset via OpenXR) - it does NOT see the XR Device Simulator's
// simulated controller, which is registered purely in the new Input
// System as "XRSimulatedController". That's why none of the bindings
// did anything when tested through the Device Simulator.
//
// This version reads buttons/stick through the new Input System's
// generic XR controller control paths instead (e.g.
// "<XRController>{LeftHand}/primaryButton"), created directly in code -
// no InputActionReference assets, no Inspector wiring needed for these.
// XRSimulatedController implements the same XRController control layout
// as real controllers, so these paths work with BOTH the simulator and
// real hardware.
//
// NOTHING ELSE CHANGES: every existing Inspector reference on this
// component (undoManager, headZoneToggle, clipperTrainingHud,
// clipperGuardController, clipper, leftHandRoot, rightHandRoot,
// leftHandLocomotionAction, rightHandLocomotionAction) stays exactly as
// already wired in the scene. Just replace this file's contents and let
// Unity recompile - no scene/Inspector changes required.
// ============================================================================
[DisallowMultipleComponent]
public class VRToolControlBindings : MonoBehaviour
{
    [Header("Required Links")]
    [SerializeField] private HairUndoManager undoManager;
    [SerializeField] private HeadZonePlayerToggle headZoneToggle;
    [SerializeField] private ClipperTrainingHud clipperTrainingHud;
    [SerializeField] private ClipperGuardController clipperGuardController;
    [SerializeField] private Clipper clipper;

    [Header("Hand Roots (for detecting which hand holds the clipper)")]
    [SerializeField] private Transform leftHandRoot;
    [SerializeField] private Transform rightHandRoot;

    [Header("Locomotion Suppression (see handoff note above)")]
    [SerializeField] private InputActionReference leftHandLocomotionAction;
    [SerializeField] private InputActionReference rightHandLocomotionAction;

    [Header("Guard Stick Tuning")]
    [Range(0.1f, 0.95f)]
    [SerializeField] private float guardStickThreshold = 0.6f;
    [SerializeField] private float guardCycleCooldown = 0.35f;

    // Raw Input System actions created in code - bound to the generic
    // XRController control layout, which both real OpenXR controllers AND
    // the XR Device Simulator's XRSimulatedController implement. No Inspector
    // wiring needed for these six.
    private InputAction _leftPrimaryButtonAction;
    private InputAction _rightPrimaryButtonAction;
    private InputAction _leftSecondaryButtonAction;
    private InputAction _rightSecondaryButtonAction;
    private InputAction _leftStickAction;
    private InputAction _rightStickAction;

    private bool _prevLeftPrimary;
    private bool _prevRightPrimary;
    private bool _prevLeftSecondary;
    private bool _prevRightSecondary;
    private float _guardCycleCooldownTimer;
    private InputAction _suppressedLocomotionAction;

    private void Awake()
    {
        _leftPrimaryButtonAction = new InputAction(
            "VRToolBindings_LeftPrimary", InputActionType.Button, "<XRController>{LeftHand}/primaryButton");
        _rightPrimaryButtonAction = new InputAction(
            "VRToolBindings_RightPrimary", InputActionType.Button, "<XRController>{RightHand}/primaryButton");
        _leftSecondaryButtonAction = new InputAction(
            "VRToolBindings_LeftSecondary", InputActionType.Button, "<XRController>{LeftHand}/secondaryButton");
        _rightSecondaryButtonAction = new InputAction(
            "VRToolBindings_RightSecondary", InputActionType.Button, "<XRController>{RightHand}/secondaryButton");
        _leftStickAction = new InputAction(
            "VRToolBindings_LeftStick", InputActionType.Value, "<XRController>{LeftHand}/primary2DAxis");
        _rightStickAction = new InputAction(
            "VRToolBindings_RightStick", InputActionType.Value, "<XRController>{RightHand}/primary2DAxis");
    }

    private void OnEnable()
    {
        _leftPrimaryButtonAction.Enable();
        _rightPrimaryButtonAction.Enable();
        _leftSecondaryButtonAction.Enable();
        _rightSecondaryButtonAction.Enable();
        _leftStickAction.Enable();
        _rightStickAction.Enable();
    }

    private void OnDisable()
    {
        _leftPrimaryButtonAction.Disable();
        _rightPrimaryButtonAction.Disable();
        _leftSecondaryButtonAction.Disable();
        _rightSecondaryButtonAction.Disable();
        _leftStickAction.Disable();
        _rightStickAction.Disable();
        RestoreSuppressedLocomotion();
    }

    private void OnDestroy()
    {
        _leftPrimaryButtonAction?.Dispose();
        _rightPrimaryButtonAction?.Dispose();
        _leftSecondaryButtonAction?.Dispose();
        _rightSecondaryButtonAction?.Dispose();
        _leftStickAction?.Dispose();
        _rightStickAction?.Dispose();
    }

    private void Update()
    {
        PollUndoAndHeadzone();
        PollClipperHudToggleAndGuardStick();
    }

    private void PollUndoAndHeadzone()
    {
        // Primary Button = X on the left controller.
        bool leftPrimary = _leftPrimaryButtonAction.IsPressed();
        if (leftPrimary && !_prevLeftPrimary)
            undoManager?.UndoLastChangeFromUIButton();
        _prevLeftPrimary = leftPrimary;

        // Primary Button = A on the right controller.
        bool rightPrimary = _rightPrimaryButtonAction.IsPressed();
        if (rightPrimary && !_prevRightPrimary)
            headZoneToggle?.ToggleHeadZones();
        _prevRightPrimary = rightPrimary;
    }

    private void PollClipperHudToggleAndGuardStick()
    {
        bool clipperHeld = clipper != null && clipper.IsHeld;
        if (!clipperHeld)
        {
            _prevLeftSecondary = false;
            _prevRightSecondary = false;
            RestoreSuppressedLocomotion();
            return;
        }

        Transform holdingInteractor = clipper.CurrentHoldingInteractorTransform;
        bool heldByLeft = holdingInteractor != null && leftHandRoot != null && holdingInteractor.IsChildOf(leftHandRoot);
        bool heldByRight = holdingInteractor != null && rightHandRoot != null && holdingInteractor.IsChildOf(rightHandRoot);
        if (!heldByLeft && !heldByRight)
        {
            RestoreSuppressedLocomotion();
            return;
        }

        bool secondaryPressed = heldByLeft ? _leftSecondaryButtonAction.IsPressed() : _rightSecondaryButtonAction.IsPressed();
        bool prevSecondary = heldByLeft ? _prevLeftSecondary : _prevRightSecondary;
        if (secondaryPressed && !prevSecondary)
            clipperTrainingHud?.Toggle();
        if (heldByLeft) _prevLeftSecondary = secondaryPressed; else _prevRightSecondary = secondaryPressed;

        bool hudVisible = clipperTrainingHud != null && clipperTrainingHud.IsVisible;
        if (!hudVisible)
        {
            RestoreSuppressedLocomotion();
            return;
        }

        InputActionReference relevantLocomotionAction = heldByLeft ? leftHandLocomotionAction : rightHandLocomotionAction;
        SuppressLocomotion(relevantLocomotionAction);

        _guardCycleCooldownTimer -= Time.deltaTime;
        if (_guardCycleCooldownTimer > 0f || clipperGuardController == null) return;

        Vector2 stick = heldByLeft ? _leftStickAction.ReadValue<Vector2>() : _rightStickAction.ReadValue<Vector2>();
        if (stick.x >= guardStickThreshold)
        {
            clipperGuardController.CycleNextGuard();
            _guardCycleCooldownTimer = guardCycleCooldown;
        }
        else if (stick.x <= -guardStickThreshold)
        {
            clipperGuardController.CyclePreviousGuard();
            _guardCycleCooldownTimer = guardCycleCooldown;
        }
    }

    private void SuppressLocomotion(InputActionReference actionReference)
    {
        if (actionReference == null || actionReference.action == null) return;
        if (_suppressedLocomotionAction == actionReference.action) return; // already suppressed
        RestoreSuppressedLocomotion();
        _suppressedLocomotionAction = actionReference.action;
        _suppressedLocomotionAction.Disable();
    }

    private void RestoreSuppressedLocomotion()
    {
        if (_suppressedLocomotionAction == null) return;
        _suppressedLocomotionAction.Enable();
        _suppressedLocomotionAction = null;
    }
}
