using System.Collections.Generic;
using UnityEngine;

public class HairManager : MonoBehaviour
{
    public static HairManager Instance { get; private set; }

    public List<HairCardData> HairCards = new List<HairCardData>();

    private void Awake()
    {
        Instance = this;
    }

    public void RegisterHair(HairCardData hair)
    {
        if (!HairCards.Contains(hair))
            HairCards.Add(hair);
    }

    public void CutHairNear(Vector3 worldPos, float radius, float cutAmount)
    {
        foreach (HairCardData hair in HairCards)
        {
            if (hair == null || !hair.IsCuttable)
                continue;

            float distance = Vector3.Distance(hair.RootPosition, worldPos);

            if (distance <= radius)
            {
                hair.Cut(cutAmount);
                UpdateHairVisual(hair);
            }
        }
    }

    public void UpdateHairVisual(HairCardData hair)
    {
        Vector3 scale = hair.OriginalScale;
        scale.y = hair.OriginalScale.y * hair.LengthRatio;
        hair.transform.localScale = scale;
    }

    public List<HairCardData> GetHairBySection(HairSectionType section)
    {
        List<HairCardData> result = new List<HairCardData>();

        foreach (HairCardData hair in HairCards)
        {
            if (hair.Section == section)
                result.Add(hair);
        }

        return result;
    }
}