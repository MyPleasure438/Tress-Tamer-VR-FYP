using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// HANDOFF NOTE:
// Central undo history for cutting, brushing, combing, and spawned debris.
// CuttingManager/tools capture state before changing hair; this restores segment visibility, mesh data, and debris state.
public class HairUndoManager : MonoBehaviour
{
    [Header("Undo Settings")]
    [SerializeField] private int maxUndoSteps = 20;
    // Desktop test shortcut. For VR, wire UndoLastChangeFromUIButton() to a world-space UI button.
    [SerializeField] private bool allowKeyboardUndo = true;
    [SerializeField] private Key undoKey = Key.Z;
    [SerializeField] private bool logUndoActions = true;

    private readonly Stack<HairUndoAction> undoStack = new Stack<HairUndoAction>();
    private HairUndoAction currentAction;
    private int actionDepth;

    private void Update()
    {
        if (!allowKeyboardUndo || Keyboard.current == null || (int)undoKey <= 0)
            return;

        if (Keyboard.current[undoKey].wasPressedThisFrame)
            UndoLastChange();
    }

    public void BeginAction(string actionName)
    {
        actionDepth++;

        if (currentAction != null)
            return;

        currentAction = new HairUndoAction(string.IsNullOrWhiteSpace(actionName) ? "Hair Change" : actionName);
    }

    public void CaptureBeforeChange(SegmentedHairCard segmentedHairCard)
    {
        if (segmentedHairCard == null)
            return;

        EnsureAction("Hair Change");
        currentAction.CaptureSegmentedHairCard(segmentedHairCard);
    }

    public void CaptureBeforeChange(MeshFilter meshFilter, HairCardData hairCardData)
    {
        if (meshFilter == null && hairCardData == null)
            return;

        EnsureAction("Hair Change");
        currentAction.CaptureHairCard(meshFilter, hairCardData);
    }

    // Generic escape hatch for state this manager doesn't know the shape of (e.g. stubble-area swaps
    // in StubbleAreaSwapController) - restoreCallback should set everything it touches back to exactly
    // how it was BEFORE the change about to happen, so it's safe to run in any order relative to other
    // captures in the same action.
    public void CaptureBeforeChange(System.Action restoreCallback)
    {
        if (restoreCallback == null)
            return;

        EnsureAction("Hair Change");
        currentAction.CaptureCustom(restoreCallback);
    }

    public void MarkCurrentActionChanged()
    {
        if (currentAction != null)
            currentAction.HasChanged = true;
    }

    public void RegisterSpawnedObject(GameObject spawnedObject)
    {
        if (spawnedObject == null)
            return;

        EnsureAction("Hair Change");
        currentAction.RegisterSpawnedObject(spawnedObject);
    }

    public void EndAction()
    {
        if (actionDepth > 0)
            actionDepth--;

        if (actionDepth > 0 || currentAction == null)
            return;

        if (currentAction.HasChanged && currentAction.HasUndoContent)
        {
            undoStack.Push(currentAction);
            TrimUndoStack();

            if (logUndoActions)
                Debug.Log($"[HairUndoManager] Saved undo step: {currentAction.ActionName}");
        }

        currentAction = null;
    }

    public void CancelCurrentAction()
    {
        currentAction = null;
        actionDepth = 0;
    }

    public void UndoLastChangeFromUIButton()
    {
        UndoLastChange();
    }

    [ContextMenu("Undo Last Hair Change")]
    public bool UndoLastChange()
    {
        if (currentAction != null)
            EndAction();

        if (undoStack.Count == 0)
        {
            if (logUndoActions)
                Debug.Log("[HairUndoManager] Nothing to undo.");

            return false;
        }

        HairUndoAction action = undoStack.Pop();
        action.Restore();

        if (logUndoActions)
            Debug.Log($"[HairUndoManager] Undid: {action.ActionName}");

        return true;
    }

    public void ClearHistory()
    {
        undoStack.Clear();
        currentAction = null;
        actionDepth = 0;
    }

    private void EnsureAction(string actionName)
    {
        if (currentAction != null)
            return;

        currentAction = new HairUndoAction(string.IsNullOrWhiteSpace(actionName) ? "Hair Change" : actionName);
    }

    private void TrimUndoStack()
    {
        if (maxUndoSteps <= 0)
            return;

        if (undoStack.Count <= maxUndoSteps)
            return;

        HairUndoAction[] actions = undoStack.ToArray();
        undoStack.Clear();

        for (int i = Mathf.Min(actions.Length, maxUndoSteps) - 1; i >= 0; i--)
            undoStack.Push(actions[i]);
    }

    private sealed class HairUndoAction
    {
        public readonly string ActionName;
        public bool HasChanged;

