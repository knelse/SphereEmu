using System;

namespace SphServer.System;

public class SphereTimer (double targetTime, bool autoRearm, Action onComplete)
{
    private double remainingTime = targetTime;
    private Action onComplete = onComplete;

    public void Arm (double targetTime, Action onComplete)
    {
        remainingTime = targetTime;
        this.onComplete = onComplete;
    }

    public void Rearm (double targetTime)
    {
        remainingTime = targetTime;
    }

    public bool Tick (double delta)
    {
        if (remainingTime <= 0)
        {
            // prevent multiple activations
            return false;
        }
        remainingTime -= delta;
        if (remainingTime <= 0)
        {
            onComplete();
        }

        var hasFinished = remainingTime <= 0;

        if (autoRearm && hasFinished)
        {
            remainingTime = targetTime;
        }

        return hasFinished;
    }
}