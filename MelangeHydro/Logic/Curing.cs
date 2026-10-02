namespace Melange.Hydro
{
    public enum CureStage
    {
        /// <summary>Not fully grown yet: no curing.</summary>
        Growing,
        /// <summary>The first 12 hours after it is fully grown: climbing to one tier above its grown quality.</summary>
        Rising,
        /// <summary>The next 12 hours: at the peak. The time to harvest.</summary>
        Peak,
        /// <summary>The 12 hours after that: slipping back.</summary>
        Falling,
        /// <summary>Back at its grown quality, and it stays there (never lower).</summary>
        Settled,
    }

    /// <summary>
    /// Curing on the aeroponic tower: a fully grown plant left on the tower rises one tier over 12 in-game hours, holds that
    /// for 12 hours, then falls back to its grown quality over the next 12 and stays there. The drying rack's pace (one tier
    /// per 720 minutes), so it feels native; harvest timing becomes the skill, and neglect costs quality, not the crop. The
    /// peak is one tier above the grown quality and never above Heavenly, so it doesn't replace the drying rack's ladder.
    /// </summary>
    /// <remarks>
    /// Plants are simulated separately on every player's game, so the quality is computed from the game time the plant
    /// became fully grown (the clock every peer shares), never from a running timer, and every peer agrees on it.
    /// </remarks>
    public static class Curing
    {
        public const int RiseMinutes = 720, HoldMinutes = 720, FallMinutes = 720;

        public static CureStage StageAt(int minutesSinceGrown)
        {
            if (minutesSinceGrown < 0) return CureStage.Growing;
            if (minutesSinceGrown < RiseMinutes) return CureStage.Rising;
            if (minutesSinceGrown < RiseMinutes + HoldMinutes) return CureStage.Peak;
            if (minutesSinceGrown < RiseMinutes + HoldMinutes + FallMinutes) return CureStage.Falling;
            return CureStage.Settled;
        }

        /// <summary>How far towards the peak the plant is, 0 to 1, at that many minutes after it was fully grown.</summary>
        public static float Progress(int minutesSinceGrown)
        {
            switch (StageAt(minutesSinceGrown))
            {
                case CureStage.Rising: return (float)minutesSinceGrown / RiseMinutes;
                case CureStage.Peak: return 1f;
                case CureStage.Falling: return 1f - (float)(minutesSinceGrown - RiseMinutes - HoldMinutes) / FallMinutes;
                default: return 0f;
            }
        }

        /// <summary>The plant's quality <paramref name="minutesSinceGrown"/> after it was fully grown at <paramref name="grownQuality"/>.</summary>
        public static float QualityAt(float grownQuality, int minutesSinceGrown)
        {
            float p = Progress(minutesSinceGrown);
            if (p <= 0f) return grownQuality;
            float peak = Quality.OneTierUp(grownQuality);
            return grownQuality + (peak - grownQuality) * p;
        }

        /// <summary>Minutes since the plant was fully grown, from two readings of the game's total minutes (never negative).</summary>
        public static int MinutesSince(int grownAtTotalMinutes, int nowTotalMinutes)
            => nowTotalMinutes > grownAtTotalMinutes ? nowTotalMinutes - grownAtTotalMinutes : 0;
    }
}
