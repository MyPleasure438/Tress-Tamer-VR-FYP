using System;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

// HANDOFF NOTE (Phase 1 - Training Guide):
// Shared small data types for the beginner training guide system.
// Nothing here touches Clipper/Comb/Brush/HaircutManager/EvaluationSystem internals -
// TrainingGuideManager only READS from HaircutManager/EvaluationSystem through their existing public API.

// Which tool the guide recommends for a given step. 'Any' means no strong recommendation.
public enum RecommendedTool
{
    Any,
    Clipper,
    Scissor,
    Comb
}

// One authored override for a specific haircut + zone combination.
// Optional: if you don't add any of these, TrainingGuideManager auto-generates
// a sensible step (tool + instruction text) from HaircutManager's target length for that zone.
[Serializable]
public class TrainingStepDefinition
{
    public HairSectionType Zone;
    public RecommendedTool Tool = RecommendedTool.Any;

    [TextArea(2, 4)]
    [Tooltip("Leave empty to auto-generate instruction text from the target length instead.")]
    public string InstructionOverride;

    [Tooltip("Overrides the displayed 'Target: ... cm' value for this zone. Leave empty to use the real target length from HaircutManager instead. Accepts a single number ('3' or '3.5') or a range ('6 - 8', '6-8', '6 to 8') - 'cm' is added automatically, don't type it yourself.")]
    public string TargetLengthOverrideCm = string.Empty;
}

// Parses TrainingStepDefinition.TargetLengthOverrideCm's free-text input ("3", "3.5", "6-8", "6 - 8",
// "6 to 8") into both a numeric cm value (used internally for tool-inference and the auto-generated
// instruction text - the midpoint for a range) and the exact display text shown in the training guide
// panel (e.g. "3 cm" or "6 - 8 cm", 'cm' always appended automatically).
public static class TargetLengthOverrideParser
{
    private static readonly Regex RangePattern = new Regex(
        @"^\s*(\d+(?:\.\d+)?)\s*(?:-|to)\s*(\d+(?:\.\d+)?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SinglePattern = new Regex(
        @"^\s*(\d+(?:\.\d+)?)\s*$",
        RegexOptions.Compiled);

    public static bool TryParse(string text, out float numericCm, out string displayText)
    {
        numericCm = 0f;
        displayText = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        Match rangeMatch = RangePattern.Match(text);

        if (rangeMatch.Success)
        {
            float low = float.Parse(rangeMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            float high = float.Parse(rangeMatch.Groups[2].Value, CultureInfo.InvariantCulture);

            if (low > high)
                (low, high) = (high, low);

            numericCm = (low + high) / 2f;
            displayText = $"{FormatNumber(low)} - {FormatNumber(high)} cm";
            return true;
        }

        Match singleMatch = SinglePattern.Match(text);

        if (singleMatch.Success)
        {
            float value = float.Parse(singleMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            numericCm = value;
            displayText = $"{FormatNumber(value)} cm";
            return true;
        }

        return false;
    }

    private static string FormatNumber(float value)
    {
        return value.ToString("0.#", CultureInfo.InvariantCulture);
    }
}

// Optional per-haircut authored step list + order. Only needed if you want to override
// the auto-generated default order/tool/instructions for a specific HaircutStyle.
[Serializable]
public class HaircutStyleStepConfig
{
    public HaircutStyle Style;
    public TrainingStepDefinition[] Steps;
}

// Resolved runtime info for whatever step the player is currently on.
// This is what VRTrainingGuidePanel reads to draw the UI - it never touches HaircutManager directly.
[Serializable]
public struct TrainingStepInfo
{
    public int StepNumber;      // 1-based, for display ("Step 3 / 8")
    public int TotalSteps;
    public HairSectionType Zone;
    public string ZoneDisplayName;
    public RecommendedTool Tool;
    public string Instruction;
    public float TargetLengthMeters;
    public float TargetLengthCm => TargetLengthMeters * 100f;
    public bool HasValidTarget;
    // Exact text to display for the target length - a plain number ("3 cm") normally, or the authored
    // range ("6 - 8 cm") when TrainingStepDefinition.TargetLengthOverrideCm is set to a range. Always
    // ends in "cm". Empty when HasValidTarget is false.
    public string TargetLengthDisplayText;
}

// Result of an optional, non-blocking zone check (see TrainingGuideManager.CheckCurrentZoneOptional).
// This is intentionally coarse (reuses EvaluationSystem's 7-zone Stage1EvaluationZone grouping) -
// it's a 'roughly on track?' checkpoint for the guide, not a replacement for the real EvaluationSystem score.
[Serializable]
public struct TrainingZoneCheckResult
{
    public bool HasData;
    public string Summary;

    // Coarse cross-check using EvaluationSystem's existing 7-zone grouping (e.g. all of
    // Fringe/FringeLeft/FringeMiddle/FringeRight combined into one 'Fringe' score).
    public float ZoneScorePercent;

    // Specific breakdown for the EXACT HairSectionType the player is currently on
    // (e.g. FringeMiddle specifically, not the whole Fringe group).
    public int SpecificZoneTotalCards;
    public int SpecificZoneCorrectCards;
    public int SpecificZoneTooLongCards;
    public int SpecificZoneTooShortCards;
}
