using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class HairClipSpawner : MonoBehaviour
{
    private Vector3 originalPosition;
    private Quaternion originalRotation;
    private XRGrabInteractable grabInteractable;

    void Awake()
    {
        originalPosition = transform.position;
        originalRotation = transform.rotation;
        
        grabInteractable = GetComponent<XRGrabInteractable>();
    }

    void Start()
    {
        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.AddListener(OnVRGrab);
        }
    }

    private void OnVRGrab(SelectEnterEventArgs args)
    {
        if (grabInteractable == null) return;

        IXRSelectInteractor interactor = args.interactorObject;

        // 1. Spawn the clone clip
        GameObject newClip = Instantiate(gameObject, transform.position, transform.rotation);
        
        // Remove spawner script from clone
        Destroy(newClip.GetComponent<HairClipSpawner>());

        // Configure physics for the clone
        ConfigureClonePhysics(newClip);

        // 2. Transfer VR grab focus from source clip to the new clone
        var interactionManager = grabInteractable.interactionManager;
        if (interactionManager != null)
        {
            interactionManager.SelectExit(interactor, grabInteractable);
            
            var cloneInteractable = newClip.GetComponent<XRGrabInteractable>();
            if (cloneInteractable != null)
            {
                interactionManager.SelectEnter(interactor, cloneInteractable);
            }
        }

        // 3. Reset original source clip back to starting point
        ResetToOriginalPosition();
    }

    private void OnMouseDown()
    {
        GameObject newClip = Instantiate(gameObject, transform.position, transform.rotation);
        Destroy(newClip.GetComponent<HairClipSpawner>());

        // Configure physics for the clone
        ConfigureClonePhysics(newClip);

        ResetToOriginalPosition();
    }

    // Helper method to set kinematic and gravity on the clone
    private void ConfigureClonePhysics(GameObject clone)
    {
        Rigidbody cloneRb = clone.GetComponent<Rigidbody>();
        if (cloneRb != null)
        {
            cloneRb.isKinematic = false;
            cloneRb.useGravity = true;
        }
    }

    public void ResetToOriginalPosition()
    {
        transform.position = originalPosition;
        transform.rotation = originalRotation;

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    private void OnDestroy()
    {
        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.RemoveListener(OnVRGrab);
        }
    }
}