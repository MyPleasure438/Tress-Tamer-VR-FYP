using UnityEngine;
using UnityEditor;

// HANDOFF NOTE: This is Claude's FIRST fix attempt from earlier today - kept as a reference/rollback
// point, not the currently-used tool. It only corrects POSITION (keeps the original GameObject's
// rotation/scale unchanged), which turned out to be incomplete: if that Transform carries leftover FBX
// import artifacts (e.g. a 100x scale, an odd rotation) that don't actually drive rendering (bones do),
// the converted object ends up correctly POSITIONED but still wrongly SIZED/ORIENTED. The current
// SmoothSMRConverter.cs supersedes this with a verified rotation+scale+position fix.
public class SmoothSMRConverterOld : EditorWindow
{
    [MenuItem("Tools/Convert SMR to Mesh Renderer (OLD - Reference Only)")]
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

        // Record the REAL visual world position BEFORE converting - the object's own Transform can be
        // unreliable (e.g. leftover FBX import scale/rotation artifacts) since bones actually drive where
        // a skinned mesh renders, not necessarily the object's own Transform. Measuring this directly is
        // more reliable than trusting the Transform math, and lets us correct for any mismatch afterward.
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

        // 5. Measure where it actually ended up, and correct the position if it doesn't match the
        // original real-world position - fixes the exact bug where converted objects appear displaced
        // (commonly caused by non-standard import Transform values that don't actually drive rendering).
        Bounds newWorldBounds = renderer.bounds;
        Vector3 correction = originalWorldBounds.center - newWorldBounds.center;

        if (correction.sqrMagnitude > 0.0001f)
        {
            selectedObj.transform.position += correction;
            Debug.Log($"Corrected position mismatch of {correction.magnitude:0.###}m after conversion.");
        }

        Debug.Log($"Successfully converted SMR to Mesh Renderer! Mesh saved to: {path}");
    }
}
