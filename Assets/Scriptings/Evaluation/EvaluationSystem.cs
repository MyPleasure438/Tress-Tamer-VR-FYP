using System.Collections;
using System.Collections.Generic;
using UnityEngine;



// HANDOFF NOTE:
// Active scoring reads HairCardData.CurrentLength against HaircutManager/target lengths.
// targetMesh and SectioningManager remain as older fallback ideas; current Stage 1 scoring is hair-card based.
public class EvaluationSystem : MonoBehaviour
{
    // --- Fields ---
    public float finalScore;
    // Legacy mesh-template comparison slot. It is not the main scoring route right now.
    [SerializeField] private Mesh targetMesh;
    [SerializeField] private int mistakeCount;

    // --- Added References for Data Validation ---
    [Header("Evaluation Settings")]
    [SerializeField] private SectioningManager sectioningManager;
    // Name kept from Stage 1 work, but this now points to the active HaircutManager.
    [SerializeField] private HaircutManager stage1HaircutManager;
    [SerializeField] private Transform hairCardsRoot;
    [SerializeField] private bool evaluateHairCardsByTargetLength = true;
    [SerializeField] private float fallbackTargetTolerance = 0.0025f;

    [Header("Stage 1 Zone Results")]
    [SerializeField] private List<Stage1ZoneScore> stage1ZoneScores = new List<Stage1ZoneScore>();
    [SerializeField] private int totalTooLongCards;
    [SerializeField] private int totalTooShortCards;
    [SerializeField] private int totalCorrectCards;

    // FIX ("per-zone breakdown still shows nonzero Correct counts for zones with no Induction stubble,
    // even though the overall Grade is correctly F/0%"): HaircutResultScreen.BuildDetailedZoneSummary()
    // used to keep its OWN separate copy of the per-card correct/too-long/too-short comparison (plain
    // CurrentLength vs targetLength+-tolerance), completely bypassing TryGetStubbleRequirementResult
    // above. That duplicate logic never got any of this file's three rounds of stubble-requirement fixes,
    // so it kept reading an un-swapped (Crew) card's CurrentLength (true physical 0, same as an
    // Induction-required zone's target) as "Correct" purely by coincidence - exactly the old bug, just
    // surviving in a second, unpatched location. Rather than trying to keep two copies of this logic in
    // sync forever, this tracks the SAME per-card Stage1HairLengthState (computed once, in
    // TryCalculateHairCardAccuracy below) broken down by the raw 20-value HairSectionType instead of the
    // 7-bucket Stage1EvaluationZone grouping, so HaircutResultScreen can read real per-zone results
    // straight from here instead of recomputing (and re-breaking) them itself.
    private readonly Dictionary<HairSectionType, SectionTally> stage1SectionScores = new Dictionary<HairSectionType, SectionTally>();

    private struct SectionTally
    {
        public int correct;
        public int tooLong;
        public int tooShort;
    }

    // Butch Cut metrics (Meters)
    private const float ButchMinThreshold = 0.02f; // 2.0 cm
    private const float ButchMaxThreshold = 0.03f; // 3.0 cm

    // --- Public Methods ---

    /// <summary>
    /// Legacy API wrapper. If hair-card scoring is enabled, it ignores the mesh arguments and scores current cards.
    /// </summary>
    public float compareToTemplate(Mesh currentHair, Hairstyle target)
    {
        if (evaluateHairCardsByTargetLength && TryCalculateHairCardAccuracy())
        {
            updateUI();
            return finalScore;
        }

        calculateAccuracy();
        updateUI();
        return finalScore;
    }

    [ContextMenu("Evaluate Active Haircut")]
    public float EvaluateActiveHaircut()
    {
        if (!TryCalculateHairCardAccuracy())
            calculateAccuracy();

        updateUI();
        return finalScore;
    }

    public IReadOnlyList<Stage1ZoneScore> GetStage1ZoneScores()
    {
        return stage1ZoneScores;
    }

    // See stage1SectionScores field comment above - single source of truth for per-(raw)zone breakdowns,
    // computed using the exact same GetLengthState/TryGetStubbleRequirementResult pipeline as the overall
    // Grade, so callers (HaircutResultScreen) can't silently drift out of sync with it again.
    public IEnumerable<HairSectionType> GetEvaluatedStage1Sections()
    {
        return stage1SectionScores.Keys;
    }

