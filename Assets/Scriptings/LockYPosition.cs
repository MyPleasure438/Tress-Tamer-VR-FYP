using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class LockYPosition : MonoBehaviour
{
    private CharacterController controller;

    [Header("Y-Lock Settings")]
    public bool isYLocked = true;
    public float targetYHeight = 0f;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        targetYHeight = transform.position.y;
    }

    /// <summary>
    /// Updates target height and snaps the CharacterController cleanly.
    /// </summary>
    public void SetTargetYHeight(float newYMeters)
    {
        targetYHeight = newYMeters;
        ApplyPosition();
    }

    private void ApplyPosition()
    {
        Vector3 newPos = transform.position;
        newPos.y = targetYHeight;

        // Temporarily disable controller so Unity's collision cache accepts manual position override
        controller.enabled = false;
        transform.position = newPos;
        controller.enabled = true;
    }

    void LateUpdate()
    {
        if (!isYLocked) return;

        // Force position back if internal physics attempts to snap upward/downward
        if (!Mathf.Approximately(transform.position.y, targetYHeight))
        {
            ApplyPosition();
        }
    }
}