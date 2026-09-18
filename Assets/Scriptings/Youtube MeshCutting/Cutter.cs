using System.Collections.Generic;
using UnityEngine;

public class Cutter : MonoBehaviour
{
    public static bool currentlyCutting;
    public static Mesh originalMesh;

    public static void Cut(GameObject _originalGameObject, Vector3 _contactPoint, Vector3 _direction, Material _cutMaterial = null, bool fill = true, bool _addRigidbody = false)
    {
        if (currentlyCutting) return;

        currentlyCutting = true;
        
        Plane plane = new Plane(_originalGameObject.transform.InverseTransformDirection(-_direction),
                                _originalGameObject.transform.InverseTransformPoint(_contactPoint));
        
        originalMesh = _originalGameObject.GetComponent<MeshFilter>().mesh;
        List<Vector3> addedVertices = new List<Vector3>();

        GeneratedMesh leftMesh = new GeneratedMesh();
        GeneratedMesh rightMesh = new GeneratedMesh();
        
        int[] submeshIndices;
        int triangleIndexA, triangleIndexB, triangleIndexC;
        
        for (int i = 0; i < originalMesh.subMeshCount; i++)
        {
            submeshIndices = originalMesh.GetTriangles(i);

            for (int j = 0; j < submeshIndices.Length; j += 3)
            {
                triangleIndexA = submeshIndices[j];
                triangleIndexB = submeshIndices[j + 1];
                triangleIndexC = submeshIndices[j + 2];

                MeshTriangle currentTriangle = GetTriangle(triangleIndexA, triangleIndexB, triangleIndexC, i);

                bool triangleALeftSide = plane.GetSide(originalMesh.vertices[triangleIndexA]);
                bool triangleBLeftSide = plane.GetSide(originalMesh.vertices[triangleIndexB]);
                bool triangleCLeftSide = plane.GetSide(originalMesh.vertices[triangleIndexC]);

                if (triangleALeftSide && triangleBLeftSide && triangleCLeftSide)
                {
                    leftMesh.AddTriangle(currentTriangle);
                }
                else if (!triangleALeftSide && !triangleBLeftSide && !triangleCLeftSide)
                {
                    rightMesh.AddTriangle(currentTriangle);
                }
                else
                {
                    CutTriangle(plane, currentTriangle, triangleALeftSide, triangleBLeftSide, triangleCLeftSide, leftMesh, rightMesh, addedVertices);
                }
            }
        }

        // ==========================================
        // MESH CREATION & DYNAMIC COLLIDER REBUILDING
        // ==========================================

        // 1. Create the Left Piece GameObject
        GameObject leftPiece = new GameObject(_originalGameObject.name + "_Left", typeof(MeshFilter), typeof(MeshRenderer));
        leftPiece.transform.position = _originalGameObject.transform.position;
        leftPiece.transform.rotation = _originalGameObject.transform.rotation;
        leftPiece.transform.localScale = _originalGameObject.transform.localScale;

        Mesh newLeftMesh = new Mesh();
        newLeftMesh.name = "LeftMesh";
        newLeftMesh.vertices = leftMesh.Vertices.ToArray();
        newLeftMesh.normals = leftMesh.Normals.ToArray();
        newLeftMesh.uv = leftMesh.UVs.ToArray();
        
        newLeftMesh.subMeshCount = leftMesh.SubmeshIndices.Count;
        for (int i = 0; i < leftMesh.SubmeshIndices.Count; i++)
        {
            newLeftMesh.SetTriangles(leftMesh.SubmeshIndices[i].ToArray(), i);
        }
        leftPiece.GetComponent<MeshFilter>().mesh = newLeftMesh;
        leftPiece.GetComponent<MeshRenderer>().materials = _originalGameObject.GetComponent<MeshRenderer>().materials;


        // 2. Create the Right Piece GameObject
        GameObject rightPiece = new GameObject(_originalGameObject.name + "_Right", typeof(MeshFilter), typeof(MeshRenderer));
        rightPiece.transform.position = _originalGameObject.transform.position;
        rightPiece.transform.rotation = _originalGameObject.transform.rotation;
        rightPiece.transform.localScale = _originalGameObject.transform.localScale;

        Mesh newRightMesh = new Mesh();
        newRightMesh.name = "RightMesh";
        newRightMesh.vertices = rightMesh.Vertices.ToArray();
        newRightMesh.normals = rightMesh.Normals.ToArray();
        newRightMesh.uv = rightMesh.UVs.ToArray();

        newRightMesh.subMeshCount = rightMesh.SubmeshIndices.Count;
        for (int i = 0; i < rightMesh.SubmeshIndices.Count; i++)
        {
            newRightMesh.SetTriangles(rightMesh.SubmeshIndices[i].ToArray(), i);
        }
        rightPiece.GetComponent<MeshFilter>().mesh = newRightMesh;
        rightPiece.GetComponent<MeshRenderer>().materials = _originalGameObject.GetComponent<MeshRenderer>().materials;


        // 3. Rebuild Custom Mesh Colliders Matching the New Cut Shapes
        MeshCollider leftCollider = leftPiece.AddComponent<MeshCollider>();
        leftCollider.sharedMesh = newLeftMesh; 
        leftCollider.convex = true;

        MeshCollider rightCollider = rightPiece.AddComponent<MeshCollider>();
        rightCollider.sharedMesh = newRightMesh; 
        rightCollider.convex = true;

        if (_addRigidbody)
        {
            leftPiece.AddComponent<Rigidbody>();
            rightPiece.AddComponent<Rigidbody>();
        }

        // 4. Cleanup & Lock Reset
        Destroy(_originalGameObject);
        currentlyCutting = false; 
    }

