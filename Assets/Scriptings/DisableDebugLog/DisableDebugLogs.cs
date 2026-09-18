using UnityEngine;

public class DisableDebugLogs : MonoBehaviour
{
    private void Awake()
    {
#if !UNITY_EDITOR
        Debug.unityLogger.logEnabled = false;
#endif
    }
}