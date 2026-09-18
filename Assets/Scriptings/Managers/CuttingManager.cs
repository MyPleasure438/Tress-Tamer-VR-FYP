using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using EzySlice;

// HANDOFF NOTE:
// This is the active haircut/collision manager used by Scissor, Clipper, Brush, Comb,
// HaircutManager, and HairUndoManager. The current preferred cutting route is hair-card
// segments and box collider cut zones, because live mesh slicing was too expensive/unstable
// for many VR hair cards. Keep the old mesh methods below as backup unless the user asks.
public class CuttingManager : MonoBehaviour
{
    // Global "pause all cutting" flag - set true while the H-key zone overview is showing (hair
    // renderers hidden, but colliders intentionally left untouched by design - see
    // HeadZonePlayerToggle.cs). Prevents scissors/clipper from still being able to cut invisible
    // hair, which previously caused already-cut hair to suddenly "pop into view" once the overview
    // was turned back off.
    public static bool IsCuttingBlocked = false;

    // Legacy/class-diagram mesh fields. Kept for backup/testing, not the main hair-card path.
    [Header("Class Diagram Fields")]
    public float cutRadius = 0.02f; // 2cm default radius
    [SerializeField] private MeshFilter hairMeshFilter; // Connects to your hairMesh asset
    [SerializeField] private ParticleSystem hairParticleSystem;

    // Legacy runtime mesh manipulation cache. Active gameplay should prefer segmented hair cards.
    private Mesh workingMesh;
    private List<Vector3> currentVertices = new List<Vector3>();
    private List<int> currentTriangles = new List<int>();
    private List<Vector2> currentUVs = new List<Vector2>();

    // Backup cache vectors to support your diagram's resetMesh() / undo() functionality
    private List<Vector3> baseVerticesCache = new List<Vector3>();
    private List<int> baseTrianglesCache = new List<int>();
    private List<Vector2> baseUVsCache = new List<Vector2>();

    [Header("Hair Card Mesh Cutting")]
    [SerializeField] private LayerMask cuttableHairLayers = ~0;
    // Usually Ignore for normal solid hair colliders; switch to Collide if hair card colliders are triggers.
    [SerializeField] private QueryTriggerInteraction hairTriggerInteraction = QueryTriggerInteraction.Ignore;
    [SerializeField] private bool requireHairCardData = true;
    // Active VR path: if hair cards were pre-split, hide segments instead of rebuilding meshes at cut time.
    [SerializeField] private bool preferSegmentedHairCards = true;
    // New additive option: when a card is already at minimum segment length and the target-respecting cut
    // is blocked (because CurrentLength already meets the target), allow removing the final visible segment
    // anyway so the stubble reveal underneath is not left with a leftover hair chunk on top of it.
    [SerializeField] private bool allowFinalSegmentRemovalForStubbleReveal = true;
    // Practice mode uses this as a hard limiter. Assessment mode can still score against targets without limiting.
    [SerializeField] private bool respectActiveHaircutTargets = true;
    // HANDOFF NOTE (Guard-driven clipper cuts): when set (by ExecuteHairCardMeshCutCylinder/BoxesRoutine's
    // optional guardTargetLengthMeters parameter), this takes priority over the hairstyle's flat per-zone
    // target inside TryGetHaircutTarget - matches how a real clipper guard physically determines cut length.
    // Always null for Scissor cuts (which never pass that parameter), so Scissor behavior is unchanged.
    private float? activeGuardOverrideLength = null;
    [Tooltip("Tolerance used only for guard-driven clipper cuts (see activeGuardOverrideLength).")]
    [SerializeField] private float guardOverrideTolerance = 0.001f;
    // HANDOFF NOTE (3cm guard hard cap): the largest clipper guard must NEVER be able to remove a hair
    // card's very last segment, no matter what - a real 3cm guard physically cannot shave hair down to
    // nothing, it always leaves a substantial stub behind. The normal target-respecting cut
    // (CutFromSegmentIndexRespectingTarget) already can't reach the last segment on its own for any
    // positive guard target - the only way it was ever possible was via Fallback A/B below, which exist
    // specifically to force-finish a cut past target-rounding for the SHORT guards (0.5/1.5cm) and
    // Scissor. IsMaxCappedGuard() excludes the 3cm guard from both fallbacks so it always stops wherever
    // the guard-target math naturally leaves it.
    [Tooltip("The largest guard size (cm) that must be hard-capped so it can never remove a hair card's final segment. Must match the largest entry in ClipperGuardController's Available Guards Cm.")]
    [SerializeField] private float maxCappedGuardLengthCm = 3f;
    [SerializeField] private float guardCapToleranceCm = 0.05f;
    [SerializeField] private HaircutManager stage1HaircutManager;
    [SerializeField] private HairUndoManager hairUndoManager;
    [SerializeField] private bool addBoxColliderToFallenHair = true;
    // Optional search root for hundreds/thousands of hair cards. Leave null only if this manager is on their parent.
    [SerializeField] private Transform hairCardsRoot;
    [SerializeField] private float minimumDistanceFromRoot = 0.01f;
    [SerializeField] private float fallenHairMass = 0.015f;
    [SerializeField] private float fallenHairDownForce = 0.025f;
    [SerializeField] private float fallenHairRandomScatter = 0f;
    [SerializeField] private float fallenHairTorque = 0.003f;
    [SerializeField] private float fallenHairLinearDamping = 1.5f;
    [SerializeField] private float fallenHairAngularDamping = 2.5f;
    [SerializeField] private float fallenHairLifetime = 6f;
    // Optional hierarchy parent for spawned debris; it only organizes the scene and does not affect cutting.
    [SerializeField] private Transform fallenHairParent;
    [SerializeField] private bool spawnFallenHair = true;
    // User-assigned debris prefab. If null, the manager falls back to mesh-derived/simple pooled objects.
    [SerializeField] private GameObject fallenHairPoolPrefab;
    [SerializeField] private bool useFallenHairPooling = true;
    [SerializeField] private int initialFallenHairPoolSize = 20;
    [SerializeField] private int maxFallenHairPoolSize = 80;
    [SerializeField] private bool fadeFallenHairBeforePooling = true;
    [SerializeField] private float fallenHairFadeDuration = 1f;
    [SerializeField] private int maxHairCardsCutPerSnip = 10;
    [SerializeField] private bool spreadCutsOverMultipleFrames = true;
    [SerializeField] private int maxCutsProcessedPerFrame = 2;
    [SerializeField] private bool logFallenHairSpawn = true;
    [SerializeField] private int overlapBufferSize = 128;
    [SerializeField] private Material cutPreviewMaterial;
    // Helps box cutting work with simple BoxColliders by testing renderer bounds when physics hits miss.
    [SerializeField] private bool useRendererBoundsFallbackForCutBoxes = true;
    [SerializeField] private int maxCutPreviewsPerUpdate = 8;
    [SerializeField] private int maxRendererFallbackChecksPerBox = 80;

    [Header("Hair Card Mesh Brushing")]
    [SerializeField] private float brushRootLockDistance = 0.015f;
    [SerializeField] private bool lockHairRootsWhileBrushing = true;

    [Header("Hair Card Mesh Combing")]
    [SerializeField] private int maxHairCardsCombedPerFrame = 12;
    [SerializeField] private float combRootLockDistance = 0.015f;
    [SerializeField] private bool lockHairRootsWhileCombing = true;
    [SerializeField] private bool combWholeHairCardFromRoot = true;
    [SerializeField] private float combTipInfluence = 1.0f;
    // For segmented cards, rotate/settle the whole source card visually so pieces do not separate as much.
    [SerializeField] private bool useVisualCombForSegmentedHairCards = true;
    [Tooltip("TUNING: how many degrees a hair card can rotate PER FRAME while actively being combed, scaled by Comb's own Comb Strength (0-1). If combing feels sluggish/hard to move hair around, raise this (try 6-10) and/or raise Comb's Comb Strength toward 1.")]
    [SerializeField] private float visualCombMaxDegreesPerFrame = 3.0f;
    [Tooltip("Keeps each card's root pinned in place while its rotation is adjusted by combing/settling, instead of letting the whole card drift as it rotates. Leave on unless you specifically want roots to move too.")]
    [SerializeField] private bool visualCombPreserveRootPosition = true;
    [Tooltip("Master switch for the automatic 'settle back' behavior below - if off, combed cards stay bent forever until manually reset (see Reset All Combed Hair Cards To Original Pose).")]
    [SerializeField] private bool applyGravityAfterVisualComb = true;
    [Tooltip("TUNING: seconds after the comb stops touching a card before it starts settling back. Lower = starts returning sooner.")]
    [SerializeField] private float visualCombGravityDelay = 0.15f;
    [Tooltip("TUNING: how fast a combed card rotates back toward its rest direction, in degrees per SECOND (not per frame - much slower than Visual Comb Max Degrees Per Frame by design). If cards feel like they take forever to return to normal or look 'stuck', raise this (try 20-40+). Once a card fully reaches its target this settle direction, it is hard-snapped to its exact original pose and stops being tracked - so it always ends up back exactly where it started, never partway or drifted.")]
    [SerializeField] private float visualCombGravityDegreesPerSecond = 8.0f;
    [Tooltip("On (recommended): combed cards settle back toward their OWN original growth direction/pose - guarantees every card returns to exactly how it looked before being combed. Off: cards instead settle toward the fixed Visual Comb Gravity Direction below (e.g. straight down), like real gravity pulling hair down rather than restoring the original style.")]
    [SerializeField] private bool visualCombSettleTowardOriginalDirection = true;
    [Tooltip("Only used when Visual Comb Settle Toward Original Direction is OFF - the fixed world-space direction combed cards fall toward instead of their own original pose.")]
    [SerializeField] private Vector3 visualCombGravityDirection = Vector3.down;

    private Collider[] hairOverlapBuffer;
    private readonly Dictionary<SegmentedHairCard, Vector3> longestHoldClosestPointBuffer = new Dictionary<SegmentedHairCard, Vector3>();
    // FEATURE (Comb Hold - Freeze): transforms in this set are completely exempt from visual comb bending
    // AND from the gravity settle-back, for as long as they're in it - Comb.cs adds/removes membership every
    // frame based on which hair cards are currently inside its interaction boxes while hold is engaged, so a
    // held card (and anything else touching the comb) stays frozen exactly in place until it physically
    // leaves the comb's boxes, at which point it resumes normal behavior (including settling back to its
    // original pose if it had been combed before).
    private readonly HashSet<Transform> frozenCombHairCardTransforms = new HashSet<Transform>();
    private readonly Dictionary<MeshFilter, GameObject> activeCutPreviews = new Dictionary<MeshFilter, GameObject>();
    private readonly Dictionary<Transform, VisualCombedHairCard> visuallyCombedHairCards = new Dictionary<Transform, VisualCombedHairCard>();
    private static readonly List<Transform> s_visualCombGravityRemovalBuffer = new List<Transform>();
    private readonly Queue<GameObject> fallenHairPool = new Queue<GameObject>();
    private readonly List<GameObject> spawnedFallenHairPoolObjects = new List<GameObject>();
    private Material generatedCutPreviewMaterial;

    private void Awake()
    {
        // Undo is treated as part of the active hair editing flow, so create it if the scene forgot it.
        if (hairUndoManager == null)
            hairUndoManager = GetComponent<HairUndoManager>();

        if (hairUndoManager == null)
            hairUndoManager = gameObject.AddComponent<HairUndoManager>();

        if (!useFallenHairPooling)
            return;

        int poolSize = Mathf.Max(0, initialFallenHairPoolSize);

        for (int i = 0; i < poolSize; i++)
            ReturnFallenHairToPool(CreateFallenHairPoolObject());
    }

    void Start()
    {
        /*
        if (hairMeshFilter != null && hairMeshFilter.mesh != null)
        {
            // Cache working mesh reference
            workingMesh = hairMeshFilter.mesh;
            
            // Extract the editable mesh data lists
            workingMesh.GetVertices(currentVertices);
            workingMesh.GetTriangles(currentTriangles, 0);
            workingMesh.GetUVs(0, currentUVs);

            // Store deep copies for resetMesh capabilities
            baseVerticesCache = new List<Vector3>(currentVertices);
            baseTrianglesCache = new List<int>(currentTriangles);
            baseUVsCache = new List<Vector2>(currentUVs);
        }
        */
    }

    private void OnDisable()
    {
        ClearHairCardMeshCutPreview();

        // HARDENING: previously this just cleared the tracking dictionary, which would silently orphan any
        // card still mid-bend (not yet settled) - its only record of where to return to would be gone with
        // no further code ever re-checking it, leaving it permanently stuck at whatever pose it was in the
        // instant this component got disabled. Resetting first guarantees nothing is ever left stranded.
        ResetAllCombedHairCardsToOriginalPose();
    }

    private void LateUpdate()
    {
        ApplyVisualCombGravity();
    }

    private void BeginHairUndoAction(string actionName)
    {
        if (hairUndoManager != null)
            hairUndoManager.BeginAction(actionName);
    }

    private void EndHairUndoAction()
    {
        if (hairUndoManager != null)
            hairUndoManager.EndAction();
    }

    private void CaptureHairUndoBeforeChange(SegmentedHairCard segmentedHairCard)
    {
        if (hairUndoManager != null)
            hairUndoManager.CaptureBeforeChange(segmentedHairCard);
    }

    private void CaptureHairUndoBeforeChange(MeshFilter meshFilter, HairCardData hairCardData)
    {
        if (hairUndoManager != null)
            hairUndoManager.CaptureBeforeChange(meshFilter, hairCardData);
    }

    private void MarkHairUndoChanged()
    {
        if (hairUndoManager != null)
            hairUndoManager.MarkCurrentActionChanged();
    }

    public void BeginHairUndoStroke(string actionName)
    {
        BeginHairUndoAction(actionName);
    }

    public void EndHairUndoStroke()
    {
        EndHairUndoAction();
    }

    private void RegisterSpawnedHairUndo(GameObject fallenHair)
    {
        if (hairUndoManager != null)
            hairUndoManager.RegisterSpawnedObject(fallenHair);
    }

    // --- Public Methods (Mapped from Class Diagram) ---

    public void ExecuteCutWithSlicing(Vector3 scissorPos, Vector3 scissorNormal)
    {
        /*
        Debug.Log($"[Slicer Debug] Attempting slice at {scissorPos} with normal {scissorNormal}");

        // 1. Safe check: Grab the primary material to patch the cross-section slice gap
        MeshRenderer renderer = hairMeshFilter.GetComponent<MeshRenderer>();
        Material primaryMat = renderer != null ? renderer.sharedMaterial : null;

        // 2. Slice using the primary cross-section material patch
        //SlicedHull hull = hairMeshFilter.gameObject.Slice(scissorPos, scissorNormal, primaryMat);
        SlicedHull hull = hairMeshFilter.gameObject.Slice(scissorPos, scissorNormal);

        if (hull == null)
        {
            Debug.LogWarning("[Slicer Debug] Slice failed! EzySlice returned null. The plane missed the triangles or Read/Write is off.");
            return; 
        }

        Debug.Log("[Slicer Debug] Slice Success! Creating hulls...");

        // 3. CreateUpperHull() builds the top half (using the original hair GameObject setup)
        GameObject topHairObject = hull.CreateUpperHull(hairMeshFilter.gameObject, primaryMat);
        
        // Update your hairMeshFilter to use this new sliced piece's mesh
        if (topHairObject != null && topHairObject.TryGetComponent<MeshFilter>(out var topFilter))
        {
            hairMeshFilter.mesh = topFilter.mesh;
            Destroy(topHairObject); // Clean up the temporary wrapper object
        }

        // 4. CreateLowerHull() builds the falling strands
        GameObject fallenHair = hull.CreateLowerHull(hairMeshFilter.gameObject, primaryMat);
        
        if (fallenHair != null)
        {
            // 5. Give the fallen hair real-world physics!
            Rigidbody rb = fallenHair.AddComponent<Rigidbody>();
            MeshCollider dc = fallenHair.AddComponent<MeshCollider>();
            dc.convex = true; // Allows it to hit and slide across your floor plane

            // Delete the fallen strands after 5 seconds so your scene stays clean
            Destroy(fallenHair, 5f);
        }
        */
    }

