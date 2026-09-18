using System;
using System.Collections.Generic;
using UnityEngine;

// HANDOFF NOTE:
// Active performance path for hair shortening. Each card owns pre-generated segment objects;
// cutting hides/disables segments instead of slicing meshes at runtime. One-segment cards cannot get shorter.
public class SegmentedHairCard : MonoBehaviour
{
    [SerializeField] private HairCardData hairCardData;
    [SerializeField] private HairCardSegment[] segments;
    [SerializeField] private bool disableCutSegments = true;
    [SerializeField] private bool disableSegmentCollidersAfterCut = true;
    // Keep this enabled unless segment order is manually authored. Cutting assumes index 0 is closest to the root.
    [SerializeField] private bool autoSortSegmentsFromRootToTip = true;

    [Header("Performance: Combined Mesh Rendering")]
    [Tooltip("When on, currently-visible segments are combined into ONE mesh on this card's own MeshFilter/MeshRenderer instead of each segment rendering individually - massively reduces draw call count (~20 per card down to 1). Segment GameObjects/colliders still work exactly as before for cut detection; only rendering changes. Turn off to fall back to the original per-segment rendering if something looks wrong.")]
    [SerializeField] private bool useCombinedMeshRendering = true;

    private MeshFilter cardMeshFilter;
    private MeshRenderer cardMeshRenderer;
    private Mesh combinedMeshInstance;

    // THE FIX for the "clipper cut appears to grow back / never finishes" regression: this MUST survive
    // a Unity domain reload (triggered by any script recompile while still in Play mode - e.g. every
    // time an edited script gets pushed in and Auto Refresh picks it up mid-session). A domain reload
    // resets every non-serialized private field to its type default and then re-runs Awake() on the
    // revived object; it does NOT touch native engine state like Collider.enabled/Renderer.enabled. Since
    // this field previously was NOT serialized, Awake() re-ran InitializeSegments() -> unset progress
    // discovery -> GetFirstAlreadyCutSegmentIndex() on every reload, and under combined-mesh rendering
    // that always returned segments.Length ("nothing cut") because segment GameObjects are never
    // deactivated, only their own Renderer/Collider are toggled. The result: currentCutStartIndex silently
    // snapped back to "full" while the real collider states (and HairCardData.CurrentLength, which IS a
    // public/serialized field) still correctly reflected the true, mostly-cut state - a live-verified
    // desync between what this class thinks is cut and what actually is. Serializing this field means it
    // survives the reload directly, so there is nothing left to (mis)recompute.
    [SerializeField, HideInInspector] private int currentCutStartIndex;

    // Gates the ONE-TIME discovery in InitializeSegments() below. False the very first time this instance
    // is ever initialized (fresh placement / fresh Play session start) - at that point currentCutStartIndex
    // legitimately needs to be discovered from scratch. True for every Awake() afterward (including ones
    // caused by a mid-session domain reload), since currentCutStartIndex above already holds the correct,
    // serialized-and-therefore-reload-surviving value and must not be overwritten again.
    [SerializeField, HideInInspector] private bool hasInitializedCutState;

    // Fires exactly once when this card newly reaches minimum segment length (fully cut for gameplay purposes).
    public event Action OnReachedMinimumLength;
    private bool hasFiredMinimumLengthEvent;

    // FEATURE (Comb Hold): while true, CutFromSegmentIndex refuses to remove anything from the root up to
    // heldMinimumKeptSegmentIndex, no matter which tool/caller drives the cut - lets Comb "grip" this card
    // at a specific point so the player can keep scissor-cutting above the grip without ever cutting the
    // held portion away or the card "breaking" free of its root. Not serialized - this is a live, momentary
    // interaction state set by Comb each time the player toggles hold, not authored data.
    private bool isHeldByComb;
    private int heldMinimumKeptSegmentIndex;

    public bool IsHeldByComb => isHeldByComb;
    public int HeldMinimumKeptSegmentIndex => heldMinimumKeptSegmentIndex;

    public HairCardData HairCardData
    {
        get
        {
            if (hairCardData == null)
                hairCardData = GetComponent<HairCardData>();

            return hairCardData;
        }
    }

    public HairCardSegment[] Segments => segments;

