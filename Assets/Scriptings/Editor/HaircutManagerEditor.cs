using UnityEngine;
using UnityEditor;

/// <summary>
/// Adds a visible "Show Perfect Haircut Now" button directly in the HaircutManager Inspector,
/// so you can preview the currently selected hairstyle's target lengths instantly - in Edit mode
/// or Play mode - without needing to right-click for the context menu or press Play.
/// </summary>
[CustomEditor(typeof(HaircutManager))]
public class HaircutManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // Draw all the normal Inspector fields exactly as before - this only adds to it.
        DrawDefaultInspector();

        HaircutManager haircutManager = (HaircutManager)target;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Quick Preview", EditorStyles.boldLabel);

        GUI.backgroundColor = new Color(0.4f, 0.85f, 0.4f);
        if (GUILayout.Button("Show Perfect Haircut Now", GUILayout.Height(36)))
        {
            haircutManager.ApplyPerfectHaircutToSceneHair();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.HelpBox("Instantly applies the currently selected hairstyle's target lengths to the hair in the scene, so you can preview the result right away without pressing Play.", MessageType.Info);
    }
}