    /*
    public void ExecuteCutWithSlicing(Vector3 scissorPos, Vector3 scissorNormal)
    {
        Debug.Log($"[Slicer Debug] Attempting slice at {scissorPos} with normal {scissorNormal}");

        // 1. Slice the GameObject using EzySlice's extension method
        //SlicedHull hull = hairMeshFilter.gameObject.Slice(scissorPos, scissorNormal);

        Material hairMat = hairMeshFilter.GetComponent<MeshRenderer>().sharedMaterial;
        SlicedHull hull = hairMeshFilter.gameObject.Slice(scissorPos, scissorNormal, hairMat);

        // CRITICAL FIX: If hull is null, stop right here and scream a warning!
        if (hull == null)
        {
            Debug.LogWarning("[Slicer Debug] Slice failed! EzySlice returned null. The plane missed the triangles or Read/Write is off.");
            return; // Exits the function early so the code below doesn't crash
        }

        // --- EVERYTHING BELOW HERE ONLY RUNS IF HULL IS NOT NULL (SUCCESS!) ---
        Debug.Log("[Slicer Debug] Slice Success! Creating hulls...");

        // 2. CreateUpperHull() builds a brand new GameObject for the top half
        GameObject topHairObject = hull.CreateUpperHull(hairMeshFilter.gameObject, null);
        
        // Update your hairMeshFilter to use this new sliced piece's mesh
        if (topHairObject != null && topHairObject.TryGetComponent<MeshFilter>(out var topFilter))
        {
            hairMeshFilter.mesh = topFilter.mesh;
            Destroy(topHairObject); // Clean up the temporary wrapper object
        }

        // 3. Use CreateLowerHull() to make the piece that falls to the floor
        GameObject fallenHair = hull.CreateLowerHull(hairMeshFilter.gameObject, null);
        
        if (fallenHair != null)
        {
            // Give the fallen hair real-world physics!
            Rigidbody rb = fallenHair.AddComponent<Rigidbody>();
            MeshCollider dc = fallenHair.AddComponent<MeshCollider>();
            dc.convex = true; // Allows it to land on your ground plane

            // Delete the fallen strands after 5 seconds so your game doesn't lag
            Destroy(fallenHair, 5f);
        }
    }
    */

    /// <summary>
    /// Called by your Scissor or Clipper tool when a cut action is triggered.
    /// </summary>
    public void ExecuteCut(Vector3 point, float customRadius)
    {
        /*
        if (hairMeshFilter == null)
        {
            Debug.LogError("[CuttingManager] Cannot cut! HairMeshFilter dependency is missing.");
            return;
        }

        // Use custom runtime parameter if provided, otherwise default to class variable field
        float activeRadius = customRadius > 0 ? customRadius : cutRadius;

        // CRITICAL RECOVERY: Repopulate cache if lists lost reference during assembly reloading
        if (currentVertices.Count == 0)
        {
            InitializeMeshData();
        }

        // Convert world-space tool position into the hair mesh's local space coordinates
        Vector3 localCutPos = hairMeshFilter.transform.InverseTransformPoint(point);
        HashSet<int> verticesToRemove = new HashSet<int>();

        // 1. Identify vertices inside your tool interaction volume
        for (int i = 0; i < currentVertices.Count; i++)
        {
            if (Vector3.Distance(currentVertices[i], localCutPos) <= activeRadius)
            {
                verticesToRemove.Add(i);
            }
        }

        if (verticesToRemove.Count == 0) return; // Cut nothing

        // 2. Spawn physical clippings at the interaction impact coordinate point
        spawnCutHairParticles(point);

        // 3. Rebuild the triangles array, dropping any polygon indexing connected to deleted segments
        List<int> newTriangles = new List<int>();
        for (int i = 0; i < currentTriangles.Count; i += 3)
        {
            int v1 = currentTriangles[i];
            int v2 = currentTriangles[i + 1];
            int v3 = currentTriangles[i + 2];

            if (!verticesToRemove.Contains(v1) && !verticesToRemove.Contains(v2) && !verticesToRemove.Contains(v3))
            {
                newTriangles.Add(v1);
                newTriangles.Add(v2);
                newTriangles.Add(v3);
            }
        }

        currentTriangles = newTriangles;
        
        // Push the mathematical updates to the physical screen mesh data
        updateMesh();

        */
    }

    public int ExecuteHairCardMeshCut(Vector3 scissorPosition, Vector3 scissorDirection, float customRadius)
    {
        BeginHairUndoAction("Cut Hair");

        float activeRadius = customRadius > 0f ? customRadius : cutRadius;
        int hitCount = CollectHairHits(scissorPosition, activeRadius);
        int cutCount = 0;
        HashSet<MeshFilter> processedMeshFilters = new HashSet<MeshFilter>();
        HashSet<SegmentedHairCard> processedSegmentedHairCards = new HashSet<SegmentedHairCard>();

        for (int i = 0; i < hitCount; i++)
        {
            if (HasReachedMaxCuts(cutCount))
                break;

            Collider hit = hairOverlapBuffer[i];

            if (hit == null)
                continue;

            if (TryCutSegmentedHairHit(hit, processedSegmentedHairCards, out bool handledSegmentedHair))
            {
                cutCount++;
                continue;
            }

            if (handledSegmentedHair)
                continue;

            MeshFilter meshFilter = hit.GetComponentInParent<MeshFilter>();

            if (meshFilter == null || meshFilter.sharedMesh == null || !processedMeshFilters.Add(meshFilter))
                continue;

            HairCardData hairCardData = meshFilter.GetComponentInParent<HairCardData>();

            if (requireHairCardData && hairCardData == null)
                continue;

            if (hairCardData != null && !hairCardData.IsCuttable)
                continue;

            if (TryCutSingleHairMesh(meshFilter, hairCardData, scissorPosition, scissorDirection))
                cutCount++;
        }

        EndHairUndoAction();
        return cutCount;
    }

    public int ExecuteHairCardMeshCut(Vector3 scissorPosition, float customRadius)
    {
        return ExecuteHairCardMeshCut(scissorPosition, Vector3.zero, customRadius);
    }

    public int ExecuteHairCardMeshCutCylinder(Vector3 cylinderCenter, Vector3 cylinderAxis, float cylinderLength, float cylinderRadius, float? guardTargetLengthMeters = null)
    {
        if (IsCuttingBlocked)
            return 0;

        if (cylinderAxis.sqrMagnitude <= 0.0001f)
            cylinderAxis = Vector3.up;

        activeGuardOverrideLength = guardTargetLengthMeters;
        BeginHairUndoAction("Cut Hair");

        cylinderAxis.Normalize();

        float activeRadius = cylinderRadius > 0f ? cylinderRadius : cutRadius;
        float activeLength = Mathf.Max(cylinderLength, activeRadius * 2f);
        Vector3 halfAxis = cylinderAxis * (activeLength * 0.5f);
        Vector3 cylinderStart = cylinderCenter - halfAxis;
        Vector3 cylinderEnd = cylinderCenter + halfAxis;
        int hitCount = CollectHairHitsCapsule(cylinderStart, cylinderEnd, activeRadius);
        int cutCount = 0;
        HashSet<MeshFilter> processedMeshFilters = new HashSet<MeshFilter>();
        HashSet<SegmentedHairCard> processedSegmentedHairCards = new HashSet<SegmentedHairCard>();

        for (int i = 0; i < hitCount; i++)
        {
            if (HasReachedMaxCuts(cutCount))
                break;

            Collider hit = hairOverlapBuffer[i];

            if (hit == null)
                continue;

            if (TryCutSegmentedHairHit(hit, processedSegmentedHairCards, out bool handledSegmentedHair))
            {
                cutCount++;
                continue;
            }

            if (handledSegmentedHair)
                continue;

            MeshFilter meshFilter = hit.GetComponentInParent<MeshFilter>();

            if (meshFilter == null || meshFilter.sharedMesh == null || !processedMeshFilters.Add(meshFilter))
                continue;

            HairCardData hairCardData = meshFilter.GetComponentInParent<HairCardData>();

            if (requireHairCardData && hairCardData == null)
                continue;

            if (hairCardData != null && !hairCardData.IsCuttable)
                continue;

            if (!TryGetCylinderHairCutPoint(meshFilter, hairCardData, cylinderStart, cylinderEnd, activeRadius, out Vector3 cutPoint))
                continue;

            if (TryCutSingleHairMesh(meshFilter, hairCardData, cutPoint, cylinderAxis))
                cutCount++;
        }

        EndHairUndoAction();
        activeGuardOverrideLength = null;
        return cutCount;
    }

    public int ExecuteHairCardMeshCutBoxes(BoxCollider[] cutBoxes, Vector3 scissorDirection)
    {
        // Active immediate scissor cut path for box collider cut zones.
        ClearHairCardMeshCutPreview();

        if (cutBoxes == null || cutBoxes.Length == 0)
            return 0;

        BeginHairUndoAction("Cut Hair");

        int cutCount = 0;
        HashSet<MeshFilter> processedMeshFilters = new HashSet<MeshFilter>();
        HashSet<SegmentedHairCard> processedSegmentedHairCards = new HashSet<SegmentedHairCard>();

        for (int boxIndex = 0; boxIndex < cutBoxes.Length; boxIndex++)
        {
            if (HasReachedMaxCuts(cutCount))
                break;

            BoxCollider cutBox = cutBoxes[boxIndex];

            if (cutBox == null || !cutBox.enabled)
                continue;

            Vector3 boxCenter = cutBox.transform.TransformPoint(cutBox.center);
            Vector3 boxHalfExtents = Vector3.Scale(cutBox.size * 0.5f, AbsVector3(cutBox.transform.lossyScale));
            Quaternion boxRotation = cutBox.transform.rotation;
            int hitCount = CollectHairHitsBox(boxCenter, boxHalfExtents, boxRotation);

            for (int i = 0; i < hitCount; i++)
            {
                if (HasReachedMaxCuts(cutCount))
                    break;

                Collider hit = hairOverlapBuffer[i];

                if (hit == null)
                    continue;

                if (TryCutSegmentedHairHit(hit, processedSegmentedHairCards, out bool handledSegmentedHair))
                {
                    cutCount++;
                    continue;
                }

                if (handledSegmentedHair)
                    continue;

                MeshFilter meshFilter = hit.GetComponentInParent<MeshFilter>();

                if (meshFilter == null || meshFilter.sharedMesh == null || processedMeshFilters.Contains(meshFilter))
                    continue;

                HairCardData hairCardData = meshFilter.GetComponentInParent<HairCardData>();

                if (requireHairCardData && hairCardData == null)
                    continue;

                if (hairCardData != null && !hairCardData.IsCuttable)
                    continue;

                if (!TryGetBoxHairCutPoint(meshFilter, hairCardData, cutBox, out Vector3 cutPoint))
                    continue;

                if (TryCutSingleHairMesh(meshFilter, hairCardData, cutPoint, scissorDirection))
                {
                    processedMeshFilters.Add(meshFilter);
                    cutCount++;
                }
            }

        }

        EndHairUndoAction();
        return cutCount;
    }

    public IEnumerator ExecuteHairCardMeshCutBoxesRoutine(BoxCollider[] cutBoxes, Vector3 scissorDirection, System.Action<int> onCompleted = null, float? guardTargetLengthMeters = null)
    {
        if (IsCuttingBlocked)
        {
            onCompleted?.Invoke(0);
            yield break;
        }

        // Active VR-friendly cut path. It can spread work over frames to reduce headset stutter.
        ClearHairCardMeshCutPreview();

        if (cutBoxes == null || cutBoxes.Length == 0)
        {
            onCompleted?.Invoke(0);
            yield break;
        }

        activeGuardOverrideLength = guardTargetLengthMeters;
        BeginHairUndoAction("Cut Hair");

        int cutCount = 0;
        int cutsThisFrame = 0;
        HashSet<MeshFilter> processedMeshFilters = new HashSet<MeshFilter>();
        HashSet<SegmentedHairCard> processedSegmentedHairCards = new HashSet<SegmentedHairCard>();

        // Regression fix: if this coroutine is stopped mid-flight (e.g. StopClipper() calling
        // StopCoroutine on the outer routine while a yield below is still pending - trivially triggered by
        // releasing the clipper trigger mid-tick), everything after an aborted yield previously never ran,
        // so the cleanup at the bottom (EndHairUndoAction / resetting activeGuardOverrideLength) was
        // skipped entirely. That left activeGuardOverrideLength stuck non-null and the undo action
        // permanently "open" (actionDepth never returns to 0) for every clipper session afterward - a real
        // source of clipper-only corruption, since Scissor's cut path is a single synchronous method call
        // that can never be interrupted mid-flight like this. Wrapping in try/finally guarantees the
        // cleanup always runs: Unity calls Dispose() on a stopped coroutine's enumerator, which executes
        // any pending finally blocks exactly like a normal early return would.
        try
        {
            for (int boxIndex = 0; boxIndex < cutBoxes.Length; boxIndex++)
            {
                if (HasReachedMaxCuts(cutCount))
                    break;

                BoxCollider cutBox = cutBoxes[boxIndex];

                if (cutBox == null || !cutBox.enabled)
                    continue;

                Vector3 boxCenter = cutBox.transform.TransformPoint(cutBox.center);
                Vector3 boxHalfExtents = Vector3.Scale(cutBox.size * 0.5f, AbsVector3(cutBox.transform.lossyScale));
                Quaternion boxRotation = cutBox.transform.rotation;
                int hitCount = CollectHairHitsBox(boxCenter, boxHalfExtents, boxRotation);

                for (int i = 0; i < hitCount; i++)
                {
                    if (HasReachedMaxCuts(cutCount))
                        break;

                    Collider hit = hairOverlapBuffer[i];

                    if (hit == null)
                        continue;

                    if (TryCutSegmentedHairHit(hit, processedSegmentedHairCards, out bool handledSegmentedHair))
                    {
                        cutCount++;
                        cutsThisFrame++;

                        if (spreadCutsOverMultipleFrames && maxCutsProcessedPerFrame > 0 && cutsThisFrame >= maxCutsProcessedPerFrame)
                        {
                            cutsThisFrame = 0;
                            yield return null;
                        }

                        continue;
                    }

                    if (handledSegmentedHair)
                        continue;

                    MeshFilter meshFilter = hit.GetComponentInParent<MeshFilter>();

                    if (meshFilter == null || meshFilter.sharedMesh == null || processedMeshFilters.Contains(meshFilter))
                        continue;

                    HairCardData hairCardData = meshFilter.GetComponentInParent<HairCardData>();

                    if (requireHairCardData && hairCardData == null)
                        continue;

                    if (hairCardData != null && !hairCardData.IsCuttable)
                        continue;

                    if (!TryGetBoxHairCutPoint(meshFilter, hairCardData, cutBox, out Vector3 cutPoint))
                        continue;

                    if (!TryCutSingleHairMesh(meshFilter, hairCardData, cutPoint, scissorDirection))
                        continue;

                    processedMeshFilters.Add(meshFilter);
                    cutCount++;
                    cutsThisFrame++;

                    if (spreadCutsOverMultipleFrames && maxCutsProcessedPerFrame > 0 && cutsThisFrame >= maxCutsProcessedPerFrame)
                    {
                        cutsThisFrame = 0;
                        yield return null;
                    }
                }
            }

            if (cutCount > 0)
                Debug.Log($"[CuttingManager] Snip cut {cutCount} hair card(s).");
        }
        finally
        {
            EndHairUndoAction();
            activeGuardOverrideLength = null;
        }

        onCompleted?.Invoke(cutCount);
    }

