using UnityEngine;
using System.Collections.Generic;
using System.Linq;

// HANDOFF NOTE (Phase 3 - Hair Clip visual effect):
// On clip attach, HIDE this zone's real hair cards and SHOW a HairSphere instance positioned at the clip.
// On detach, hide the HairSphere and restore the real cards. Does not touch CuttingManager.cs,
// HairCardData.cs, or any cutting logic - purely visual.
//
// ADDED: hover preview - while a clip is being held NEAR (but not yet placed in) a socket, shows the SAME
// HairSphere at the same position, but tinted with a semi-transparent preview material (matching the
// socket's own placement-preview color) - so the player sees exactly what the result will look like
// BEFORE committing to place the clip, instead of only seeing a preview of the tiny clip itself. Wired to
// the socket's own hoverEntered/hoverExited events (separate from selectEntered/selectExited, which
// still fire only on actual placement).
//
// FIX: hiding the renderers alone did NOT stop scissor/clipper/comb from finding and acting on these cards -
// their colliders were always left active (by design, same as the H-key zone toggle), so Physics queries in
// CuttingManager still hit them. That let a cut/comb/hold on a clip-hidden card force its renderer back on
// as a side effect of the normal "show what I just cut" code path - the card would visibly "pop into view"
// mid-clip. Now OnClipAttached/OnClipDetached also flips each zone card's HairCardData.IsCuttable, the exact
// same flag CuttingManager already gates every cut/comb/hold/measure path on elsewhere - so a clip-hidden
// card is now genuinely untouchable (not just invisible) until the clip comes off.
//
// FIX ("cut hair respawns when the clip comes off"): CacheRenderersIfNeeded() caches EVERY MeshRenderer
// under each zone card via GetComponentsInChildren - under SegmentedHairCard's combined-mesh rendering (the
// project default), that includes both the card's own combined-mesh renderer (the ONE renderer that should
// legitimately show/hide with the clip) AND every individual segment's own renderer, which SegmentedHairCard
// keeps permanently disabled once that segment is cut away (rendering happens entirely via the combined mesh
// instead - see SegmentedHairCard.SetFirstHiddenSegmentIndex). OnClipDetached() used to blindly set every
// cached renderer's .enabled = true on detach, which turned the already-cut segments' renderers back on too
// - visually "regrowing" hair that had already been cut, even though the real cut data
// (HairCardData.CurrentLength / SegmentedHairCard's own currentCutStartIndex) was never touched by this
// script. Now OnClipDetached() doesn't flip renderers directly at all - it calls each card's own
// SegmentedHairCard.SetVisibleLength(data.CurrentLength) instead, which reapplies the card's true cut state
// authoritatively (correctly re-hides already-cut segments, rebuilds + re-shows the combined mesh) instead
// of guessing.
//
// Wired to all 20 ClipSocket_<Zone> GameObjects' XR Socket Interactor events:
// Select Entered/Select Exited -> OnClipAttached()/OnClipDetached() (unchanged, same method names)
// Hover Entered/Hover Exited -> OnClipHoverEntered()/OnClipHoverExited() (new - needs wiring in Inspector)
[DisallowMultipleComponent]
public class ClipZoneGatherEffect : MonoBehaviour
{
    [Header("Zone")]
    [SerializeField] private HairSectionType zone;
    [Tooltip("Character root GameObject (same one HaircutManager uses) - needed to correctly scope which cards belong to this zone.")]
    [SerializeField] private GameObject hairCardsRoot;

    [Header("Hair Sphere Visual")]
    [Tooltip("The HairSphere prefab (twisted-knot mesh made in Blender) to show in place of this zone's real hair while clipped.")]
    [SerializeField] private GameObject hairSpherePrefab;
    [Tooltip("Extra outward push (meters) from the head's approximate center, so the sphere visual sits clearly on top of the scalp instead of at/inside the surface, regardless of exactly where the clip's own pivot sits.")]
    [SerializeField] private float outwardSafetyMargin = 0.02f;
    [Tooltip("Uniform scale applied to the spawned HairSphere instance - tune to roughly match how much hair this zone actually has.")]
    [SerializeField] private float hairSphereScale = 1f;
    [Tooltip("Manual world-space offset (meters) applied on top of the clip position - use this to nudge the sphere so its actual visual base sits at the clip, since the sphere was centered on its overall bounding box, not necessarily its true attachment point. Tune by eye in Play mode.")]
    [SerializeField] private Vector3 manualPositionOffset = Vector3.zero;

