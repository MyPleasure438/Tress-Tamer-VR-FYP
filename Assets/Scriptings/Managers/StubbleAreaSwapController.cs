using UnityEngine;
using System.Collections.Generic;

// HANDOFF NOTE (Stubble area swap - Induction/Butch/Crew):
// Implements the decided per-area stubble plan:
//   - HairCards_CrewCut is fully shown from scene start (the passive "what's under the hair" baseline -
//     simply cutting a long hair card away reveals it) - BUT per-area it IS toggled off the moment that
//     area swaps to Butch or Induction (see GetRevealer() below). Crew and Butch/Induction cards occupy
//     the same physical spots, so leaving Crew on everywhere would show it double-layered under whatever
//     swapped in. HairCards_ButchCut and HairCards_InductionCut both start fully hidden.
//   - When a long hair card is cut down to its LAST segment (SegmentedHairCard.IsAtMinimumSegmentLength)
//     and that final segment is then removed:
//       * Clipper with the 1.5cm guard -> that specific area swaps to HairCards_ButchCut.
//       * Clipper with the 0.5cm guard -> that specific area swaps straight to HairCards_InductionCut.
//       * Scissor (no guard concept) -> capped at HairCards_ButchCut, same as the 1.5cm clipper result.
//       * Any other clipper guard size -> no special swap; the long hair still disappears (existing
//         allowFinalSegmentRemovalForStubbleReveal fallback in CuttingManager), just leaving the
//         always-on Crew stubble visible underneath, nothing extra to do.
//   - An area already swapped to Butch can be swapped FURTHER to Induction with a 0.5cm clipper pass
//     (Butch -> Induction). Scissor cannot do this. Induction is the floor - nothing cuts past it.
//   - Undo reverses these swaps exactly, bundled into the same HairUndoManager action as the cut that
//     triggered them (see CuttingManager.TryCutSegmentedHairHit for the call sites).
//   - While an area is swapped, HairCardData.CurrentLength for that card is overridden to 1.5cm (Butch)
//     or 0.5cm (Induction) in the SAME units the rest of the project already uses (meters) - this is a
//     deliberate reuse of the existing field so scoring/measurement/instructions all pick it up for
//     free, with no changes needed anywhere else.
//
// FIX (regression - "hair cards grow back to full length" when clippering the last segments):
//   Two real bugs were found and fixed here:
//     1. Instance was set in Awake() with no matching cleanup, so ticking this component's enabled
//        checkbox off did NOT actually stop CuttingManager from routing into this class's logic
//        (Awake always runs regardless of the enabled checkbox; only Start/Update/etc. are gated by
//        it) - added OnEnable/OnDisable/OnDestroy so "disabled" now really means disabled.
//     2. The old ghost-collider helper reached into a hair card segment's GameObject/Collider/Renderer
//        state directly from OUTSIDE SegmentedHairCard, bypassing that class's own combined-mesh
//        bookkeeping (RebuildCombinedVisibleMesh) entirely. That is now
//        SegmentedHairCard.EnableFirstSegmentGhostCollider() instead - it lives inside the class that
//        owns cardMeshRenderer/currentCutStartIndex, so it can never desync from them.
//     3. areaStates was keyed by GameObject NAME, which is not guaranteed unique across zones/prefabs -
//        a name collision would have silently conflated two unrelated cards' swap states. Now keyed by
//        the SegmentedHairCard instance itself.
//
// WIRING NEEDED IN UNITY (cannot be done from here - see chat for full instructions):
//   1. Add this component to a persistent GameObject (e.g. the same one CuttingManager/HairUndoManager
//      live on).
//   2. Drag HairCards_CrewCut/ButchCut/InductionCut's StubbleCardRevealer components into the three
//      revealer fields below (add StubbleCardRevealer to each if not already present).
//   3. Drag the SAME HairUndoManager instance that CuttingManager already uses into Hair Undo Manager -
//      it must be the identical instance, not a second copy, or undo actions will not bundle correctly.
//   4. Make sure a StubbleMatchData component (with its long_hair_to_stubble_mapping.json) exists
//      somewhere in the scene - this was set up in earlier work and should already be present.
//   5. IMPORTANT: if a StubbleRevealCoordinator GameObject is active anywhere in the scene, DISABLE it
//      (do not delete - just untick its checkbox). Its old "one active style, auto-reveal on any cut"
//      behavior directly conflicts with this new per-area, tool-specific system.
[DisallowMultipleComponent]
public class StubbleAreaSwapController : MonoBehaviour
{
    public static StubbleAreaSwapController Instance { get; private set; }

    private enum AreaState { None, Butch, Induction }

