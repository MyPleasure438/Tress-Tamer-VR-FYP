using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit;
using System.Collections.Generic;
using TMPro;

// HANDOFF NOTE:
// Active comb/styling tool. It bends hair cards along comb movement and normally should not require a button in VR.
// Preferred interaction area is combInteractionBoxes covering the comb teeth; fallback radius is for quick testing.
//
// HOLD FEATURE: while the comb's interaction boxes are touching a hair card, the Activate button (the SAME
// physical VR trigger XRGrabInteractable already uses for every other tool - e.g. Scissor.TriggerCut()) grips
// that card at the exact point being touched, so it can't be cut any shorter than that point (protects the
// root-side portion) while the player keeps using Scissor/Clipper. Which physical button that is depends only
// on which hand is currently holding the Comb - there's no separate/conflicting binding, because Activate is
// read from whichever hand's interactor is selecting this object, exactly like Scissor/Clipper/HairClip.
//
// Unlike Scissor (which needs its Activated event wired to TriggerCut() by hand in the Inspector), this script
// subscribes to grabInteractable.activated/deactivated itself in OnEnable/OnDisable - no Inspector UnityEvent
// wiring required.
//
// Two interaction modes (see Hold Interaction Mode below):
//  - Hold To Use (default): press and hold Activate to grip, release to let go. While the button is held down
//    and nothing is gripped yet, it keeps retrying every frame - this also makes gripping feel more reliable
//    if the comb wasn't exactly touching hair on the exact frame the button was pressed.
//  - Toggle: press once to grip, press again (any time) to release.
//
// While held, a small floating label shows two lines: "Current Longest: X.X cm" - the longest root-to-comb
// -point length among ALL hair cards currently inside the comb's interaction boxes (not just the one
// actually gripped) - and "Target Longest: Y.Y cm" - that same card's HaircutManager-resolved target length
// (needs Haircut Manager assigned below; shows "-" if it has none) - so the player can see at a glance both
// what they're about to grab/cut AND how far it still is from its target as they move the comb around.
//
// FREEZE: while the hold feature is engaged (toggled on, or the Activate button held down in Hold To Use
// mode), every hair card currently touching the comb's interaction boxes - not just the one actually gripped
// - is frozen completely in place via CuttingManager.SetHairCardFrozenForComb(), so moving the comb around
// while holding does not bend/comb them at all. The instant a card leaves the boxes it's unfrozen again and
// resumes normal behaviour, including settling back toward its original pose if it had been combed before.
public class Comb : BaseTool
{
    public enum CombHoldInteractionMode
    {
        [InspectorName("Hold To Use (press and hold to grip, release to let go)")]
        HoldToUse,
        [InspectorName("Toggle (press to grip, press again to release)")]
        Toggle
    }

    [Header("Grab State")]
    [Tooltip("Auto-found on Awake if left empty. Combing only happens while this comb is actually being held - prevents accidental combing from a comb sitting on a table/floor.")]
    [SerializeField] private XRGrabInteractable grabInteractable;

    [Header("Cutting/Styling Engine Links")]
    [Tooltip("Drag the Hair GameObject with the CuttingManager attached here")]
    [SerializeField] private CuttingManager cuttingManager;

    [Tooltip("Your existing HaircutManager - needed for the 'Target Longest' readout, which shows the resolved target length for whichever card is currently the longest under the comb's boxes. Auto-found via FindObjectOfType if left empty.")]
    [SerializeField] private HaircutManager haircutManager;

    [Tooltip("Create an empty GameObject at the center of the comb's teeth and drag it here")]
    [SerializeField] private Transform combCenter;

    [Tooltip("Optional. Assign narrow trigger/box colliders covering the comb teeth for more accurate combing.")]
    [SerializeField] private BoxCollider[] combInteractionBoxes;