    [Header("Hover Preview")]
    [Tooltip("Semi-transparent material shown on the HairSphere while a clip is hovering nearby but not yet placed - use the same preview material as the socket's own placement-preview color for visual consistency.")]
    [SerializeField] private Material hoverPreviewMaterial;

    private List<MeshRenderer> _cachedRenderers;
    private List<HairCardData> _cachedHairCardDatas;
    // Parallel to _cachedHairCardDatas (same index = same card) - used on detach to reapply each card's
    // TRUE cut state instead of blindly re-enabling every cached renderer. Null entries are skipped (e.g.
    // a card with no SegmentedHairCard component for some reason).
    private List<SegmentedHairCard> _cachedSegmentedHairCards;
    private Vector3? _cachedHeadCenterWorld;
    private GameObject _sphereInstance;
    private List<MeshRenderer> _attachedClipRenderers;
    private Dictionary<MeshRenderer, Material[]> _cachedOriginalSphereMaterials;
    private bool _isPreviewOnly;
    private bool _isActuallyAttached;

    // Shared registry other systems can check (e.g. HeadZonePlayerToggle's H-key hide/restore) so they
    // don't blindly re-show hair cards that should stay hidden because their zone is currently clipped.
    public static readonly HashSet<HairSectionType> CurrentlyClippedZones = new HashSet<HairSectionType>();

    /// <summary>Wire to this socket's XR Socket Interactor 'Hover Entered' event.</summary>
    public void OnClipHoverEntered(UnityEngine.XR.Interaction.Toolkit.HoverEnterEventArgs args)
    {
        // Don't show a preview if a clip is already actually placed here - the real attached visual
        // already covers that case.
        if (_isActuallyAttached)
            return;

        // Only show the preview for actual hair clips - the socket's hoverEntered event fires for ANY
        // grabbable object that comes near (e.g. scissors), not just clips. HairClipQuickAttach only
        // exists on real hair clip objects, making it a reliable type check here.
        Transform hoveringObject = (args.interactableObject as Component)?.transform;
        if (hoveringObject == null || hoveringObject.GetComponent<HairClipQuickAttach>() == null)
            return;

        // FIX (neighboring-socket false "blue glow"): sockets sit close together on the scalp, so a clip
        // that is already snapped into a DIFFERENT socket can still physically overlap a nearby empty
        // socket's own hover trigger. Without this check, that neighbor socket would incorrectly show a
        // "would go here" preview for a clip that was never actually brought near it - it just happens to
        // be resting close by because it's placed next door.
        // NOTE: checking isSelected alone is WRONG here and was the previous (broken) version of this fix -
        // isSelected is also true the entire time the player is simply holding the clip in their hand (the
        // hand's own Ray/Direct Interactor "selects" it too), which silently killed every legitimate preview.
        // What we actually want to detect is "already placed in a DIFFERENT socket", so specifically look
        // for an XRSocketInteractor among the interactors currently selecting this clip - a hand interactor
        // doesn't count, only another socket does.
        if (args.interactableObject is UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable selectable)
        {
            foreach (var interactor in selectable.interactorsSelecting)
            {
                if (interactor is UnityEngine.XR.Interaction.Toolkit.Interactors.XRSocketInteractor)
                    return;
            }
        }

        EnsureSphereInstanceExists();
        PositionSphereInstance();
        ApplyPreviewMaterial();

        _isPreviewOnly = true;

        if (_sphereInstance != null)
            _sphereInstance.SetActive(true);
    }

    /// <summary>Wire to this socket's XR Socket Interactor 'Hover Exited' event.</summary>
    public void OnClipHoverExited(UnityEngine.XR.Interaction.Toolkit.HoverExitEventArgs args)
    {
        // Only hide/restore if this was just a preview - if the clip actually got placed (selectEntered
        // already fired), leave the real attached visual alone.
        if (!_isPreviewOnly)
            return;

        _isPreviewOnly = false;

        if (_sphereInstance != null)
            _sphereInstance.SetActive(false);

        RestoreOriginalMaterial();
    }

    /// <summary>Wire to this socket's XR Socket Interactor 'Select Entered' event.</summary>