    [Header("Stubble Mesh Revealers")]
    [Tooltip("StubbleCardRevealer on HairCards_CrewCut. Fully shown at Start and never toggled per-area afterward - it is the always-on baseline.")]
    [SerializeField] private StubbleCardRevealer crewRevealer;
    [Tooltip("StubbleCardRevealer on HairCards_ButchCut.")]
    [SerializeField] private StubbleCardRevealer butchRevealer;
    [Tooltip("StubbleCardRevealer on HairCards_InductionCut.")]
    [SerializeField] private StubbleCardRevealer inductionRevealer;

    [Header("Undo Integration")]
    [Tooltip("Must be the SAME HairUndoManager instance CuttingManager uses, so swap undo bundles into the same action as the cut that triggered it.")]
    [SerializeField] private HairUndoManager hairUndoManager;

    [Header("Guard Matching")]
    [Tooltip("How close (in cm) a clipper guard needs to be to 1.5/0.5 to count as a match. Matches ClipperGuardController's own nearest-match tolerance style.")]
    [SerializeField] private float guardMatchToleranceCm = 0.05f;

    private const float ButchLengthCm = 1.5f;
    private const float InductionLengthCm = 0.5f;

    // Keyed by the SegmentedHairCard instance itself (not its GameObject name) - hair card names are not
    // guaranteed unique across zones/prefabs, and a name collision here would silently conflate two
    // unrelated cards' swap states (one card's Butch/Induction status leaking onto another's).
    private readonly Dictionary<SegmentedHairCard, AreaState> areaStates = new Dictionary<SegmentedHairCard, AreaState>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnEnable()
    {
        if (Instance == null)
            Instance = this;
    }