    public int UpdateHairCardMeshCutBoxPreview(BoxCollider[] cutBoxes, Vector3 scissorDirection)
    {
        // Yellow preview path: shows the portion that would be removed if a snip happened now.
        ClearHairCardMeshCutPreview();

        if (cutBoxes == null || cutBoxes.Length == 0)
            return 0;

        int previewCount = 0;
        HashSet<MeshFilter> processedMeshFilters = new HashSet<MeshFilter>();

        for (int boxIndex = 0; boxIndex < cutBoxes.Length; boxIndex++)
        {
            BoxCollider cutBox = cutBoxes[boxIndex];

            if (cutBox == null || !cutBox.enabled)
                continue;

            Vector3 boxCenter = cutBox.transform.TransformPoint(cutBox.center);
            Vector3 boxHalfExtents = Vector3.Scale(cutBox.size * 0.5f, AbsVector3(cutBox.transform.lossyScale));
            Quaternion boxRotation = cutBox.transform.rotation;
            int hitCount = CollectHairHitsBox(boxCenter, boxHalfExtents, boxRotation);

            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = hairOverlapBuffer[i];

                if (hit == null)
                    continue;

                MeshFilter meshFilter = hit.GetComponentInParent<MeshFilter>();

                if (meshFilter == null || meshFilter.sharedMesh == null || processedMeshFilters.Contains(meshFilter))
                    continue;

                HairCardData hairCardData = meshFilter.GetComponentInParent<HairCardData>();

                if (requireHairCardData && hairCardData == null)
                    continue;

                if (hairCardData != null && !hairCardData.IsCuttable)
                    continue;

                if (!TryGetBoxHairCutPoint(meshFilter, hairCardData, cutBox, out Vector3 cutPoint))
                    continue;

                if (TryCreateCutPreview(meshFilter, hairCardData, cutPoint, scissorDirection))
                {
                    processedMeshFilters.Add(meshFilter);
                    previewCount++;
                }
            }

            if (useRendererBoundsFallbackForCutBoxes)
                previewCount += ProcessBoxRendererBoundsFallback(cutBox, scissorDirection, processedMeshFilters, true);

            if (maxCutPreviewsPerUpdate > 0 && previewCount >= maxCutPreviewsPerUpdate)
                return previewCount;
        }

