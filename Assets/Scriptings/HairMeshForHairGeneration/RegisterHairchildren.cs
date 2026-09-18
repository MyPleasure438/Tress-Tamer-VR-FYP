using UnityEngine;

public class RegisterHairChildren : MonoBehaviour
{
    private void Start()
    {
        if (HairManager.Instance == null)
        {
            Debug.LogError("No HairManager found in scene.");
            return;
        }

        int count = 0;

        foreach (Transform child in transform)
        {
            HairCardData data = child.GetComponent<HairCardData>();

            if (data == null)
                data = child.gameObject.AddComponent<HairCardData>();

            data.OriginalScale = child.localScale;
            data.RootPosition = child.position;
            data.CurrentLength = data.OriginalLength;

            HairManager.Instance.RegisterHair(data);
            count++;
        }

        Debug.Log($"Registered {count} hair cards.");
    }
}