using UnityEngine;
using UnityEditor;

public class SmoothSMRConverter : EditorWindow
{
    [MenuItem("Tools/Convert SMR to Mesh Renderer")]
    public static void ConvertSelectedSMR()
    {
        GameObject selectedObj = Selection.activeGameObject;
        if (selectedObj == null) return;

        SkinnedMeshRenderer smr = selectedObj.GetComponent<SkinnedMeshRenderer>();
        if (smr == null)
        {
            Debug.LogError("Selected GameObject does not have a SkinnedMeshRenderer!");
            return;
        }

        // Record the REAL visual world position BEFORE converting - the object's own Transform can carry
        // leftover FBX import artifacts that do not actually drive rendering (bones do).
        Bounds originalWorldBounds = smr.bounds;

        // 1. Bake the current pose into a temporary mesh
        Mesh bakedMesh = new Mesh();
        smr.BakeMesh(bakedMesh);

        // 2. Save the mesh as an asset file so it persists
        string path = "Assets/BakedMesh_" + selectedObj.name + "_" + System.DateTime.Now.Ticks + ".asset";
        AssetDatabase.CreateAsset(bakedMesh, path);
        AssetDatabase.SaveAssets();

        // 3. Cache materials and remove SMR
        Material[] materials = smr.sharedMaterials;
        Undo.DestroyObjectImmediate(smr);

        // 4. Add MeshFilter and MeshRenderer
        MeshFilter filter = selectedObj.AddComponent<MeshFilter>();
        filter.sharedMesh = bakedMesh;

        MeshRenderer renderer = selectedObj.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = materials;

        // 5. Verified empirically (visual gizmo comparison, not just bounds math): BakeMesh's raw output
        // for this rig is in a Z-up orientation (classic Blender convention) rather than Unity's Y-up, even
        // though the live bone-driven rendering correctly compensates for this. A -90 degree X rotation
        // converts it to the correct Y-up orientation - confirmed by comparing the 'up' axis gizmo directly
        // against the original SkinnedMeshRenderer before conversion.
        selectedObj.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        selectedObj.transform.localScale = Vector3.one;

        // Reposition so bounds center matches the real original position (must be done AFTER rotation,
        // since rotating around the pivot shifts the bounds center too).
        Vector3 correction = originalWorldBounds.center - renderer.bounds.center;
        selectedObj.transform.position += correction;

        Debug.Log($"Successfully converted SMR to Mesh Renderer! Mesh saved to: {path}");
    }
}
