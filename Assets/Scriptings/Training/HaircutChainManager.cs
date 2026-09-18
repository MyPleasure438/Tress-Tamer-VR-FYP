using System.Collections.Generic;
using UnityEngine;

// HANDOFF NOTE:
// Manages the 'All Haircuts' chain mode: player picks 'All Haircuts' for a stage, trains through each
// haircut in that stage one after another (auto-advancing on 'End Haircut Session'), then shows
// StageSummaryScreen with all results once the stage's haircuts are done.
//
// Stage 1 is populated with real haircuts (Induction/Butch/Crew). Stages 2-5 are placeholder-empty for
// now per explicit instruction - fill in Haircuts arrays later as more styles get built. Next Stage still
// works even for a stage with no dedicated select-screen UI yet - it just switches HaircutManager directly.
//
// HOW TO WIRE THIS UP IN UNITY:
// 1. Create an empty GameObject (e.g. 'Haircut Chain Manager'), add this component.
// 2. Drag in HaircutManager, EvaluationSystem, HaircutResultScreen, StageSummaryScreen, VRTrainingMenuScreen.
// 3. In HaircutResultScreen's Inspector, drag this component into its new 'Chain Manager' field so
//    'End Haircut Session' knows to advance the chain instead of just hiding.
[System.Serializable]
public class StageChainDefinition
{
    public string StageName;
    public HaircutStyle[] Haircuts;
}

[System.Serializable]
public struct ChainResultEntry
{
    public HaircutStyle Style;
    public float Score;
    public string Grade;
}

[DisallowMultipleComponent]
public class HaircutChainManager : MonoBehaviour
{
    [Header("Required Scene Links")]
    [SerializeField] private HaircutManager haircutManager;
    [SerializeField] private EvaluationSystem evaluationSystem;
    [SerializeField] private HaircutResultScreen resultScreen;
    [SerializeField] private StageSummaryScreen stageSummaryScreen;
    [SerializeField] private VRTrainingMenuScreen menuScreen;
    [Tooltip("Optional. Refreshed to show the new haircut's steps when the chain advances.")]
    [SerializeField] private TrainingGuideManager trainingGuideManager;
    [SerializeField] private VRTrainingGuidePanel trainingGuidePanel;

    [Header("Stage Chains (index 0 = Stage 1, etc.) - Stages 2-5 left as empty placeholders for now")]
    [SerializeField]
    private StageChainDefinition[] stages = new StageChainDefinition[]
    {
        new StageChainDefinition { StageName = "Stage 1: Buzz Cut", Haircuts = new[] { HaircutStyle.Stage1_InductionCut, HaircutStyle.Stage1_ButchCut, HaircutStyle.Stage1_CrewCut } },
        new StageChainDefinition { StageName = "Stage 2: Blunt Bob", Haircuts = new[] { HaircutStyle.Stage2_StandardBob, HaircutStyle.Stage2_LongBob, HaircutStyle.Stage2_ALineBob } },
        new StageChainDefinition { StageName = "Stage 3: Undercut", Haircuts = new[] { HaircutStyle.Stage3_DisconnectedUndercut, HaircutStyle.Stage3_SlickedBackUndercut, HaircutStyle.Stage3_HardPartUndercut } },
        new StageChainDefinition { StageName = "Stage 4: Fade", Haircuts = new[] { HaircutStyle.Stage4_LowFade, HaircutStyle.Stage4_MidFade, HaircutStyle.Stage4_HighFade } },
        new StageChainDefinition { StageName = "Stage 5: Avant-Garde", Haircuts = new[] { HaircutStyle.Stage5_AsymmetricalBob, HaircutStyle.Stage5_SideSweptPixie, HaircutStyle.Stage5_AvantGardeAngle } },
    };

    public bool IsChainActive { get; private set; }
    private int currentStageIndex;
    private int currentHaircutIndex;
    private List<ChainResultEntry> recordedResults = new List<ChainResultEntry>();

    public string CurrentStageName => (currentStageIndex >= 0 && currentStageIndex < stages.Length) ? stages[currentStageIndex].StageName : "Stage";

    public int GetStageCount() => stages.Length;
    public string GetStageName(int index) => (index >= 0 && index < stages.Length) ? stages[index].StageName : "Stage";
    public bool HasHaircuts(int index) => index >= 0 && index < stages.Length && stages[index].Haircuts != null && stages[index].Haircuts.Length > 0;

