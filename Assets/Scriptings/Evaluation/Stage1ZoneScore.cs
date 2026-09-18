using System;
using UnityEngine;

[Serializable]
public class Stage1ZoneScore
{
    public Stage1EvaluationZone Zone;
    public int TotalCards;
    public int CorrectCards;
    public int TooLongCards;
    public int TooShortCards;
    public float Score;

    public void Reset(Stage1EvaluationZone zone)
    {
        Zone = zone;
        TotalCards = 0;
        CorrectCards = 0;
        TooLongCards = 0;
        TooShortCards = 0;
        Score = 0f;
    }

    public void AddResult(Stage1HairLengthState state)
    {
        TotalCards++;

        switch (state)
        {
            case Stage1HairLengthState.Correct:
                CorrectCards++;
                break;

            case Stage1HairLengthState.TooLong:
                TooLongCards++;
                break;

            case Stage1HairLengthState.TooShort:
                TooShortCards++;
                break;
        }

        Score = TotalCards > 0 ? ((float)CorrectCards / TotalCards) * 100f : 0f;
    }

    public string BuildSummary()
    {
        return $"{Zone}: {Score:F1}% ({CorrectCards}/{TotalCards}) Too Long: {TooLongCards}, Too Short: {TooShortCards}";
    }
}
