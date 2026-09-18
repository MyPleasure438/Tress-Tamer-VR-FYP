using System.Collections.Generic;
using UnityEngine;

public class MeshTriangle
{
    public List<Vector3> Vertices { get; set; } = new List<Vector3>();
    public List<Vector3> Normals { get; set; } = new List<Vector3>();
    public List<Vector2> UVs { get; set; } = new List<Vector2>();
    public int SubmeshIndex { get; set; }

    public MeshTriangle(Vector3[] _vertices, Vector3[] _normals, Vector2[] _uvs, int _submeshIndex)
    {
        Clear();

        Vertices.AddRange(_vertices);
        Normals.AddRange(_normals);
        UVs.AddRange(_uvs);

        SubmeshIndex = _submeshIndex; // Fixed typo assigning param to itself
    }

    public void Clear()
    {
        Vertices.Clear();
        Normals.Clear();
        UVs.Clear();
        SubmeshIndex = 0;
    }
}


/*
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
        
        // Don't forget to set currentlyCutting back to false when your mesh creation logic finishes!
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
        var uvLeft = Vector2.Lerp(leftMeshTriangle.UVs[0], rightMeshTriangle.UVs[0], normalizedDistance);

        // Raycast 2
        _plane.Raycast(new Ray(leftMeshTriangle.Vertices[1], (rightMeshTriangle.Vertices[1] - leftMeshTriangle.Vertices[1]).normalized), out distance);
        normalizedDistance = distance / (rightMeshTriangle.Vertices[1] - leftMeshTriangle.Vertices[1]).magnitude;
        
        Vector3 vertRight = Vector3.Lerp(leftMeshTriangle.Vertices[1], rightMeshTriangle.Vertices[1], normalizedDistance);
        _addedVertices.Add(vertRight);

        Vector3 normalRight = Vector3.Lerp(leftMeshTriangle.Normals[1], rightMeshTriangle.Normals[1], normalizedDistance);
        Vector2 uvRight = Vector2.Lerp(leftMeshTriangle.UVs[1], rightMeshTriangle.UVs[1], normalizedDistance);

        // Sub-Triangle 1 Construction
        MeshTriangle currentTriangle;
        Vector3[] updatedVertices = { leftMeshTriangle.Vertices[0], vertLeft, vertRight }; // Fixed Vector3.left -> vertLeft
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

*/