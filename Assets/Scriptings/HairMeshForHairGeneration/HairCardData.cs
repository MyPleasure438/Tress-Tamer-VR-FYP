using UnityEngine;

// HANDOFF NOTE:
// Per-card metadata used by CuttingManager, HaircutManager, EvaluationSystem, feedback coloring, and undo.
// The root is normally this transform/RootGizmo, and GrowthDirection should point from root toward the visible tip.
public class HairCardData : MonoBehaviour
{
    [Header("Hair Properties")]
    public int HairID;

    public HairSectionType Section;

    public bool IsCuttable = true;

    public bool IsSelected = false;

    [Header("Hair Shape")]
    public float OriginalLength = 0.20f;
    public float CurrentLength = 0.20f;

    public float Thickness = 1.0f;

    public Vector3 GrowthDirection = Vector3.down;

    [Header("Haircut Target")]
    public bool UseTargetLength = true;
    public float TargetLength = 0.025f;
    public float TargetTolerance = 0.0025f;

    [Header("Growth Direction Auto Setup")]
    [Tooltip("Automatically calculates GrowthDirection from RootGizmo to the farthest area of the mesh.")]
    // Prefer auto setup for hundreds of cards; manual growth directions are slow and easy to mirror incorrectly.
    public bool AutoCalculateGrowthDirection = true;

    [Tooltip("How many of the farthest mesh vertices are averaged. Higher is smoother for curved hair cards.")]
    [Min(1)]
    public int FarthestVertexSampleCount = 8;

    [Tooltip("Updates OriginalLength and CurrentLength from the measured mesh length when calculating direction.")]
    public bool SetLengthFromMesh = true;

    [Tooltip("Shows the calculated root-to-tip direction in the Scene view when this hair card is selected.")]
    public bool DrawGrowthDirectionGizmo = true;

    [Header("Root Gizmo")]
    [Tooltip("Assign the transform placed at the root of this hair card.")]
    // In this project the hair card transform gizmo is treated as the root unless this override is assigned.
    public Transform RootGizmo;

    [Tooltip("When enabled, RootPosition is always taken from RootGizmo.")]
    public bool UseRootGizmo = true;

    [Tooltip("Useful if the hair/root gizmo moves during play.")]
    public bool UpdateRootFromGizmoEveryFrame = false;

    [HideInInspector]
    public Vector3 RootPosition;

    [HideInInspector]
    public Vector3 OriginalScale;

    private void Awake()
    {
        OriginalScale = transform.localScale;
        RefreshRootPosition();

        if (AutoCalculateGrowthDirection)
            AutoCalculateGrowthDirectionFromMesh();
    }

    private void OnValidate()
    {
        RefreshRootPosition();
    }

    private void LateUpdate()
    {
        if (UpdateRootFromGizmoEveryFrame)
            RefreshRootPosition();
    }

    public void RefreshRootPosition()
    {
        if (UseRootGizmo && RootGizmo != null)
        {
            RootPosition = RootGizmo.position;
            return;
        }

        RootPosition = transform.position;
    }

    public Vector3 GetRootPosition()
    {
        if (UseRootGizmo && RootGizmo != null)
            return RootGizmo.position;

        return RootPosition;
    }

    public Vector3 GetGrowthDirection()
    {
        if (GrowthDirection.sqrMagnitude > 0.0001f)
            return GrowthDirection.normalized;

        return -transform.up;
    }

    [ContextMenu("Auto Calculate Growth Direction From Mesh")]
    public void AutoCalculateGrowthDirectionFromMesh()
    {
        MeshFilter meshFilter = GetComponent<MeshFilter>();

        if (meshFilter == null || meshFilter.sharedMesh == null)
            return;

        Vector3[] vertices = meshFilter.sharedMesh.vertices;

        if (vertices == null || vertices.Length == 0)
            return;

        Vector3 root = GetRootPosition();
        int sampleCount = Mathf.Clamp(FarthestVertexSampleCount, 1, vertices.Length);
        Vector3[] bestDirections = new Vector3[sampleCount];
        float[] bestDistances = new float[sampleCount];

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 worldVertex = transform.TransformPoint(vertices[i]);
            Vector3 rootToVertex = worldVertex - root;
            float distance = rootToVertex.magnitude;

            if (distance <= 0.0001f)
                continue;

            InsertFarthestVertex(rootToVertex / distance, distance, bestDirections, bestDistances);
        }

        Vector3 averagedDirection = Vector3.zero;
        float measuredLength = 0f;
        int validCount = 0;

        for (int i = 0; i < bestDirections.Length; i++)
        {
            if (bestDistances[i] <= 0f)
                continue;

            averagedDirection += bestDirections[i];
            measuredLength += bestDistances[i];
            validCount++;
        }

        if (validCount == 0 || averagedDirection.sqrMagnitude <= 0.0001f)
            return;

        GrowthDirection = averagedDirection.normalized;

        if (SetLengthFromMesh)
        {
            OriginalLength = measuredLength / validCount;
            CurrentLength = Mathf.Min(CurrentLength > 0f ? CurrentLength : OriginalLength, OriginalLength);
        }
    }

    private void InsertFarthestVertex(Vector3 direction, float distance, Vector3[] bestDirections, float[] bestDistances)
    {
        for (int i = 0; i < bestDistances.Length; i++)
        {
            if (distance <= bestDistances[i])
                continue;

            for (int j = bestDistances.Length - 1; j > i; j--)
            {
                bestDistances[j] = bestDistances[j - 1];
                bestDirections[j] = bestDirections[j - 1];
            }

            bestDistances[i] = distance;
            bestDirections[i] = direction;
            return;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!DrawGrowthDirectionGizmo)
            return;

        Vector3 root = Application.isPlaying ? GetRootPosition() : (UseRootGizmo && RootGizmo != null ? RootGizmo.position : transform.position);
        Vector3 direction = GrowthDirection.sqrMagnitude > 0.0001f ? GrowthDirection.normalized : -transform.up;
        float length = OriginalLength > 0f ? OriginalLength : 0.2f;

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(root, root + direction * length);
        Gizmos.DrawWireSphere(root, 0.01f);
        Gizmos.DrawWireSphere(root + direction * length, 0.008f);
    }

    public void Cut(float amount)
    {
        if (!IsCuttable)
            return;

        CurrentLength = Mathf.Max(0f, CurrentLength - amount);
    }

    public void SetTargetLength(float targetLength, float tolerance)
    {
        UseTargetLength = true;
        TargetLength = Mathf.Max(0f, targetLength);
        TargetTolerance = Mathf.Max(0f, tolerance);
    }

    public bool HasUsableTargetLength()
    {
        return UseTargetLength && TargetLength > 0f;
    }

    public bool IsAtOrBelowTarget(float extraTolerance = 0f)
    {
        if (!HasUsableTargetLength())
            return false;

        return CurrentLength <= TargetLength + TargetTolerance + Mathf.Max(0f, extraTolerance);
    }

    public float LengthRatio
    {
        get
        {
            if (OriginalLength <= 0f)
                return 0f;

            return CurrentLength / OriginalLength;
        }
    }
}
