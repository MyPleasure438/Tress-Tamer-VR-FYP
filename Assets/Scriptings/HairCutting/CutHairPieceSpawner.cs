using UnityEngine;

public class CutHairPieceSpawner : MonoBehaviour
{
    [Header("Physics")]
    [SerializeField] private float pieceMass = 0.015f;
    [SerializeField] private float fallForce = 0.08f;
    [SerializeField] private float randomTorque = 0.025f;
    [SerializeField] private float lifetime = 6f;

    [Header("Shape")]
    [Tooltip("Extra scale applied to the falling piece. Use this if the spawned piece looks too small or too large.")]
    [SerializeField] private Vector3 pieceScaleMultiplier = Vector3.one;

    [Tooltip("Adds a simple box collider so flat hair cards can fall without needing a readable convex mesh collider.")]
    [SerializeField] private bool addBoxCollider = true;

    public GameObject SpawnCutPiece(HairCardData sourceHair, Vector3 cutWorldPosition, float removedLength)
    {
        if (sourceHair == null || removedLength <= 0f)
            return null;

        MeshFilter sourceMeshFilter = sourceHair.GetComponent<MeshFilter>();
        MeshRenderer sourceRenderer = sourceHair.GetComponent<MeshRenderer>();

        if (sourceMeshFilter == null || sourceMeshFilter.sharedMesh == null || sourceRenderer == null)
        {
            Debug.LogWarning($"[CutHairPieceSpawner] {sourceHair.name} needs a MeshFilter and MeshRenderer to spawn a cut piece.");
            return null;
        }

        GameObject piece = new GameObject($"{sourceHair.name}_CutPiece");
        piece.transform.SetPositionAndRotation(cutWorldPosition, sourceHair.transform.rotation);
        piece.transform.localScale = Vector3.Scale(GetPieceScale(sourceHair, removedLength), pieceScaleMultiplier);

        MeshFilter pieceMeshFilter = piece.AddComponent<MeshFilter>();
        pieceMeshFilter.sharedMesh = sourceMeshFilter.sharedMesh;

        MeshRenderer pieceRenderer = piece.AddComponent<MeshRenderer>();
        pieceRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
        pieceRenderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
        pieceRenderer.receiveShadows = sourceRenderer.receiveShadows;

        if (addBoxCollider)
            AddPieceBoxCollider(piece, sourceMeshFilter.sharedMesh);

        Rigidbody rigidbody = piece.AddComponent<Rigidbody>();
        rigidbody.mass = pieceMass;
        rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rigidbody.AddForce((Vector3.down + Random.insideUnitSphere * 0.35f) * fallForce, ForceMode.Impulse);
        rigidbody.AddTorque(Random.insideUnitSphere * randomTorque, ForceMode.Impulse);

        Destroy(piece, lifetime);
        return piece;
    }

    private static Vector3 GetPieceScale(HairCardData sourceHair, float removedLength)
    {
        Vector3 scale = sourceHair.OriginalScale;
        float lengthRatio = sourceHair.OriginalLength <= 0f ? 1f : removedLength / sourceHair.OriginalLength;
        scale.y = sourceHair.OriginalScale.y * lengthRatio;
        return scale;
    }

    private static void AddPieceBoxCollider(GameObject piece, Mesh sourceMesh)
    {
        BoxCollider collider = piece.AddComponent<BoxCollider>();
        Bounds bounds = sourceMesh.bounds;

        collider.center = bounds.center;
        collider.size = new Vector3(
            Mathf.Max(bounds.size.x, 0.01f),
            Mathf.Max(bounds.size.y, 0.01f),
            Mathf.Max(bounds.size.z, 0.01f)
        );
    }
}
