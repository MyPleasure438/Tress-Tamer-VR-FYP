using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Bridges long hair card cutting to stubble reveal. Listens for every SegmentedHairCard
/// in the scene reaching minimum segment length, looks up its matching stubble card via
/// StubbleMatchData, and reveals that card on whichever StubbleCardRevealer is currently active.
///
/// Only one of the three stubble styles (Induction/Butch/Crew) should be active at a time -
/// call SetActiveStyle(...) whenever the player's chosen haircut changes (e.g. from HaircutManager).
/// </summary>
public class StubbleRevealCoordinator : MonoBehaviour
{
    public enum StubbleStyle { Induction, Butch, Crew }

    [Header("Assign the StubbleCardRevealer on each stubble GameObject.")]
    public StubbleCardRevealer inductionRevealer;
    public StubbleCardRevealer butchRevealer;
    public StubbleCardRevealer crewRevealer;

    [Header("Which stubble style is active at scene start.")]
    public StubbleStyle startingStyle = StubbleStyle.Crew;

    [System.Serializable]
    public struct GuardStubbleMapping
    {
        public float guardCm;
        public StubbleStyle style;
    }

    [Header("Guard-Driven Reveal (Phase 2 addition)")]
    [Tooltip("If true and Clipper Guard Controller is assigned, each card's revealed stubble style is chosen by whichever guard was attached WHEN THAT CARD reached minimum length - not one fixed style for the whole session. This lets mixed guards during one session produce mixed stubble lengths across zones, matching real technique. If false (or no controller assigned), falls back to the old single-style behavior via SetActiveStyle().")]
    [SerializeField] private bool driveStubbleStyleFromGuard = false;

    [Tooltip("Optional. Needed for guard-driven reveal - drag the same ClipperGuardController used by the Clipper.")]
    [SerializeField] private ClipperGuardController clipperGuardController;

    [Tooltip("Maps each real guard size (cm) to the stubble style it should reveal. Nearest match is used, same pattern as ClipperGuardController.SetGuardLengthCm.")]
    [SerializeField] private GuardStubbleMapping[] guardToStyleMap = new GuardStubbleMapping[]
    {
        new GuardStubbleMapping { guardCm = 0.5f, style = StubbleStyle.Induction },
        new GuardStubbleMapping { guardCm = 1f, style = StubbleStyle.Butch },
        new GuardStubbleMapping { guardCm = 1.5f, style = StubbleStyle.Butch },
        new GuardStubbleMapping { guardCm = 2f, style = StubbleStyle.Crew },
        new GuardStubbleMapping { guardCm = 3f, style = StubbleStyle.Crew },
    };

    private StubbleCardRevealer _activeRevealer;
    private List<SegmentedHairCard> _subscribedCards = new List<SegmentedHairCard>();

    void Start()
    {
        // Make sure every revealer has run its own hidden-by-default setup.
        if (inductionRevealer != null) inductionRevealer.Initialize();
        if (butchRevealer != null) butchRevealer.Initialize();
        if (crewRevealer != null) crewRevealer.Initialize();

        SetActiveStyle(startingStyle);
        SubscribeToAllHairCards();
    }

    public void SetActiveStyle(StubbleStyle style)
    {
        // Fully hide all three first, so only the chosen style can ever show.
        if (inductionRevealer != null) inductionRevealer.HideAllCards();
        if (butchRevealer != null) butchRevealer.HideAllCards();
        if (crewRevealer != null) crewRevealer.HideAllCards();

        switch (style)
        {
            case StubbleStyle.Induction: _activeRevealer = inductionRevealer; break;
            case StubbleStyle.Butch: _activeRevealer = butchRevealer; break;
            case StubbleStyle.Crew: _activeRevealer = crewRevealer; break;
        }

        if (_activeRevealer == null)
            Debug.LogWarning("StubbleRevealCoordinator: no revealer assigned for style " + style);
    }

    private void SubscribeToAllHairCards()
    {
        SegmentedHairCard[] allCards = FindObjectsOfType<SegmentedHairCard>();
        foreach (var card in allCards)
        {
            card.OnReachedMinimumLength += () => HandleCardFullyCut(card);
            _subscribedCards.Add(card);
        }
        Debug.Log("StubbleRevealCoordinator: subscribed to " + allCards.Length + " hair cards.");
    }

    private void HandleCardFullyCut(SegmentedHairCard card)
    {
        if (StubbleMatchData.Instance == null)
            return;

        StubbleCardRevealer revealer = ResolveRevealerForThisCut();

        if (revealer == null)
            return;

        string longCardName = card.gameObject.name;
        // Updated for StubbleMatchData's list-based API (a long card can now own a whole cluster of
        // stubble quads instead of exactly one - see StubbleMatchData's handoff note). This class is the
        // old, disabled reveal system kept only so the project keeps compiling; StubbleAreaSwapController
        // is the active one.
        if (StubbleMatchData.Instance.TryGetStubbleIndices(longCardName, out List<int> stubbleIndices))
        {
            foreach (int stubbleIndex in stubbleIndices)
                revealer.RevealCard(stubbleIndex);
        }
        else
        {
            Debug.LogWarning("StubbleRevealCoordinator: no stubble match found for '" + longCardName + "'.");
        }
    }

    // Picks which revealer THIS specific cut should use. Guard-driven when enabled and available -
    // reflects whatever guard was attached at the moment this card finished cutting, so mixing guards
    // across zones in one session produces mixed stubble lengths. Falls back to the single style set
    // via SetActiveStyle() otherwise (old behavior, fully preserved).
    private StubbleCardRevealer ResolveRevealerForThisCut()
    {
        if (driveStubbleStyleFromGuard && clipperGuardController != null && guardToStyleMap != null && guardToStyleMap.Length > 0)
        {
            StubbleStyle nearestStyle = GetNearestGuardStyle(clipperGuardController.CurrentGuardCm);
            return GetRevealerForStyle(nearestStyle);
        }

        return _activeRevealer;
    }

    private StubbleStyle GetNearestGuardStyle(float currentGuardCm)
    {
        StubbleStyle bestStyle = guardToStyleMap[0].style;
        float bestDiff = Mathf.Abs(guardToStyleMap[0].guardCm - currentGuardCm);

        for (int i = 1; i < guardToStyleMap.Length; i++)
        {
            float diff = Mathf.Abs(guardToStyleMap[i].guardCm - currentGuardCm);

            if (diff < bestDiff)
            {
                bestDiff = diff;
                bestStyle = guardToStyleMap[i].style;
            }
        }

        return bestStyle;
    }

    private StubbleCardRevealer GetRevealerForStyle(StubbleStyle style)
    {
        switch (style)
        {
            case StubbleStyle.Induction: return inductionRevealer;
            case StubbleStyle.Butch: return butchRevealer;
            case StubbleStyle.Crew: return crewRevealer;
            default: return null;
        }
    }

    void OnDestroy()
    {
        foreach (var card in _subscribedCards)
        {
            if (card != null)
            {
                // Note: individual lambda unsubscription is intentionally skipped here since
                // these objects are destroyed together at scene teardown in normal play.
            }
        }
    }
}