    [Header("Comb Settings")]
    [Tooltip("How strongly the comb's movement bends/repositions hair cards it passes through. If the comb feels 'hard to move hair cards around', raise this. If hair reacts too violently to small movements, lower it. Also see CuttingManager's 'Visual Comb Max Degrees Per Frame' - that caps how fast a card can visually rotate per frame regardless of this strength.")]
    [SerializeField] private float combStrength = 0.5f;
    [Tooltip("Only used when Use Interaction Boxes is off or no boxes are assigned - radius of a simple sphere check around Comb Center for both combing and hold-grabbing.")]
    [SerializeField] private float fallbackInteractionRadius = 0.05f;
    [Tooltip("Minimum distance (meters) the comb must move between frames before it counts as a combing stroke - filters out tiny jitter so the comb doesn't bend hair from hand tremor alone.")]
    [SerializeField] private float minimumMovementToComb = 0.001f;
    [Tooltip("On (recommended): use Comb Interaction Boxes for precise combing and hold-grabbing around the teeth. Off: fall back to a simple sphere at Comb Center using Fallback Interaction Radius.")]
    [SerializeField] private bool useInteractionBoxes = true;

    [Header("Input")]
    [Tooltip("Off is better for VR: the comb works when it moves through hair. On is useful for mouse testing.")]
    [SerializeField] private bool requireLeftMouseButton = false;
    [Tooltip("Desktop/editor testing only: mirrors whatever VR controller input drives - in Hold To Use mode, holding this key down grips/releases like the trigger; in Toggle mode, each press toggles hold.")]
    [SerializeField] private Key testToggleHoldKey = Key.H;

    [Header("Hold (grip a hair card at a point, protects it from being cut shorter)")]
    [Tooltip("Master switch for the hold feature. Turn off to disable it entirely without removing the wiring.")]
    [SerializeField] private bool holdFeatureEnabled = true;
    [Tooltip("Hold To Use (recommended, matches how the VR trigger feels for every other tool): press and hold Activate to grip a hair card, release to let go. Toggle: press once to grip, press again to release - the old behaviour.")]
    [SerializeField] private CombHoldInteractionMode holdInteractionMode = CombHoldInteractionMode.HoldToUse;
    [Tooltip("Extra search padding (meters), added ONLY when looking for a hair card to grip - does not change Comb Interaction Boxes' visible size or the actual combing area. If the comb 'sometimes is difficult to hold hair cards in place', raise this (e.g. 0.01-0.02) to make gripping more forgiving. 0 = exact box bounds only.")]
    [SerializeField] private float holdGrabBoxPadding = 0.01f;

    [Header("Held Length Readout")]
    [Tooltip("Optional. World-space TextMeshPro label shown while a card is held, displaying 'Current Longest' (the longest root-to-touch-point length among all hair cards currently inside the comb's interaction boxes, not just the one actually gripped) and 'Target Longest' (that same card's resolved target length). Auto-created next to the comb if left empty.")]
    [SerializeField] private TextMeshPro heldLengthLabel;
    [Tooltip("Local offset (from the comb's own transform) the auto-created label sits at.")]
    [SerializeField] private Vector3 heldLengthLabelLocalOffset = new Vector3(0f, 0.04f, 0f);
    [SerializeField] private float heldLengthLabelFontSize = 3f;
    [SerializeField] private Color heldLengthLabelColor = Color.yellow;

    private Vector3 lastPosition;
    private bool hasLastPosition;

    private bool isCombing;
    private bool isActivateButtonHeld;

    private SegmentedHairCard heldHairCard;
    private HairCardData heldHairCardData;
    private Vector3 heldWorldPoint;

    // FEATURE (Freeze): every hair card currently frozen because it's touching the comb's boxes while hold
    // is engaged, and a scratch buffer reused each frame to avoid per-frame allocations.
    private readonly HashSet<SegmentedHairCard> currentlyFrozenHairCards = new HashSet<SegmentedHairCard>();
    private readonly HashSet<SegmentedHairCard> frozenScanBuffer = new HashSet<SegmentedHairCard>();

    public bool IsHoldingHairCard => heldHairCard != null;
    // Length from the held card's root to the comb's actual grip point - NOT the card's full/original length.
    public float HeldLengthMeters { get; private set; }
    public float HeldLengthCentimeters => HeldLengthMeters * 100f;

    // Longest root-to-touch-point length among ALL hair cards currently inside the comb's interaction boxes
    // (not just the gripped one) - what the "Current Longest" label line shows. Falls back to
    // HeldLengthMeters if nothing is currently detected inside the boxes (e.g. no boxes assigned).
    public float LongestHeldLengthMeters { get; private set; }
    public float LongestHeldLengthCentimeters => LongestHeldLengthMeters * 100f;

