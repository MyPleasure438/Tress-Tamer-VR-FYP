using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// HANDOFF NOTE:
// Attach to the hair clip prefab/instances, alongside XRGrabInteractable and HairClipQuickAttach (and,
// only on the tray's master supply clip, HairClipSpawner - see SAFETY note below).
//
// The instant the player lets go of a clip (releases the grip), this waits a short beat for XR
// Interaction Toolkit's own socket auto-select to finish doing its thing (a clip released right on top
// of a ClipSocket_<Zone> on the head needs a moment to actually snap in), then checks whether the clip
// ended up selected by ANYTHING - a hand, or the case we actually care about, a head socket. If it's
// still completely unselected (dropped loose in mid-air, on the floor, on a table, wherever - not
// clipped onto the head), it despawns itself instead of sitting there as clutter. HairClipSpawner already
// hands out an endless fresh supply from the tray, so a loose dropped clip is never actually needed
// again.
//
// SAFETY: the tray's own master/supply clip (the one HairClipSpawner is still attached to) is NEVER
// despawned by this script, even though ITS selectExited also fires every single time a player grabs
// from the tray (HairClipSpawner hands the player a brand new clone and immediately snaps the master back
// to its resting spot - see HairClipSpawner.OnVRGrab). Despawning that one would kill the entire supply,
// so this script simply no-ops whenever a HairClipSpawner is present on the same GameObject.
[RequireComponent(typeof(XRGrabInteractable))]
public class HairClipDespawnIfUnused : MonoBehaviour
{
    [Tooltip("How long (seconds) to wait after being let go before checking whether the clip ended up socketed - gives XR Interaction Toolkit's own socket auto-select a moment to finish snapping it in, so a clip released right at a socket doesn't get destroyed out from under that snap.")]
    [SerializeField] private float despawnCheckDelay = 0.15f;

    [Header("Despawn Sound")]
    [Tooltip("Optional. Drag a sound clip here to play it once, at the clip's last position, the instant it despawns - leave empty for silence.")]
    [SerializeField] private AudioClip despawnSoundClip;
    [Range(0f, 1f)]
    [SerializeField] private float despawnSoundVolume = 1f;

    private XRGrabInteractable _grabInteractable;
    private HairClipSpawner _spawner;

    private void Awake()
    {
        _grabInteractable = GetComponent<XRGrabInteractable>();

        // Present only on the tray's master/supply clip - see class HANDOFF NOTE. Never despawn that one.
        _spawner = GetComponent<HairClipSpawner>();
    }

    private void OnEnable()
    {
        if (_grabInteractable != null)
            _grabInteractable.selectExited.AddListener(OnSelectExited);
    }

    private void OnDisable()
    {
        if (_grabInteractable != null)
            _grabInteractable.selectExited.RemoveListener(OnSelectExited);
    }

    private void OnSelectExited(SelectExitEventArgs args)
    {
        if (_spawner != null)
            return;

        StartCoroutine(DespawnIfStillUnselectedAfterDelay());
    }

    private IEnumerator DespawnIfStillUnselectedAfterDelay()
    {
        if (despawnCheckDelay > 0f)
            yield return new WaitForSeconds(despawnCheckDelay);
        else
            yield return null;

        // Still nothing holding it (no hand, and - the case we actually care about - no head socket) ->
        // it was just let go and dropped loose somewhere. Gone.
        if (_grabInteractable != null && !_grabInteractable.isSelected)
        {
            // PlayClipAtPoint (not a PlayOneShot on this object's own AudioSource) deliberately - this
            // GameObject is about to be destroyed, so the sound needs its own short-lived audio source
            // that survives independently and cleans itself up once the clip finishes.
            if (despawnSoundClip != null)
                AudioSource.PlayClipAtPoint(despawnSoundClip, transform.position, despawnSoundVolume);

            Destroy(gameObject);
        }
    }
}
