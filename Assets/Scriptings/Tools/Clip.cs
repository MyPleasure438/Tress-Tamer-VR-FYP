using UnityEngine;

public class Clip : BaseTool
{
    [SerializeField] private bool isAttached; 
    [SerializeField] private int targetSectionID;
    


    void Start()
    {
        hapticIntensity = 3;
        toolID = 2;
        //toolModel = 
        //toolAnimator = 
    }
         public override void useTool()
    {
        
    }
    
    public override int getCurrentToolID()
    {
        return toolID;
    }

    public override void playHapticFeedback(float duration)
    {
        
    }  

    private void applySmoothing(Mesh targetMesh)
    {
        
    }

    public void collectFragment(float radius)
    {
        
    }

    private void playClipSound()
    {
        
    }

}
 