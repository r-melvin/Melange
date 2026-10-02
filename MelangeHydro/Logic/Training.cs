namespace Melange.Hydro
{
    public enum TrainingLevel
    {
        None = 0,
        /// <summary>Operates Hydro Trays; up to 16 pots.</summary>
        Hydroponics = 1,
        /// <summary>Operates Aeroponic Towers too; up to 24 pots. Needs Hydroponics first.</summary>
        Aeroponics = 2,
    }

    /// <summary>
    /// Training an employed botanist (decided with the user): a dialogue option pays for it, and only trained botanists may be
    /// assigned hydro or aero holes. Priced as multiples of what Manny would charge for a botanist right now, meant as small
    /// change by the late ranks.
    /// </summary>
    public static class Training
    {
        public const float HydroponicsPriceMultiple = 2f, AeroponicsPriceMultiple = 5f;
        public const int HydroponicsPotLimit = 16, AeroponicsPotLimit = 24;
        /// <summary>The game's own limit (Botanist.MaxAssignedPots on the prefab).</summary>
        public const int VanillaPotLimit = 8;

        /// <summary>
        /// The game's extra signing fee (Fixer.GetAdditionalSigningFee): +100 for each of the first six employees ever
        /// recruited, +250 for each after, capped at +500 in all. The live game value is used when it can be read; this is the
        /// same sum, for when it can't and for the tests.
        /// </summary>
        public static float AdditionalSigningFee(int lifetimeRecruited)
        {
            float fee = 0f;
            for (int i = 0; i < lifetimeRecruited; i++) fee += i > 5 ? 250f : 100f;
            return fee < 500f ? fee : 500f;
        }

        /// <summary>What Manny would charge to hire a botanist now: the botanist's own signing fee plus the extra fee.</summary>
        public static float HirePrice(float baseSigningFee, float additionalFee) => baseSigningFee + additionalFee;

        public static float Price(TrainingLevel level, float hirePrice)
        {
            switch (level)
            {
                case TrainingLevel.Hydroponics: return hirePrice * HydroponicsPriceMultiple;
                case TrainingLevel.Aeroponics: return hirePrice * AeroponicsPriceMultiple;
                default: return 0f;
            }
        }

        /// <summary>
        /// A botanist's pot limit at a training level. Never below what he already had: another mod (EmployeeTweaks, Lithium)
        /// may have raised the field past ours, and training must not lower it.
        /// </summary>
        public static int PotLimit(TrainingLevel level, int ownLimit)
        {
            int trained = level == TrainingLevel.Aeroponics ? AeroponicsPotLimit : level == TrainingLevel.Hydroponics ? HydroponicsPotLimit : 0;
            return ownLimit > trained ? ownLimit : trained;
        }

        /// <summary>Whether a botanist at <paramref name="level"/> may be assigned a hole of this kind (vanilla pots: always).</summary>
        public static bool CanOperate(TrainingLevel level, HoleKind kind)
        {
            switch (kind)
            {
                case HoleKind.Hydro: return level >= TrainingLevel.Hydroponics;
                case HoleKind.Aero: return level >= TrainingLevel.Aeroponics;
                default: return true;
            }
        }

        /// <summary>The next course for a botanist, or None when he has had both.</summary>
        public static TrainingLevel Next(TrainingLevel current)
            => current == TrainingLevel.None ? TrainingLevel.Hydroponics : current == TrainingLevel.Hydroponics ? TrainingLevel.Aeroponics : TrainingLevel.None;

        /// <summary>Whether a course can be offered now: unlocked by rank, the one before it done, not had already.</summary>
        public static bool CanOffer(TrainingLevel current, TrainingLevel course, bool hydroUnlocked, bool aeroUnlocked, out string reason)
        {
            reason = null;
            if (course == TrainingLevel.None) { reason = "no such course"; return false; }
            if (current >= course) { reason = "already trained"; return false; }
            if (course == TrainingLevel.Hydroponics && !hydroUnlocked) { reason = "unlocks at Underlord III"; return false; }
            if (course == TrainingLevel.Aeroponics)
            {
                if (!aeroUnlocked) { reason = "unlocks at Baron III"; return false; }
                if (current < TrainingLevel.Hydroponics) { reason = "needs hydroponics first"; return false; }
            }
            return true;
        }

        public static string Describe(TrainingLevel level)
            => level == TrainingLevel.Aeroponics ? "aeroponics" : level == TrainingLevel.Hydroponics ? "hydroponics" : "none";
    }
}