    public bool TryGetStage1SectionTally(HairSectionType section, out int correct, out int tooLong, out int tooShort)
    {
        if (stage1SectionScores.TryGetValue(section, out SectionTally tally))
        {
            correct = tally.correct;
            tooLong = tally.tooLong;
            tooShort = tally.tooShort;
            return true;
        }

        correct = 0;
        tooLong = 0;
        tooShort = 0;
        return false;
    }

    public string GetStage1ResultSummary()
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        builder.AppendLine($"Score: {finalScore:F1}%");
        builder.AppendLine($"Correct: {totalCorrectCards}, Too Long: {totalTooLongCards}, Too Short: {totalTooShortCards}");

        for (int i = 0; i < stage1ZoneScores.Count; i++)
            builder.AppendLine(stage1ZoneScores[i].BuildSummary());

        return builder.ToString();
    }

    /// <summary>
    /// Generates a visual heatmap based on evaluation details.
    /// </summary>
    public void generateHeatmap()
    {
        Debug.Log("Generating feedback heatmap...");
    }

    /// <summary>
    /// Updates the user interface with the evaluation results.
    /// </summary>
    public void updateUI()
    {
        Debug.Log($"Updating UI. Current Score: {finalScore:F2}%, Mistakes/Deviations: {mistakeCount}");
    }

    /// <summary>
    /// Returns an array of colors representing the generated feedback heatmap.
    /// </summary>
    public Color[] getFeedbackHeatmap()
    {
        return new Color[0];
    }

    // --- Private Methods ---

    /// <summary>
    /// Calculates internal accuracy based on the Butch Cut criteria.
    /// </summary>
    private void calculateAccuracy()
    {
        if (sectioningManager == null)
        {
            Debug.LogError("EvaluationSystem lacks a SectioningManager link!");
            return;
        }

        List<SectioningData> sections = sectioningManager.GetActiveSections();
        if (sections.Count == 0) return;

        int passingSections = 0;
        mistakeCount = 0;

        foreach (SectioningData section in sections)
        {
            // Evaluate if the section hair falls into the uniform Butch criteria
            if (section.currentLength >= ButchMinThreshold && section.currentLength <= ButchMaxThreshold)
            {
                passingSections++;
            }
            else
            {
                mistakeCount++; // Out of bounds counts as a deviation mistake
            }
        }

        // Output calculation as a clean percentage value
        finalScore = ((float)passingSections / sections.Count) * 100f;
        Debug.Log($"Accuracy processed natively: {finalScore:F2}% Adherence.");
    }

    private bool TryCalculateHairCardAccuracy()
    {
        Transform root = hairCardsRoot != null ? hairCardsRoot : transform;
        HairCardData[] hairCards = root.GetComponentsInChildren<HairCardData>(true);

        if (hairCards == null || hairCards.Length == 0)
            return false;

        ResetStage1ZoneScores();
        stage1SectionScores.Clear();
        int evaluatedCards = 0;
        mistakeCount = 0;
        totalTooLongCards = 0;
        totalTooShortCards = 0;
        totalCorrectCards = 0;

        for (int i = 0; i < hairCards.Length; i++)
        {
            HairCardData hairCard = hairCards[i];

            if (hairCard == null || !TryGetHairCardTarget(hairCard, out float targetLength, out float tolerance))
                continue;

            evaluatedCards++;
            Stage1HairLengthState lengthState = GetLengthState(hairCard, targetLength, tolerance);
            AddStage1ZoneResult(MapToStage1Zone(hairCard.Section), lengthState);
            AddStage1SectionResult(hairCard.Section, lengthState);

            if (lengthState == Stage1HairLengthState.Correct)
            {
                totalCorrectCards++;
            }
            else
            {
                mistakeCount++;

                if (lengthState == Stage1HairLengthState.TooLong)
                    totalTooLongCards++;
                else
                    totalTooShortCards++;
            }
        }

        if (evaluatedCards == 0)
            return false;

        finalScore = ((float)totalCorrectCards / evaluatedCards) * 100f;
        Debug.Log($"Hair card target accuracy: {finalScore:F2}% ({totalCorrectCards}/{evaluatedCards}) for Stage 1.\n{GetStage1ResultSummary()}");
        return true;
    }

    private bool TryGetHairCardTarget(HairCardData hairCard, out float targetLength, out float tolerance)
    {
        targetLength = 0f;
        tolerance = Mathf.Max(0f, fallbackTargetTolerance);

        if (hairCard == null)
            return false;

        // FIX (pre-existing bug: 0-length "shave to stubble" zones were silently excluded from scoring
        // entirely): this used to return `targetLength > 0f` here, which is FALSE for a legitimately
        // resolved target of exactly 0. The caller (TryCalculateHairCardAccuracy) treats a false return as
        // "skip this card", so every zone configured to shave all the way down never got evaluated at all -
        // it just never contributed to Correct/TooLong/TooShort counts, rather than being scored as
        // Correct or Incorrect. 0 is a legitimate, meaningful target (see HaircutManager's Custom Per
        // Section mode) and must be evaluated like any other target length.
        if (stage1HaircutManager != null && stage1HaircutManager.TryGetTargetLength(hairCard, out targetLength, out tolerance))
            return true;

        if (!hairCard.HasUsableTargetLength())
            return false;

        targetLength = hairCard.TargetLength;
        tolerance = hairCard.TargetTolerance;
        return true;
    }

    private Stage1HairLengthState GetLengthState(HairCardData hairCard, float targetLength, float tolerance)
    {
        SegmentedHairCard segmentedHairCard = hairCard != null ? hairCard.GetComponent<SegmentedHairCard>() : null;

        // FIX ("Grade A shown even though no Induction stubble is actually in the scene"): a required
        // stubble style must be checked BEFORE the plain numeric comparison below, not just used to
        // decide whether to auto-pass via IsCompletedByMinimumVisibleSegment. The numeric fallback alone
        // cannot tell Crew apart from a correct result: StubbleAreaSwapController only overrides
        // HairCardData.CurrentLength away from the real physical value for Butch/Induction swaps (see
        // ApplySwap) - an UN-swapped ("Crew"/None) card keeps CurrentLength at its true physical 0, which
        // coincidentally equals a 0 target exactly. So a card that ended up Crew when Induction was
        // required was previously falling through to the numeric check and reading as "Correct" purely by
        // coincidence, completely bypassing the requirement. Checking this first and returning a hard
        // mismatch result (instead of merely declining to auto-pass) closes that gap.
        if (TryGetStubbleRequirementResult(hairCard, segmentedHairCard, out bool requirementMatches))
            return requirementMatches ? Stage1HairLengthState.Correct : Stage1HairLengthState.TooLong;

        if (IsCompletedByMinimumVisibleSegment(segmentedHairCard, targetLength, tolerance))
            return Stage1HairLengthState.Correct;

        float safeTolerance = Mathf.Max(0f, tolerance);
        float currentLength = hairCard != null ? hairCard.CurrentLength : 0f;

        if (currentLength > targetLength + safeTolerance)
            return Stage1HairLengthState.TooLong;

        if (currentLength < targetLength - safeTolerance)
            return Stage1HairLengthState.TooShort;

        return Stage1HairLengthState.Correct;
    }

    // Returns true only when this zone has a developer-configured required stubble style at all - `matches`
    // then tells whether the card actually achieved it.
    //
    // FIX (still showing Grade A with a Required Style set and no matching stubble in the scene): this
    // USED to also require the zone's separately-configured numeric target length to independently resolve
    // to ~0 (via IsTargetBelowMinimumSegmentLength) before the stubble check would even engage - meaning a
    // developer had to keep TWO different arrays in perfect sync (Custom Section Target Lengths AND
    // Section Stubble Requirements) for this to do anything at all. If the numeric target for a zone
    // wasn't ALSO set to 0 (e.g. Target Length Mode isn't even Custom Per Section, or that specific entry
    // was left at its old value), the whole requirement silently never applied and grading fell straight
    // through to the plain length comparison - exactly this bug. Setting a Required Style for a zone now
    // means that zone is judged ENTIRELY by stubble-finish match on its own, with no dependency on any
    // other numeric target setting.
    private bool TryGetStubbleRequirementResult(HairCardData hairCard, SegmentedHairCard segmentedHairCard, out bool matches)
    {
        matches = false;

        if (hairCard == null || segmentedHairCard == null)
            return false;

        if (stage1HaircutManager == null || !stage1HaircutManager.TryGetRequiredStubbleStyle(hairCard.Section, out RequiredStubbleStyle requiredStyle))
            return false; // no requirement configured for this zone - caller uses the normal logic unchanged

        if (!segmentedHairCard.IsAtMinimumSegmentLength)
        {
            // Not fully shaved down yet, so there is no achieved stubble finish to compare against -
            // force NOT correct (rather than falling through to a numeric comparison that could
            // coincidentally read as "Correct") since this zone requires a specific finish and hasn't
            // reached one yet.
            matches = false;
            return true;
        }

        // Fails CLOSED (matches = false) if StubbleAreaSwapController isn't present/enabled in the scene,
        // rather than silently skipping the check - a configured requirement that can't be verified must
        // never score as correct.
        if (StubbleAreaSwapController.Instance == null)
        {
            matches = false;
            return true;
        }

        RequiredStubbleStyle achievedStyle = StubbleAreaSwapController.Instance.GetAchievedStubbleStyle(segmentedHairCard);
        matches = achievedStyle == requiredStyle;
        return true;
    }

    private bool IsCompletedByMinimumVisibleSegment(SegmentedHairCard segmentedHairCard, float targetLength, float tolerance)
    {
        if (segmentedHairCard == null)
            return false;

        return segmentedHairCard.IsAtMinimumSegmentLength && segmentedHairCard.IsTargetBelowMinimumSegmentLength(targetLength, tolerance);
    }

    private void ResetStage1ZoneScores()
    {
        stage1ZoneScores.Clear();
        Stage1EvaluationZone[] zones = (Stage1EvaluationZone[])System.Enum.GetValues(typeof(Stage1EvaluationZone));

        for (int i = 0; i < zones.Length; i++)
        {
            Stage1ZoneScore zoneScore = new Stage1ZoneScore();
            zoneScore.Reset(zones[i]);
            stage1ZoneScores.Add(zoneScore);
        }
    }

    private void AddStage1ZoneResult(Stage1EvaluationZone zone, Stage1HairLengthState state)
    {
        for (int i = 0; i < stage1ZoneScores.Count; i++)
        {
            if (stage1ZoneScores[i].Zone != zone)
                continue;

            stage1ZoneScores[i].AddResult(state);
            return;
        }
    }

    private void AddStage1SectionResult(HairSectionType section, Stage1HairLengthState state)
    {
        SectionTally tally = stage1SectionScores.TryGetValue(section, out SectionTally existing) ? existing : new SectionTally();

        switch (state)
        {
            case Stage1HairLengthState.Correct:
                tally.correct++;
                break;
            case Stage1HairLengthState.TooLong:
                tally.tooLong++;
                break;
            default:
                tally.tooShort++;
                break;
        }

        stage1SectionScores[section] = tally;
    }

    private Stage1EvaluationZone MapToStage1Zone(HairSectionType section)
    {
        switch (section)
        {
            case HairSectionType.Fringe:
            case HairSectionType.FringeLeft:
            case HairSectionType.FringeMiddle:
            case HairSectionType.FringeRight:
                return Stage1EvaluationZone.Fringe;

            case HairSectionType.Crown:
            case HairSectionType.CrownBack:
            case HairSectionType.CrownLeft:
            case HairSectionType.CrownRight:
                return Stage1EvaluationZone.Crown;

            case HairSectionType.TopLeft:
            case HairSectionType.TopMiddle:
            case HairSectionType.TopRight:
                return Stage1EvaluationZone.Top;

            case HairSectionType.LeftSide:
            case HairSectionType.LeftSideSideburn:
            case HairSectionType.LeftSideUpper:
            case HairSectionType.TransitionBackLeftAtMiddleSection:
            case HairSectionType.TransitionBackLeftAtUpperSection:
                return Stage1EvaluationZone.LeftSide;

            case HairSectionType.RightSide:
            case HairSectionType.RightSideSideburn:
            case HairSectionType.RightSideUpper:
            case HairSectionType.TransitionBackRightAtMiddleSection:
            case HairSectionType.TransitionBackRightAtUpperSection:
                return Stage1EvaluationZone.RightSide;

            case HairSectionType.BackNape:
                return Stage1EvaluationZone.Nape;

            case HairSectionType.Back:
            case HairSectionType.BackMiddle:
            case HairSectionType.BackUpper:
            default:
                return Stage1EvaluationZone.Back;
        }
    }
}
