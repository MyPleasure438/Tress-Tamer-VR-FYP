using UnityEngine;
using UnityEngine.InputSystem;

// HANDOFF NOTE (Phase 2 - Clipper Guard Training):
// New, self-contained script. Does NOT modify Clipper.cs, ClipperTrainingHud.cs, or any XR Interaction
// Toolkit setup - it only calls ClipperTrainingHud.SetVisible()/Toggle(), which already exist.
//
// HOW TO WIRE THIS UP IN UNITY:
// 1. Add this component anywhere (e.g. same GameObject as ClipperTrainingHud, or the Clipper itself).
// 2. Drag your ClipperTrainingHud into the Hud field.
// 3. Auto show/hide on pickup (recommended): on the Clipper's GameObject, find its XR Grab Interactable
//    component - under its Interactable Events, drag THIS component into 'Select Entered' and call
//    OnClipperPickedUp(), then drag it into 'Select Exited' and call OnClipperPutDown(). No code edit
//    to Clipper.cs needed - those events already exist on the stock XR Grab Interactable component.
// 4. Manual override: desktop testing key defaults to G (edit Toggle Key in Inspector). For a VR
//    controller button, bind an Input Action to call ToggleHud() the same way you would for any other
//    button-triggered method.
[DisallowMultipleComponent]
public class ClipperTrainingHudToggle : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ClipperTrainingHud hud;

    [Header("Input")]
    [Tooltip("Desktop testing key. VR should call ToggleHud() from a controller button event instead.")]
    [SerializeField] private Key toggleKey = Key.G;

    [Header("Auto Show On Pickup")]
    [Tooltip("If true, the HUD is hidden by default and only shows automatically while the clipper is held (via OnClipperPickedUp/OnClipperPutDown). The manual toggle still works on top of this at any time.")]
    [SerializeField] private bool startHidden = true;

    private void Start()
    {
        if (hud != null && startHidden)
            hud.SetVisible(false);
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
            ToggleHud();
    }

    /// <summary>Call this from a VR controller button event (Input Action callback) to manually show/hide.</summary>
    public void ToggleHud()
    {
        if (hud != null)
            hud.Toggle();
    }

    /// <summary>Wire to the Clipper's XR Grab Interactable 'Select Entered' event.</summary>
    public void OnClipperPickedUp()
    {
        if (hud != null)
            hud.SetVisible(true);
    }

    /// <summary>Wire to the Clipper's XR Grab Interactable 'Select Exited' event.</summary>
    public void OnClipperPutDown()
    {
        if (hud != null)
            hud.SetVisible(false);
    }
}