    public void OnClipAttached(UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs args)
    {
        Transform clipTransform = (args.interactableObject as Component)?.transform;

        if (clipTransform == null)
            return;

        // Only real hair clips are allowed to trigger the gather effect.
        if (clipTransform.GetComponent<HairClipQuickAttach>() == null)
            return;

        // Clear previews from all other clip sockets.
        ClipZoneGatherEffect[] allSockets =
            FindObjectsByType<ClipZoneGatherEffect>(
                FindObjectsSortMode.None);

        foreach (ClipZoneGatherEffect socket in allSockets)
        {
            if (socket != null && socket != this)
                socket.ClearHoverPreview();
        }

        CacheRenderersIfNeeded();

        CurrentlyClippedZones.Add(zone);
        _isActuallyAttached = true;
        _isPreviewOnly = false;

        foreach (var renderer in _cachedRenderers)
        {
            if (renderer != null)
                renderer.enabled = false;
        }

        if (_cachedHairCardDatas != null)
        {
            foreach (var data in _cachedHairCardDatas)
            {
                if (data != null)
                    data.IsCuttable = false;
            }
        }

        EnsureSphereInstanceExists();
        PositionSphereInstance();
        RestoreOriginalMaterial(); // make sure it's the REAL material now, not left over from a preview

        if (_sphereInstance != null)
            _sphereInstance.SetActive(true);

        // Hide the physical clip's own renderer while attached - HairBunType2 already includes its own
        // clip mesh baked in, so showing both at once would look like two overlapping clips.
        _attachedClipRenderers = new List<MeshRenderer>(clipTransform.GetComponentsInChildren<MeshRenderer>(true));
        foreach (var r in _attachedClipRenderers)
        {
            if (r != null)
                r.enabled = false;
        }
    }

    /*
    public void OnClipAttached(UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs args)
    {
        Transform clipTransform = (args.interactableObject as Component)?.transform;
        if (clipTransform == null)
            return;

        CacheRenderersIfNeeded();
        CurrentlyClippedZones.Add(zone);
        _isActuallyAttached = true;
        _isPreviewOnly = false;

        foreach (var renderer in _cachedRenderers)
        {
            if (renderer != null)
                renderer.enabled = false;
        }

        if (_cachedHairCardDatas != null)
        {
            foreach (var data in _cachedHairCardDatas)
            {
                if (data != null)
                    data.IsCuttable = false;
            }
        }

        EnsureSphereInstanceExists();
        PositionSphereInstance();
        RestoreOriginalMaterial(); // make sure it's the REAL material now, not left over from a preview

        if (_sphereInstance != null)
            _sphereInstance.SetActive(true);

        // Hide the physical clip's own renderer while attached - HairBunType2 already includes its own
        // clip mesh baked in, so showing both at once would look like two overlapping clips.
        _attachedClipRenderers = new List<MeshRenderer>(clipTransform.GetComponentsInChildren<MeshRenderer>(true));
        foreach (var r in _attachedClipRenderers)
        {
            if (r != null)
                r.enabled = false;
        }
    }
    */
    /// <summary>Wire to this socket's XR Socket Interactor 'Select Exited' event.</summary>

    public void OnClipDetached(UnityEngine.XR.Interaction.Toolkit.SelectExitEventArgs args)
    {
        Transform clipTransform =
        (args.interactableObject as Component)?.transform;

        if (clipTransform == null ||
            clipTransform.GetComponent<HairClipQuickAttach>() == null)
            return;

        CurrentlyClippedZones.Remove(zone);
        _isActuallyAttached = false;

        if (_sphereInstance != null)
            _sphereInstance.SetActive(false);

        // NOTE: deliberately NOT looping _cachedRenderers here anymore - see FIX comment at the top of this
        // file. Blindly re-enabling every cached renderer used to turn already-cut segments' renderers back
        // on too. Each card's own SegmentedHairCard.SetFirstHiddenSegmentIndex() call below re-derives the
        // CORRECT renderer state (combined mesh + per-segment) from the card's real cut data instead.
        if (_cachedHairCardDatas != null)
        {
            foreach (var data in _cachedHairCardDatas)
            {
                if (data != null)
                    data.IsCuttable = true;
            }
        }

        if (_cachedSegmentedHairCards != null)
        {
            // FIX ("fully-cut card's last segment reappears and becomes uncuttable after a clip"): this
            // used to call SetVisibleLength(data.CurrentLength) - but SetVisibleLength derives an index
            // FROM A LENGTH, and that conversion clamps to a MINIMUM of 1 visible segment for anything not
            // exactly zero (SetVisibleLength's own "small-but-real target lengths still show at least a
            // sliver" rule). A fully-cut card's true index is 0 (nothing visible), but its CurrentLength is
            // rarely an exact 0f in float terms - so re-deriving from length round-tripped back to "show 1
            // segment", resurrecting hair that was already fully cut away and desyncing it from the still-0
            // authoritative index (which is also why cutting it again looked like it did nothing - the
            // combined mesh was correctly empty, but a lone stale segment renderer was drawing on top of
            // it). SegmentedHairCard.CurrentCutStartIndex IS that authoritative index already - reapplying
            // it directly via SetFirstHiddenSegmentIndex is a lossless resync with no rounding/clamping,
            // unlike going back through a length.
            for (int i = 0; i < _cachedSegmentedHairCards.Count; i++)
            {
                SegmentedHairCard segmentedCard = _cachedSegmentedHairCards[i];

                if (segmentedCard != null)
                    segmentedCard.SetFirstHiddenSegmentIndex(segmentedCard.CurrentCutStartIndex);
            }
        }

        // Restore the physical clip's own visual now that it's been removed from this zone.
        if (_attachedClipRenderers != null)
        {
            foreach (var r in _attachedClipRenderers)
            {
                if (r != null)
                    r.enabled = true;
            }
            _attachedClipRenderers = null;
        }
    }

