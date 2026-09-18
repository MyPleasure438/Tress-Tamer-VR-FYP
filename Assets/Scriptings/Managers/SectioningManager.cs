using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SectioningManager : MonoBehaviour
{
    // --- Private Fields ---
    [SerializeField] private List<SectioningData> activeSections = new List<SectioningData>();
    [SerializeField] private Material highlightMaterial;

    // --- Added for External Script Access ---
    public List<SectioningData> GetActiveSections() => activeSections;

    // --- Public Methods ---

    /// <summary>
    /// Scans active sections to check if the tool is cutting inside them.
    /// </summary>
    public void ProcessCutAtPosition(Vector3 toolTipPosition, float calculatedCutDistance)
    {
        foreach (SectioningData section in activeSections)
        {
            if (section.containsPoint(toolTipPosition))
            {
                section.RegisterCut(calculatedCutDistance);
                UpdateShaderData(section);
            }
        }
    }

    private void UpdateShaderData(SectioningData section)
    {
        if (highlightMaterial != null)
        {
            // Your future Shader Graph bridge to send dynamic vertex arrays or colors
        }
    }

    /// <summary>
    /// Creates a new section at the specified origin with a given radius.
    /// </summary>
    public void createSection(Vector3 origin, float radius)
    {
        SectioningData newSection = new SectioningData();
        
        newSection.sectionID = activeSections.Count + 1; 
        newSection.centerPoint = origin;
        newSection.radius = radius;
        newSection.isLocked = false;
        newSection.currentLength = newSection.initialLength; // Match states

        activeSections.Add(newSection);
        Debug.Log($"Created Section {newSection.sectionID} at {origin} with radius {radius}");
    }

    /// <summary>
    /// Toggles the lock state of a specific section by its ID.
    /// </summary>
    public void toggleLock(int sectionID)
    {
        SectioningData targetSection = activeSections.Find(s => s.sectionID == sectionID);

        if (targetSection != null)
        {
            targetSection.toggleLockState();
        }
        else
        {
            Debug.LogWarning($"Section with ID {sectionID} not found.");
        }
    }

    /// <summary>
    /// Clears all currently active sections.
    /// </summary>
    public void clearAllSections()
    {
        activeSections.Clear();
        Debug.Log("All sections cleared.");
    }
}