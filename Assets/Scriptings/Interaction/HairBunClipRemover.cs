using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// HANDOFF NOTE:
// Lets the player remove a socketed hair clip by pointing at the much bigger, more visible hair bun that
// appears while a clip is attached (see ClipZoneGatherEffect.cs), instead of needing to precisely hit the
// tiny hidden clip itself. Attach to the 'SocketedGrabZone' child object on the hairbunType2WithClip
// prefab (the one with the SphereCollider already added).
//
// How it works: this collider becomes a simple 'point and select' target (XRSimpleInteractable - can be
// selected, but isn't grabbable/movable itself). When selected, it finds the parent socket's currently
// held clip and force-transfers the selection from the socket directly to whichever interactor (the
// player's hand/ray) just selected the bun - so the clip comes out into the player's hand immediately,
// even though they never touched the clip itself.
//
// HOW TO WIRE THIS UP IN UNITY:
// 1. On the 'SocketedGrabZone' child GameObject (hairbunType2WithClip prefab), add an XRSimpleInteractable
//    component first (if not already present).
// 2. Add this component alongside it - it will auto-find both automatically.
[RequireComponent(typeof(XRSimpleInteractable))]
public class HairBunClipRemover : MonoBehaviour
{
    private XRSimpleInteractable simpleInteractable;
    private XRSocketInteractor parentSocket;

    private void Awake()
    {
        simpleInteractable = GetComponent<XRSimpleInteractable>();
        parentSocket = GetComponentInParent<XRSocketInteractor>();
    }

    private void OnEnable()
    {
        simpleInteractable.selectEntered.AddListener(OnSelectEntered);
    }

    private void OnDisable()
    {
        simpleInteractable.selectEntered.RemoveListener(OnSelectEntered);
    }

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (parentSocket == null || parentSocket.interactablesSelected.Count == 0)
            return;

        var clipInteractable = parentSocket.interactablesSelected[0];
        var interactionManager = parentSocket.interactionManager;
        var playerInteractor = args.interactorObject;

        // Force-transfer: release from the socket, immediately grab with whichever interactor just
        // selected the bun.
        interactionManager.SelectExit(parentSocket, clipInteractable);
        interactionManager.SelectEnter(playerInteractor, clipInteractable);
    }
}
