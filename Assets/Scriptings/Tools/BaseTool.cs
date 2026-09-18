using System;
using UnityEngine;



public abstract class BaseTool : MonoBehaviour
{
    protected  float hapticIntensity;
    protected int toolID;
    protected GameObject toolModel;
    protected Animator toolAnimator;

    
    public abstract void playHapticFeedback(float duration);
    public abstract int getCurrentToolID();
    public abstract void useTool();
}