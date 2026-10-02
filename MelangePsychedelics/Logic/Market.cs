using System;

namespace Melange.Psychedelics
{
    /// <summary>
    /// Randy's Bait &amp; Tackle. By day his stall sells terrariums, crickets and nets; at night he stands round the back for a
    /// couple of hours and sells toads at a steep markup that climbs with each one sold that night, from a small stock.
    /// </summary>
    public static class RandysStall
    {
        public const int DayOpen = 7 * 60, DayClose = 19 * 60;
        public const int DefaultNightOpen = 22 * 60, DefaultNightHours = 2;
        public const float DefaultMarkup = 4f, StepPerSale = 0.15f;
        public const int MinStock = 2, MaxStock = 4;

        public static bool DayOpenAt(int minuteOfDay) => minuteOfDay >= DayOpen && minuteOfDay < DayClose;

        public static Window Night(int openAt, int hours) => new Window(((openAt % Clock.MinutesPerDay) + Clock.MinutesPerDay) % Clock.MinutesPerDay, Math.Max(1, Math.Min(8, hours)) * 60);

        /// <summary>
        /// The night now open, as the in-game day it opened (the part after midnight belongs to the evening before), or -1 when
        /// he isn't out.
        /// </summary>
        public static int NightOpen(int day, int minuteOfDay, Window night)
        {
            if (night.OpenSameDay(minuteOfDay)) return day;
            if (day > 0 && night.OpenNextDay(minuteOfDay)) return day - 1;
            return -1;
        }

        public static int Stock(long seed, int night) => Dice.Range(seed, night, 31, MinStock, MaxStock);

        /// <summary>The price of the next toad tonight: the base times the markup, plus 15% for each already sold tonight, whole dollars.</summary>
        public static float ToadPrice(float basePrice, float markup, int soldTonight)
            => (float)Math.Round(Math.Max(0f, basePrice) * Math.Max(1f, markup) * (1f + StepPerSale * Math.Max(0, soldTonight)));
    }
}
