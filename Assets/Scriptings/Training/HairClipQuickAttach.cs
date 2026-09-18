using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit;

// HANDOFF NOTE (Phase 3 - Hair Clip quick-attach assist):
// New, self-contained script on the HairClip prefab. Does not touch the sockets, cutting logic, or
// anything else. Problem it solves: the socket's physical snap radius is tight (2.5cm), which is hard
// to hit precisely in VR. This adds a more forgiving 'press to attach' shortcut: while holding a clip in
// hand (grabbed, not already socketed) and within a larger, more forgiving range of ANY clip socket,
// pressing the controller's Activate button (the same trigger XRGrabInteractable already listens to for
// 'Activated', separate from the grip/select button used to hold it) force-attaches it to the nearest
// socket in range, without needing to physically nail the tight socket radius.
[RequireComponent(typeof(XRGrabInteractable))]
public class HairClipQuickAttach : MonoBehaviour
{
    [Tooltip("How close the clip needs to be to a socket (in meters) for the Activate button to force-attach it. More forgiving than the socket's own physical snap radius.")]
    [SerializeField] private float attachRange = 0.08f;

    private XRGrabInteractable _grabInteractable;

    private void Awake()
    {
        _grabInteractable = GetComponent<XRGrabInteractable>();
    }

    private void OnEnable()
    {
        _grabInteractable.activated.AddListener(OnActivated);
    }

    private void OnDisable()
    {
        _grabInteractable.activated.RemoveListener(OnActivated);
    }

    private void OnActivated(ActivateEventArgs args)
    {
        // Only relevant while held in a hand (not already resting in a socket).
        if (!_grabInteractable.isSelected)
            return;

        var currentInteractor = _grabInteractable.interactorsSelecting.Count > 0 ? _grabInteractable.interactorsSelecting[0] : null;
        if (currentInteractor is XRSocketInteractor)
            return; // already socketed, nothing to do

        XRSocketInteractor nearestSocket = FindNearestSocketInRange();
        if (nearestSocket == null)
            return;

        var interactionManager = _grabInteractable.interactionManager;
        if (interactionManager == null || currentInteractor == null)
            return;

        interactionManager.SelectExit((IXRSelectInteractor)currentInteractor, (IXRSelectInteractable)_grabInteractable);
        interactionManager.SelectEnter((IXRSelectInteractor)nearestSocket, (IXRSelectInteractable)_grabInteractable);
    }

    private XRSocketInteractor FindNearestSocketInRange()
    {
        XRSocketInteractor[] allSockets = Object.FindObjectsByType<XRSocketInteractor>(FindObjectsSortMode.None);
        XRSocketInteractor nearest = null;
        float nearestDistSqr = attachRange * attachRange;

        foreach (var socket in allSockets)
        {
            float distSqr = (socket.transform.position - transform.position).sqrMagnitude;
            if (distSqr <= nearestDistSqr)
            {
                nearestDistSqr = distSqr;
                nearest = socket;
            }
        }

        return nearest;
    }
}