    // HaircutManager's resolved target length for whichever card LongestHeldLengthMeters came from - what
    // the "Target Longest" label line shows. -1 (HasLongestHeldTarget false) if no target applies to that
    // card, or no card was found at all.
    public float LongestHeldTargetLengthMeters { get; private set; } = -1f;
    public float LongestHeldTargetLengthCentimeters => LongestHeldTargetLengthMeters * 100f;
    public bool HasLongestHeldTarget => LongestHeldTargetLengthMeters >= 0f;

    private void Awake()
    {
        if (grabInteractable == null)
            grabInteractable = GetComponent<XRGrabInteractable>();

        if (haircutManager == null)
            haircutManager = FindAnyObjectByType<HaircutManager>();
    }

    private void OnEnable()
    {
        if (grabInteractable != null)
        {
            grabInteractable.activated.AddListener(OnActivated);
            grabInteractable.deactivated.AddListener(OnDeactivated);
        }
    }

    private void OnDisable()
    {
        if (grabInteractable != null)
        {
            grabInteractable.activated.RemoveListener(OnActivated);
            grabInteractable.deactivated.RemoveListener(OnDeactivated);
        }

        isActivateButtonHeld = false;
        ReleaseHeldHairCard();
    }

    // Fires when the Activate button (the same physical trigger every other tool's XRGrabInteractable uses)
    // is pressed down while this comb is the thing currently held in a hand.
    private void OnActivated(ActivateEventArgs args)
    {
        isActivateButtonHeld = true;

        if (!holdFeatureEnabled || !IsCurrentlyHeld())
            return;

        if (holdInteractionMode == CombHoldInteractionMode.Toggle)
        {
            ToggleHoldOnTouchedHairCard();
            return;
        }

        // Hold To Use: try to grip immediately. If nothing's touched yet, Update() keeps retrying every
        // frame the button stays held, so gripping doesn't depend on hitting the exact press-frame.
        TryBeginHold();
    }

    // Fires when the Activate button is released.
    private void OnDeactivated(DeactivateEventArgs args)
    {
        isActivateButtonHeld = false;

        if (holdInteractionMode == CombHoldInteractionMode.HoldToUse)
            ReleaseHeldHairCard();
    }

    void Update()
    {
        UpdateHeldLengthLabelTransform();

        HandleDesktopTestKey();

        // Hold To Use: keep trying to grip every frame the button is held down and nothing is gripped yet -
        // this is what makes holding feel more reliable if the comb wasn't touching hair on the exact press frame.
        if (holdFeatureEnabled && holdInteractionMode == CombHoldInteractionMode.HoldToUse && isActivateButtonHeld && heldHairCard == null && IsCurrentlyHeld())
            TryBeginHold();

        // Setting the comb down releases whatever it was holding - a comb sitting on the table/floor
        // shouldn't keep protecting hair the player isn't actively working on anymore.
        if (heldHairCard != null && !IsCurrentlyHeld())
            ReleaseHeldHairCard();

        if (heldHairCard != null)
        {
            RecomputeLongestHeldLength();
            UpdateFrozenHairCardsWhileHolding();
        }

        if (cuttingManager == null || combCenter == null)
            return;

        bool shouldComb = ShouldCombThisFrame();

        if (!shouldComb)
        {
            isCombing = false;
            hasLastPosition = false;
            return;
        }

        Vector3 currentPosition = combCenter.position;

        if (!hasLastPosition)
        {
            lastPosition = currentPosition;
            hasLastPosition = true;
            return;
        }

        Vector3 movementDelta = currentPosition - lastPosition;
        lastPosition = currentPosition;

        if (movementDelta.sqrMagnitude < minimumMovementToComb * minimumMovementToComb)
            return;

        int affectedHairCards = 0;

        // Box path is more accurate for the teeth. Radius path is a simpler fallback.
        if (useInteractionBoxes && combInteractionBoxes != null && combInteractionBoxes.Length > 0)
            affectedHairCards = cuttingManager.ExecuteHairCardMeshCombBoxes(combInteractionBoxes, movementDelta, combStrength);
        else
            affectedHairCards = cuttingManager.ExecuteHairCardMeshComb(currentPosition, fallbackInteractionRadius, movementDelta, combStrength);

        if (affectedHairCards <= 0)
            return;

        if (!isCombing)
        {
            isCombing = true;
            Debug.Log("[Comb] Started combing hair cards.");
        }

        playHapticFeedback(Time.deltaTime);
    }