        return previewCount;
    }

    private int ProcessBoxRendererBoundsFallback(
        BoxCollider cutBox,
        Vector3 scissorDirection,
        HashSet<MeshFilter> processedMeshFilters,
        bool createPreview)
    {
        MeshFilter[] meshFilters = hairCardsRoot != null
            ? hairCardsRoot.GetComponentsInChildren<MeshFilter>(true)
            : FindObjectsOfType<MeshFilter>(true);
        int successCount = 0;

        for (int i = 0; i < meshFilters.Length; i++)
        {
            if (createPreview && maxRendererFallbackChecksPerBox > 0 && i >= maxRendererFallbackChecksPerBox)
                break;

            MeshFilter meshFilter = meshFilters[i];

            if (meshFilter == null || meshFilter.sharedMesh == null || processedMeshFilters.Contains(meshFilter))
                continue;

            if (!DoesRendererBoundsOverlapBox(meshFilter, cutBox))
                continue;

            HairCardData hairCardData = meshFilter.GetComponentInParent<HairCardData>();

            if (requireHairCardData && hairCardData == null)
                continue;

            if (hairCardData != null && !hairCardData.IsCuttable)
                continue;

            if (!TryGetBoxHairCutPoint(meshFilter, hairCardData, cutBox, out Vector3 cutPoint))
                continue;

            bool succeeded = createPreview
                ? TryCreateCutPreview(meshFilter, hairCardData, cutPoint, scissorDirection)
                : TryCutSingleHairMesh(meshFilter, hairCardData, cutPoint, scissorDirection);

            if (!succeeded)
                continue;

            processedMeshFilters.Add(meshFilter);
            successCount++;

            if (createPreview && maxCutPreviewsPerUpdate > 0 && successCount >= maxCutPreviewsPerUpdate)
                return successCount;
        }

        return successCount;
    }

    private bool DoesRendererBoundsOverlapBox(MeshFilter meshFilter, BoxCollider cutBox)
    {
        Renderer renderer = meshFilter.GetComponent<Renderer>();

        if (renderer != null)

            return renderer.bounds.Intersects(cutBox.bounds);

        Bounds worldBounds = TransformLocalBounds(meshFilter.transform, meshFilter.sharedMesh.bounds);
        return worldBounds.Intersects(cutBox.bounds);
    }

    private Bounds TransformLocalBounds(Transform targetTransform, Bounds localBounds)
    {
        Vector3 center = targetTransform.TransformPoint(localBounds.center);
        Vector3 extents = localBounds.extents;

        Vector3 worldExtents =
            AbsVector3(targetTransform.TransformVector(new Vector3(extents.x, 0f, 0f))) +
            AbsVector3(targetTransform.TransformVector(new Vector3(0f, extents.y, 0f))) +
            AbsVector3(targetTransform.TransformVector(new Vector3(0f, 0f, extents.z)));

        return new Bounds(center, worldExtents * 2f);
    }

    public void ClearHairCardMeshCutPreview()
    {
        foreach (GameObject preview in activeCutPreviews.Values)
        {
            if (preview == null)
                continue;

            MeshFilter previewMeshFilter = preview.GetComponent<MeshFilter>();

            if (previewMeshFilter != null && previewMeshFilter.sharedMesh != null)
                Destroy(previewMeshFilter.sharedMesh);

            Destroy(preview);
        }

        activeCutPreviews.Clear();
    }

    public int ExecuteHairCardMeshBrush(Vector3 brushWorldPosition, float brushRadius, Vector3 worldMovementDelta, float strength)
    {
        if (IsCuttingBlocked)
            return 0;

        if (worldMovementDelta.sqrMagnitude <= 0.0000001f || strength <= 0f)
            return 0;

        BeginHairUndoAction("Brush Hair");

        int hitCount = CollectHairHits(brushWorldPosition, brushRadius);
        int brushedCount = 0;
        HashSet<MeshFilter> processedMeshFilters = new HashSet<MeshFilter>();

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = hairOverlapBuffer[i];

            if (hit == null)
                continue;

            MeshFilter meshFilter = hit.GetComponentInParent<MeshFilter>();

            if (meshFilter == null || meshFilter.sharedMesh == null || !processedMeshFilters.Add(meshFilter))
                continue;

            HairCardData hairCardData = meshFilter.GetComponentInParent<HairCardData>();

            if (requireHairCardData && hairCardData == null)
                continue;

            if (hairCardData != null && !hairCardData.IsCuttable)
                continue;

            if (TryBrushSingleHairMesh(meshFilter, hairCardData, brushWorldPosition, brushRadius, worldMovementDelta, strength))
                brushedCount++;
        }

        EndHairUndoAction();
        return brushedCount;
    }

    public int ExecuteHairCardMeshComb(Vector3 combWorldPosition, float combRadius, Vector3 worldMovementDelta, float strength)
    {
        if (worldMovementDelta.sqrMagnitude <= 0.0000001f || strength <= 0f)
            return 0;

        return ExecuteHairCardMeshBrush(combWorldPosition, combRadius, worldMovementDelta, strength);
    }

    public int ExecuteHairCardMeshCombBoxes(BoxCollider[] combBoxes, Vector3 worldMovementDelta, float strength)
    {
        if (IsCuttingBlocked)
            return 0;

        // Active comb path when the comb has tooth-area boxes assigned.
        if (combBoxes == null || combBoxes.Length == 0)
            return 0;

        if (worldMovementDelta.sqrMagnitude <= 0.0000001f || strength <= 0f)
            return 0;

        BeginHairUndoAction("Comb Hair");

        int combedCount = 0;
        HashSet<MeshFilter> processedMeshFilters = new HashSet<MeshFilter>();
        HashSet<SegmentedHairCard> processedSegmentedHairCards = new HashSet<SegmentedHairCard>();

        for (int boxIndex = 0; boxIndex < combBoxes.Length; boxIndex++)
        {
            if (maxHairCardsCombedPerFrame > 0 && combedCount >= maxHairCardsCombedPerFrame)
                break;

            BoxCollider combBox = combBoxes[boxIndex];

            if (combBox == null || !combBox.enabled)
                continue;

            Vector3 boxCenter = combBox.transform.TransformPoint(combBox.center);
            Vector3 boxHalfExtents = Vector3.Scale(combBox.size * 0.5f, AbsVector3(combBox.transform.lossyScale));
            Quaternion boxRotation = combBox.transform.rotation;
            int hitCount = CollectHairHitsBox(boxCenter, boxHalfExtents, boxRotation);

            for (int i = 0; i < hitCount; i++)
            {
                if (maxHairCardsCombedPerFrame > 0 && combedCount >= maxHairCardsCombedPerFrame)
                    break;

                Collider hit = hairOverlapBuffer[i];

                if (hit == null)
                    continue;

                if (TryVisualCombSegmentedHairHit(hit, processedSegmentedHairCards, worldMovementDelta, strength, out bool handledSegmentedHair))
                {
                    combedCount++;
                    continue;
                }

                if (handledSegmentedHair)
                    continue;

                MeshFilter meshFilter = hit.GetComponentInParent<MeshFilter>();

                if (meshFilter == null || meshFilter.sharedMesh == null || !processedMeshFilters.Add(meshFilter))
                    continue;

                HairCardData hairCardData = meshFilter.GetComponentInParent<HairCardData>();

                if (requireHairCardData && hairCardData == null)
                    continue;

                if (hairCardData != null && !hairCardData.IsCuttable)
                    continue;

                if (TryCombSingleHairMeshWithBox(meshFilter, hairCardData, combBox, worldMovementDelta, strength))
                    combedCount++;
            }
        }

        EndHairUndoAction();
        return combedCount;
    }

    // FEATURE (Comb Hold): read-only lookup for Comb's "grip a hair card at a point" feature - finds the
    // nearest SegmentedHairCard currently overlapping the comb's interaction boxes, using the exact same
    // cuttableHairLayers/hairTriggerInteraction rules (via CollectHairHitsBox) as scissor/clipper cutting,
    // so "can I hold it" always agrees with "can I cut it". Does not cut, comb, or modify anything - just
    // reports which card and world point is closest to referencePoint (typically the comb's own center),
    // so the caller can figure out how far along the card that point sits.
    public bool TryFindNearestHairCardSegmentInBoxes(BoxCollider[] boxes, Vector3 referencePoint, out SegmentedHairCard segmentedHairCard, out Vector3 touchPoint, float extraPaddingMeters = 0f)
    {
        segmentedHairCard = null;
        touchPoint = referencePoint;

        if (boxes == null)
            return false;

        Vector3 padding = Vector3.one * Mathf.Max(0f, extraPaddingMeters);
        float bestSqrDistance = float.PositiveInfinity;

        for (int boxIndex = 0; boxIndex < boxes.Length; boxIndex++)
        {
            BoxCollider box = boxes[boxIndex];

            if (box == null || !box.enabled)
                continue;

            Vector3 boxCenter = box.transform.TransformPoint(box.center);
            // Extra padding makes the SEARCH area a bit more forgiving than the visual box itself (e.g. for
            // Comb's hold-grab, where marginal/edge-of-box overlaps otherwise make it feel finicky to grip) -
            // purely a lookup-radius nudge, does not change the box's actual visual size or cutting/combing area.
            Vector3 boxHalfExtents = Vector3.Scale(box.size * 0.5f, AbsVector3(box.transform.lossyScale)) + padding;
            Quaternion boxRotation = box.transform.rotation;
            int hitCount = CollectHairHitsBox(boxCenter, boxHalfExtents, boxRotation);

            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = hairOverlapBuffer[i];

                if (hit == null)
                    continue;

                HairCardSegment segment = hit.GetComponentInParent<HairCardSegment>();

                if (segment == null || segment.Owner == null)
                    continue;

                // Cards hidden by a hair clip (see ClipZoneGatherEffect) have IsCuttable=false - same rule
                // TryCutSegmentedHairHit/TryVisualCombSegmentedHairHit already use, so a clip-hidden card
                // can't be gripped by the comb's hold feature either, even though its collider is still live.
                HairCardData candidateData = segment.Owner.HairCardData;

                if (candidateData != null && !candidateData.IsCuttable)
                    continue;

                Vector3 closestPoint = hit.ClosestPoint(referencePoint);
                float sqrDistance = (closestPoint - referencePoint).sqrMagnitude;

                if (sqrDistance >= bestSqrDistance)
                    continue;

                bestSqrDistance = sqrDistance;
                segmentedHairCard = segment.Owner;
                touchPoint = closestPoint;
            }
        }

        return segmentedHairCard != null;
    }

    // FEATURE (Comb Hold - Freeze): marks/unmarks a hair card's transform as frozen for the comb - see the
    // frozenCombHairCardTransforms field comment for what freezing actually does. Comb.cs is the only caller;
    // it drives this every frame based on which cards are currently inside its interaction boxes while its
    // hold feature is engaged.
    public void SetHairCardFrozenForComb(Transform hairCardTransform, bool frozen)
    {
        if (hairCardTransform == null)
            return;

        if (frozen)
            frozenCombHairCardTransforms.Add(hairCardTransform);
        else
            frozenCombHairCardTransforms.Remove(hairCardTransform);
    }

    public bool IsHairCardFrozenForComb(Transform hairCardTransform)
    {
        return hairCardTransform != null && frozenCombHairCardTransforms.Contains(hairCardTransform);
    }

    // FEATURE (Comb Hold - Freeze): collects every distinct SegmentedHairCard currently overlapping ANY of
    // the given boxes (not just the single nearest one, unlike TryFindNearestHairCardSegmentInBoxes) - used
    // by Comb to decide which cards to freeze/unfreeze each frame while its hold feature is engaged. Read-only.
    public int CollectHairCardsInBoxes(BoxCollider[] boxes, HashSet<SegmentedHairCard> results, float extraPaddingMeters = 0f)
    {
        results.Clear();

        if (boxes == null)
            return 0;

        Vector3 padding = Vector3.one * Mathf.Max(0f, extraPaddingMeters);

        for (int boxIndex = 0; boxIndex < boxes.Length; boxIndex++)
        {
            BoxCollider box = boxes[boxIndex];

            if (box == null || !box.enabled)
                continue;

            Vector3 boxCenter = box.transform.TransformPoint(box.center);
            Vector3 boxHalfExtents = Vector3.Scale(box.size * 0.5f, AbsVector3(box.transform.lossyScale)) + padding;
            Quaternion boxRotation = box.transform.rotation;
            int hitCount = CollectHairHitsBox(boxCenter, boxHalfExtents, boxRotation);

            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = hairOverlapBuffer[i];

                if (hit == null)
                    continue;

                HairCardSegment segment = hit.GetComponentInParent<HairCardSegment>();

                if (segment == null || segment.Owner == null)
                    continue;

                // Clip-hidden cards (IsCuttable=false, see ClipZoneGatherEffect) are excluded here so the
                // comb never freezes/touches/measures them - they behave as if they aren't there at all
                // while a clip covers them, same as cutting and bending already treat them.
                HairCardData ownerData = segment.Owner.HairCardData;

                if (ownerData != null && !ownerData.IsCuttable)
                    continue;

                results.Add(segment.Owner);
            }
        }

        return results.Count;
    }

    // FEATURE (Comb Hold Readout - "Longest Held"): scans every hair card currently overlapping the comb's
    // interaction boxes (not just the single nearest one Comb actually clamps) and reports the longest
    // root-to-touch-point length among them, purely for the on-comb HUD label. Read-only - does not cut,
    // comb, or hold/clamp anything.
    public bool TryGetLongestHairCardLengthInBoxes(BoxCollider[] boxes, Vector3 referencePoint, out float longestLengthMeters, out SegmentedHairCard longestHairCard, float extraPaddingMeters = 0f)
    {
        longestLengthMeters = 0f;
        longestHairCard = null;

        if (boxes == null)
            return false;

        Vector3 padding = Vector3.one * Mathf.Max(0f, extraPaddingMeters);
        longestHoldClosestPointBuffer.Clear();

        for (int boxIndex = 0; boxIndex < boxes.Length; boxIndex++)
        {
            BoxCollider box = boxes[boxIndex];

            if (box == null || !box.enabled)
                continue;

            Vector3 boxCenter = box.transform.TransformPoint(box.center);
            Vector3 boxHalfExtents = Vector3.Scale(box.size * 0.5f, AbsVector3(box.transform.lossyScale)) + padding;
            Quaternion boxRotation = box.transform.rotation;
            int hitCount = CollectHairHitsBox(boxCenter, boxHalfExtents, boxRotation);

            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = hairOverlapBuffer[i];

                if (hit == null)
                    continue;

                HairCardSegment segment = hit.GetComponentInParent<HairCardSegment>();

                if (segment == null || segment.Owner == null)
                    continue;

                SegmentedHairCard card = segment.Owner;

                // Clip-hidden cards (IsCuttable=false, see ClipZoneGatherEffect) never factor into the
                // "Longest Held" readout - the player shouldn't see a measurement for hair they can't see.
                if (card.HairCardData != null && !card.HairCardData.IsCuttable)
                    continue;

                Vector3 closestPoint = hit.ClosestPoint(referencePoint);

                if (!longestHoldClosestPointBuffer.TryGetValue(card, out Vector3 existingPoint))
                {
                    longestHoldClosestPointBuffer[card] = closestPoint;
                    continue;
                }

                // A card can straddle multiple boxes/segments - keep whichever touch point sits furthest
                // from the root, so the readout reflects the deepest point the comb is actually touching.
                HairCardData cardData = card.HairCardData;

                if (cardData == null)
                    continue;

                float existingDist = Vector3.Dot(existingPoint - cardData.GetRootPosition(), cardData.GetGrowthDirection());
                float candidateDist = Vector3.Dot(closestPoint - cardData.GetRootPosition(), cardData.GetGrowthDirection());

                if (candidateDist > existingDist)
                    longestHoldClosestPointBuffer[card] = closestPoint;
            }
        }

        foreach (KeyValuePair<SegmentedHairCard, Vector3> entry in longestHoldClosestPointBuffer)
        {
            HairCardData cardData = entry.Key.HairCardData;

            if (cardData == null)
                continue;

            float lengthMeters = Mathf.Max(0f, Vector3.Dot(entry.Value - cardData.GetRootPosition(), cardData.GetGrowthDirection()));

            if (longestHairCard == null || lengthMeters > longestLengthMeters)
            {
                longestLengthMeters = lengthMeters;
                longestHairCard = entry.Key;
            }
        }

        return longestHairCard != null;
    }

    private bool TryVisualCombSegmentedHairHit(
        Collider hit,
        HashSet<SegmentedHairCard> processedSegmentedHairCards,
        Vector3 worldMovementDelta,
        float strength,
        out bool handledSegmentedHair)
    {
        handledSegmentedHair = false;

        if (!useVisualCombForSegmentedHairCards || hit == null)
            return false;

        HairCardSegment segment = hit.GetComponentInParent<HairCardSegment>();

        if (segment == null || segment.Owner == null)
            return false;

        SegmentedHairCard segmentedHairCard = segment.Owner;
        handledSegmentedHair = true;

        if (processedSegmentedHairCards.Contains(segmentedHairCard))
            return false;

        processedSegmentedHairCards.Add(segmentedHairCard);

        HairCardData hairCardData = segmentedHairCard.HairCardData;

        if (requireHairCardData && hairCardData == null)
            return false;

        if (hairCardData != null && !hairCardData.IsCuttable)
            return false;

        return TryVisualCombHairCard(segmentedHairCard.transform, hairCardData, worldMovementDelta, strength);
    }

    private bool TryVisualCombHairCard(Transform hairCardTransform, HairCardData hairCardData, Vector3 worldMovementDelta, float strength)
    {
        if (hairCardTransform == null || worldMovementDelta.sqrMagnitude <= 0.0000001f || strength <= 0f)
            return false;

        // FEATURE (Comb Hold - Freeze): while Comb's hold feature is engaged, every hair card currently
        // touching its interaction boxes is frozen completely in place - no bending at all - until it leaves
        // the boxes. Bail out before any rotation/undo work below.
        if (frozenCombHairCardTransforms.Contains(hairCardTransform))
            return false;

        Vector3 currentGrowthDirection = hairCardData != null
            ? hairCardData.GetGrowthDirection()
            : hairCardTransform.TransformDirection(Vector3.down);
        Vector3 combDirection = worldMovementDelta.normalized;

        if (currentGrowthDirection.sqrMagnitude <= 0.0001f || combDirection.sqrMagnitude <= 0.0001f)
            return false;

        float maxRadians = Mathf.Deg2Rad * Mathf.Max(0f, visualCombMaxDegreesPerFrame) * Mathf.Clamp01(strength);
        Vector3 targetGrowthDirection = Vector3.RotateTowards(currentGrowthDirection, combDirection, maxRadians, 0f);

        if (targetGrowthDirection.sqrMagnitude <= 0.0001f)
            return false;

        // FEATURE (guaranteed return to original pose): capture this card's TRUE rest local transform the
        // very first time it's ever combed, BEFORE this stroke's rotation is applied - this is the exact
        // pose ApplyVisualCombGravity hard-snaps back to once fully settled, so repeated comb/settle cycles
        // can never leave a card visibly drifted ("stuck floating") the way relying purely on incremental
        // RotateTowards + position-compensation math could.
        bool isFirstEverCombTouch = applyGravityAfterVisualComb && !visuallyCombedHairCards.ContainsKey(hairCardTransform);
        Vector3 capturedRestLocalPosition = isFirstEverCombTouch ? hairCardTransform.localPosition : Vector3.zero;
        Quaternion capturedRestLocalRotation = isFirstEverCombTouch ? hairCardTransform.localRotation : Quaternion.identity;

        CaptureHairUndoBeforeChange(null, hairCardData);

        Vector3 rootBefore = hairCardData != null ? hairCardData.GetRootPosition() : hairCardTransform.position;
        Quaternion rotationDelta = Quaternion.FromToRotation(currentGrowthDirection, targetGrowthDirection.normalized);
        hairCardTransform.rotation = rotationDelta * hairCardTransform.rotation;

        if (hairCardData != null)
            hairCardData.GrowthDirection = targetGrowthDirection.normalized;

        if (visualCombPreserveRootPosition)
        {
            Vector3 rootAfter = hairCardData != null ? hairCardData.GetRootPosition() : hairCardTransform.position;
            hairCardTransform.position += rootBefore - rootAfter;
        }

        RegisterVisualCombedHairCard(hairCardTransform, hairCardData, currentGrowthDirection, isFirstEverCombTouch, capturedRestLocalPosition, capturedRestLocalRotation);
        MarkHairUndoChanged();

        return true;
    }

    private void RegisterVisualCombedHairCard(Transform hairCardTransform, HairCardData hairCardData, Vector3 restGrowthDirection, bool isFirstEverCombTouch, Vector3 capturedRestLocalPosition, Quaternion capturedRestLocalRotation)
    {
        if (!applyGravityAfterVisualComb || hairCardTransform == null)
            return;

        Vector3 restLocalPosition = capturedRestLocalPosition;
        Quaternion restLocalRotation = capturedRestLocalRotation;

        if (!isFirstEverCombTouch && visuallyCombedHairCards.TryGetValue(hairCardTransform, out VisualCombedHairCard existingHairCard))
        {
            restGrowthDirection = existingHairCard.RestGrowthDirection;
            restLocalPosition = existingHairCard.RestLocalPosition;
            restLocalRotation = existingHairCard.RestLocalRotation;
        }

        visuallyCombedHairCards[hairCardTransform] = new VisualCombedHairCard
        {
            HairCardData = hairCardData,
            LastCombedTime = Time.time,
            RestGrowthDirection = restGrowthDirection.sqrMagnitude > 0.0001f ? restGrowthDirection.normalized : Vector3.down,
            RestLocalPosition = restLocalPosition,
            RestLocalRotation = restLocalRotation
        };
    }

    private void ApplyVisualCombGravity()
    {
        if (!applyGravityAfterVisualComb || visuallyCombedHairCards.Count == 0)
            return;

        if (visualCombGravityDegreesPerSecond <= 0f)
            return;

        s_visualCombGravityRemovalBuffer.Clear();
        float maxRadians = Mathf.Deg2Rad * visualCombGravityDegreesPerSecond * Time.deltaTime;

        foreach (KeyValuePair<Transform, VisualCombedHairCard> pair in visuallyCombedHairCards)
        {
            Transform hairCardTransform = pair.Key;
            VisualCombedHairCard visualHairCard = pair.Value;

            if (hairCardTransform == null)
            {
                s_visualCombGravityRemovalBuffer.Add(hairCardTransform);
                continue;
            }

            // FEATURE (Comb Hold - Freeze): frozen cards (currently touching the comb's boxes while hold is
            // engaged) stay exactly as-is - no settling either - until Comb.cs unfreezes them (which happens
            // the moment they leave the boxes), at which point settling resumes/starts immediately.
            if (frozenCombHairCardTransforms.Contains(hairCardTransform))
                continue;

            if (Time.time - visualHairCard.LastCombedTime < visualCombGravityDelay)
                continue;

            HairCardData hairCardData = visualHairCard.HairCardData;
            Vector3 currentDirection = hairCardData != null
                ? hairCardData.GetGrowthDirection()
                : hairCardTransform.TransformDirection(Vector3.down);

            if (currentDirection.sqrMagnitude <= 0.0001f)
                continue;

            Vector3 targetDirection = visualCombSettleTowardOriginalDirection
                ? visualHairCard.RestGrowthDirection
                : visualCombGravityDirection.normalized;

            if (targetDirection.sqrMagnitude <= 0.0001f)
                continue;

            Vector3 settledDirection = Vector3.RotateTowards(currentDirection, targetDirection, maxRadians, 0f);

            if (settledDirection.sqrMagnitude <= 0.0001f)
                continue;

            // FEATURE (guaranteed return to original pose): RotateTowards already clamps to targetDirection
            // once the remaining angle fits within this frame's step, so reaching it here means this IS the
            // settling frame. While settling toward the card's own original direction (as opposed to a fixed
            // gravity direction, which has no "original pose" to return to), hard-snap the transform to the
            // EXACT rest local position/rotation captured on first touch, instead of trusting the accumulated
            // rotation + position-compensation math from potentially dozens of comb strokes - this is what
            // guarantees the card always returns to precisely where it started, with no drift.
            bool reachedTarget = Vector3.Angle(settledDirection, targetDirection) <= 0.01f;

            if (reachedTarget && visualCombSettleTowardOriginalDirection)
            {
                hairCardTransform.localPosition = visualHairCard.RestLocalPosition;
                hairCardTransform.localRotation = visualHairCard.RestLocalRotation;

                if (hairCardData != null)
                    hairCardData.GrowthDirection = visualHairCard.RestGrowthDirection;

                s_visualCombGravityRemovalBuffer.Add(hairCardTransform);
                continue;
            }

            Vector3 rootBefore = hairCardData != null ? hairCardData.GetRootPosition() : hairCardTransform.position;
            Quaternion rotationDelta = Quaternion.FromToRotation(currentDirection, settledDirection.normalized);
            hairCardTransform.rotation = rotationDelta * hairCardTransform.rotation;

            if (hairCardData != null)
                hairCardData.GrowthDirection = settledDirection.normalized;

            if (visualCombPreserveRootPosition)
            {
                Vector3 rootAfter = hairCardData != null ? hairCardData.GetRootPosition() : hairCardTransform.position;
                hairCardTransform.position += rootBefore - rootAfter;
            }

            // Settling toward a fixed gravity direction has no rest-pose snap (there's no single "original"
            // to return to), but once it reaches that target there's still no reason to keep re-processing
            // a no-op rotation every frame forever - stop tracking it too.
            if (reachedTarget)
                s_visualCombGravityRemovalBuffer.Add(hairCardTransform);
        }

        for (int i = 0; i < s_visualCombGravityRemovalBuffer.Count; i++)
            visuallyCombedHairCards.Remove(s_visualCombGravityRemovalBuffer[i]);
    }

    // Manual safety valve / instant fix: immediately snaps every currently-tracked combed hair card back to
    // its exact original pose, bypassing the gradual gravity settle entirely. Useful if a card looks stuck
    // and you don't want to wait for (or debug) the automatic settle, or just to reset the whole head at once.
    [ContextMenu("Reset All Combed Hair Cards To Original Pose")]
    public void ResetAllCombedHairCardsToOriginalPose()
    {
        int resetCount = 0;

        foreach (KeyValuePair<Transform, VisualCombedHairCard> pair in visuallyCombedHairCards)
        {
            Transform hairCardTransform = pair.Key;

            if (hairCardTransform == null)
                continue;

            VisualCombedHairCard visualHairCard = pair.Value;
            hairCardTransform.localPosition = visualHairCard.RestLocalPosition;
            hairCardTransform.localRotation = visualHairCard.RestLocalRotation;

            if (visualHairCard.HairCardData != null)
                visualHairCard.HairCardData.GrowthDirection = visualHairCard.RestGrowthDirection;

            resetCount++;
        }

        visuallyCombedHairCards.Clear();
        Debug.Log($"[CuttingManager] Reset {resetCount} combed hair card(s) to their original pose.");
    }

    [ContextMenu("Add Mesh Colliders To Hair Cards")]
    public void AddMeshCollidersToHairCards()
    {
        Transform searchRoot = hairCardsRoot != null ? hairCardsRoot : transform;
        MeshFilter[] meshFilters = searchRoot.GetComponentsInChildren<MeshFilter>(true);
        int addedCount = 0;

        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter meshFilter = meshFilters[i];

            if (meshFilter.sharedMesh == null)
                continue;

            if (requireHairCardData && meshFilter.GetComponentInParent<HairCardData>() == null)
                continue;

            MeshCollider meshCollider = meshFilter.GetComponent<MeshCollider>();

            if (meshCollider == null)
            {
                meshCollider = meshFilter.gameObject.AddComponent<MeshCollider>();
                addedCount++;
            }

            meshCollider.sharedMesh = meshFilter.sharedMesh;
            meshCollider.convex = false;
        }

        Debug.Log($"[CuttingManager] Added or refreshed hair mesh colliders: {addedCount}");
    }

    private int CollectHairHits(Vector3 scissorPosition, float activeRadius)
    {
        if (hairOverlapBuffer == null || hairOverlapBuffer.Length != overlapBufferSize)
            hairOverlapBuffer = new Collider[Mathf.Max(8, overlapBufferSize)];

        int hitCount = Physics.OverlapSphereNonAlloc(
            scissorPosition,
            activeRadius,
            hairOverlapBuffer,
            cuttableHairLayers,
            hairTriggerInteraction
        );

        while (hitCount == hairOverlapBuffer.Length)
        {
            hairOverlapBuffer = new Collider[hairOverlapBuffer.Length * 2];
            hitCount = Physics.OverlapSphereNonAlloc(
                scissorPosition,
                activeRadius,
                hairOverlapBuffer,
                cuttableHairLayers,
                hairTriggerInteraction
            );
        }

        return hitCount;
    }

    private int CollectHairHitsCapsule(Vector3 start, Vector3 end, float radius)
    {
        if (hairOverlapBuffer == null || hairOverlapBuffer.Length != overlapBufferSize)
            hairOverlapBuffer = new Collider[Mathf.Max(8, overlapBufferSize)];

        int hitCount = Physics.OverlapCapsuleNonAlloc(
            start,
            end,
            radius,
            hairOverlapBuffer,
            cuttableHairLayers,
            hairTriggerInteraction
        );

        while (hitCount == hairOverlapBuffer.Length)
        {
            hairOverlapBuffer = new Collider[hairOverlapBuffer.Length * 2];
            hitCount = Physics.OverlapCapsuleNonAlloc(
                start,
                end,
                radius,
                hairOverlapBuffer,
                cuttableHairLayers,
                hairTriggerInteraction
            );
        }

        return hitCount;
    }

    private int CollectHairHitsBox(Vector3 center, Vector3 halfExtents, Quaternion orientation)
    {
        if (hairOverlapBuffer == null || hairOverlapBuffer.Length != overlapBufferSize)
            hairOverlapBuffer = new Collider[Mathf.Max(8, overlapBufferSize)];

        int hitCount = Physics.OverlapBoxNonAlloc(
            center,
            halfExtents,
            hairOverlapBuffer,
            orientation,
            cuttableHairLayers,
            hairTriggerInteraction
        );

        while (hitCount == hairOverlapBuffer.Length)
        {
            hairOverlapBuffer = new Collider[hairOverlapBuffer.Length * 2];
            hitCount = Physics.OverlapBoxNonAlloc(
                center,
                halfExtents,
                hairOverlapBuffer,
                orientation,
                cuttableHairLayers,
                hairTriggerInteraction
            );
        }

        return hitCount;
    }

    private bool TryCutSingleHairMesh(MeshFilter meshFilter, HairCardData hairCardData, Vector3 cutWorldPosition, Vector3 scissorDirection)
    {
        Mesh sourceMesh = meshFilter.mesh;

        if (sourceMesh == null || sourceMesh.vertexCount < 3)
            return false;

        Vector3 rootWorldPosition = GetHairRootWorldPosition(meshFilter, hairCardData);
        Vector3 growthDirection = GetHairGrowthDirection(meshFilter, hairCardData, scissorDirection);
        float rootToCutDistance = Vector3.Dot(cutWorldPosition - rootWorldPosition, growthDirection);

        if (rootToCutDistance < 0f)
        {
            growthDirection = -growthDirection;
            rootToCutDistance = -rootToCutDistance;
        }

        if (rootToCutDistance <= minimumDistanceFromRoot)
            return false;

        if (Vector3.Dot(rootWorldPosition - cutWorldPosition, growthDirection) > 0f)
            growthDirection = -growthDirection;

        if (TryGetHaircutTarget(hairCardData, out float targetLength, out float targetTolerance))
        {
            float safeTolerance = Mathf.Max(0f, targetTolerance);
            float minimumAllowedLength = Mathf.Max(minimumDistanceFromRoot, targetLength - safeTolerance);

            if (hairCardData.CurrentLength <= targetLength + safeTolerance)
                return false;

            if (rootToCutDistance < minimumAllowedLength)
            {
                rootToCutDistance = minimumAllowedLength;
                cutWorldPosition = rootWorldPosition + growthDirection * rootToCutDistance;
            }
        }

        Vector3 planePointLocal = meshFilter.transform.InverseTransformPoint(cutWorldPosition);
        Vector3 planeNormalLocal = meshFilter.transform.InverseTransformDirection(growthDirection).normalized;

        if (!BuildClippedMesh(sourceMesh, planePointLocal, planeNormalLocal, true, out Mesh keptMesh))
            return false;

        Mesh cutMesh = null;

        if (spawnFallenHair && !BuildClippedMesh(sourceMesh, planePointLocal, planeNormalLocal, false, out cutMesh))
        {
            Destroy(keptMesh);
            return false;
        }

        CaptureHairUndoBeforeChange(meshFilter, hairCardData);

        meshFilter.mesh = keptMesh;
        RefreshColliderAfterCut(meshFilter, keptMesh);

        if (spawnFallenHair)
            SpawnFallenHair(meshFilter, cutMesh, cutWorldPosition);

        UpdateHairCardLength(hairCardData, rootToCutDistance);
        MarkHairUndoChanged();

        return true;
    }

    private bool HasReachedMaxCuts(int cutCount)
    {
        return maxHairCardsCutPerSnip > 0 && cutCount >= maxHairCardsCutPerSnip;
    }

    private bool TryCutSegmentedHairHit(Collider hit, HashSet<SegmentedHairCard> processedSegmentedHairCards, out bool handledSegmentedHair)
    {
        handledSegmentedHair = false;

        if (!preferSegmentedHairCards || hit == null)
            return false;

        HairCardSegment segment = hit.GetComponentInParent<HairCardSegment>();

        if (segment == null || segment.Owner == null)
        {
            // Not a per-segment collider - but if this hit still belongs to a GameObject that has a
            // SegmentedHairCard somewhere in its parent chain (e.g. a leftover/legacy collider still
            // sitting on the card's own root from before per-segment colliders existed), treat this as
            // already handled by the segmented system and do nothing, rather than falling through to the
            // legacy single-mesh cut path in the caller. Running BOTH systems on the same card corrupts
            // state: the legacy path clones and directly replaces the card's MeshFilter.mesh, and the very
            // next segment-driven cut elsewhere on the same card calls RebuildCombinedVisibleMesh(), which
            // overwrites that mesh again from the (untouched) segment states - visually "growing back"
            // whatever the legacy path had just cut away. This is the prime suspect for hair cards
            // reverting to full length while their last few segments are being clippered.
            if (hit.GetComponentInParent<SegmentedHairCard>() != null)
                handledSegmentedHair = true;

            return false;
        }

        handledSegmentedHair = true;

        if (processedSegmentedHairCards.Contains(segment.Owner))
            return false;

        HairCardData hairCardData = segment.Owner.HairCardData;

        if (requireHairCardData && hairCardData == null)
            return false;

        if (hairCardData != null && !hairCardData.IsCuttable)
            return false;

        // Stubble downgrade re-entry: this card's long hair is already fully gone and its area already
        // swapped to a stubble style (Butch/Induction). The collider that was just hit is the
        // deliberately-kept-alive "ghost" collider on segment 0 (see StubbleAreaSwapController), not
        // real hair - hand this off there and stop, since there is no more SegmentedHairCard cutting to
        // do on an already-fully-hidden card.
        if (StubbleAreaSwapController.Instance != null &&
            StubbleAreaSwapController.Instance.TryHandleAlreadySwappedArea(segment.Owner, activeGuardOverrideLength))
        {
            processedSegmentedHairCards.Add(segment.Owner);
            return true;
        }

        CaptureHairUndoBeforeChange(segment.Owner);

        int cutStartIndexBeforeCut = segment.Owner.CurrentCutStartIndex;

        bool cutSucceeded = TryGetHaircutTarget(hairCardData, out float targetLength, out float targetTolerance)
            ? segment.Owner.CutFromSegmentIndexRespectingTarget(segment.SegmentIndex, targetLength, targetTolerance)
            : segment.Cut();

        // Fallback A (any tool): the normal cut was blocked only because the target was already met, but
        // this is specifically the last remaining visible segment (index 0) of a card already at minimum
        // segment length - remove it so the stubble reveal shows cleanly instead of a leftover hair chunk.
        //
        // Fallback B (Clipper/guard cuts only - THE FIX for "clipper guard doesn't finish the cut"):
        // CutFromSegmentIndexRespectingTarget rounds UP to the nearest WHOLE segment so a guard cut never
        // goes shorter than the guard length. For a card whose segments are coarse relative to a short
        // guard (1.5cm/0.5cm), that rounding can permanently strand it 1-2 segments short of ever reaching
        // zero - every further hit at that same spot recomputes the exact same blocked result forever, so
        // those last 1-2 segments' colliders never close and the card's combined-mesh renderer never goes
        // inactive. That is exactly the "guard doesn't close the box colliders / mesh renderer never goes
        // inactive" symptom. Per the original spec ("force them to be cut anyway" once down to their last
        // one or two segments), any guard-driven Clipper cut on a card already down to its last 1-2
        // segments now force-completes all the way to fully hidden instead of staying stuck.
        if (!cutSucceeded && allowFinalSegmentRemovalForStubbleReveal && cutStartIndexBeforeCut > 0 && !IsMaxCappedGuard(activeGuardOverrideLength))
        {
            bool isLastSegment = segment.SegmentIndex == 0 && segment.Owner.IsAtMinimumSegmentLength;
            bool isStrandedByGuardRounding = activeGuardOverrideLength.HasValue && cutStartIndexBeforeCut <= 2;

            if (isLastSegment || isStrandedByGuardRounding)
                cutSucceeded = segment.Owner.CutFromSegmentIndex(0);
        }

        if (!cutSucceeded)
            return false;

        MarkHairUndoChanged();

        // Newly reached fully-invisible FROM having some visible length before this cut - the moment to
        // decide whether a stubble style (Butch/Induction) should replace it, based on whichever
        // tool/guard just did this cut (activeGuardOverrideLength is only ever set by Clipper). Uses the
        // actual before/after transition (rather than requiring the "was already at minimum" state
        // specifically) so Fallback B's forced multi-segment jump straight to zero still triggers this
        // correctly.
        if (cutStartIndexBeforeCut > 0 && segment.Owner.CurrentCutStartIndex == 0 && StubbleAreaSwapController.Instance != null)
        {
            StubbleAreaSwapController.Instance.TryApplyInitialSwap(segment.Owner, activeGuardOverrideLength);
        }

        if (spawnFallenHair && fallenHairPoolPrefab != null)
            SpawnFallenHairPrefabOnly(segment);

        processedSegmentedHairCards.Add(segment.Owner);
        return true;
    }

    // True when the currently-active guard override is the largest ("hard cap") guard size - see the
    // handoff note on maxCappedGuardLengthCm above. Compares in centimeters (same unit the guard system
    // is authored in) rather than the raw meters value, so the tolerance stays meaningful regardless of
    // how small guardOverrideTolerance (a separate, much tighter target-matching tolerance) is set to.
    private bool IsMaxCappedGuard(float? guardLengthMeters)
    {
        if (!guardLengthMeters.HasValue)
            return false;

        float guardCm = guardLengthMeters.Value * 100f;
        return Mathf.Abs(guardCm - maxCappedGuardLengthCm) <= Mathf.Max(0.001f, guardCapToleranceCm);
    }

    private bool TryGetHaircutTarget(HairCardData hairCardData, out float targetLength, out float tolerance)
    {
        // Practice uses this target as a hard stop; Assessment lets the player overcut and only scores later.
        targetLength = 0f;
        tolerance = 0f;

        if (!respectActiveHaircutTargets || hairCardData == null)
            return false;

        // Guard-driven clipper cuts: when a guard length is actively supplied by the calling tool, it takes
        // priority over the hairstyle's flat per-zone target - matches how a real clipper guard determines
        // cut length. Only ever set by Clipper's cut calls; Scissor cuts never set this, so unaffected.
        if (activeGuardOverrideLength.HasValue)
        {
            targetLength = Mathf.Max(0f, activeGuardOverrideLength.Value);
            tolerance = Mathf.Max(0f, guardOverrideTolerance);
            return targetLength > 0f;
        }

        if (stage1HaircutManager != null && !stage1HaircutManager.ShouldLimitCutsToTarget)
            return false;

        // FIX (same duplicated-return bug as EvaluationSystem.TryGetHairCardTarget had): a resolved target
        // of exactly 0 (shave to stubble) is legitimate and must still report success here - returning
        // `targetLength > 0f` made a 0-length zone report "no target", which is wrong even though it
        // happens not to change segmented-card cutting today (CutFromSegmentIndexRespectingTarget's own
        // 0-target early-out reaches the same CutFromSegmentIndex call either way) - it DOES matter for
        // TryCreateCutPreview below, which uses this return value to decide whether to clamp the preview
        // cut position to the target at all.
        if (stage1HaircutManager != null && stage1HaircutManager.TryGetTargetLength(hairCardData, out targetLength, out tolerance))
            return true;

        if (!hairCardData.HasUsableTargetLength())
            return false;

        targetLength = hairCardData.TargetLength;
        tolerance = hairCardData.TargetTolerance;
        return true;
    }

    private bool TryCreateCutPreview(MeshFilter meshFilter, HairCardData hairCardData, Vector3 cutWorldPosition, Vector3 scissorDirection)
    {
        Mesh sourceMesh = meshFilter.mesh;

        if (sourceMesh == null || sourceMesh.vertexCount < 3)
            return false;

        Vector3 rootWorldPosition = GetHairRootWorldPosition(meshFilter, hairCardData);
        Vector3 growthDirection = GetHairGrowthDirection(meshFilter, hairCardData, scissorDirection);
        float rootToCutDistance = Vector3.Dot(cutWorldPosition - rootWorldPosition, growthDirection);

        if (rootToCutDistance < 0f)
        {
            growthDirection = -growthDirection;
            rootToCutDistance = -rootToCutDistance;
        }

        if (rootToCutDistance <= minimumDistanceFromRoot)
            return false;

        if (Vector3.Dot(rootWorldPosition - cutWorldPosition, growthDirection) > 0f)
            growthDirection = -growthDirection;

        if (TryGetHaircutTarget(hairCardData, out float targetLength, out float targetTolerance))
        {
            float safeTolerance = Mathf.Max(0f, targetTolerance);
            float minimumAllowedLength = Mathf.Max(minimumDistanceFromRoot, targetLength - safeTolerance);

            if (hairCardData.CurrentLength <= targetLength + safeTolerance)
                return false;

            if (rootToCutDistance < minimumAllowedLength)
            {
                rootToCutDistance = minimumAllowedLength;
                cutWorldPosition = rootWorldPosition + growthDirection * rootToCutDistance;
            }
        }

        Vector3 planePointLocal = meshFilter.transform.InverseTransformPoint(cutWorldPosition);
        Vector3 planeNormalLocal = meshFilter.transform.InverseTransformDirection(growthDirection).normalized;

        if (!BuildClippedMesh(sourceMesh, planePointLocal, planeNormalLocal, false, out Mesh previewMesh))
            return false;

        GameObject preview = new GameObject(meshFilter.name + "_CutPreview");
        preview.transform.SetPositionAndRotation(meshFilter.transform.position + growthDirection * 0.001f, meshFilter.transform.rotation);
        preview.transform.localScale = meshFilter.transform.lossyScale;

        MeshFilter previewMeshFilter = preview.AddComponent<MeshFilter>();
        previewMeshFilter.mesh = previewMesh;

        MeshRenderer previewRenderer = preview.AddComponent<MeshRenderer>();
        previewRenderer.sharedMaterial = GetCutPreviewMaterial();
        previewRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        previewRenderer.receiveShadows = false;

        activeCutPreviews[meshFilter] = preview;
        return true;
    }

    private bool TryGetCylinderHairCutPoint(
        MeshFilter meshFilter,
        HairCardData hairCardData,
        Vector3 cylinderStart,
        Vector3 cylinderEnd,
        float cylinderRadius,
        out Vector3 cutPoint)
    {
        Vector3 root = GetHairRootWorldPosition(meshFilter, hairCardData);
        Vector3 growthDirection = GetHairGrowthDirection(meshFilter, hairCardData, cylinderEnd - cylinderStart);
        float hairLength = EstimateHairLength(meshFilter, hairCardData, root, growthDirection);
        Vector3 tip = root + growthDirection * hairLength;

        ClosestPointsBetweenSegments(root, tip, cylinderStart, cylinderEnd, out Vector3 pointOnHair, out Vector3 pointOnCylinder);

        if (Vector3.Distance(pointOnHair, pointOnCylinder) > cylinderRadius)
        {
            cutPoint = Vector3.zero;
            return false;
        }

        float distanceFromRoot = Vector3.Dot(pointOnHair - root, growthDirection);

        if (distanceFromRoot <= minimumDistanceFromRoot || distanceFromRoot >= hairLength)
        {
            cutPoint = Vector3.zero;
            return false;
        }

        cutPoint = pointOnHair;
        return true;
    }

    private bool TryGetBoxHairCutPoint(MeshFilter meshFilter, HairCardData hairCardData, BoxCollider cutBox, out Vector3 cutPoint)
    {
        Vector3 root = GetHairRootWorldPosition(meshFilter, hairCardData);
        Vector3 growthDirection = GetHairGrowthDirection(meshFilter, hairCardData, cutBox.transform.up);
        float hairLength = EstimateHairLength(meshFilter, hairCardData, root, growthDirection);
        Mesh mesh = meshFilter.sharedMesh;
        float distanceFromRoot;

        if (mesh == null || mesh.vertexCount == 0)
        {
            if (!TryGetBoxCenterCutDistance(cutBox, root, growthDirection, hairLength, out distanceFromRoot))
            {
                cutPoint = Vector3.zero;
                return false;
            }
        }
        else if (!TryGetMeshBoxCutDistance(meshFilter, mesh, cutBox, root, growthDirection, out distanceFromRoot))
        {
            if (!TryGetBoxCenterCutDistance(cutBox, root, growthDirection, hairLength, out distanceFromRoot))
            {
                cutPoint = Vector3.zero;
                return false;
            }
        }

        if (distanceFromRoot <= minimumDistanceFromRoot || distanceFromRoot >= hairLength)
        {
            cutPoint = Vector3.zero;
            return false;
        }

        cutPoint = root + growthDirection * distanceFromRoot;
        return true;
    }

    private bool TryGetBoxCenterCutDistance(
        BoxCollider cutBox,
        Vector3 root,
        Vector3 growthDirection,
        float hairLength,
        out float cutDistance)
    {
        Vector3 boxCenter = cutBox.transform.TransformPoint(cutBox.center);
        cutDistance = Vector3.Dot(boxCenter - root, growthDirection);

        return cutDistance > minimumDistanceFromRoot && cutDistance < hairLength;
    }

    private bool TryGetMeshBoxCutDistance(
        MeshFilter meshFilter,
        Mesh mesh,
        BoxCollider cutBox,
        Vector3 root,
        Vector3 growthDirection,
        out float cutDistance)
    {
        Vector3[] vertices = mesh.vertices;
        Vector3 halfSize = cutBox.size * 0.5f;
        float distanceSum = 0f;
        int candidateCount = 0;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 worldVertex = meshFilter.transform.TransformPoint(vertices[i]);
            Vector3 localVertex = cutBox.transform.InverseTransformPoint(worldVertex) - cutBox.center;

            if (!IsPointInsideLocalBox(localVertex, halfSize))
                continue;

            distanceSum += Vector3.Dot(worldVertex - root, growthDirection);
            candidateCount++;
        }

        for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
        {
            int[] triangles = mesh.GetTriangles(submesh);

            for (int i = 0; i < triangles.Length; i += 3)
            {
                AddBoxEdgeCutCandidate(meshFilter, vertices, triangles[i], triangles[i + 1], cutBox, halfSize, root, growthDirection, ref distanceSum, ref candidateCount);
                AddBoxEdgeCutCandidate(meshFilter, vertices, triangles[i + 1], triangles[i + 2], cutBox, halfSize, root, growthDirection, ref distanceSum, ref candidateCount);
                AddBoxEdgeCutCandidate(meshFilter, vertices, triangles[i + 2], triangles[i], cutBox, halfSize, root, growthDirection, ref distanceSum, ref candidateCount);
            }
        }

        if (candidateCount == 0)
        {
            cutDistance = 0f;
            return false;
        }

        cutDistance = distanceSum / candidateCount;
        return true;
    }

    private void AddBoxEdgeCutCandidate(
        MeshFilter meshFilter,
        Vector3[] vertices,
        int firstIndex,
        int secondIndex,
        BoxCollider cutBox,
        Vector3 halfSize,
        Vector3 root,
        Vector3 growthDirection,
        ref float distanceSum,
        ref int candidateCount)
    {
        Vector3 firstWorld = meshFilter.transform.TransformPoint(vertices[firstIndex]);
        Vector3 secondWorld = meshFilter.transform.TransformPoint(vertices[secondIndex]);
        Vector3 firstLocal = cutBox.transform.InverseTransformPoint(firstWorld) - cutBox.center;
        Vector3 secondLocal = cutBox.transform.InverseTransformPoint(secondWorld) - cutBox.center;
        Vector3 localDirection = secondLocal - firstLocal;

        if (!TryGetSegmentBoxIntersection(firstLocal, localDirection, halfSize, out float enterT, out float exitT))
            return;

        float t = Mathf.Clamp01((enterT + exitT) * 0.5f);
        Vector3 worldPoint = Vector3.Lerp(firstWorld, secondWorld, t);
        distanceSum += Vector3.Dot(worldPoint - root, growthDirection);
        candidateCount++;
    }

    private bool IsPointInsideLocalBox(Vector3 point, Vector3 halfSize)
    {
        const float tolerance = 0.0001f;

        return Mathf.Abs(point.x) <= halfSize.x + tolerance
            && Mathf.Abs(point.y) <= halfSize.y + tolerance
            && Mathf.Abs(point.z) <= halfSize.z + tolerance;
    }

    private bool TryGetSegmentBoxIntersection(Vector3 localStart, Vector3 localDirection, Vector3 halfSize, out float enterT, out float exitT)
    {
        enterT = 0f;
        exitT = 1f;

        if (!ClipSegmentToSlab(localStart.x, localDirection.x, halfSize.x, ref enterT, ref exitT))
            return false;

        if (!ClipSegmentToSlab(localStart.y, localDirection.y, halfSize.y, ref enterT, ref exitT))
            return false;

        if (!ClipSegmentToSlab(localStart.z, localDirection.z, halfSize.z, ref enterT, ref exitT))
            return false;

        return enterT <= exitT;
    }

    private bool ClipSegmentToSlab(float start, float direction, float halfSize, ref float enterT, ref float exitT)
    {
        if (Mathf.Abs(direction) < 0.000001f)
            return start >= -halfSize && start <= halfSize;

        float firstT = (-halfSize - start) / direction;
        float secondT = (halfSize - start) / direction;

        if (firstT > secondT)
        {
            float temp = firstT;
            firstT = secondT;
            secondT = temp;
        }

        enterT = Mathf.Max(enterT, firstT);
        exitT = Mathf.Min(exitT, secondT);

        return enterT <= exitT;
    }

    private Vector3 AbsVector3(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    private float EstimateHairLength(MeshFilter meshFilter, HairCardData hairCardData, Vector3 root, Vector3 growthDirection)
    {
        Mesh mesh = meshFilter.sharedMesh;
        float maxDistance = hairCardData != null && hairCardData.CurrentLength > minimumDistanceFromRoot
            ? hairCardData.CurrentLength
            : minimumDistanceFromRoot;

        if (mesh == null || mesh.vertexCount == 0)
            return maxDistance;

        Vector3[] vertices = mesh.vertices;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 worldVertex = meshFilter.transform.TransformPoint(vertices[i]);
            float distanceFromRoot = Vector3.Dot(worldVertex - root, growthDirection);
            maxDistance = Mathf.Max(maxDistance, distanceFromRoot);
        }

        return maxDistance;
    }

    private void ClosestPointsBetweenSegments(
        Vector3 firstStart,
        Vector3 firstEnd,
        Vector3 secondStart,
        Vector3 secondEnd,
        out Vector3 firstPoint,
        out Vector3 secondPoint)
    {
        Vector3 firstDirection = firstEnd - firstStart;
        Vector3 secondDirection = secondEnd - secondStart;
        Vector3 startOffset = firstStart - secondStart;
        float firstLengthSqr = Vector3.Dot(firstDirection, firstDirection);
        float secondLengthSqr = Vector3.Dot(secondDirection, secondDirection);
        float secondProjection = Vector3.Dot(secondDirection, startOffset);
        float firstT;
        float secondT;

        if (firstLengthSqr <= 0.000001f && secondLengthSqr <= 0.000001f)
        {
            firstPoint = firstStart;
            secondPoint = secondStart;
            return;
        }

        if (firstLengthSqr <= 0.000001f)
        {
            firstT = 0f;
            secondT = Mathf.Clamp01(secondProjection / secondLengthSqr);
        }
        else
        {
            float firstProjection = Vector3.Dot(firstDirection, startOffset);

            if (secondLengthSqr <= 0.000001f)
            {
                secondT = 0f;
                firstT = Mathf.Clamp01(-firstProjection / firstLengthSqr);
            }
            else
            {
                float directionDot = Vector3.Dot(firstDirection, secondDirection);
                float denominator = firstLengthSqr * secondLengthSqr - directionDot * directionDot;

                firstT = denominator != 0f
                    ? Mathf.Clamp01((directionDot * secondProjection - firstProjection * secondLengthSqr) / denominator)
                    : 0f;

                secondT = (directionDot * firstT + secondProjection) / secondLengthSqr;

                if (secondT < 0f)
                {
                    secondT = 0f;
                    firstT = Mathf.Clamp01(-firstProjection / firstLengthSqr);
                }
                else if (secondT > 1f)
                {
                    secondT = 1f;
                    firstT = Mathf.Clamp01((directionDot - firstProjection) / firstLengthSqr);
                }
            }
        }

        firstPoint = firstStart + firstDirection * firstT;
        secondPoint = secondStart + secondDirection * secondT;
    }

    private bool TryBrushSingleHairMesh(
        MeshFilter meshFilter,
        HairCardData hairCardData,
        Vector3 brushWorldPosition,
        float brushRadius,
        Vector3 worldMovementDelta,
        float strength)
    {
        Mesh mesh = meshFilter.mesh;

        if (mesh == null || mesh.vertexCount == 0)
            return false;

        Vector3[] vertices = mesh.vertices;
        Vector3 localBrushPosition = meshFilter.transform.InverseTransformPoint(brushWorldPosition);
        Vector3 localMovementDelta = meshFilter.transform.InverseTransformVector(worldMovementDelta * strength);
        float localBrushRadius = WorldRadiusToLocalRadius(meshFilter.transform, brushRadius);
        float sqrLocalBrushRadius = localBrushRadius * localBrushRadius;
        bool changed = false;

        Vector3 rootWorldPosition = GetHairRootWorldPosition(meshFilter, hairCardData);
        Vector3 growthDirection = GetHairGrowthDirection(meshFilter, hairCardData, worldMovementDelta);

        for (int i = 0; i < vertices.Length; i++)
        {
            float sqrDistance = (vertices[i] - localBrushPosition).sqrMagnitude;

            if (sqrDistance > sqrLocalBrushRadius)
                continue;

            if (lockHairRootsWhileBrushing)
            {
                Vector3 vertexWorldPosition = meshFilter.transform.TransformPoint(vertices[i]);
                float distanceFromRoot = Mathf.Abs(Vector3.Dot(vertexWorldPosition - rootWorldPosition, growthDirection));

                if (distanceFromRoot <= brushRootLockDistance)
                    continue;
            }

            float distance = Mathf.Sqrt(sqrDistance);
            float falloff = 1f - Mathf.Clamp01(distance / localBrushRadius);
            vertices[i] += localMovementDelta * falloff;
            changed = true;
        }

        if (!changed)
            return false;

        CaptureHairUndoBeforeChange(meshFilter, hairCardData);

        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        RefreshColliderAfterCut(meshFilter, mesh);
        MarkHairUndoChanged();

        return true;
    }

    private bool TryCombSingleHairMeshWithBox(
        MeshFilter meshFilter,
        HairCardData hairCardData,
        BoxCollider combBox,
        Vector3 worldMovementDelta,
        float strength)
    {
        Mesh mesh = meshFilter.mesh;

        if (mesh == null || mesh.vertexCount == 0 || combBox == null)
            return false;

        Vector3[] vertices = mesh.vertices;
        Vector3 localMovementDelta = meshFilter.transform.InverseTransformVector(worldMovementDelta * strength);
        Vector3 boxHalfSize = combBox.size * 0.5f;
        bool changed = false;

        Vector3 rootWorldPosition = GetHairRootWorldPosition(meshFilter, hairCardData);
        Vector3 growthDirection = GetHairGrowthDirection(meshFilter, hairCardData, worldMovementDelta);
        float hairLength = EstimateHairLength(meshFilter, hairCardData, rootWorldPosition, growthDirection);

        if (combWholeHairCardFromRoot)
        {
            bool touchedByComb = false;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 vertexWorldPosition = meshFilter.transform.TransformPoint(vertices[i]);
                Vector3 boxLocalVertex = combBox.transform.InverseTransformPoint(vertexWorldPosition) - combBox.center;

                if (IsPointInsideLocalBox(boxLocalVertex, boxHalfSize))
                {
                    touchedByComb = true;
                    break;
                }
            }

            if (!touchedByComb)
                return false;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 vertexWorldPosition = meshFilter.transform.TransformPoint(vertices[i]);
                float distanceFromRoot = Mathf.Max(0f, Vector3.Dot(vertexWorldPosition - rootWorldPosition, growthDirection));

                if (lockHairRootsWhileCombing && distanceFromRoot <= combRootLockDistance)
                    continue;

                float rootToTipRatio = hairLength > 0.0001f ? Mathf.Clamp01(distanceFromRoot / hairLength) : 1f;
                float falloff = rootToTipRatio * rootToTipRatio;
                vertices[i] += localMovementDelta * falloff * combTipInfluence;
                changed = true;
            }

            if (!changed)
                return false;

            CaptureHairUndoBeforeChange(meshFilter, hairCardData);

            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            RefreshColliderAfterCut(meshFilter, mesh);
            MarkHairUndoChanged();

            return true;
        }

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 vertexWorldPosition = meshFilter.transform.TransformPoint(vertices[i]);
            Vector3 boxLocalVertex = combBox.transform.InverseTransformPoint(vertexWorldPosition) - combBox.center;

            if (!IsPointInsideLocalBox(boxLocalVertex, boxHalfSize))
                continue;

            if (lockHairRootsWhileCombing)
            {
                float distanceFromRoot = Mathf.Abs(Vector3.Dot(vertexWorldPosition - rootWorldPosition, growthDirection));

                if (distanceFromRoot <= combRootLockDistance)
                    continue;
            }

            float xFalloff = boxHalfSize.x > 0.0001f ? Mathf.Abs(boxLocalVertex.x) / boxHalfSize.x : 0f;
            float yFalloff = boxHalfSize.y > 0.0001f ? Mathf.Abs(boxLocalVertex.y) / boxHalfSize.y : 0f;
            float zFalloff = boxHalfSize.z > 0.0001f ? Mathf.Abs(boxLocalVertex.z) / boxHalfSize.z : 0f;
            float falloff = 1f - Mathf.Clamp01(Mathf.Max(xFalloff, yFalloff, zFalloff));

            vertices[i] += localMovementDelta * falloff;
            changed = true;
        }

        if (!changed)
            return false;

        CaptureHairUndoBeforeChange(meshFilter, hairCardData);

        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        RefreshColliderAfterCut(meshFilter, mesh);
        MarkHairUndoChanged();

        return true;
    }

    private Vector3 GetHairRootWorldPosition(MeshFilter meshFilter, HairCardData hairCardData)
    {
        if (hairCardData != null)
            return hairCardData.GetRootPosition();

        return meshFilter.transform.position;
    }

    private Vector3 GetHairGrowthDirection(MeshFilter meshFilter, HairCardData hairCardData, Vector3 scissorDirection)
    {
        if (hairCardData != null)
            return hairCardData.GetGrowthDirection();

        if (scissorDirection.sqrMagnitude > 0.0001f)
            return scissorDirection.normalized;

        return -meshFilter.transform.up;
    }

    private void UpdateHairCardLength(HairCardData hairCardData, float newLength)
    {
        if (hairCardData == null)
            return;

        hairCardData.RefreshRootPosition();
        hairCardData.CurrentLength = Mathf.Max(minimumDistanceFromRoot, newLength);
    }

    private void RefreshColliderAfterCut(MeshFilter meshFilter, Mesh keptMesh)
    {
        MeshCollider meshCollider = meshFilter.GetComponent<MeshCollider>();

        if (meshCollider == null)
            return;

        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = keptMesh;
    }

    private float WorldRadiusToLocalRadius(Transform targetTransform, float worldRadius)
    {
        Vector3 scale = targetTransform.lossyScale;
        float largestScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

        if (largestScale <= 0.0001f)
            return worldRadius;

        return worldRadius / largestScale;
    }

    private void SpawnFallenHair(MeshFilter sourceMeshFilter, Mesh cutMesh, Vector3 cutWorldPosition)
    {
        GameObject fallenHair = useFallenHairPooling ? GetFallenHairFromPool() : CreateFallenHairPoolObject();
        fallenHair.layer = sourceMeshFilter.gameObject.layer;
        fallenHair.name = sourceMeshFilter.name + "_CutSection";
        fallenHair.transform.SetPositionAndRotation(cutWorldPosition, sourceMeshFilter.transform.rotation);
        fallenHair.transform.localScale = sourceMeshFilter.transform.lossyScale;
        fallenHair.transform.SetParent(fallenHairParent, true);
        fallenHair.SetActive(true);
        RegisterSpawnedHairUndo(fallenHair);

        MeshFilter fallenMeshFilter = fallenHair.GetComponentInChildren<MeshFilter>();

        if (fallenMeshFilter == null)
            fallenMeshFilter = fallenHair.AddComponent<MeshFilter>();

        FallenHairPoolItem poolItem = GetFallenHairPoolItem(fallenHair, fallenMeshFilter);
        poolItem.SetRuntimeMesh(cutMesh);

        MeshRenderer sourceRenderer = sourceMeshFilter.GetComponent<MeshRenderer>();
        MeshRenderer fallenRenderer = fallenMeshFilter.GetComponent<MeshRenderer>();

        if (fallenRenderer == null)
            fallenRenderer = fallenMeshFilter.gameObject.AddComponent<MeshRenderer>();

        if (sourceRenderer != null && fallenRenderer != null)
        {
            fallenRenderer.enabled = true;
            fallenRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
            fallenRenderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
            fallenRenderer.receiveShadows = sourceRenderer.receiveShadows;
            PrepareRendererForFallenHairFade(fallenRenderer);
        }
        else if (fallenRenderer != null)
        {
            fallenRenderer.enabled = false;
        }

        BoxCollider boxCollider = fallenMeshFilter.GetComponent<BoxCollider>();
        if (addBoxColliderToFallenHair)
        {
            if (boxCollider == null)
                boxCollider = fallenMeshFilter.gameObject.AddComponent<BoxCollider>();

            boxCollider.enabled = true;
            boxCollider.center = cutMesh.bounds.center;
            boxCollider.size = new Vector3(
                Mathf.Max(cutMesh.bounds.size.x, 0.01f),
                Mathf.Max(cutMesh.bounds.size.y, 0.01f),
                Mathf.Max(cutMesh.bounds.size.z, 0.01f)
            );
        }
        else if (boxCollider != null)
        {
            boxCollider.enabled = false;
        }

        Rigidbody rigidbody = fallenHair.GetComponent<Rigidbody>();
        if (rigidbody == null)
            rigidbody = fallenHair.AddComponent<Rigidbody>();

        rigidbody.isKinematic = false;
        rigidbody.useGravity = true;
        rigidbody.mass = fallenHairMass;
        rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rigidbody.linearVelocity = Vector3.zero;
        rigidbody.angularVelocity = Vector3.zero;
        ApplyFallenHairDamping(rigidbody);
        rigidbody.AddForce(GetFallenHairImpulseDirection() * fallenHairDownForce, ForceMode.Impulse);
        rigidbody.AddTorque(Random.insideUnitSphere * fallenHairTorque, ForceMode.Impulse);

        if (logFallenHairSpawn)
            Debug.Log($"[CuttingManager] Spawned fallen hair: {fallenHair.name}, vertices: {cutMesh.vertexCount}, layer: {LayerMask.LayerToName(fallenHair.layer)}");

        if (fallenHairLifetime > 0f)
        {
            if (useFallenHairPooling)
                StartCoroutine(DeactivateFallenHairAfterDelay(fallenHair, fallenHairLifetime, fallenHairFadeDuration));
            else
                Destroy(fallenHair, fallenHairLifetime);
        }
    }

    private void SpawnFallenHairPrefabOnly(HairCardSegment segment)
    {
        // Active debris path for segmented hair: spawn the user's prefab at the cut segment location.
        if (segment == null)
            return;

        Vector3 spawnPosition = GetSegmentDebrisSpawnPosition(segment);
        GameObject fallenHair = useFallenHairPooling ? GetFallenHairFromPool() : CreateFallenHairPoolObject();
        fallenHair.layer = segment.gameObject.layer;
        fallenHair.name = segment.name + "_Debris";
        fallenHair.transform.SetPositionAndRotation(spawnPosition, segment.transform.rotation);

        // Deliberately NOT setting localScale here - the HairDebris prefab has its own correct
        // authored default scale (confirmed: 50,50,50). Previously this was overwritten with
        // segment.transform.lossyScale (meaningful only within the hair card's own deeply-nested, tiny
        // parent hierarchy), which made debris appear far too small - only visible when zoomed in closely.

        fallenHair.transform.SetParent(fallenHairParent, true);
        fallenHair.SetActive(true);
        RegisterSpawnedHairUndo(fallenHair);

        MeshFilter meshFilter = fallenHair.GetComponentInChildren<MeshFilter>(true);
        FallenHairPoolItem poolItem = GetFallenHairPoolItem(fallenHair, meshFilter);
        poolItem.RestoreOriginalMesh();

        Renderer[] renderers = fallenHair.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].enabled = true;
            PrepareRendererForFallenHairFade(renderers[i]);
        }

        Collider[] colliders = fallenHair.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = true;

        Rigidbody rigidbody = fallenHair.GetComponent<Rigidbody>();
        if (rigidbody == null)
            rigidbody = fallenHair.AddComponent<Rigidbody>();

        rigidbody.isKinematic = false;
        rigidbody.useGravity = true;
        rigidbody.mass = fallenHairMass;
        rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rigidbody.linearVelocity = Vector3.zero;
        rigidbody.angularVelocity = Vector3.zero;
        ApplyFallenHairDamping(rigidbody);
        rigidbody.AddForce(GetFallenHairImpulseDirection() * fallenHairDownForce, ForceMode.Impulse);
        rigidbody.AddTorque(Random.insideUnitSphere * fallenHairTorque, ForceMode.Impulse);

        if (fallenHairLifetime > 0f)
        {
            if (useFallenHairPooling)
                StartCoroutine(DeactivateFallenHairAfterDelay(fallenHair, fallenHairLifetime, fallenHairFadeDuration));
            else
                Destroy(fallenHair, fallenHairLifetime);
        }
    }

    private Vector3 GetSegmentDebrisSpawnPosition(HairCardSegment segment)
    {
        Renderer renderer = segment.GetComponentInChildren<Renderer>(true);

        if (renderer != null)

            return renderer.bounds.center;

        Collider collider = segment.GetComponentInChildren<Collider>(true);

        if (collider != null)
            return collider.bounds.center;

        return segment.transform.position;
    }

    private GameObject GetFallenHairFromPool()
    {
        while (fallenHairPool.Count > 0)
        {
            GameObject pooledObject = fallenHairPool.Dequeue();

            if (pooledObject != null)
                return pooledObject;
        }

        return CreateFallenHairPoolObject();
    }

    private GameObject CreateFallenHairPoolObject()
    {
        GameObject fallenHair = fallenHairPoolPrefab != null
            ? Instantiate(fallenHairPoolPrefab)
            : new GameObject("Pooled_FallenHair");

        fallenHair.name = "Pooled_FallenHair";

        if (fallenHair.GetComponentInChildren<MeshFilter>() == null)
            fallenHair.AddComponent<MeshFilter>();

        if (fallenHair.GetComponentInChildren<MeshRenderer>() == null)
            fallenHair.AddComponent<MeshRenderer>();

        if (fallenHair.GetComponentInChildren<Collider>(true) == null)
            fallenHair.GetComponentInChildren<MeshFilter>(true).gameObject.AddComponent<BoxCollider>();

        FallenHairPoolItem poolItem = GetFallenHairPoolItem(fallenHair, fallenHair.GetComponentInChildren<MeshFilter>(true));
        poolItem.CaptureOriginalRenderers(fallenHair.GetComponentsInChildren<Renderer>(true));

        Rigidbody rigidbody = fallenHair.GetComponent<Rigidbody>();
        if (rigidbody == null)
            rigidbody = fallenHair.AddComponent<Rigidbody>();

        rigidbody.mass = fallenHairMass;
        rigidbody.useGravity = true;
        rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        ApplyFallenHairDamping(rigidbody);

        spawnedFallenHairPoolObjects.Add(fallenHair);
        return fallenHair;
    }

    private Vector3 GetFallenHairImpulseDirection()
    {
        Vector3 direction = Vector3.down;

        if (fallenHairRandomScatter > 0f)
            direction += Random.insideUnitSphere * fallenHairRandomScatter;

        if (direction.sqrMagnitude <= 0.0001f)
            return Vector3.down;

        return direction.normalized;
    }

    private void ApplyFallenHairDamping(Rigidbody rigidbody)
    {
        if (rigidbody == null)
            return;

        rigidbody.linearDamping = Mathf.Max(0f, fallenHairLinearDamping);
        rigidbody.angularDamping = Mathf.Max(0f, fallenHairAngularDamping);
    }

    private IEnumerator DeactivateFallenHairAfterDelay(GameObject fallenHair, float delay, float fadeDuration)
    {
        float safeFadeDuration = fadeFallenHairBeforePooling ? Mathf.Clamp(fadeDuration, 0f, delay) : 0f;
        float visibleDuration = Mathf.Max(0f, delay - safeFadeDuration);

        if (visibleDuration > 0f)
            yield return new WaitForSeconds(visibleDuration);

        if (fallenHair == null)
            yield break;

        if (safeFadeDuration > 0f)
            yield return FadeFallenHair(fallenHair, safeFadeDuration);

        ReturnFallenHairToPool(fallenHair);
    }

    private IEnumerator FadeFallenHair(GameObject fallenHair, float duration)
    {
        Renderer[] renderers = fallenHair.GetComponentsInChildren<Renderer>(true);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float alpha = 1f - Mathf.Clamp01(elapsed / duration);

            for (int i = 0; i < renderers.Length; i++)
                SetRendererAlpha(renderers[i], alpha);

            yield return null;
        }
    }

    private void ReturnFallenHairToPool(GameObject fallenHair)
    {
        if (fallenHair == null)
            return;

        FallenHairPoolItem poolItem = fallenHair.GetComponent<FallenHairPoolItem>();
        if (poolItem != null)
            poolItem.RestoreOriginalMesh();

        Rigidbody rigidbody = fallenHair.GetComponent<Rigidbody>();
        if (rigidbody != null)
        {
            rigidbody.linearVelocity = Vector3.zero;
            rigidbody.angularVelocity = Vector3.zero;
            rigidbody.isKinematic = true;
        }

        Collider[] colliders = fallenHair.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = false;

        Renderer[] renderers = fallenHair.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (poolItem == null)
                SetRendererAlpha(renderers[i], 1f);

            renderers[i].enabled = false;
        }

        if (poolItem != null)
            poolItem.RestoreOriginalMaterials();

        fallenHair.transform.SetParent(fallenHairParent, false);
        fallenHair.SetActive(false);

        if (!useFallenHairPooling)
            return;

        if (maxFallenHairPoolSize > 0 && fallenHairPool.Count >= maxFallenHairPoolSize)
        {
            spawnedFallenHairPoolObjects.Remove(fallenHair);
            Destroy(fallenHair);
            return;
        }

        fallenHairPool.Enqueue(fallenHair);
    }

    private FallenHairPoolItem GetFallenHairPoolItem(GameObject fallenHair, MeshFilter meshFilter)
    {
        FallenHairPoolItem poolItem = fallenHair.GetComponent<FallenHairPoolItem>();

        if (poolItem == null)
            poolItem = fallenHair.AddComponent<FallenHairPoolItem>();

        poolItem.CaptureOriginalMesh(meshFilter);
        return poolItem;
    }

    private void PrepareRendererForFallenHairFade(Renderer renderer)
    {
        if (renderer == null || !fadeFallenHairBeforePooling)
            return;

        Material[] materials = renderer.materials;

        for (int i = 0; i < materials.Length; i++)
            ConfigureMaterialForFade(materials[i]);
    }

    private void SetRendererAlpha(Renderer renderer, float alpha)
    {
        if (renderer == null)
            return;

        Material[] materials = renderer.materials;

        for (int i = 0; i < materials.Length; i++)
        {
            Material material = materials[i];

            if (material == null)
                continue;

            if (material.HasProperty("_BaseColor"))
            {
                Color color = material.GetColor("_BaseColor");
                color.a = alpha;
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                Color color = material.GetColor("_Color");
                color.a = alpha;
                material.SetColor("_Color", color);
            }
        }
    }

    private void ConfigureMaterialForFade(Material material)
    {
        // Do not assume every hair material has _Color. Shader Graph materials often only expose _BaseColor.
        if (material == null)
            return;

        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);

        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0f);

        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);

        material.renderQueue = 3000;
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
    }

    private Material GetCutPreviewMaterial()
    {
        if (cutPreviewMaterial != null)
            return cutPreviewMaterial;

        if (generatedCutPreviewMaterial != null)
            return generatedCutPreviewMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        if (shader == null)
            shader = Shader.Find("Standard");

        generatedCutPreviewMaterial = new Material(shader);
        generatedCutPreviewMaterial.name = "Generated Cut Preview Yellow";
        generatedCutPreviewMaterial.color = new Color(1f, 0.9f, 0.05f, 0.65f);

        return generatedCutPreviewMaterial;
    }

    private bool BuildClippedMesh(Mesh sourceMesh, Vector3 planePoint, Vector3 planeNormal, bool keepRootSide, out Mesh clippedMesh)
    {
        Vector3[] sourceVertices = sourceMesh.vertices;
        Vector3[] sourceNormals = sourceMesh.normals;
        Vector2[] sourceUVs = sourceMesh.uv;

        List<Vector3> newVertices = new List<Vector3>();
        List<Vector3> newNormals = new List<Vector3>();
        List<Vector2> newUVs = new List<Vector2>();
        List<int>[] submeshTriangles = new List<int>[sourceMesh.subMeshCount];

        for (int submesh = 0; submesh < sourceMesh.subMeshCount; submesh++)
        {
            submeshTriangles[submesh] = new List<int>();
            int[] triangles = sourceMesh.GetTriangles(submesh);

            for (int i = 0; i < triangles.Length; i += 3)
            {
                MeshCutVertex a = CreateCutVertex(sourceVertices, sourceNormals, sourceUVs, triangles[i]);
                MeshCutVertex b = CreateCutVertex(sourceVertices, sourceNormals, sourceUVs, triangles[i + 1]);
                MeshCutVertex c = CreateCutVertex(sourceVertices, sourceNormals, sourceUVs, triangles[i + 2]);

                AddClippedTriangle(a, b, c, planePoint, planeNormal, keepRootSide, newVertices, newNormals, newUVs, submeshTriangles[submesh]);
            }
        }

        if (newVertices.Count < 3)
        {
            clippedMesh = null;
            return false;
        }

        clippedMesh = new Mesh();
        clippedMesh.name = sourceMesh.name + (keepRootSide ? "_Kept" : "_Cut");

        if (newVertices.Count > 65535)
            clippedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        clippedMesh.SetVertices(newVertices);
        clippedMesh.SetNormals(newNormals);
        clippedMesh.SetUVs(0, newUVs);
        clippedMesh.subMeshCount = submeshTriangles.Length;

        for (int i = 0; i < submeshTriangles.Length; i++)
            clippedMesh.SetTriangles(submeshTriangles[i], i);

        clippedMesh.RecalculateBounds();

        if (sourceNormals == null || sourceNormals.Length == 0)
            clippedMesh.RecalculateNormals();

        return true;
    }

    private MeshCutVertex CreateCutVertex(Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int index)
    {
        return new MeshCutVertex
        {
            Position = vertices[index],
            Normal = normals != null && normals.Length > index ? normals[index] : Vector3.up,
            UV = uvs != null && uvs.Length > index ? uvs[index] : Vector2.zero
        };
    }

    private void AddClippedTriangle(
        MeshCutVertex a,
        MeshCutVertex b,
        MeshCutVertex c,
        Vector3 planePoint,
        Vector3 planeNormal,
        bool keepRootSide,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> triangles)
    {
        List<MeshCutVertex> polygon = new List<MeshCutVertex> { a, b, c };
        List<MeshCutVertex> clippedPolygon = ClipPolygonAgainstPlane(polygon, planePoint, planeNormal, keepRootSide);

        if (clippedPolygon.Count < 3)
            return;

        int startIndex = vertices.Count;

        for (int i = 0; i < clippedPolygon.Count; i++)
        {
            vertices.Add(clippedPolygon[i].Position);
            normals.Add(clippedPolygon[i].Normal);
            uvs.Add(clippedPolygon[i].UV);
        }

        for (int i = 1; i < clippedPolygon.Count - 1; i++)
        {
            triangles.Add(startIndex);
            triangles.Add(startIndex + i);
            triangles.Add(startIndex + i + 1);
        }
    }

    private List<MeshCutVertex> ClipPolygonAgainstPlane(List<MeshCutVertex> polygon, Vector3 planePoint, Vector3 planeNormal, bool keepRootSide)
    {
        List<MeshCutVertex> result = new List<MeshCutVertex>();

        for (int i = 0; i < polygon.Count; i++)
        {
            MeshCutVertex current = polygon[i];
            MeshCutVertex previous = polygon[(i + polygon.Count - 1) % polygon.Count];

            float currentDistance = Vector3.Dot(current.Position - planePoint, planeNormal);
            float previousDistance = Vector3.Dot(previous.Position - planePoint, planeNormal);
            bool currentInside = IsInsideCutPlane(currentDistance, keepRootSide);
            bool previousInside = IsInsideCutPlane(previousDistance, keepRootSide);

            if (currentInside != previousInside)
                result.Add(MeshCutVertex.Lerp(previous, current, previousDistance / (previousDistance - currentDistance)));

            if (currentInside)
                result.Add(current);
        }

        return result;
    }

    private bool IsInsideCutPlane(float distance, bool keepRootSide)
    {
        return keepRootSide ? distance <= 0.0001f : distance >= -0.0001f;
    }

    private struct MeshCutVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 UV;

        public static MeshCutVertex Lerp(MeshCutVertex a, MeshCutVertex b, float t)
        {
            t = Mathf.Clamp01(t);

            return new MeshCutVertex
            {
                Position = Vector3.Lerp(a.Position, b.Position, t),
                Normal = Vector3.Lerp(a.Normal, b.Normal, t).normalized,
                UV = Vector2.Lerp(a.UV, b.UV, t)
            };
        }
    }

    private struct VisualCombedHairCard
    {
        public HairCardData HairCardData;
        public float LastCombedTime;
        public Vector3 RestGrowthDirection;
        // Captured once, on this card's very first comb touch - see TryVisualCombHairCard/RegisterVisual-
        // CombedHairCard. ApplyVisualCombGravity hard-snaps to these once fully settled, guaranteeing an
        // exact, drift-free return to the original pose instead of relying purely on incremental rotation.
        public Vector3 RestLocalPosition;
        public Quaternion RestLocalRotation;
    }

    /// <summary>
    /// Restores the hair mesh structure back to its uncut, factory state.
    /// </summary>
    public void resetMesh()
    {
        if (workingMesh == null || baseVerticesCache.Count == 0) return;

        currentVertices = new List<Vector3>(baseVerticesCache);
        currentTriangles = new List<int>(baseTrianglesCache);
        currentUVs = new List<Vector2>(baseUVsCache);

        updateMesh();
        Debug.Log("[CuttingManager] Mesh structural elements reset to initial state.");
    }

    /// <summary>
    /// Finalizes modifications and locks state values.
    /// </summary>
    public void finishCuttingSession()
    {
        // Triggers final bounding updates and structural evaluations
        if (workingMesh != null)
        {
            workingMesh.Optimize();
        }
        Debug.Log("[CuttingManager] Cutting session wrapped up. Submitting geometry alterations.");
    }

    /// <summary>
    /// Executes proximity checks using a traditional ray tracing line.
    /// </summary>
    public void castRayDetection()
    {
        // Future code implementation window for structural alignment tracing checks
    }

    /// <summary>
    /// Spawns particle physical clippings cascading down onto the floor plane.
    /// </summary>
    public void spawnCutHairParticles(Vector3 position)
    {
        if (hairParticleSystem != null)
        {
            hairParticleSystem.transform.position = position;
            hairParticleSystem.Emit(15); // Blast out 15 hair mesh fragments
        }
    }

    /// <summary>
    /// Reverts the very last geometric slice state change.
    /// </summary>
    public void undo()
    {
        UndoLastHairChange();
    }

    public void UndoLastHairChange()
    {
        if (hairUndoManager == null)
            hairUndoManager = GetComponent<HairUndoManager>();

        if (hairUndoManager == null)
        {
            Debug.LogWarning("[CuttingManager] Undo requested, but no HairUndoManager was found.");
            return;
        }

        if (hairUndoManager.UndoLastChange())
            visuallyCombedHairCards.Clear();
    }


    // Legacy mesh-only comb/brush helpers. Kept as fallback; active tools use the hair-card methods above.

    public void ExecuteComb(Vector3 combWorldPos, float radius, Vector3 movementDelta, float strength)
    {
        if (workingMesh == null || currentVertices.Count == 0) return;

        Vector3 localCombPos = hairMeshFilter.transform.InverseTransformPoint(combWorldPos);
        Vector3 localMovementDir = hairMeshFilter.transform.InverseTransformDirection(movementDelta);
        bool meshChanged = false;

        for (int i = 0; i < currentVertices.Count; i++)
        {
            float distance = Vector3.Distance(currentVertices[i], localCombPos);
            if (distance <= radius)
            {
                float falloff = 1f - (distance / radius);
                currentVertices[i] += localMovementDir * strength * falloff;
                meshChanged = true;
            }
        }

        if (meshChanged) updateMesh();
    }

    public void ExecuteBrushSmooth(Vector3 brushWorldPos, float radius, float strength)
    {
        if (workingMesh == null || currentVertices.Count == 0 || currentTriangles.Count == 0) return;

        Vector3 localBrushPos = hairMeshFilter.transform.InverseTransformPoint(brushWorldPos);
        Vector3[] smoothedPositions = new Vector3[currentVertices.Count];
        int[] neighborCount = new int[currentVertices.Count];

        for (int i = 0; i < currentVertices.Count; i++) smoothedPositions[i] = currentVertices[i];

        for (int i = 0; i < currentTriangles.Count; i += 3)
        {
            int v1 = currentTriangles[i]; int v2 = currentTriangles[i + 1]; int v3 = currentTriangles[i + 2];
            smoothedPositions[v1] += currentVertices[v2] + currentVertices[v3]; neighborCount[v1] += 2;
            smoothedPositions[v2] += currentVertices[v1] + currentVertices[v3]; neighborCount[v2] += 2;
            smoothedPositions[v3] += currentVertices[v1] + currentVertices[v2]; neighborCount[v3] += 2;
        }

        bool meshChanged = false;
        for (int i = 0; i < currentVertices.Count; i++)
        {
            float distance = Vector3.Distance(currentVertices[i], localBrushPos);
            if (distance <= radius && neighborCount[i] > 0)
            {
                Vector3 averageNeighborPos = smoothedPositions[i] / neighborCount[i];
                float falloff = 1f - (distance / radius);
                currentVertices[i] = Vector3.Lerp(currentVertices[i], averageNeighborPos, strength * falloff * Time.deltaTime * 10f);
                meshChanged = true;
            }
        }

        if (meshChanged) updateMesh();
    }


    // --- Private Mesh Refresh Methods ---

    /// <summary>
    /// Handles flushing current memory lists back onto the active GPU vertex buffers.
    /// </summary>
    private void updateMesh()
    {
        if (workingMesh == null) return;

        workingMesh.Clear(); // Empty trailing buffers
        workingMesh.SetVertices(currentVertices);
        workingMesh.SetTriangles(currentTriangles, 0);
        workingMesh.SetUVs(0, currentUVs);
        
        workingMesh.RecalculateNormals();
        workingMesh.RecalculateBounds();
    }

    private void InitializeMeshData()
    {
        workingMesh = hairMeshFilter.mesh;
        workingMesh.GetVertices(currentVertices);
        workingMesh.GetTriangles(currentTriangles, 0);
        workingMesh.GetUVs(0, currentUVs);
    }
}