        private readonly List<HairUndoState> states = new List<HairUndoState>();
        private readonly List<GameObject> spawnedObjects = new List<GameObject>();
        private readonly List<System.Action> customRestoreCallbacks = new List<System.Action>();
        private readonly HashSet<int> capturedKeys = new HashSet<int>();
        private readonly HashSet<int> spawnedObjectKeys = new HashSet<int>();

        public int StateCount => states.Count;
        public bool HasUndoContent => states.Count > 0 || spawnedObjects.Count > 0 || customRestoreCallbacks.Count > 0;

        public HairUndoAction(string actionName)
        {
            ActionName = actionName;
        }

        public void CaptureSegmentedHairCard(SegmentedHairCard segmentedHairCard)
        {
            int key = segmentedHairCard.GetInstanceID();

            if (!capturedKeys.Add(key))
                return;

            states.Add(HairUndoState.FromSegmentedHairCard(segmentedHairCard));
        }

        public void CaptureHairCard(MeshFilter meshFilter, HairCardData hairCardData)
        {
            int key = meshFilter != null ? meshFilter.GetInstanceID() : hairCardData.GetInstanceID();

            if (!capturedKeys.Add(key))
                return;

            states.Add(HairUndoState.FromHairCard(meshFilter, hairCardData));
        }

        public void RegisterSpawnedObject(GameObject spawnedObject)
        {
            int key = spawnedObject.GetInstanceID();

            if (!spawnedObjectKeys.Add(key))
                return;

            spawnedObjects.Add(spawnedObject);
        }

        // No dedupe key here on purpose - unlike the typed captures above, each custom capture is a
        // one-off closure over its own specific "restore to exactly this" snapshot, not a repeatable
        // handle to the same object, so there is nothing meaningful to de-duplicate by.
        public void CaptureCustom(System.Action restoreCallback)
        {
            customRestoreCallbacks.Add(restoreCallback);
        }

        public void Restore()
        {
            for (int i = states.Count - 1; i >= 0; i--)
                states[i].Restore();

            for (int i = spawnedObjects.Count - 1; i >= 0; i--)
                HideSpawnedObject(spawnedObjects[i]);

            // Each custom callback restores its own fully-independent captured state (not a delta), so
            // execution order between them does not matter - reverse order kept only for consistency
            // with the other two lists above.
            for (int i = customRestoreCallbacks.Count - 1; i >= 0; i--)
                customRestoreCallbacks[i]?.Invoke();
        }

        private void HideSpawnedObject(GameObject spawnedObject)
        {
            if (spawnedObject == null)
                return;

            FallenHairPoolItem poolItem = spawnedObject.GetComponent<FallenHairPoolItem>();
            if (poolItem != null)
            {
                poolItem.RestoreOriginalMesh();
                poolItem.RestoreOriginalMaterials();
            }

            Rigidbody rigidbody = spawnedObject.GetComponent<Rigidbody>();
            if (rigidbody != null)
            {
                rigidbody.linearVelocity = Vector3.zero;
                rigidbody.angularVelocity = Vector3.zero;
                rigidbody.isKinematic = true;
            }

            Collider[] colliders = spawnedObject.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                colliders[i].enabled = false;

            Renderer[] renderers = spawnedObject.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].enabled = false;

