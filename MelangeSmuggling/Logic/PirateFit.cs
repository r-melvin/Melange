using System;
using System.Globalization;

namespace Melange.Smuggling
{
    /// <summary>
    /// How Dafydd's tricorn and eyepatch (scripts/art/build_pirate.py) are fitted to his head. The models are drawn for eyes
    /// <see cref="ModelEyeSpacing"/> apart; at runtime the distance between his eyeballs scales them, and the hat sits a
    /// number of eye spacings above and behind the point between his eyes (in his facing frame: right, up, forward).
    /// </summary>
    public static class PirateFit
    {
        public const double ModelEyeSpacing = 0.066, MinScale = 0.6, MaxScale = 1.8;
        /// <summary>The hat's origin (the crown's inner rim, centred) from the point between the eyes, in eye spacings.</summary>
        public const double HatUp = 1.1, HatBack = 1.35;
        /// <summary>With no eyes found: the point between the eyes from the head bone, in metres (a guess at a human head).</summary>
        public const double EyesAboveHeadBone = 0.09, EyesBeforeHeadBone = 0.08;

        /// <summary>The models' scale for eyes this far apart: 1 for an unusable measurement, else clamped to a sane range.</summary>
        public static double Scale(double eyeSpacing)
        {
            if (double.IsNaN(eyeSpacing) || double.IsInfinity(eyeSpacing) || eyeSpacing <= 0) return 1.0;
            return Math.Max(MinScale, Math.Min(MaxScale, eyeSpacing / ModelEyeSpacing));
        }

        /// <summary>A user's scale setting: positive and finite, else 1.</summary>
        public static double UserScale(double s) => double.IsNaN(s) || double.IsInfinity(s) || s <= 0 ? 1.0 : Math.Min(3.0, s);

        /// <summary>"x,y,z" in metres (invariant culture); empty or malformed is zero, so a typo only loses the nudge.</summary>
        public static (double X, double Y, double Z) ParseOffset(string text)
        {
            var parts = (text ?? "").Split(',');
            if (parts.Length != 3) return (0, 0, 0);
            var v = new double[3];
            for (int i = 0; i < 3; i++)
                if (!double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]) || double.IsNaN(v[i]) || Math.Abs(v[i]) > 1.0)
                    return (0, 0, 0);
            return (v[0], v[1], v[2]);
        }
    }
}
