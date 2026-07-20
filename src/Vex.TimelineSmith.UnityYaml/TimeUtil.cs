namespace Vex.TimelineSmith.UnityYaml;

/// <summary>Unity stores clip times in seconds; multiplayer frame data uses integer frames.</summary>
public static class TimeUtil
{
    public const double DefaultFrameRate = 60;

    /// <summary>
    /// Seconds → frame index. Floor with epsilon so 109/60 maps cleanly to 109.
    /// </summary>
    public static int SecToFrame(double seconds, double frameRate)
    {
        if (frameRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameRate));
        }

        if (seconds <= 0)
        {
            return 0;
        }

        return (int)Math.Floor(seconds * frameRate + 1e-9);
    }

    public static double FrameToSec(int frame, double frameRate)
    {
        if (frameRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameRate));
        }

        return frame / frameRate;
    }
}