    // Desktop/editor testing only - mirrors whatever the VR Activate button would do (see class HANDOFF NOTE).
    private void HandleDesktopTestKey()
    {
        if (Keyboard.current == null || !IsCurrentlyHeld())
            return;

        KeyControl key = Keyboard.current[testToggleHoldKey];

        if (holdInteractionMode == CombHoldInteractionMode.Toggle)
        {
            if (key.wasPressedThisFrame)
                ToggleHoldOnTouchedHairCard();

            return;
        }

        if (key.wasPressedThisFrame)
        {
            isActivateButtonHeld = true;
            TryBeginHold();
        }
        else if (key.wasReleasedThisFrame)
        {
            isActivateButtonHeld = false;
            ReleaseHeldHairCard();
        }
    }

    private bool ShouldCombThisFrame()
    {
        if (!IsCurrentlyHeld())
            return false;

        if (!requireLeftMouseButton)
            return true;

        return Mouse.current != null && Mouse.current.leftButton.isPressed;
    }

    private bool IsCurrentlyHeld()
    {
        return grabInteractable != null && grabInteractable.isSelected;
    }

    private void OnDrawGizmosSelected()
    {
        if (useInteractionBoxes && combInteractionBoxes != null && combInteractionBoxes.Length > 0)
        {
            Gizmos.color = Color.green;

            for (int i = 0; i < combInteractionBoxes.Length; i++)
            {
                BoxCollider box = combInteractionBoxes[i];

                if (box == null)
                    continue;

                Matrix4x4 previousMatrix = Gizmos.matrix;
                Gizmos.matrix = Matrix4x4.TRS(box.transform.position, box.transform.rotation, box.transform.lossyScale);
                Gizmos.DrawWireCube(box.center, box.size);
                Gizmos.matrix = previousMatrix;
            }

            return;
        }

        if (combCenter == null)
            return;

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(combCenter.position, fallbackInteractionRadius);
    }

    void Start()
    {
        hapticIntensity = 3;
        toolID = 4;
        //toolModel =
        //toolAnimator =
    }

    // If a card is already held, this releases it (regardless of what's currently being touched). Otherwise
    // it grips whatever hair card the comb's interaction boxes are currently touching, at the exact point
    // being touched. Kept public for backward compatibility / Toggle mode / manual Inspector wiring.
    public void ToggleHoldOnTouchedHairCard()
    {
        if (!holdFeatureEnabled || !IsCurrentlyHeld())
            return;

        if (heldHairCard != null)
        {
            ReleaseHeldHairCard();
            return;
        }

        TryBeginHold();
    }

    // Attempts to grip whatever hair card the comb's interaction boxes are currently touching. Safe to call
    // every frame - does nothing if a card is already held or nothing is currently touched. Returns true if
    // a card is held after this call (either already was, or was just gripped).
    private bool TryBeginHold()
    {
        if (heldHairCard != null)
            return true;

        if (!TryFindTouchedHairCard(out SegmentedHairCard touchedCard, out HairCardData touchedData, out Vector3 touchPoint))
            return false;

        int segmentIndex = touchedCard.GetNearestSegmentIndexToWorldPoint(touchPoint);
        touchedCard.SetHeldByComb(true, segmentIndex);

        heldHairCard = touchedCard;
        heldHairCardData = touchedData;
        heldWorldPoint = touchPoint;
        RecomputeHeldLength();

        Debug.Log($"[Comb] Holding hair card '{touchedCard.gameObject.name}' at {HeldLengthCentimeters:0.0} cm from root.");
        return true;
    }

    private void ReleaseHeldHairCard()
    {
        if (heldHairCard != null)
        {
            Debug.Log($"[Comb] Released hair card '{heldHairCard.gameObject.name}'.");
            heldHairCard.SetHeldByComb(false);
        }

        heldHairCard = null;
        heldHairCardData = null;
        HeldLengthMeters = 0f;
        LongestHeldLengthMeters = 0f;
        LongestHeldTargetLengthMeters = -1f;
        SetHeldLengthLabelVisible(false);

        // Letting go of hold unfreezes everything immediately - cards still touching the comb become
        // combable/settling again right away instead of staying frozen with nothing protecting them.
        UnfreezeAllCombHeldCards();
    }