    private static void CutTriangle(Plane _plane, MeshTriangle _triangle, bool _triangleALeftSide, bool _triangleBLeftSide, bool _triangleCLeftSide, GeneratedMesh _leftSide, GeneratedMesh _rightSide, List<Vector3> _addedVertices)
    {
        List<bool> leftSide = new List<bool> { _triangleALeftSide, _triangleBLeftSide, _triangleCLeftSide };

        MeshTriangle leftMeshTriangle = new MeshTriangle(new Vector3[2], new Vector3[2], new Vector2[2], _triangle.SubmeshIndex);
        MeshTriangle rightMeshTriangle = new MeshTriangle(new Vector3[2], new Vector3[2], new Vector2[2], _triangle.SubmeshIndex);

        bool left = false;
        bool right = false;

        for (int i = 0; i < 3; i++)
        {
            if (leftSide[i])
            {
                if (!left)
                {
                    left = true;
                    leftMeshTriangle.Vertices[0] = _triangle.Vertices[i];
                    leftMeshTriangle.Vertices[1] = leftMeshTriangle.Vertices[0];
                    leftMeshTriangle.UVs[0] = _triangle.UVs[i];
                    leftMeshTriangle.UVs[1] = leftMeshTriangle.UVs[0];
                    leftMeshTriangle.Normals[0] = _triangle.Normals[i];
                    leftMeshTriangle.Normals[1] = leftMeshTriangle.Normals[0];
                }
                else
                {
                    leftMeshTriangle.Vertices[1] = _triangle.Vertices[i];
                    leftMeshTriangle.Normals[1] = _triangle.Normals[i];
                    leftMeshTriangle.UVs[1] = _triangle.UVs[i];
                }
            }
            else
            {
                if (!right)
                {
                    right = true;
                    rightMeshTriangle.Vertices[0] = _triangle.Vertices[i];
                    rightMeshTriangle.Vertices[1] = rightMeshTriangle.Vertices[0];
                    rightMeshTriangle.UVs[0] = _triangle.UVs[i];
                    rightMeshTriangle.UVs[1] = rightMeshTriangle.UVs[0];
                    rightMeshTriangle.Normals[0] = _triangle.Normals[i];
                    rightMeshTriangle.Normals[1] = rightMeshTriangle.Normals[0];
                }
                else
                {
                    rightMeshTriangle.Vertices[1] = _triangle.Vertices[i];
                    rightMeshTriangle.Normals[1] = _triangle.Normals[i];
                    rightMeshTriangle.UVs[1] = _triangle.UVs[i];
                }
            }
        }

        float normalizedDistance;
        float distance;
        
        // Raycast 1
        _plane.Raycast(new Ray(leftMeshTriangle.Vertices[0], (rightMeshTriangle.Vertices[0] - leftMeshTriangle.Vertices[0]).normalized), out distance);
        normalizedDistance = distance / (rightMeshTriangle.Vertices[0] - leftMeshTriangle.Vertices[0]).magnitude;
        
        Vector3 vertLeft = Vector3.Lerp(leftMeshTriangle.Vertices[0], rightMeshTriangle.Vertices[0], normalizedDistance);
        _addedVertices.Add(vertLeft);

        Vector3 normalLeft = Vector3.Lerp(leftMeshTriangle.Normals[0], rightMeshTriangle.Normals[0], normalizedDistance);
        Vector2 uvLeft = Vector2.Lerp(leftMeshTriangle.UVs[0], rightMeshTriangle.UVs[0], normalizedDistance);

        // Raycast 2
        _plane.Raycast(new Ray(leftMeshTriangle.Vertices[1], (rightMeshTriangle.Vertices[1] - leftMeshTriangle.Vertices[1]).normalized), out distance);
        normalizedDistance = distance / (rightMeshTriangle.Vertices[1] - leftMeshTriangle.Vertices[1]).magnitude;
        
        Vector3 vertRight = Vector3.Lerp(leftMeshTriangle.Vertices[1], rightMeshTriangle.Vertices[1], normalizedDistance);
        _addedVertices.Add(vertRight);

        Vector3 normalRight = Vector3.Lerp(leftMeshTriangle.Normals[1], rightMeshTriangle.Normals[1], normalizedDistance);
        Vector2 uvRight = Vector2.Lerp(leftMeshTriangle.UVs[1], rightMeshTriangle.UVs[1], normalizedDistance);

        // Sub-Triangle 1 Construction
        MeshTriangle currentTriangle;
        Vector3[] updatedVertices = { leftMeshTriangle.Vertices[0], vertLeft, vertRight }; 
        Vector3[] updatedNormals = { leftMeshTriangle.Normals[0], normalLeft, normalRight };
        Vector2[] updatedUVs = { leftMeshTriangle.UVs[0], uvLeft, uvRight };

        currentTriangle = new MeshTriangle(updatedVertices, updatedNormals, updatedUVs, _triangle.SubmeshIndex);

        if (updatedVertices[0] != updatedVertices[1] && updatedVertices[0] != updatedVertices[2])
        {
            if (Vector3.Dot(Vector3.Cross(updatedVertices[1] - updatedVertices[0], updatedVertices[2] - updatedVertices[0]), updatedNormals[0]) < 0)
            {
                FlipTriangle(currentTriangle);
            }
            _leftSide.AddTriangle(currentTriangle);
        }

        // Sub-Triangle 2 Construction
        updatedVertices = new Vector3[] { leftMeshTriangle.Vertices[0], leftMeshTriangle.Vertices[1], vertRight };
        updatedNormals = new Vector3[] { leftMeshTriangle.Normals[0], leftMeshTriangle.Normals[1], normalRight };
        updatedUVs = new Vector2[] { leftMeshTriangle.UVs[0], leftMeshTriangle.UVs[1], uvRight };

        currentTriangle = new MeshTriangle(updatedVertices, updatedNormals, updatedUVs, _triangle.SubmeshIndex);

        if (updatedVertices[0] != updatedVertices[1] && updatedVertices[0] != updatedVertices[2])
        {
            if (Vector3.Dot(Vector3.Cross(updatedVertices[1] - updatedVertices[0], updatedVertices[2] - updatedVertices[0]), updatedNormals[0]) < 0)
            {
                FlipTriangle(currentTriangle);
            }
            _leftSide.AddTriangle(currentTriangle);
        }
    }

