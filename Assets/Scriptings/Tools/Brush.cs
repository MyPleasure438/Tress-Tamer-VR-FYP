using UnityEngine;
using UnityEngine.UIElements;
using System.Collections;
using UnityEngine.InputSystem;

// HANDOFF NOTE:
// Active brush/styling tool. It does not cut hair; it bends/smooths hair cards through CuttingManager.
// Current desktop test path holds left mouse to brush. For VR, this can be changed to brush on contact/movement.
public class Brush : BaseTool
{
    [Header("Cutting/Styling Engine Links")]
    [Tooltip("Drag the Hair GameObject with the CuttingManager attached here")]
    [SerializeField] private CuttingManager cuttingManager;

    [Tooltip("Create an empty GameObject at the center of the brush bristles and drag it here")]
    [SerializeField] private Transform brushCenter;

    [Header("Brush Settings")]
    [SerializeField] private float brushStrength = 0.3f; 
    [SerializeField] private float brushRadius = 0.06f;
    [SerializeField] private float minimumMovementToBrush = 0.001f;
    [SerializeField] private ParticleSystem ParticleSystem;
    
    private bool isBrushing = false;
    private Vector3 lastBrushPosition;
    private bool hasLastBrushPosition;

    void Update()
    {
        if (Mouse.current == null) return;

        // Mouse input is only a desktop test path; CuttingManager handles the actual hair deformation.
        if (Mouse.current.leftButton.wasPressedThisFrame && !isBrushing)
        {
            isBrushing = true;
            hasLastBrushPosition = false;
            if (cuttingManager != null)
                cuttingManager.BeginHairUndoStroke("Brush Hair");
            if (ParticleSystem != null) ParticleSystem.Play();
            playBrushSound();
            Debug.Log("[Brush] Started brushing/smoothing the hair.");
        }

        // 2. Detect release to stop brushing action
        if (Mouse.current.leftButton.wasReleasedThisFrame && isBrushing)
        {
            isBrushing = false;
            hasLastBrushPosition = false;
            if (cuttingManager != null)
                cuttingManager.EndHairUndoStroke();
            if (ParticleSystem != null) ParticleSystem.Stop();
            Debug.Log("[Brush] Stopped brushing.");
        }

        // Brush movement direction is the styling direction; root/gizmo should stay anchored by hair-card data.
        if (isBrushing && cuttingManager != null && brushCenter != null)
        {
            playHapticFeedback(Time.deltaTime);

            Vector3 currentBrushPosition = brushCenter.position;

            if (!hasLastBrushPosition)
            {
                lastBrushPosition = currentBrushPosition;
                hasLastBrushPosition = true;
                return;
            }

            Vector3 movementDelta = currentBrushPosition - lastBrushPosition;
            lastBrushPosition = currentBrushPosition;

            if (movementDelta.sqrMagnitude >= minimumMovementToBrush * minimumMovementToBrush)
                cuttingManager.ExecuteHairCardMeshBrush(currentBrushPosition, brushRadius, movementDelta, brushStrength);
            
            // Optional framework hook for gathering loose mesh chunks if needed later
            collectFragment(brushRadius);
        }
    }

    private void OnDisable()
    {
        if (!isBrushing)
            return;

        isBrushing = false;
        hasLastBrushPosition = false;

        if (cuttingManager != null)
            cuttingManager.EndHairUndoStroke();
    }

    // Displays the brush's radius field in yellow/orange within the scene view
    private void OnDrawGizmosSelected()
    {
        if (brushCenter != null)
        {
            Gizmos.color = new Color(1f, 0.6f, 0f); // Orange
            Gizmos.DrawWireSphere(brushCenter.position, brushRadius);
        }
    }

    void Start()
    {
        hapticIntensity = 3;
        toolID = 1;
        //toolModel = 
        //toolAnimator = 
    }

    public override void useTool()
    {
        // Framework hook if needed
    }
    
    public override int getCurrentToolID()
    {
        return toolID; // Returns 1 to match your system mapping
    }

    public override void playHapticFeedback(float duration)
    {
        // VR controller hook
    }  

    private void applySmoothing(Mesh mesh)
    {
        // Internal smoothing logic can be overridden here if executing locally on a custom mesh instance
    }

    public void collectFragment(float radius)
    {
        // Placeholder for gathering hair fragments or physics triggers
    }

    private void playBrushSound()
    {
        // Hook for playing soft bristle brush audio clip
    }
}