class FallenHairPoolItem : MonoBehaviour
{
    private MeshFilter meshFilter;
    private Mesh originalMesh;
    private Mesh runtimeMesh;
    private Renderer[] originalRenderers;
    private Material[][] originalMaterials;

    public void CaptureOriginalMesh(MeshFilter targetMeshFilter)
    {
        if (targetMeshFilter == null)
            return;

        if (meshFilter == targetMeshFilter)
            return;

        meshFilter = targetMeshFilter;
        originalMesh = meshFilter.sharedMesh;
    }

    public void SetRuntimeMesh(Mesh mesh)
    {
        RestoreOriginalMesh();

        if (meshFilter == null)
            return;

        runtimeMesh = mesh;
        meshFilter.mesh = runtimeMesh;
    }

    public void RestoreOriginalMesh()
    {
        if (runtimeMesh != null)
        {
            Destroy(runtimeMesh);
            runtimeMesh = null;
        }

        if (meshFilter != null)
            meshFilter.sharedMesh = originalMesh;
    }

    public void CaptureOriginalRenderers(Renderer[] renderers)
    {
        if (renderers == null || originalRenderers != null)
            return;

        originalRenderers = renderers;
        originalMaterials = new Material[renderers.Length][];

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
                continue;

            originalMaterials[i] = renderers[i].sharedMaterials;
        }
    }

    public void RestoreOriginalMaterials()
    {
        if (originalRenderers == null || originalMaterials == null)
            return;

        for (int i = 0; i < originalRenderers.Length; i++)
        {
            if (originalRenderers[i] == null || i >= originalMaterials.Length)
                continue;

            originalRenderers[i].sharedMaterials = originalMaterials[i];
        }
    }
}
/*
using UnityEngine;
using System.Collections.Generic;

public class CuttingManager : MonoBehaviour
{
    [Header("Dependencies")]
    //[SerializeField] private SectioningManager sectioningManager;
    [SerializeField] private MeshFilter hairMeshFilter;

    private Mesh workingMesh;
    private List<Vector3> currentVertices = new List<Vector3>();
    private List<int> currentTriangles = new List<int>();
    private List<Vector2> currentUVs = new List<Vector2>();

    void Start()
    {
        if (hairMeshFilter != null)
        {
            // Cache the editable mesh data lists
            workingMesh = hairMeshFilter.mesh;
            workingMesh.GetVertices(currentVertices);
            workingMesh.GetTriangles(currentTriangles, 0);
            workingMesh.GetUVs(0, currentUVs);
        }
    }

    /// <summary>
    /// Called by your Scissor or Clipper tool when a cut action is triggered.
    /// </summary>
    /// <param name="cutPosition">The world-space position of the tool tip.</param>
    /// <param name="cutRadius">The physical size of the cut zone.</param>
    public void ExecuteCut(Vector3 cutPosition, float cutRadius)
    {
        if (hairMeshFilter == null)
        {
            Debug.LogError("[CuttingManager] Cannot cut! HairMeshFilter dependency is missing.");
            return;
        }

        // ====================================================================
        // CRITICAL FIX: If arrays are empty, extract them directly from the mesh filter!
        // ====================================================================
        if (currentVertices.Count == 0)
        {
            workingMesh = hairMeshFilter.mesh;
            workingMesh.GetVertices(currentVertices);
            workingMesh.GetTriangles(currentTriangles, 0);
            workingMesh.GetUVs(0, currentUVs);
            
            Debug.Log($"[Manager Fix] Successfully recovered mesh data. Populated {currentVertices.Count} vertices.");
        }

        // Convert world-space tool position into the hair mesh's local space coordinates
        Vector3 localCutPos = hairMeshFilter.transform.InverseTransformPoint(cutPosition);
        
        Debug.Log($"[Manager Debug] Starting cut check. Total vertices in list: {currentVertices.Count}. Radius: {cutRadius}");

        HashSet<int> verticesToRemove = new HashSet<int>();

        // 1. Identify which vertices are close enough to be cut away
        for (int i = 0; i < currentVertices.Count; i++)
        {
            if (Vector3.Distance(currentVertices[i], localCutPos) <= cutRadius)
            {
                verticesToRemove.Add(i);
            }
        }

        Debug.Log($"[Manager Debug] Vertices matching distance criteria: {verticesToRemove.Count}");

        if (verticesToRemove.Count == 0) return; // Nothing was hit

        // 2. Rebuild the triangles array, dropping any triangle that connects to a cut vertex
        List<int> newTriangles = new List<int>();
        for (int i = 0; i < currentTriangles.Count; i += 3)
        {
            int v1 = currentTriangles[i];
            int v2 = currentTriangles[i + 1];
            int v3 = currentTriangles[i + 2];

            if (!verticesToRemove.Contains(v1) && !verticesToRemove.Contains(v2) && !verticesToRemove.Contains(v3))
            {
                newTriangles.Add(v1);
                newTriangles.Add(v2);
                newTriangles.Add(v3);
            }
        }

        // 3. Update the working arrays and upload them back to the GPU
        currentTriangles = newTriangles;
        
        workingMesh.Clear(); // Clear old buffers
        workingMesh.SetVertices(currentVertices);
        workingMesh.SetTriangles(currentTriangles, 0);
        workingMesh.SetUVs(0, currentUVs);
        
        workingMesh.RecalculateNormals();
        workingMesh.RecalculateBounds();

        Debug.Log($"[Manager Debug] Mesh buffer flushed to GPU. New triangle index count: {currentTriangles.Count}");
    }

    // ==========================================
    // ADD THIS FOR THE COMB TOOL
    // ==========================================
    public void ExecuteComb(Vector3 combWorldPos, float radius, Vector3 movementDelta, float strength)
    {
        if (workingMesh == null || currentVertices.Count == 0) return;

        // Convert world positions and directions to the hair mesh's local space
        Vector3 localCombPos = hairMeshFilter.transform.InverseTransformPoint(combWorldPos);
        Vector3 localMovementDir = hairMeshFilter.transform.InverseTransformDirection(movementDelta);

        bool meshChanged = false;

        for (int i = 0; i < currentVertices.Count; i++)
        {
            float distance = Vector3.Distance(currentVertices[i], localCombPos);
            if (distance <= radius)
            {
                // Linear falloff: vertices closer to the center move more
                float falloff = 1f - (distance / radius);
                
                // Move the vertex in the direction of the hand swipe
                currentVertices[i] += localMovementDir * strength * falloff;
                meshChanged = true;
            }
        }

        if (meshChanged)
        {
            workingMesh.SetVertices(currentVertices);
            workingMesh.RecalculateNormals();
            workingMesh.RecalculateBounds();
        }
    }

    // ==========================================
    // ADD THIS FOR THE BRUSH TOOL
    // ==========================================
    public void ExecuteBrushSmooth(Vector3 brushWorldPos, float radius, float strength)
    {
        if (workingMesh == null || currentVertices.Count == 0 || currentTriangles.Count == 0) return;

        Vector3 localBrushPos = hairMeshFilter.transform.InverseTransformPoint(brushWorldPos);
        
        Vector3[] smoothedPositions = new Vector3[currentVertices.Count];
        int[] neighborCount = new int[currentVertices.Count];

        // Initialize tracking arrays
        for (int i = 0; i < currentVertices.Count; i++)
        {
            smoothedPositions[i] = currentVertices[i];
        }

        // Map vertex connections using triangle topology indices
        for (int i = 0; i < currentTriangles.Count; i += 3)
        {
            int v1 = currentTriangles[i];
            int v2 = currentTriangles[i + 1];
            int v3 = currentTriangles[i + 2];

            smoothedPositions[v1] += currentVertices[v2] + currentVertices[v3];
            neighborCount[v1] += 2;

            smoothedPositions[v2] += currentVertices[v1] + currentVertices[v3];
            neighborCount[v2] += 2;

            smoothedPositions[v3] += currentVertices[v1] + currentVertices[v2];
            neighborCount[v3] += 2;
        }

        bool meshChanged = false;

        for (int i = 0; i < currentVertices.Count; i++)
        {
            float distance = Vector3.Distance(currentVertices[i], localBrushPos);
            if (distance <= radius && neighborCount[i] > 0)
            {
                Vector3 averageNeighborPos = smoothedPositions[i] / neighborCount[i];
                float falloff = 1f - (distance / radius);

                // Blend current position toward its surrounding neighbor average
                currentVertices[i] = Vector3.Lerp(currentVertices[i], averageNeighborPos, strength * falloff * Time.deltaTime * 10f);
                meshChanged = true;
            }
        }

        if (meshChanged)
        {
            workingMesh.SetVertices(currentVertices);
            workingMesh.RecalculateNormals();
            workingMesh.RecalculateBounds();
        }
    }
}
*/
