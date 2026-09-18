using UnityEngine;
using UnityEngine.InputSystem; // <-- This lets us use the New Input System!

public class ClickToCut : MonoBehaviour
{
    void Update()
    {
        // Check if the left mouse button was pressed this frame
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            // Get the current mouse position on the screen
            Vector2 mousePosition = Mouse.current.position.ReadValue();
            
            Ray ray = Camera.main.ScreenPointToRay(mousePosition);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit))
            {
                GameObject hitObject = hit.collider.gameObject;

                // Safety checks to ensure it's a valid static mesh
                MeshFilter meshFilter = hitObject.GetComponent<MeshFilter>();
                MeshRenderer meshRenderer = hitObject.GetComponent<MeshRenderer>();

                if (meshFilter != null && meshRenderer != null)
                {
                    if (meshFilter.sharedMesh != null && meshFilter.sharedMesh.vertexCount > 0)
                    {
                        Debug.Log($"[ClickToCut] Sent cut command for: {hitObject.name}");
                        
                        // Execute the cut using the ray's path direction
                        Cutter.Cut(hitObject, hit.point, ray.direction);
                    }
                }
                else
                {
                    Debug.LogWarning($"[ClickToCut] {hitObject.name} is missing a MeshFilter or MeshRenderer.");
                }
            }
        }
    }
}