using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Controls per-card visibility on a single baked stubble mesh (Induction/Butch/Crew cut).
/// Each "card" corresponds to 4 consecutive triangles in the mesh's triangle array
/// (2 quads x 2 triangles), in the same order the mesh was originally built in Blender.
/// All cards start hidden; call RevealCard(index) to show a specific one, e.g. when the
/// matching long hair card has been fully cut down (see long_hair_to_stubble_mapping.json).
/// </summary>
[RequireComponent(typeof(SkinnedMeshRenderer))]
public class StubbleCardRevealer : MonoBehaviour
{
    [Tooltip("Triangles per card. Do not change unless the stubble mesh generation changes (currently 2 quads = 4 triangles per card).")]
    public int trianglesPerCard = 4;

    [Tooltip("If true, all cards are hidden on Initialize. If false, all cards start visible.")]
    public bool startHidden = true;

    private SkinnedMeshRenderer _smr;
    private Mesh _instanceMesh;
    private int[] _originalTriangles;
    private int _cardCount;
    private bool _initialized = false;
    private HashSet<int> _visibleCards = new HashSet<int>();

    // FIX (stubble reveal state silently resetting on every mid-Play script recompile): _visibleCards
    // above is a plain non-serialized field, so a Unity domain reload wipes it back to empty - same class
    // of bug as SegmentedHairCard.currentCutStartIndex earlier this session. Awake() DOES get replayed
    // for this component after a reload (confirmed live: Crew's own visibility went back to "all hidden"
    // - its startHidden default - after a script push, discarding the ShowAllCards() override that
    // StubbleAreaSwapController.Start() had applied, since Start() itself is NOT replayed on reload).
    // That meant every further push while testing silently undid whatever Butch/Induction/Crew reveal
    // progress existed. These two fields let Initialize() tell "genuinely first-ever setup" (decide from
    // startHidden, exactly as before) apart from "reload replay" (restore the exact prior visible set
    // instead of re-deciding from startHidden) - mirrors the same fix pattern used for
    // SegmentedHairCard/hasInitializedCutState.
    [SerializeField, HideInInspector] private List<int> serializedVisibleCardIndices = new List<int>();
    [SerializeField, HideInInspector] private bool hasCapturedInitialVisibility;

    void Awake()
    {
        Initialize();
    }

    /// <summary>Sets up the instance mesh and initial visibility. Safe to call manually
    /// (e.g. from an editor test script) since it will not run twice.</summary>
    public void Initialize()
    {
        if (_initialized)
            return;

        _smr = GetComponent<SkinnedMeshRenderer>();
        if (_smr == null || _smr.sharedMesh == null)
        {
            Debug.LogError("StubbleCardRevealer: no SkinnedMeshRenderer/mesh found on " + name);
            return;
        }

        // Work on an instance so we never modify the shared mesh asset.
        _instanceMesh = Instantiate(_smr.sharedMesh);
        _instanceMesh.name = _smr.sharedMesh.name + "_Instance";
        _originalTriangles = _smr.sharedMesh.triangles;
        _cardCount = _originalTriangles.Length / (trianglesPerCard * 3);

        _smr.sharedMesh = _instanceMesh;
        _initialized = true;

        if (!hasCapturedInitialVisibility)
        {
            // Genuinely the first time ever for this instance - decide from the Inspector default exactly
            // as before.
            hasCapturedInitialVisibility = true;

            if (startHidden)
                HideAllCards();
            else
                ShowAllCards();
        }
        else
        {
            // A reload replay, not a fresh start - restore whatever was actually visible before the
            // reload instead of re-deciding from startHidden (which would silently discard real progress).
            _visibleCards = new HashSet<int>(serializedVisibleCardIndices);
            RebuildTriangles();
        }
    }

    public int CardCount => _cardCount;

    public void HideAllCards()
    {
        _visibleCards.Clear();
        _instanceMesh.triangles = new int[0];
        SyncSerializedVisibility();
    }

    public void ShowAllCards()
    {
        _visibleCards.Clear();
        for (int i = 0; i < _cardCount; i++)
            _visibleCards.Add(i);
        _instanceMesh.triangles = _originalTriangles;
        SyncSerializedVisibility();
    }

    public bool IsCardVisible(int cardIndex)
    {
        return _visibleCards.Contains(cardIndex);
    }

    /// <summary>Reveal a single stubble card by index (0 to CardCount-1).</summary>
    public void RevealCard(int cardIndex)
    {
        if (cardIndex < 0 || cardIndex >= _cardCount)
        {
            Debug.LogWarning("StubbleCardRevealer: card index " + cardIndex + " out of range (0.." + (_cardCount - 1) + ").");
            return;
        }

        if (_visibleCards.Contains(cardIndex))
            return; // already visible

        _visibleCards.Add(cardIndex);
        RebuildTriangles();
        SyncSerializedVisibility();
    }

    /// <summary>Hide a single stubble card by index, if you ever need to reverse a reveal (e.g. undo).</summary>
    public void HideCard(int cardIndex)
    {
        if (!_visibleCards.Contains(cardIndex))
            return;

        _visibleCards.Remove(cardIndex);
        RebuildTriangles();
        SyncSerializedVisibility();
    }

    // Mirrors _visibleCards into the serialized backing list every time it changes, so the NEXT domain
    // reload's Initialize() call has an accurate snapshot to restore from instead of losing progress.
    private void SyncSerializedVisibility()
    {
        serializedVisibleCardIndices.Clear();
        serializedVisibleCardIndices.AddRange(_visibleCards);
    }

    private void RebuildTriangles()
    {
        int stride = trianglesPerCard * 3;
        int[] newTriangles = new int[_visibleCards.Count * stride];
        int writeIndex = 0;

        List<int> sortedVisible = new List<int>(_visibleCards);
        sortedVisible.Sort();

        foreach (int cardIndex in sortedVisible)
        {
            int sourceStart = cardIndex * stride;
            System.Array.Copy(_originalTriangles, sourceStart, newTriangles, writeIndex, stride);
            writeIndex += stride;
        }

        _instanceMesh.triangles = newTriangles;
    }
}