    // FEATURE (Freeze): while hold is engaged, scans every hair card currently touching the comb's
    // interaction boxes (broader than just the single gripped card) and keeps CuttingManager's frozen set in
    // sync with it - freezing newly-touching cards, unfreezing ones that left. Cheap: reuses scratch HashSets.
    private void UpdateFrozenHairCardsWhileHolding()
    {
        if (cuttingManager == null)
            return;

        frozenScanBuffer.Clear();

        if (useInteractionBoxes && combInteractionBoxes != null && combInteractionBoxes.Length > 0)
        {
            cuttingManager.CollectHairCardsInBoxes(combInteractionBoxes, frozenScanBuffer, holdGrabBoxPadding);
        }
        else
        {
            Vector3 referencePoint = combCenter != null ? combCenter.position : transform.position;
            Collider[] fallbackOverlap = Physics.OverlapSphere(referencePoint, fallbackInteractionRadius + Mathf.Max(0f, holdGrabBoxPadding));

            for (int i = 0; i < fallbackOverlap.Length; i++)
            {
                HairCardSegment segment = fallbackOverlap[i] != null ? fallbackOverlap[i].GetComponentInParent<HairCardSegment>() : null;

                if (segment == null || segment.Owner == null)
                    continue;

                // Clip-hidden cards (IsCuttable=false) are excluded here too, matching the box path.
                if (segment.Owner.HairCardData != null && !segment.Owner.HairCardData.IsCuttable)
                    continue;

                frozenScanBuffer.Add(segment.Owner);
            }
        }

        foreach (SegmentedHairCard card in frozenScanBuffer)
        {
            if (card != null && currentlyFrozenHairCards.Add(card))
                cuttingManager.SetHairCardFrozenForComb(card.transform, true);
        }

        currentlyFrozenHairCards.RemoveWhere(card =>
        {
            if (card != null && frozenScanBuffer.Contains(card))
                return false;

            if (card != null)
                cuttingManager.SetHairCardFrozenForComb(card.transform, false);

            return true;
        });
    }

    private void UnfreezeAllCombHeldCards()
    {
        if (cuttingManager != null)
        {
            foreach (SegmentedHairCard card in currentlyFrozenHairCards)
            {
                if (card != null)
                    cuttingManager.SetHairCardFrozenForComb(card.transform, false);
            }
        }

        currentlyFrozenHairCards.Clear();
    }

    private bool TryFindTouchedHairCard(out SegmentedHairCard segmentedHairCard, out HairCardData hairCardData, out Vector3 touchPoint)
    {
        segmentedHairCard = null;
        hairCardData = null;
        touchPoint = combCenter != null ? combCenter.position : transform.position;

        if (cuttingManager == null)
            return false;

        Vector3 referencePoint = combCenter != null ? combCenter.position : transform.position;

        if (useInteractionBoxes && combInteractionBoxes != null && combInteractionBoxes.Length > 0)
        {
            if (!cuttingManager.TryFindNearestHairCardSegmentInBoxes(combInteractionBoxes, referencePoint, out segmentedHairCard, out touchPoint, holdGrabBoxPadding))
                return false;
        }
        else
        {
            Collider[] fallbackOverlap = Physics.OverlapSphere(referencePoint, fallbackInteractionRadius + Mathf.Max(0f, holdGrabBoxPadding));
            float bestSqrDistance = float.PositiveInfinity;

            for (int i = 0; i < fallbackOverlap.Length; i++)
            {
                HairCardSegment segment = fallbackOverlap[i] != null ? fallbackOverlap[i].GetComponentInParent<HairCardSegment>() : null;

                if (segment == null || segment.Owner == null)
                    continue;

                // Clip-hidden cards (IsCuttable=false, see ClipZoneGatherEffect) can't be gripped by hold.
                if (segment.Owner.HairCardData != null && !segment.Owner.HairCardData.IsCuttable)
                    continue;

                Vector3 closestPoint = fallbackOverlap[i].ClosestPoint(referencePoint);
                float sqrDistance = (closestPoint - referencePoint).sqrMagnitude;

                if (sqrDistance >= bestSqrDistance)
                    continue;

                bestSqrDistance = sqrDistance;
                segmentedHairCard = segment.Owner;
                touchPoint = closestPoint;
            }

            if (segmentedHairCard == null)
                return false;
        }

        hairCardData = segmentedHairCard.HairCardData;
        return hairCardData != null;
    }