    private void OnDisable()
    {
        // So ticking this component's enabled checkbox off actually behaves like "disabled" for anyone
        // testing with it - CuttingManager only routes into this class's logic while Instance != null.
        if (Instance == this)
            Instance = null;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Start()
    {
        // No matter which hairstyle/stage was chosen, Crew is always the passive baseline underneath
        // everything; Butch/Induction only ever appear via an explicit swap below.
        if (crewRevealer != null)
        {
            crewRevealer.Initialize();
            crewRevealer.ShowAllCards();
        }

        if (butchRevealer != null)
        {
            butchRevealer.Initialize();
            butchRevealer.HideAllCards();
        }

        if (inductionRevealer != null)
        {
            inductionRevealer.Initialize();
            inductionRevealer.HideAllCards();
        }
    }

    /// <summary>
    /// Call the moment a long hair card newly reaches fully-invisible (0 visible segments) FROM having
    /// just been at its last segment. Decides Butch/Induction/none based on which tool/guard did the cut.
    /// Safe to call even if this card has no stubble match or isn't eligible - it just does nothing.
    /// </summary>
    public void TryApplyInitialSwap(SegmentedHairCard card, float? activeGuardOverrideLengthMeters)
    {
        if (card == null)
            return;

        if (GetState(card) != AreaState.None)
            return; // already swapped by an earlier cut - further changes go through TryHandleAlreadySwappedArea instead

        string key = card.gameObject.name;

        if (StubbleMatchData.Instance == null || !StubbleMatchData.Instance.TryGetStubbleIndices(key, out List<int> stubbleIndices) || stubbleIndices.Count == 0)
            return; // no baked stubble cards for this long hair card - nothing to swap to, Crew just shows through

        AreaState targetState;

        if (activeGuardOverrideLengthMeters.HasValue)
        {
            float guardCm = activeGuardOverrideLengthMeters.Value * 100f;

            if (IsNear(guardCm, ButchLengthCm))
                targetState = AreaState.Butch;
            else if (IsNear(guardCm, InductionLengthCm))
                targetState = AreaState.Induction;
            else
                return; // some other guard size - no special swap, long hair still disappears via the normal fallback
        }
        else
        {
            // Scissor (or anything else that never sets a guard length) - capped at Butch, per the plan.
            targetState = AreaState.Butch;
        }

        ApplySwap(card, stubbleIndices, AreaState.None, targetState);
    }

    /// <summary>
    /// Call when a cut hits a long hair card whose area is ALREADY swapped (Butch or Induction) - these
    /// cards keep a deliberately-re-enabled "ghost" collider on segment 0 specifically so further passes
    /// can still be detected even though the card itself has nothing left to visually cut. Returns true
    /// if this call fully handled the hit (whether or not anything actually changed), so the caller knows
    /// not to also run normal SegmentedHairCard cutting logic on it.
    /// </summary>
    public bool TryHandleAlreadySwappedArea(SegmentedHairCard card, float? activeGuardOverrideLengthMeters)
    {
        if (card == null)
            return false;

        AreaState currentState = GetState(card);

        if (currentState == AreaState.None)
            return false; // not swapped yet - let the normal/initial path handle this hit

        if (currentState == AreaState.Induction)
            return true; // already at the floor, nothing cuts past it - handled (no-op)

        // currentState == Butch here. Only a 0.5cm Clipper pass can push it further to Induction.
        if (!activeGuardOverrideLengthMeters.HasValue)
            return true; // Scissor touching an already-Butch area - capped, handled (no-op)

        float guardCm = activeGuardOverrideLengthMeters.Value * 100f;

        if (!IsNear(guardCm, InductionLengthCm))
            return true; // some other guard size on an already-Butch area - handled (no-op)

        string key = card.gameObject.name;

        if (StubbleMatchData.Instance == null || !StubbleMatchData.Instance.TryGetStubbleIndices(key, out List<int> stubbleIndices) || stubbleIndices.Count == 0)
            return true; // shouldn't happen (we only got into Butch state via a successful match), but stay safe

        ApplySwap(card, stubbleIndices, AreaState.Butch, AreaState.Induction);
        return true;
    }

    // FIX ("Crew still shows even after cutting" - coverage gap, not a toggle bug): the original mapping
    // gave each long hair card exactly ONE stubble quad out of 2865 total, so even a fully-correct
    // hide/reveal toggle only ever cleared a single tiny patch of Crew per cut - the other ~77% of the
    // Crew mesh had nothing driving it and could never hide. The mapping was regenerated so every long
    // card owns its full nearby CLUSTER of quads (see StubbleMatchData's handoff note), so stubbleIndices
    // here is now typically several indices, not one - this loops over all of them so a single cut clears
    // that card's entire physical footprint on the stubble mesh instead of one representative dot.
    private void ApplySwap(SegmentedHairCard card, List<int> stubbleIndices, AreaState fromState, AreaState toState)
    {
        StubbleCardRevealer fromRevealer = GetRevealer(fromState);
        StubbleCardRevealer toRevealer = GetRevealer(toState);

        int count = stubbleIndices.Count;
        bool[] fromWasVisible = new bool[count];
        bool[] toWasVisible = new bool[count];

        for (int i = 0; i < count; i++)
        {
            fromWasVisible[i] = fromRevealer != null && fromRevealer.IsCardVisible(stubbleIndices[i]);
            toWasVisible[i] = toRevealer != null && toRevealer.IsCardVisible(stubbleIndices[i]);
        }

        HairCardData data = card.HairCardData;
        float previousLengthMeters = data != null ? data.CurrentLength : 0f;

        if (hairUndoManager != null)
        {
            hairUndoManager.CaptureBeforeChange(() =>
            {
                for (int i = 0; i < count; i++)
                {
                    int idx = stubbleIndices[i];

                    if (fromRevealer != null)
                    {
                        if (fromWasVisible[i]) fromRevealer.RevealCard(idx);
                        else fromRevealer.HideCard(idx);
                    }

                    if (toRevealer != null)
                    {
                        if (toWasVisible[i]) toRevealer.RevealCard(idx);
                        else toRevealer.HideCard(idx);
                    }
                }

                if (data != null)
                    data.CurrentLength = previousLengthMeters;

                SetStateInternal(card, fromState);
            });
        }

        foreach (int idx in stubbleIndices)
        {
            if (fromRevealer != null)
                fromRevealer.HideCard(idx);

            if (toRevealer != null)
                toRevealer.RevealCard(idx);
        }

        if (data != null)
            data.CurrentLength = (toState == AreaState.Butch ? ButchLengthCm : InductionLengthCm) / 100f;

        SetStateInternal(card, toState);

        // Keep this card detectable for a possible FURTHER downgrade later (Butch -> Induction), even
        // though it has nothing left to visually cut - see class handoff note above. This now lives on
        // SegmentedHairCard itself (EnableFirstSegmentGhostCollider) so it can never desync from that
        // class's own combined-mesh/renderer bookkeeping the way the old external version could.
        card.EnableFirstSegmentGhostCollider();

        if (hairUndoManager != null)
            hairUndoManager.MarkCurrentActionChanged();
    }

    // FIX ("Crew stubble stays visible under Butch/Induction"): this used to return null for
    // AreaState.None on the assumption that Crew is a passive, always-on baseline that never needs
    // toggling. That assumption was wrong in practice - Crew's stubble cards are shown everywhere from
    // Start() (crewRevealer.ShowAllCards()) at the SAME physical spots the Butch/Induction cards occupy,
    // so when ApplySwap only revealed Butch/Induction but never hid the Crew card underneath, both ended
    // up visible in the same area simultaneously. Returning crewRevealer here means ApplySwap's normal
    // fromRevealer.HideCard(...)/toRevealer.RevealCard(...) pairing now correctly hides Crew in that
    // specific area the moment Butch/Induction takes over there (and correctly un-hides it again if an
    // Undo reverts the swap), instead of Crew being a special case that's never touched.
    private StubbleCardRevealer GetRevealer(AreaState state)
    {
        switch (state)
        {
            case AreaState.Butch: return butchRevealer;
            case AreaState.Induction: return inductionRevealer;
            default: return crewRevealer; // None = Crew - now toggled per-area just like Butch/Induction
        }
    }

    private AreaState GetState(SegmentedHairCard card)
    {
        return areaStates.TryGetValue(card, out AreaState state) ? state : AreaState.None;
    }

    // FEATURE (per-zone required stubble style scoring): EvaluationSystem needs to know which stubble
    // finish a card ACTUALLY ended up with, not just whether it's fully cut, so it can gate a 0-target
    // zone's "Correct" result on the developer-configured requirement (see
    // HaircutManager.TryGetRequiredStubbleStyle). AreaState.None genuinely does mean "achieved Crew" here
    // (not "not yet touched") whenever this is called from evaluation, because evaluation only ever asks
    // this for cards that are already confirmed fully cut (IsAtMinimumSegmentLength) - the only way to
    // reach that state while still AreaState.None is exactly "cut away with a guard that didn't match
    // Butch/Induction", which leaves the always-on Crew mesh showing through, per this class's own
    // TryApplyInitialSwap logic above.
    // FIX ("cannot cut the hair I previously cut at all" after pressing Reset Haircut): HaircutManager.
    // RestoreOriginalHairLength() only knows about SegmentedHairCard's own segments/colliders - it has no
    // idea this class exists, so it never cleared areaStates. Any card that had already been swapped to
    // Butch/Induction before the reset kept that STALE entry, even though its long hair was visually
    // restored to full length. TryHandleAlreadySwappedArea (called first, on every hit, for every segment
    // of the card) only checks GetState(card) - it has no idea the card is long-haired again - so it kept
    // intercepting every future cut on that card and silently no-opping it, forever, exactly matching the
    // report ("previously cut" cards specifically break; never-swapped ones are unaffected). Call this
    // from HaircutManager.RestoreOriginalHairLength() (or any other "reset the whole head" action) to
    // revert every currently-swapped area back to Crew and wipe the tracking dictionary clean, so a fresh
    // cutting pass behaves exactly like it would on a never-before-touched card.
    public void ResetAllSwaps()
    {
        if (areaStates.Count == 0)
            return;

        // Snapshot the keys first - the loop below only reads areaStates (via GetState), the actual
        // clearing happens once at the end, but this is defensive against future changes that might
        // mutate mid-loop.
        List<SegmentedHairCard> cards = new List<SegmentedHairCard>(areaStates.Keys);

        foreach (SegmentedHairCard card in cards)
        {
            if (card == null)
                continue;

            AreaState currentState = GetState(card);

            if (currentState == AreaState.None)
                continue;

            string key = card.gameObject.name;

            if (StubbleMatchData.Instance == null || !StubbleMatchData.Instance.TryGetStubbleIndices(key, out List<int> stubbleIndices) || stubbleIndices.Count == 0)
                continue;

            StubbleCardRevealer fromRevealer = GetRevealer(currentState);
            StubbleCardRevealer toRevealer = GetRevealer(AreaState.None);

            foreach (int idx in stubbleIndices)
            {
                if (fromRevealer != null)
                    fromRevealer.HideCard(idx);

                if (toRevealer != null)
                    toRevealer.RevealCard(idx);
            }
        }

        areaStates.Clear();
    }

    public RequiredStubbleStyle GetAchievedStubbleStyle(SegmentedHairCard card)
    {
        switch (GetState(card))
        {
            case AreaState.Butch: return RequiredStubbleStyle.ButchCut;
            case AreaState.Induction: return RequiredStubbleStyle.InductionCut;
            default: return RequiredStubbleStyle.CrewCut;
        }
    }

    private void SetStateInternal(SegmentedHairCard card, AreaState state)
    {
        if (state == AreaState.None)
            areaStates.Remove(card);
        else
            areaStates[card] = state;
    }

    private bool IsNear(float a, float b)
    {
        return Mathf.Abs(a - b) <= Mathf.Max(0.001f, guardMatchToleranceCm);
    }
}
