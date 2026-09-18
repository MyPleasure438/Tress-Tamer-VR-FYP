using UnityEngine;
using UnityEngine.InputSystem;

public class GameManager : MonoBehaviour
{
    // --- FIELDS FROM YOUR UML DIAGRAM ---
    public int currentState; // Represented as an int or your custom Enum
    public string playerName;
    private float sessionTimer;
    private float currentScore;

    // --- ADDITIONAL FIELDS FOR INVENTORY TRACKING ---
    [Header("Tool Setup")]
    [Tooltip("Assign your Brush, Scissor, Clipper, and Comb components here in the Inspector")]
    [SerializeField] private BaseTool[] totalTools;
    private int currentToolIndex = 0;

    void Start()
    {
        initializeApp();
    }

    void Update()
    {
        // 1. Monitor key inputs for switching tools during your prototype phase
        if (Keyboard.current != null)
        {
            if (Keyboard.current.digit1Key.wasPressedThisFrame) SwitchTool(0);
            if (Keyboard.current.digit2Key.wasPressedThisFrame) SwitchTool(1);
            if (Keyboard.current.digit3Key.wasPressedThisFrame) SwitchTool(2);
            if (Keyboard.current.digit4Key.wasPressedThisFrame) SwitchTool(3);
        }
    }

    private void SwitchTool(int index)
    {
        if (totalTools == null || index < 0 || index >= totalTools.Length) return;

        // Turn off all tools first
        for (int i = 0; i < totalTools.Length; i++)
        {
            if (totalTools[i] != null) totalTools[i].gameObject.SetActive(false);
        }

        currentToolIndex = index;
        
        // Pass the chosen tool directly into your diagrammed method hook!
        attachToolToHand(totalTools[currentToolIndex]);
    }

    // --- METHODS FROM YOUR UML DIAGRAM ---

    public void changeScene(string sceneName)
    {
        // Scene management logic...
    }

    public void savePerformance(float score)
    {
        // Scoring/Save logic...
    }

    public void resetSession()
    {
        // Session reset logic...
    }

    public void attachToolToHand(BaseTool tool)
    {
        if (tool == null) return;

        // 1. Activate the target tool object
        tool.gameObject.SetActive(true);

        // 2. Output tracking information to confirm your system works
        Debug.Log($"[GameManager] Attached tool to hand: {tool.gameObject.name} (ID: {tool.getCurrentToolID()})");
        
        // (Later when you move to VR, this is where you will physically 
        // snap the tool model to your controller transform anchors!)
    }

    private void initializeApp()
    {
        // Run setup logic...
        if (totalTools != null && totalTools.Length > 0)
        {
            SwitchTool(0); // Equip default tool at launch
        }
    }

    private void calculateFinalResults()
    {
        // Score calculation logic...
    }
}