    // Call this instead of the normal single-haircut BeginTraining flow when the player picked 'All Haircuts'.
    public void StartChain(int stageIndex)
    {
        if (stageIndex < 0 || stageIndex >= stages.Length)
        {
            Debug.LogWarning($"HaircutChainManager: stage index {stageIndex} is out of range.");
            return;
        }

        if (stages[stageIndex].Haircuts == null || stages[stageIndex].Haircuts.Length == 0)
        {
            Debug.LogWarning($"HaircutChainManager: {stages[stageIndex].StageName} has no haircuts configured yet (placeholder).");
            return;
        }

        currentStageIndex = stageIndex;
        currentHaircutIndex = 0;
        recordedResults.Clear();
        IsChainActive = true;

        ApplyCurrentHaircut();
        RefreshTrainingGuideForCurrentHaircut();

        if (trainingGuidePanel != null)
            trainingGuidePanel.SetVisible(true);

        if (resultScreen != null)
            resultScreen.ShowResults();
    }

    // Called by HaircutResultScreen's 'End Haircut Session' button when a chain is active, instead of just hiding.
    public void AdvanceChain()
    {
        if (!IsChainActive)
            return;

        RecordCurrentResult();

        currentHaircutIndex++;
        var currentStage = stages[currentStageIndex];

        if (currentHaircutIndex < currentStage.Haircuts.Length)
        {
            ApplyCurrentHaircut();
            RefreshTrainingGuideForCurrentHaircut();

            if (resultScreen != null)
                resultScreen.ShowResults();
        }
        else
        {
            IsChainActive = false;

            if (resultScreen != null)
                resultScreen.HideResults();

            if (trainingGuidePanel != null)
                trainingGuidePanel.SetVisible(false);

            if (stageSummaryScreen != null)
                stageSummaryScreen.ShowSummary(currentStage.StageName, recordedResults);
        }
    }

    public void RetryStage()
    {
        StartChain(currentStageIndex);

        if (stageSummaryScreen != null)
            stageSummaryScreen.HideSummary();
    }

    public void GoToNextStage()
    {
        int nextIndex = currentStageIndex + 1;

        // Check BEFORE hiding the summary screen - if the next stage isn't configured yet, show a clear
        // 'coming soon' message instead of silently hiding everything with nothing to replace it.
        bool nextStageReady = nextIndex >= 0 && nextIndex < GetStageCount() && HasHaircuts(nextIndex);

        if (!nextStageReady)
        {
            string upcomingName = (nextIndex >= 0 && nextIndex < GetStageCount()) ? GetStageName(nextIndex) : "Next Stage";

            if (stageSummaryScreen != null)
                stageSummaryScreen.ShowComingSoon(upcomingName);

            return;
        }

        if (stageSummaryScreen != null)
            stageSummaryScreen.HideSummary();

        StartChain(nextIndex);
    }

    public void ReturnToMainMenu()
    {
        IsChainActive = false;

        if (resultScreen != null)
            resultScreen.HideResults();

        if (trainingGuidePanel != null)
            trainingGuidePanel.SetVisible(false);

        if (stageSummaryScreen != null)
            stageSummaryScreen.HideSummary();

        if (menuScreen != null)
            menuScreen.ShowMainMenu();
    }

    private void ApplyCurrentHaircut()
    {
        if (haircutManager == null)
            return;

        haircutManager.RestoreOriginalHairLength();
        haircutManager.SetActiveHairstyle(stages[currentStageIndex].Haircuts[currentHaircutIndex]);
    }

    private void RefreshTrainingGuideForCurrentHaircut()
    {
        if (trainingGuideManager != null)
            trainingGuideManager.RefreshStepsForActiveHaircut();

        if (trainingGuidePanel != null)
            trainingGuidePanel.Refresh();
    }

    private void RecordCurrentResult()
    {
        if (evaluationSystem == null || haircutManager == null)
            return;

        float score = evaluationSystem.finalScore;
        string grade = score >= 85f ? "A" : score >= 70f ? "B" : score >= 50f ? "C" : "F";

        recordedResults.Add(new ChainResultEntry
        {
            Style = stages[currentStageIndex].Haircuts[currentHaircutIndex],
            Score = score,
            Grade = grade
        });
    }
}
