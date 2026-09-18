using UnityEngine;

public class HairCardSegment : MonoBehaviour
{
    public SegmentedHairCard Owner;
    public int SegmentIndex;

    public bool Cut()
    {
        if (Owner == null)
            return false;

        return Owner.CutFromSegmentIndex(SegmentIndex);
    }
}
