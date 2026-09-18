using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class StubbleMatchEntry
{
    public string long_card_name;
    public int matched_stubble_index;
    public float matched_stubble_distance_m;
}

[System.Serializable]
public class StubbleMappingData
{
    public string description;
    public string stubble_source_object;
    public int stubble_card_count;
    public List<StubbleMatchEntry> mapping;
}

/// <summary>
/// Loads long_hair_to_stubble_mapping.json from Resources and exposes a fast
/// name -> stubble card index lookup. Add this once to a persistent manager
/// object (e.g. the same object as CuttingManager or HaircutManager).
/// </summary>
public class StubbleMatchData : MonoBehaviour
{
    private static StubbleMatchData _instance;

    // FIX (stubble area swap silently never firing): this used to be a plain auto-property set only
    // from Awake(). Awake() never runs on a MonoBehaviour whose GameObject is inactive at scene load -
    // and in this project StubbleMatchData ended up sharing a GameObject ("Stubble") with
    // StubbleRevealCoordinator, which the handoff notes told the user to disable by unticking the
    // GameObject's own active checkbox (rather than just the component checkbox). That silently took
    // StubbleMatchData down with it: Instance stayed null forever, so every
    // StubbleAreaSwapController.TryApplyInitialSwap/TryHandleAlreadySwappedArea call bailed out on its
    // "no stubble data loaded" check and nothing ever visibly changed - exactly the "not implemented"
    // symptom, even though the swap logic itself was correct.
    //
    // Now Instance lazily self-heals on first access: if nothing has set it yet (fresh domain reload, or
    // this component's own GameObject is inactive so Awake() never ran), it actively searches the scene
    // INCLUDING inactive objects and loads the JSON right then. This makes the whole system independent
    // of exactly which GameObject this component lives on or whether that GameObject is active - it will
    // find and load itself the moment anything actually asks for it.
    // Checks actual dictionary content, not just the IsLoaded flag - observed live (via repeated
    // RunCommand-driven domain reloads while diagnosing this) that IsLoaded can end up true while
    // LongCardToStubbleIndices is still empty, most likely a one-frame race between Resources still
    // settling and Awake()'s automatic Load() call right as a domain reload finishes. Gating on the flag
    // alone would then get permanently stuck "loaded" with nothing in it for the rest of the session -
    // gating on real content instead means any access anywhere always self-heals to a working state.
    private static bool NeedsLoad(StubbleMatchData candidate)
    {
        return candidate != null && (!candidate.IsLoaded || candidate.LongCardToStubbleIndices.Count == 0);
    }

    public static StubbleMatchData Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindObjectOfType<StubbleMatchData>(true);

            if (NeedsLoad(_instance))
                _instance.Load();

            return _instance;
        }
    }

    [Tooltip("Filename in a Resources folder, without extension.")]
    public string resourceName = "long_hair_to_stubble_mapping";

    // FIX (Crew stubble stays visible under Butch/Induction, take 2): the ORIGINAL mapping only assigned
    // ONE stubble card per long hair card (659 long cards -> 659 of 2865 total stubble quads), so cutting
    // hair only ever revealed a single tiny quad's worth of Butch/Induction per card while ~77% of the
    // Crew mesh had no long hair card pointing at it at all and could never hide, no matter what got cut -
    // that is what "Crew still shows" actually was. The mapping JSON was regenerated (see
    // long_hair_to_stubble_mapping_backup_single_index.json for the old one) as one entry PER STUBBLE
    // QUAD (all 2865, full coverage) instead of one entry per long card, with each quad assigned to its
    // geometrically nearest long hair card - so a long card can now own a whole cluster of quads (its
    // real physical footprint) instead of exactly one. That means a single long card name can legitimately
    // appear multiple times in the JSON now, so the lookup has to return ALL of them, not just the last
    // one parsed (a plain Dictionary<string,int> can only ever hold one value per key).
    public Dictionary<string, List<int>> LongCardToStubbleIndices { get; private set; } = new Dictionary<string, List<int>>();
    public bool IsLoaded { get; private set; } = false;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        Load();
    }

    public void Load()
    {
        TextAsset jsonFile = Resources.Load<TextAsset>(resourceName);
        if (jsonFile == null)
        {
            Debug.LogError("StubbleMatchData: could not find Resources/" + resourceName + ".json. Make sure the file is in an Assets/Resources folder.");
            return;
        }

        StubbleMappingData data = JsonUtility.FromJson<StubbleMappingData>(jsonFile.text);
        if (data == null || data.mapping == null)
        {
            Debug.LogError("StubbleMatchData: failed to parse mapping JSON.");
            return;
        }

        LongCardToStubbleIndices.Clear();
        foreach (var entry in data.mapping)
        {
            if (!LongCardToStubbleIndices.TryGetValue(entry.long_card_name, out List<int> indices))
            {
                indices = new List<int>();
                LongCardToStubbleIndices[entry.long_card_name] = indices;
            }

            indices.Add(entry.matched_stubble_index);
        }

        IsLoaded = true;
        Debug.Log("StubbleMatchData: loaded " + data.mapping.Count + " stubble card entries across " + LongCardToStubbleIndices.Count + " hair cards.");
    }

    /// <summary>Returns true and outputs ALL matching stubble card indices for this long card (its full
    /// cluster, may be more than one - see the handoff note above), if it has any. Self-heals if called on
    /// a reference obtained before data was ready (see NeedsLoad above) - safe to call even on a component
    /// fetched directly instead of through Instance.</summary>
    public bool TryGetStubbleIndices(string longCardName, out List<int> stubbleIndices)
    {
        if (NeedsLoad(this))
            Load();

        return LongCardToStubbleIndices.TryGetValue(longCardName, out stubbleIndices);
    }
}