    private void RecomputeHeldLength()
    {
        if (heldHairCardData == null)
        {
            HeldLengthMeters = 0f;
            return;
        }

        // Root-to-grip-point length, NOT the card's full/original length - exactly what the player asked
        // to see: how much hair is between the root and where they're actually holding/about to cut above.
        float distanceAlongGrowth = Vector3.Dot(heldWorldPoint - heldHairCardData.GetRootPosition(), heldHairCardData.GetGrowthDirection());
        HeldLengthMeters = Mathf.Max(0f, distanceAlongGrowth);
    }

    // Scans every hair card currently inside the comb's interaction boxes (not just the gripped one) and
    // shows the longest root-to-touch-point length among them as "Current Longest", plus that same card's
    // HaircutManager-resolved target length as "Target Longest". Falls back to the actually gripped card's
    // own length/target if boxes aren't set up or nothing is currently found inside them.
    private void RecomputeLongestHeldLength()
    {
        if (cuttingManager == null)
            return;

        Vector3 referencePoint = combCenter != null ? combCenter.position : transform.position;
        float longestLengthMeters = HeldLengthMeters;
        SegmentedHairCard longestCard = null;

        if (useInteractionBoxes && combInteractionBoxes != null && combInteractionBoxes.Length > 0)
        {
            if (cuttingManager.TryGetLongestHairCardLengthInBoxes(combInteractionBoxes, referencePoint, out float boxLongestMeters, out SegmentedHairCard boxLongestCard, holdGrabBoxPadding))
            {
                longestLengthMeters = boxLongestMeters;
                longestCard = boxLongestCard;
            }
        }

        LongestHeldLengthMeters = longestLengthMeters;

        // Falls back to the actually-gripped card's data when the box scan found nothing (e.g. no boxes
        // assigned, or nothing currently overlapping them) - same fallback longestLengthMeters already uses.
        HairCardData longestCardData = longestCard != null ? longestCard.HairCardData : heldHairCardData;
        bool hasTarget = false;
        float targetLengthMeters = -1f;

        if (haircutManager != null && longestCardData != null)
            hasTarget = haircutManager.TryGetTargetLength(longestCardData, out targetLengthMeters, out _);

        LongestHeldTargetLengthMeters = hasTarget ? targetLengthMeters : -1f;

        EnsureHeldLengthLabel();
        SetHeldLengthLabelVisible(true);

        if (heldLengthLabel != null)
        {
            string targetText = hasTarget ? $"{LongestHeldTargetLengthCentimeters:0.0} cm" : "-";
            heldLengthLabel.text = $"Current Longest: {LongestHeldLengthCentimeters:0.0} cm\nTarget Longest: {targetText}";
        }
    }

    private void EnsureHeldLengthLabel()
    {
        if (heldLengthLabel != null)
            return;

        GameObject labelObject = new GameObject("Comb Held Length Label");
        labelObject.transform.SetParent(transform, false);
        labelObject.transform.localPosition = heldLengthLabelLocalOffset;

        heldLengthLabel = labelObject.AddComponent<TextMeshPro>();
        heldLengthLabel.fontSize = heldLengthLabelFontSize;
        heldLengthLabel.color = heldLengthLabelColor;
        heldLengthLabel.alignment = TextAlignmentOptions.Center;
        heldLengthLabel.text = string.Empty;
    }

    private void SetHeldLengthLabelVisible(bool visible)
    {
        if (heldLengthLabel != null)
            heldLengthLabel.gameObject.SetActive(visible);
    }

    // Keeps the label facing the player and following the comb while a card is held - cheap, only runs
    // while the label GameObject actually exists and is active.
    private void UpdateHeldLengthLabelTransform()
    {
        if (heldLengthLabel == null || !heldLengthLabel.gameObject.activeSelf)
            return;

        Camera mainCamera = Camera.main;

        if (mainCamera != null)
            heldLengthLabel.transform.rotation = Quaternion.LookRotation(heldLengthLabel.transform.position - mainCamera.transform.position, Vector3.up);
    }

    public override void useTool()
    {
        // Framework hook if needed
    }

    public override int getCurrentToolID()
    {
        return toolID; // Returns 4 to match your toolID setting
    }

    public override void playHapticFeedback(float duration)
    {
        // VR controller hook
    }
}
