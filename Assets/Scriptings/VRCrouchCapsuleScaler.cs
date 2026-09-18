using UnityEngine;

public class VRCrouchCapsuleScaler : MonoBehaviour
{
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private CharacterController controller;
    [SerializeField] private float minHeight = 0.5f;

    void Update()
    {
        if (cameraTransform == null || controller == null) return;

        // Scale capsule height to match the physical head height above the floor
        float headHeight = Mathf.Max(cameraTransform.localPosition.y, minHeight);

        controller.height = headHeight;

        // Recenter capsule between floor and head
        Vector3 newCenter = controller.center;
        newCenter.y = headHeight / 2f;
        controller.center = newCenter;
    }
}