    public int CurrentCutStartIndex => currentCutStartIndex;

    public bool IsAtMinimumSegmentLength
    {
        get
        {
            return segments != null && segments.Length > 0 && currentCutStartIndex <= 1;
        }
    }

    private void Awake()
    {
        cardMeshFilter = GetComponent<MeshFilter>();
        cardMeshRenderer = GetComponent<MeshRenderer>();
        InitializeSegments();
    }

    private void OnValidate()
    {
        InitializeSegments();
    }

    public void SetSegments(HairCardSegment[] newSegments)
    {
        // FIX ("some cards can't be cut past a certain point after re-segmenting to a different segment
        // count", e.g. FringeRight.009/.010 stuck cutting only their first ~20% after going from 20
        // segments to 100): SetSegments is how HairCardSegmentBatchSetup hands over a BRAND NEW segments
        // array (different count, different GameObjects) whenever cards are regenerated. hasInitializedCutState
        // is meant to distinguish "genuinely first-ever setup" from "domain reload replaying the SAME
        // segments" (see the field's own comment below) - but a regenerate is neither of those: it's a new
        // segment set that just happens to arrive on an instance whose hasInitializedCutState was already
        // true from the PREVIOUS (now-replaced) segment array. Without this reset, InitializeSegments() left
        // currentCutStartIndex at whatever stale value it had under the old segment count (e.g. 20, from
        // when the card had 20 segments) - so under the new 100-segment array, everything from index 20
        // onward silently read as "already cut" and CutFromSegmentIndex() refused to touch it, even though
        // the visible mesh still showed the full uncut card. Resetting this here forces InitializeSegments()
        // to rediscover currentCutStartIndex fresh against the NEW segments (via GetFirstAlreadyCutSegmentIndex(),
        // which correctly finds "nothing cut yet" for freshly-generated segments since their colliders all
        // start enabled) instead of carrying over a count that no longer means anything.
        hasInitializedCutState = false;
        segments = newSegments;
        InitializeSegments();
    }