            spawnedObject.SetActive(false);
        }
    }

    private sealed class HairUndoState
    {
        private Transform targetTransform;
        private Vector3 localPosition;
        private Quaternion localRotation;
        private Vector3 localScale;

        private HairCardData hairCardData;
        private float currentLength;
        private Vector3 growthDirection;
        private Vector3 rootPosition;

        private SegmentedHairCard segmentedHairCard;
        private int currentCutStartIndex;
        private bool[] segmentActiveStates;
        private bool[][] segmentColliderStates;

        private MeshUndoState meshState;

        public static HairUndoState FromSegmentedHairCard(SegmentedHairCard segmentedHairCard)
        {
            HairUndoState state = new HairUndoState();
            state.segmentedHairCard = segmentedHairCard;
            state.CaptureTransform(segmentedHairCard.transform);
            state.CaptureHairData(segmentedHairCard.HairCardData);
            state.CaptureSegments(segmentedHairCard);
            return state;
        }

        public static HairUndoState FromHairCard(MeshFilter meshFilter, HairCardData hairCardData)
        {
            HairUndoState state = new HairUndoState();
            Transform target = hairCardData != null ? hairCardData.transform : meshFilter.transform;
            state.CaptureTransform(target);
            state.CaptureHairData(hairCardData);
            state.meshState = MeshUndoState.Capture(meshFilter);
            return state;
        }

        public void Restore()
        {
            RestoreTransform();

            if (meshState != null)
                meshState.Restore();

            if (segmentedHairCard != null)
                segmentedHairCard.RestoreSegmentRuntimeState(currentCutStartIndex, segmentActiveStates, segmentColliderStates);

            RestoreHairData();
        }

        private void CaptureTransform(Transform target)
        {
            targetTransform = target;

            if (targetTransform == null)
                return;

            localPosition = targetTransform.localPosition;
            localRotation = targetTransform.localRotation;
            localScale = targetTransform.localScale;
        }

        private void RestoreTransform()
        {
            if (targetTransform == null)
                return;

            targetTransform.localPosition = localPosition;
            targetTransform.localRotation = localRotation;
            targetTransform.localScale = localScale;
        }

        private void CaptureHairData(HairCardData data)
        {
            hairCardData = data;

            if (hairCardData == null)
                return;

            currentLength = hairCardData.CurrentLength;
            growthDirection = hairCardData.GrowthDirection;
            rootPosition = hairCardData.RootPosition;
        }

        private void RestoreHairData()
        {
            if (hairCardData == null)
                return;

            hairCardData.CurrentLength = currentLength;
            hairCardData.GrowthDirection = growthDirection;
            hairCardData.RootPosition = rootPosition;
            hairCardData.RefreshRootPosition();
        }

        private void CaptureSegments(SegmentedHairCard segmentedCard)
        {
            HairCardSegment[] segments = segmentedCard.Segments;
            currentCutStartIndex = segmentedCard.CurrentCutStartIndex;

            if (segments == null)
                return;

            segmentActiveStates = new bool[segments.Length];
            segmentColliderStates = new bool[segments.Length][];

            for (int i = 0; i < segments.Length; i++)
            {
                HairCardSegment segment = segments[i];

                if (segment == null)
                    continue;

                segmentActiveStates[i] = segment.gameObject.activeSelf;
                Collider[] colliders = segment.GetComponents<Collider>();
                segmentColliderStates[i] = new bool[colliders.Length];

                for (int colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++)
                    segmentColliderStates[i][colliderIndex] = colliders[colliderIndex].enabled;
            }
        }
    }

    private sealed class MeshUndoState
    {
        private MeshFilter meshFilter;
        private string meshName;
        private Vector3[] vertices;
        private Vector3[] normals;
        private Vector4[] tangents;
        private Vector2[] uv;
        private Vector2[] uv2;
        private Color[] colors;
        private int[][] triangles;
        private Bounds bounds;

        public static MeshUndoState Capture(MeshFilter targetMeshFilter)
        {
            if (targetMeshFilter == null || targetMeshFilter.sharedMesh == null)
                return null;

            Mesh mesh = targetMeshFilter.sharedMesh;
            MeshUndoState state = new MeshUndoState
            {
                meshFilter = targetMeshFilter,
                meshName = mesh.name,
                vertices = mesh.vertices,
                normals = mesh.normals,
                tangents = mesh.tangents,
                uv = mesh.uv,
                uv2 = mesh.uv2,
                colors = mesh.colors,
                bounds = mesh.bounds,
                triangles = new int[Mathf.Max(1, mesh.subMeshCount)][]
            };

            for (int i = 0; i < state.triangles.Length && i < mesh.subMeshCount; i++)
                state.triangles[i] = mesh.GetTriangles(i);

            return state;
        }

        public void Restore()
        {
            if (meshFilter == null || vertices == null || triangles == null)
                return;

            Mesh restoredMesh = new Mesh
            {
                name = string.IsNullOrEmpty(meshName) ? "UndoRestoredHairMesh" : meshName + "_UndoRestored"
            };

            restoredMesh.vertices = vertices;
            restoredMesh.subMeshCount = triangles.Length;

            for (int i = 0; i < triangles.Length; i++)
                restoredMesh.SetTriangles(triangles[i] ?? System.Array.Empty<int>(), i);

            if (uv != null && uv.Length == vertices.Length)
                restoredMesh.uv = uv;

            if (uv2 != null && uv2.Length == vertices.Length)
                restoredMesh.uv2 = uv2;

            if (colors != null && colors.Length == vertices.Length)
                restoredMesh.colors = colors;

            if (normals != null && normals.Length == vertices.Length)
                restoredMesh.normals = normals;
            else
                restoredMesh.RecalculateNormals();

            if (tangents != null && tangents.Length == vertices.Length)
                restoredMesh.tangents = tangents;

            restoredMesh.bounds = bounds;
            restoredMesh.RecalculateBounds();

            meshFilter.mesh = restoredMesh;
            RefreshMeshCollider(meshFilter, restoredMesh);
        }

        private static void RefreshMeshCollider(MeshFilter meshFilter, Mesh mesh)
        {
            MeshCollider meshCollider = meshFilter.GetComponent<MeshCollider>();

            if (meshCollider == null)
                return;

            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mesh;
        }
    }
}