    /*
    public void OnClipDetached(UnityEngine.XR.Interaction.Toolkit.SelectExitEventArgs args)
    {
        CurrentlyClippedZones.Remove(zone);
        _isActuallyAttached = false;

        if (_sphereInstance != null)
            _sphereInstance.SetActive(false);

        // NOTE: deliberately NOT looping _cachedRenderers here anymore - see FIX comment at the top of this
        // file. Blindly re-enabling every cached renderer used to turn already-cut segments' renderers back
        // on too. Each card's own SegmentedHairCard.SetFirstHiddenSegmentIndex() call below re-derives the
        // CORRECT renderer state (combined mesh + per-segment) from the card's real cut data instead.
        if (_cachedHairCardDatas != null)
        {
            foreach (var data in _cachedHairCardDatas)
            {
                if (data != null)
                    data.IsCuttable = true;
            }
        }

        if (_cachedSegmentedHairCards != null)
        {
            // FIX ("fully-cut card's last segment reappears and becomes uncuttable after a clip"): this
            // used to call SetVisibleLength(data.CurrentLength) - but SetVisibleLength derives an index
            // FROM A LENGTH, and that conversion clamps to a MINIMUM of 1 visible segment for anything not
            // exactly zero (SetVisibleLength's own "small-but-real target lengths still show at least a
            // sliver" rule). A fully-cut card's true index is 0 (nothing visible), but its CurrentLength is
            // rarely an exact 0f in float terms - so re-deriving from length round-tripped back to "show 1
            // segment", resurrecting hair that was already fully cut away and desyncing it from the still-0
            // authoritative index (which is also why cutting it again looked like it did nothing - the
            // combined mesh was correctly empty, but a lone stale segment renderer was drawing on top of
            // it). SegmentedHairCard.CurrentCutStartIndex IS that authoritative index already - reapplying
            // it directly via SetFirstHiddenSegmentIndex is a lossless resync with no rounding/clamping,
            // unlike going back through a length.
            for (int i = 0; i < _cachedSegmentedHairCards.Count; i++)
            {
                SegmentedHairCard segmentedCard = _cachedSegmentedHairCards[i];

                if (segmentedCard != null)
                    segmentedCard.SetFirstHiddenSegmentIndex(segmentedCard.CurrentCutStartIndex);
            }
        }

        // Restore the physical clip's own visual now that it's been removed from this zone.
        if (_attachedClipRenderers != null)
        {
            foreach (var r in _attachedClipRenderers)
            {
                if (r != null)
                    r.enabled = true;
            }
            _attachedClipRenderers = null;
        }
    }
    */
    private void PositionSphereInstance()
    {
        if (_sphereInstance == null)
            return;

        // Use the SOCKET's own fixed position (this GameObject), not wherever the physical clip object
        // happens to be sitting - guarantees it always shares the exact same position as the socket
        // regardless of the clip's precise snap position.
        Vector3 socketPosition = transform.position;
        Vector3 headCenter = _cachedHeadCenterWorld ?? socketPosition;
        Vector3 outwardDir = (socketPosition - headCenter);
        outwardDir = outwardDir.sqrMagnitude > 0.0001f ? outwardDir.normalized : Vector3.up;
        Vector3 desiredWorldPosition = socketPosition + outwardDir * outwardSafetyMargin + manualPositionOffset;

        _sphereInstance.transform.position = desiredWorldPosition;
        _sphereInstance.transform.localScale = Vector3.one * hairSphereScale;

        // Self-correcting fix: whatever prefab is assigned might have its own baked-in offset on its root
        // or children - instead of trusting the prefab's pivot to be meaningful, measure where its actual
        // visible mesh ended up and nudge the whole instance so that measured center lands exactly on the
        // desired position, regardless of any internal offset the prefab happens to have.
        var instanceRenderers = _sphereInstance.GetComponentsInChildren<MeshRenderer>(true);
        if (instanceRenderers.Length > 0)
        {
            Bounds combined = instanceRenderers[0].bounds;
            foreach (var r in instanceRenderers)
                combined.Encapsulate(r.bounds);

            Vector3 correction = desiredWorldPosition - combined.center;
            _sphereInstance.transform.position += correction;
        }
    }

