using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class Hairstyle
{
    // --- Public Fields ---
    public string styleName;
    public Mesh referenceMesh;
    public int difficultyRating;

    // --- Public Methods ---

    /// <summary>
    /// Loads the configuration data for this hairstyle template.
    /// </summary>
    public void loadTemplateData()
    {
        // TODO: Implement your data-loading logic here (e.g., from Resources, Addressables, or a ScriptableObject)
        Debug.Log($"Loading template data for hairstyle: {styleName}");
    }
}