    private void InitializeSegments()
    {
        if (segments == null)
        {
            if (!hasInitializedCutState)
                currentCutStartIndex = 0;

            return;
        }

        if (autoSortSegmentsFromRootToTip)
            SortSegmentsFromRootToTip();

        // Only discover the cut progress from scratch the FIRST time this instance is ever initialized -
        // every subsequent Awake()/OnValidate() call (including ones from a domain reload mid-Play-session)
        // must leave the already-correct, serialized currentCutStartIndex alone. See the field's own comment
        // above for the full story of why this matters.
        if (!hasInitializedCutState)
        {
            currentCutStartIndex = GetFirstAlreadyCutSegmentIndex();
            hasInitializedCutState = true;
        }

        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] == null)
                continue;

            segments[i].Owner = this;
            segments[i].SegmentIndex = i;
        }
    }

    [ContextMenu("Sort Segments From Root To Tip")]
    public void SortSegmentsFromRootToTip()
    {
        if (segments == null || segments.Length <= 1)
            return;

        Vector3 root = GetSortingRootPosition();

        Array.Sort(segments, (a, b) =>
        {
            if (a == b)
                return 0;

            if (a == null)
                return 1;

            if (b == null)
                return -1;

            float aDistance = (GetSegmentWorldCenter(a) - root).sqrMagnitude;
            float bDistance = (GetSegmentWorldCenter(b) - root).sqrMagnitude;
            return aDistance.CompareTo(bDistance);
        });

        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] == null)
                continue;

            segments[i].Owner = this;
            segments[i].SegmentIndex = i;
        }
    }

    // Called by Comb when the player toggles "hold" while touching this card - segmentIndexAtHoldPoint is
    // the segment nearest the comb's grip point (root = 0, tip = Segments.Length). While held, every cut
    // path (direct HairCardSegment.Cut(), CutFromSegmentIndexRespectingTarget's guard/target cuts, scissor
    // cuts routed through CuttingManager) is clamped in CutFromSegmentIndex below to never remove anything
    // from the root up to that point, so the card can't be cut shorter than where it's being physically
    // held no matter which tool is doing the cutting. Pass held=false to release (e.g. toggling off, or the
    // comb being dropped/let go).
    public void SetHeldByComb(bool held, int segmentIndexAtHoldPoint = 0)
    {
        isHeldByComb = held;
        heldMinimumKeptSegmentIndex = held && segments != null
            ? Mathf.Clamp(segmentIndexAtHoldPoint, 0, segments.Length)
            : 0;
    }

    // Given a world-space point roughly along this card (e.g. where the comb is currently touching it),
    // returns the nearest segment index (root = 0, tip = Segments.Length) - used by Comb to work out both
    // where to grip (SetHeldByComb) and how long the held portion currently is, without needing to know
    // anything about this card's internal segment layout itself.
    public int GetNearestSegmentIndexToWorldPoint(Vector3 worldPoint)
    {
        if (segments == null || segments.Length == 0)
            return 0;

        HairCardData data = HairCardData;

        if (data == null || data.OriginalLength <= 0f)
            return 0;

        Vector3 root = data.GetRootPosition();
        Vector3 growthDirection = data.GetGrowthDirection();
        float distanceAlongGrowth = Vector3.Dot(worldPoint - root, growthDirection);
        float ratio = Mathf.Clamp01(distanceAlongGrowth / data.OriginalLength);

        return Mathf.Clamp(Mathf.RoundToInt(ratio * segments.Length), 0, segments.Length);
    }

    public bool CutFromSegmentIndex(int segmentIndex)
    {
        if (segments == null || segments.Length == 0)
            return false;

        segmentIndex = Mathf.Clamp(segmentIndex, 0, segments.Length - 1);

        // FEATURE (Comb Hold): never let a cut reach past the comb's grip point while held - see
        // SetHeldByComb above for the full rationale. This is the single choke point every cut path
        // (direct, guard/target-respecting, scissor-via-CuttingManager) funnels through, so enforcing the
        // floor here alone protects the held portion consistently no matter which caller is cutting.
        if (isHeldByComb)
            segmentIndex = Mathf.Max(segmentIndex, heldMinimumKeptSegmentIndex);

        if (segmentIndex >= currentCutStartIndex)
            return false;

        bool cutSomething = false;

        for (int i = segmentIndex; i < currentCutStartIndex; i++)
        {
            HairCardSegment segment = segments[i];

            if (segment == null || !segment.gameObject.activeSelf)
                continue;

            if (useCombinedMeshRendering)
            {
                // Keep GameObject active (colliders need this) - segment's own renderer is already
                // permanently off under this mode, only the collider needs toggling here.
                if (disableSegmentCollidersAfterCut)
                    DisableColliders(segment);
            }
            else
            {
                if (disableSegmentCollidersAfterCut)
                    DisableColliders(segment);

                if (disableCutSegments)
                    segment.gameObject.SetActive(false);
            }

            cutSomething = true;
        }

        if (!cutSomething)
            return false;

        currentCutStartIndex = segmentIndex;

        if (useCombinedMeshRendering)
            RebuildCombinedVisibleMesh(segmentIndex);

        UpdateHairLength(segmentIndex);
        return true;
    }

    public bool CutFromSegmentIndexRespectingTarget(int segmentIndex, float targetLength, float tolerance)
    {
        if (segments == null || segments.Length == 0)
            return false;

        HairCardData data = HairCardData;

        if (data == null || data.OriginalLength <= 0f || targetLength <= 0f)
            return CutFromSegmentIndex(segmentIndex);

        // FIX ("clipper guard can't cut certain cards no matter what"): with only `segments.Length`
        // discrete cut points on a card, a tight caller-supplied tolerance (CuttingManager's guard
        // tolerance defaults to 1mm) can land JUST past the nearest achievable segment boundary - e.g. a
        // card with ~7mm segments and a 1.5cm target can compute a minimum-kept-length that's 0.1mm below
        // the tolerance floor, so CeilToInt rounds up to the NEXT segment out, one full ~7mm increment
        // farther from the target than necessary. Once currentCutStartIndex reaches that over-rounded
        // index, every further cut recomputes the exact same blocked index forever - permanently stuck,
        // for any card whose length just happens not to divide evenly against the target and a 1mm
        // tolerance. Widening the effective tolerance to at least half a segment's length means the target
        // always resolves to whichever segment boundary is genuinely NEAREST it, not just the nearest one
        // that's conservatively never-shorter - matching what a player expects a guard to settle at, and
        // matching how every other card that happens to round more kindly already behaves.
        float segmentLength = data.OriginalLength / segments.Length;
        float safeTolerance = Mathf.Max(Mathf.Max(0f, tolerance), segmentLength * 0.5f);
        float minimumAllowedLength = Mathf.Max(0f, targetLength - safeTolerance);

        if (data.CurrentLength <= targetLength + safeTolerance)
            return false;

        float minimumKeptRatio = Mathf.Clamp01(minimumAllowedLength / data.OriginalLength);
        int minimumKeptSegmentIndex = Mathf.CeilToInt(minimumKeptRatio * segments.Length);
        minimumKeptSegmentIndex = Mathf.Clamp(minimumKeptSegmentIndex, 0, segments.Length - 1);

        return CutFromSegmentIndex(Mathf.Max(segmentIndex, minimumKeptSegmentIndex));
    }

    public void SetVisibleLength(float targetLength)
    {
        if (segments == null || segments.Length == 0)
            return;

        HairCardData data = HairCardData;

        if (data == null || data.OriginalLength <= 0f)
            return;

        // A target of (effectively) zero means the card should be fully hidden - bypass the normal
        // minimum-1-segment safety clamp below entirely for this specific case, so the card actually
        // disappears instead of always leaving one segment visible.
        if (targetLength <= 0.0001f)
        {
            SetFirstHiddenSegmentIndex(0);
            return;
        }

        float keptRatio = Mathf.Clamp01(targetLength / data.OriginalLength);
        int firstHiddenSegmentIndex = Mathf.RoundToInt(keptRatio * segments.Length);
        // Minimum of 1 for any genuinely non-zero target, so small-but-real target lengths still show
        // at least a sliver of hair rather than accidentally rounding down to nothing.
        firstHiddenSegmentIndex = Mathf.Clamp(firstHiddenSegmentIndex, 1, segments.Length);
        SetFirstHiddenSegmentIndex(firstHiddenSegmentIndex);
    }

    public void SetFirstHiddenSegmentIndex(int firstHiddenSegmentIndex)
    {
        if (segments == null || segments.Length == 0)
            return;

        firstHiddenSegmentIndex = Mathf.Clamp(firstHiddenSegmentIndex, 0, segments.Length);

        for (int i = 0; i < segments.Length; i++)
        {
            HairCardSegment segment = segments[i];

            if (segment == null)
                continue;

            bool shouldBeVisible = i < firstHiddenSegmentIndex;

            if (useCombinedMeshRendering)
            {
                // Keep the segment GameObject active always (colliders need this for cut detection to
                // keep working exactly as before) - only its OWN renderer gets permanently disabled,
                // since rendering now happens via one combined mesh on this card instead.
                segment.gameObject.SetActive(true);

                MeshRenderer segmentRenderer = segment.GetComponent<MeshRenderer>();
                if (segmentRenderer != null)
                    segmentRenderer.enabled = false;
            }
            else
            {
                // Original behavior, unchanged - full fallback if combined mesh rendering is turned off.
                segment.gameObject.SetActive(shouldBeVisible);
            }

            Collider[] colliders = segment.GetComponents<Collider>();

            for (int colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++)
                colliders[colliderIndex].enabled = shouldBeVisible;
        }

        currentCutStartIndex = firstHiddenSegmentIndex;

        if (useCombinedMeshRendering)
            RebuildCombinedVisibleMesh(firstHiddenSegmentIndex);

        UpdateHairLength(firstHiddenSegmentIndex);
    }

    // Combines the geometry of all currently-visible segments (index < firstHiddenSegmentIndex) into ONE
    // mesh assigned to this card's OWN MeshFilter/MeshRenderer, instead of each segment rendering
    // individually. Rebuilt only when the visible count actually changes (i.e. on cut events), not
    // every frame - this is the core of the draw-call reduction.
    private void RebuildCombinedVisibleMesh(int firstHiddenSegmentIndex)
    {
        // Lazy fallback in case Awake() hasn't run yet on this instance (e.g. testing via Editor script
        // execution on an already-loaded scene object, where Awake doesn't automatically re-run just
        // because the script was recompiled) - harmless no-op during normal Play mode where Awake already
        // populated these correctly.
        if (cardMeshFilter == null)
            cardMeshFilter = GetComponent<MeshFilter>();
        if (cardMeshRenderer == null)
            cardMeshRenderer = GetComponent<MeshRenderer>();

        if (cardMeshFilter == null || cardMeshRenderer == null)
            return;

        if (firstHiddenSegmentIndex <= 0)
        {
            // Nothing visible - just hide the combined renderer rather than building an empty mesh.
            cardMeshRenderer.enabled = false;
            return;
        }

        List<CombineInstance> combineInstances = new List<CombineInstance>();

        for (int i = 0; i < firstHiddenSegmentIndex && i < segments.Length; i++)
        {
            HairCardSegment segment = segments[i];
            if (segment == null)
                continue;

            MeshFilter segmentMeshFilter = segment.GetComponent<MeshFilter>();
            if (segmentMeshFilter == null || segmentMeshFilter.sharedMesh == null)
                continue;

            CombineInstance ci = new CombineInstance();
            ci.mesh = segmentMeshFilter.sharedMesh;
            // Relative transform from this segment to THIS card's own transform, since the combined
            // mesh will be assigned to this card's own MeshFilter (its local space).
            ci.transform = transform.worldToLocalMatrix * segment.transform.localToWorldMatrix;
            combineInstances.Add(ci);
        }

        if (combineInstances.Count == 0)
        {
            cardMeshRenderer.enabled = false;
            return;
        }

        if (combinedMeshInstance == null)
        {
            combinedMeshInstance = new Mesh();
            combinedMeshInstance.name = gameObject.name + "_CombinedVisible";
        }
        else
        {
            combinedMeshInstance.Clear();
        }

        combinedMeshInstance.CombineMeshes(combineInstances.ToArray(), mergeSubMeshes: true, useMatrices: true);
        cardMeshFilter.sharedMesh = combinedMeshInstance;
        cardMeshRenderer.enabled = true;
    }

    public bool IsTargetBelowMinimumSegmentLength(float targetLength, float tolerance)
    {
        HairCardData data = HairCardData;

        if (data == null || data.OriginalLength <= 0f || segments == null || segments.Length == 0)
            return false;

        float minimumSegmentLength = data.OriginalLength / segments.Length;
        return targetLength + Mathf.Max(0f, tolerance) <= minimumSegmentLength;
    }

    public void RestoreAllSegments()
    {
        if (segments == null)
            return;

        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] == null)
                continue;

            segments[i].gameObject.SetActive(true);
            Collider[] colliders = segments[i].GetComponents<Collider>();

            for (int colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++)
                colliders[colliderIndex].enabled = true;
        }

        currentCutStartIndex = segments.Length;
        hasFiredMinimumLengthEvent = false;

        HairCardData data = HairCardData;
        if (data != null)
            data.CurrentLength = data.OriginalLength;
    }

    public void RestoreSegmentRuntimeState(int firstHiddenSegmentIndex, bool[] segmentActiveStates, bool[][] colliderEnabledStates)
    {
        if (segments == null || segments.Length == 0)
            return;

        currentCutStartIndex = Mathf.Clamp(firstHiddenSegmentIndex, 0, segments.Length);

        for (int i = 0; i < segments.Length; i++)
        {
            HairCardSegment segment = segments[i];

            if (segment == null)
                continue;

            if (segmentActiveStates != null && i < segmentActiveStates.Length)
                segment.gameObject.SetActive(segmentActiveStates[i]);

            Collider[] colliders = segment.GetComponents<Collider>();

            for (int colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++)
            {
                if (colliderEnabledStates == null ||
                    i >= colliderEnabledStates.Length ||
                    colliderEnabledStates[i] == null ||
                    colliderIndex >= colliderEnabledStates[i].Length)
                    continue;

                colliders[colliderIndex].enabled = colliderEnabledStates[i][colliderIndex];
            }
        }

        // Was previously nested INSIDE the loop above (so it ran once per segment instead of once total) -
        // harmless when restoring to a fixed index since every call passes the same value, but wrong in
        // spirit and wasteful. Moved out so this always runs exactly once per state change, matching every
        // other caller of RebuildCombinedVisibleMesh in this file.
        if (useCombinedMeshRendering)
            RebuildCombinedVisibleMesh(currentCutStartIndex);
    }

    // Card must already be fully hidden (currentCutStartIndex == 0, combined mesh renderer off) before
    // calling this. Re-enables ONLY segment 0's collider - keeping its own renderer off and explicitly
    // re-asserting that this card's own combined-mesh renderer stays off - so a future cut pass over this
    // spot can still be detected (e.g. for a Butch -> Induction stubble downgrade) without ever making the
    // card visible again. Deliberately lives here rather than in an external caller so it can touch
    // cardMeshRenderer directly instead of reaching into this card's rendering state from outside and
    // risking it going out of sync with this class's own bookkeeping.
    public void EnableFirstSegmentGhostCollider()
    {
        if (segments == null || segments.Length == 0 || segments[0] == null)
            return;

        if (currentCutStartIndex != 0)
            return;

        HairCardSegment segment = segments[0];
        segment.gameObject.SetActive(true);

        MeshRenderer segmentRenderer = segment.GetComponent<MeshRenderer>();
        if (segmentRenderer != null)
            segmentRenderer.enabled = false;

        Collider[] colliders = segment.GetComponents<Collider>();
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = true;

        if (cardMeshRenderer == null)
            cardMeshRenderer = GetComponent<MeshRenderer>();

        if (cardMeshRenderer != null)
            cardMeshRenderer.enabled = false;
    }

    private void DisableColliders(HairCardSegment segment)
    {
        Collider[] colliders = segment.GetComponents<Collider>();

        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = false;
    }

    private int GetFirstAlreadyCutSegmentIndex()
    {
        if (segments == null || segments.Length == 0)
            return 0;

        // Defense in depth for the one-time discovery above: under combined-mesh rendering, a cut segment's
        // GameObject is deliberately kept ACTIVE forever (only its own Renderer/Collider get disabled - see
        // the Header("Performance: Combined Mesh Rendering") tooltip on useCombinedMeshRendering), so
        // GameObject.activeSelf can never be used to detect a cut segment in that mode. Check whether it has
        // a live collider instead, which is the real signal of "still cuttable" either way.
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] == null)
                return i;

            if (useCombinedMeshRendering)
            {
                if (!HasAnyEnabledCollider(segments[i]))
                    return i;
            }
            else if (!segments[i].gameObject.activeSelf)
            {
                return i;
            }
        }

        return segments.Length;
    }

    private bool HasAnyEnabledCollider(HairCardSegment segment)
    {
        Collider[] colliders = segment.GetComponents<Collider>();

        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i].enabled)
                return true;
        }

        return false;
    }

    private Vector3 GetSortingRootPosition()
    {
        HairCardData data = HairCardData;

        if (data != null)
            return data.GetRootPosition();

        return transform.position;
    }

    private Vector3 GetSegmentWorldCenter(HairCardSegment segment)
    {
        Renderer renderer = segment.GetComponent<Renderer>();

        if (renderer != null)
            return renderer.bounds.center;

        Collider collider = segment.GetComponent<Collider>();

        if (collider != null)
            return collider.bounds.center;

        MeshFilter meshFilter = segment.GetComponent<MeshFilter>();

        if (meshFilter != null && meshFilter.sharedMesh != null)
            return segment.transform.TransformPoint(meshFilter.sharedMesh.bounds.center);

        return segment.transform.position;
    }

    private void UpdateHairLength(int cutSegmentIndex)
    {
        HairCardData data = HairCardData;

        if (data == null || data.OriginalLength <= 0f || segments == null || segments.Length == 0)
            return;

        float keptRatio = Mathf.Clamp01((float)cutSegmentIndex / segments.Length);
        data.CurrentLength = data.OriginalLength * keptRatio;

        if (IsAtMinimumSegmentLength && !hasFiredMinimumLengthEvent)
        {
            hasFiredMinimumLengthEvent = true;
            OnReachedMinimumLength?.Invoke();
        }
    }
}