    private void ApplyPreviewMaterial()
    {
        if (_sphereInstance == null || hoverPreviewMaterial == null)
            return;

        var renderers = _sphereInstance.GetComponentsInChildren<MeshRenderer>(true);

        if (_cachedOriginalSphereMaterials == null)
            _cachedOriginalSphereMaterials = new Dictionary<MeshRenderer, Material[]>();

        foreach (var r in renderers)
        {
            if (r == null)
                continue;

            if (!_cachedOriginalSphereMaterials.ContainsKey(r))
                _cachedOriginalSphereMaterials[r] = r.sharedMaterials;

            Material[] previewMaterials = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < previewMaterials.Length; i++)
                previewMaterials[i] = hoverPreviewMaterial;

            r.sharedMaterials = previewMaterials;
        }
    }

    private void RestoreOriginalMaterial()
    {
        if (_sphereInstance == null || _cachedOriginalSphereMaterials == null)
            return;

        foreach (var kvp in _cachedOriginalSphereMaterials)
        {
            if (kvp.Key != null)
                kvp.Key.sharedMaterials = kvp.Value;
        }
    }

    private void EnsureSphereInstanceExists()
    {
        if (_sphereInstance != null || hairSpherePrefab == null)
            return;

        _sphereInstance = Instantiate(hairSpherePrefab, transform);
        _sphereInstance.name = "HairSphereVisual_" + zone;
        _sphereInstance.SetActive(false);
    }

    private void ClearHoverPreview()
    {
        if (!_isPreviewOnly)
            return;

        _isPreviewOnly = false;

        if (_sphereInstance != null)
            _sphereInstance.SetActive(false);

        RestoreOriginalMaterial();
    }

    private void CacheRenderersIfNeeded()
    {
        if (_cachedRenderers != null)
            return;

        _cachedRenderers = new List<MeshRenderer>();
        _cachedHairCardDatas = new List<HairCardData>();
        _cachedSegmentedHairCards = new List<SegmentedHairCard>();

        if (hairCardsRoot == null)
            return;

        var allCards = hairCardsRoot.GetComponentsInChildren<HairCardData>(true);

        if (_cachedHeadCenterWorld == null && allCards.Length > 0)
        {
            Vector3 sum = Vector3.zero;
            foreach (var c in allCards)
                sum += c.transform.position;
            _cachedHeadCenterWorld = sum / allCards.Length;
        }

        var cardsInZone = allCards.Where(c => c.Section == zone);

        foreach (var card in cardsInZone)
        {
            _cachedHairCardDatas.Add(card);
            // Same GameObject as the HairCardData above - used on detach to correctly reapply this card's
            // true cut state instead of blindly re-enabling every cached renderer (see FIX comment above).
            _cachedSegmentedHairCards.Add(card.GetComponent<SegmentedHairCard>());

            // Include child renderers too - SegmentedHairCard generates the actual visible cut-up segments
            // at runtime under a child '_GeneratedHairSegments' object, which may be what's really drawing
            // once in Play mode, not just the card's own top-level MeshRenderer.
            var renderers = card.GetComponentsInChildren<MeshRenderer>(true);
            _cachedRenderers.AddRange(renderers);
        }
    }



}