    private static MeshTriangle GetTriangle(int a, int b, int c, int submeshIndex)
    {
        Vector3[] verts = { originalMesh.vertices[a], originalMesh.vertices[b], originalMesh.vertices[c] };
        Vector3[] norms = { originalMesh.normals[a], originalMesh.normals[b], originalMesh.normals[c] };
        Vector2[] uvs = { originalMesh.uv[a], originalMesh.uv[b], originalMesh.uv[c] };
        return new MeshTriangle(verts, norms, uvs, submeshIndex);
    }

    private static void FlipTriangle(MeshTriangle triangle)
    {
        Vector3 tempVert = triangle.Vertices[1];
        triangle.Vertices[1] = triangle.Vertices[2];
        triangle.Vertices[2] = tempVert;

        Vector3 tempNorm = triangle.Normals[1];
        triangle.Normals[1] = triangle.Normals[2];
        triangle.Normals[2] = tempNorm;

        Vector2 tempUV = triangle.UVs[1];
        triangle.UVs[1] = triangle.UVs[2];
        triangle.UVs[2] = tempUV;
    }
}
/*
using System.Collections.Generic;
using System.Linq.Expressions;
using UnityEngine;
using UnityEngine.LowLevelPhysics;

public class Cutter : MonoBehaviour
{
    public static bool currentlyCutting;
    public static Mesh originalMesh;

    public static void Cut(GameObject _originalGameObject, Vector3 _contactPoint, Vector3 _direction, Material _cutMaterial = null, bool fill = true, bool _addRigidbody = false)
    {
        if(currentlyCutting)
        {
            return;
        }

        currentlyCutting = true;
        Plane plane = new Plane(_originalGameObject.transform.InverseTransformDirection(-_direction),
        _originalGameObject.transform.InverseTransformPoint(_contactPoint));
        originalMesh = _originalGameObject.GetComponent<MeshFilter>().mesh;
        List<Vector3> addedVertices = new List<Vector3>();

        generatedMesh leftMesh = new generatedMesh();
        generatedMesh rightMesh = new generatedMesh();
        
        int[] submeshIndices;
        int triangleIndexA, triangleIndexB, triangleIndexC;
        
        for (int i = 0; i < originalMesh.subMeshCount; i++)
        {
            submeshIndices = originalMesh.GetTriangles(i);

            for (int j = 0; j < submeshIndices.Length; j+=3)
            {
                triangleIndexA = submeshIndices[j];
                triangleIndexB = submeshIndices[j + 1];
                triangleIndexC = submeshIndices[j + 2];

                meshTriangle currentTriangle = GetTriangle(triangleIndexA, triangleIndexB, triangleIndexC, i);

                bool triangleALeftSide = plane.GetSide(originalMesh.vertices[triangleIndexA]);
                bool triangleBLeftSide = plane.GetSide(originalMesh.vertices[triangleIndexB]);
                bool triangleCLeftSide = plane.GetSide(originalMesh.vertices[triangleIndexC]);

                if(triangleALeftSide && triangleBLeftSide && triangleCLeftSide)
                {
                    leftMesh.AddTriangle(currentTriangle);
                }
                else if(!triangleALeftSide && !triangleBLeftSide && !triangleCLeftSide)
                {
                    rightMesh.AddTriangle(currentTriangle);
                }
                else
                {
                    CutTriangle(plane, currentTriangle, triangleALeftSide, triangleBLeftSide, triangleCLeftSide, leftMesh, rightMesh, addedVertices);
                }



            }
        }

    }

    private static void CutTriangle(Plane _plane, meshTriangle _triangle, bool _triangleALeftSide, bool _triangleBLeftSide, bool _triangleCLeftSide, generatedMesh _leftSide, generatedMesh _rightSide, List<Vector3> _addedVertices)
    {
        List<bool> leftSide = new List<bool>();
        leftSide.Add(_triangleALeftSide);
        leftSide.Add(_triangleBLeftSide);
        leftSide.Add(_triangleCLeftSide);

        meshTriangle leftMeshTriangle = new meshTriangle(new Vector3[2], new Vector3[2], new Vector2[2], _triangle.SubmeshIndex);
        meshTriangle rightMeshTriangle = new meshTriangle(new Vector3[2], new Vector3[2], new Vector2[2], _triangle.SubmeshIndex);

        bool left = false;
        bool right = false;

        for(int i = 0; i < 3; i++)
        {
            if (leftSide[i])
            {
                if (!left)
                {
                    left = true;

                    leftMeshTriangle.Vertices[0] = _triangle.Vertices[i];
                    leftMeshTriangle.Vertices[1] = leftMeshTriangle.Vertices[0];

                    leftMeshTriangle.UVs[0] = _triangle.UVs[i];
                    leftMeshTriangle.UVs[1] = leftMeshTriangle.UVs[0];

                    leftMeshTriangle.Normals[0] = _triangle.Normals[i];
                    leftMeshTriangle.Normals[1] = leftMeshTriangle.Normals[0];

                }

                else
                {
                    leftMeshTriangle.Vertices[1] = _triangle.Vertices[i];
                    leftMeshTriangle.Normals[1] = _triangle.Normals[i];
                    leftMeshTriangle.UVs[1] = _triangle.UVs[i];
                }
            }
            else
            {
                if (!right)
                {
                    right = true;

                    rightMeshTriangle.Vertices[0] = _triangle.Vertices[i];
                    rightMeshTriangle.Vertices[1] = rightMeshTriangle.Vertices[0];

                    rightMeshTriangle.UVs[0] = _triangle.UVs[i];
                    rightMeshTriangle.UVs[1] = rightMeshTriangle.UVs[0];

                    rightMeshTriangle.Normals[0] = _triangle.Normals[i];
                    rightMeshTriangle.Normals[1] = rightMeshTriangle.Normals[0];

                }

                else
                {
                    rightMeshTriangle.Vertices[1] = _triangle.Vertices[i];
                    rightMeshTriangle.Normals[1] = _triangle.Normals[i];
                    rightMeshTriangle.UVs[1] = _triangle.UVs[i];
                }

            }
        }
    }

    float normalizedDistance;
    float distance;
    _plane.Raycast(new Ray(leftMeshTriangle.Vertices[0], (rightMeshTriangle.Vertices[0] - leftMeshTriangle.Vertices[0]).normalized), out distancce);

    normalizedDistance = DistanceJoint2D / (rightMeshTriangle.Vertices[0] - leftMeshTriangle.Vertices[0]).magnitude;
    Vector3 vertLeft = Vector3.Lerp(leftMeshTriangle.Vertices[0], rightMeshTriangle.Vertices[0], normalizedDistance);
    _addedVertices.Add(vertLeft);

    Vector3 normalLeft = Vector3.Lerp(leftMeshTriangle.Normals[0], rightMeshTriangle.Normals[0], normalizedDistance);
    Vector2 uvLeft = Vector2.Lerp(leftMeshTriangle.UVs[0], rightMeshTriangle.UVs[0], normalizedDistance);

    _plane.Raycast(new Ray(leftMeshTriangle.Vertices[1], (rightMeshTriangle.Vertices[1] - leftMeshTriangle.Vertices[1]).normalized), out distance);

    normalizedDistance = distance / (rightMeshTriangle.Vertices[1] - leftMeshTriangle.Vertices[1]). magnitude;
    Vector3 vertRight = Vector3.Lerp(leftMeshTriangle.Vertices[1], rightMeshTriangle.Vertices[1], normalizedDistance);
    _addedVertices.Add(vertRight);

    Vector3 normalRight = Vector3.Lerp(leftMeshTriangle.Normals[1], rightMeshTriangle.Normals[1], normalizedDistance);
    Vector2 uvRight = Vector2.Lerp(leftMeshTriangle.UVs[1], rightMeshTriangle.UVs[1], normalizedDistance);

    //
    meshTriangle currentTriangle;
    Vector3[] updatedVertices = new Vector3[] {leftMeshTriangle.Vertices[0], Vector3.left, vertRight};
    Vector3[] updatedNormals = new Vector3[] {leftMeshTriangle.Normals[0], normalLeft, normalRight};
    Vector2[] updatedUVs = new Vector2[] {leftMeshTriangle.UVs[0], uvLeft, uvRight};

    currentTriangle = new meshTriangle(updatedVertices, updatedNormals, updatedUVs, _triangle.SubmeshIndex);

    if(updatedVertices[0] != updatedVertices[1] && updatedVertices[0] != updatedVertices[2])
    {
        if(Vector3.Dot(Vector3.Cross(updatedVertices[1] - updatedVertices[0], updatedVertices[2] - updatedVertices[0]), updatedNormals[0]) < 0)
        {
            FlipTriangle(currentTriangle);
        }
    }

    updatedVertices = new Vector3[] {leftMeshTriangle.Vertices[0], leftMeshTriangle.Vertices[1], vertRight};
    updatedNormals = new Vector3[] {leftMeshTriangle.Normals[0], leftMeshTriangle.Normals[1], normalRight};
    updatedUVs = new Vector2[] {leftMeshTriangle.UVs[0], leftMeshTriangle.UVs[1], uvRight};

    currentTriangle = new meshTriangle(updatedVertices, updatedNormals, updatedUVs, _triangle.SubmeshIndex);

    if(updatedVertices[0] != updatedVertices[1] && updatedVertices[0] != updatedVertices[2])
    {
        if(Vector3.Dot(Vector3.Cross(updatedVertices[1] - updatedVertices[0], updatedVertices[2] - updatedVertices[0]), updatedNormals[0]) < 0)
        {
            FlipTriangle(currentTriangle);
        }

    }


}
*/