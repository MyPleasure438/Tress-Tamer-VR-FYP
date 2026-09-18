using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit;

// HANDOFF NOTE:
// Hides the visible ray (XRInteractorLineVisual) while the controller is holding a grabbed object, and
// shows it again once released. Does not touch the underlying raycasting logic - only the visual line.
// Attach this to the SAME GameObject as XRRayInteractor (e.g. Left/Right Ray Interactor).
[RequireComponent(typeof(XRRayInteractor))]
public class HideRayWhileGrabbing : MonoBehaviour
{
    private XRRayInteractor rayInteractor;
    private XRInteractorLineVisual lineVisual;

    private void Awake()
    {
        rayInteractor = GetComponent<XRRayInteractor>();
        lineVisual = GetComponent<XRInteractorLineVisual>();
    }

    private void OnEnable()
    {
        rayInteractor.selectEntered.AddListener(OnSelectEntered);
        rayInteractor.selectExited.AddListener(OnSelectExited);
    }

    private void OnDisable()
    {
        rayInteractor.selectEntered.RemoveListener(OnSelectEntered);
        rayInteractor.selectExited.RemoveListener(OnSelectExited);
    }

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (lineVisual != null)
            lineVisual.enabled = false;
    }

    private void OnSelectExited(SelectExitEventArgs args)
    {
        if (lineVisual != null)
            lineVisual.enabled = true;
    }
}
