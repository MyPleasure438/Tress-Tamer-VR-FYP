using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class SectioningData
{
    // --- Public Fields ---
    public int sectionID;
    public Vector3 centerPoint;
    public float radius;
    public bool isLocked;
    public List<int> affectedVertices = new List<int>();

    // --- Added for Butch Cut Tracking ---
    [Header("Hair Length State (Meters)")]
    public float initialLength = 0.06f; // Default 6cm
    public float currentLength = 0.06f;

    // --- Public Methods ---

    /// <summary>
    /// Updates the length of this section's hair if the cut is shorter.
    /// </summary>
    public void RegisterCut(float cutDistanceFromScalp)
    {
        if (isLocked) return;

        if (cutDistanceFromScalp < currentLength)
        {
            // Floor it at 0.5cm (0.005m) for head safety
            currentLength = Mathf.Max(0.005f, cutDistanceFromScalp);
            Debug.Log($"Section {sectionID} cut down to: {currentLength * 100f} cm");
        }
    }

    /// <summary>
    /// Toggles the current locked state of the section.
    /// </summary>
    public void toggleLockState()
    {
        isLocked = !isLocked;
        Debug.Log($"Section {sectionID} lock state toggled to: {isLocked}");
    }

    /// <summary>
    /// Checks if a given 3D point is inside this section.
    /// </summary>
    public bool containsPoint(Vector3 point)
    {
        float distance = Vector3.Distance(centerPoint, point);
        return distance <= radius;
    }

    /// <summary>
    /// Updates the center position of the section.
    /// </summary>
    public void updatePosition(Vector3 newPos)
    {
        centerPoint = newPos;
        Debug.Log($"Section {sectionID} moved to: {newPos}");
    }

    /// <summary>
    /// Returns the total number of vertices affected by this section.
    /// </summary>
    public int getVertexCount()
    {
        return affectedVertices != null ? affectedVertices.Count : 0;
    }
}