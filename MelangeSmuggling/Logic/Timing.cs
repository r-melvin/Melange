namespace Melange.Smuggling
{
    /// <summary>
    /// The clock, in the game's own terms: absolute minutes are ElapsedDays * 1440 + minutes since midnight (what
    /// TimeManager.GetTotalMinSum returns), and times of day are 24-hour numbers (200 = 02:00). Everything compares with
    /// "has it passed", never "is it now", because sleeping jumps the clock (and it stands still at 04:00 until you do).
    /// </summary>
    public static class Timing
    {
        public const int MinutesPerDay = 1440;

        public static int MinuteOfDay(int hhmm)
        {
            if (hhmm < 0) hhmm = 0;
            int h = hhmm / 100, m = hhmm % 100;
            if (m > 59) m = 59;
            if (h > 23) h = 23;
            return h * 60 + m;
        }

        public static int At(int day, int hhmm) => day * MinutesPerDay + MinuteOfDay(hhmm);

        public static int DayOf(int absolute) => absolute < 0 ? -1 : absolute / MinutesPerDay;

        public static int HhmmOf(int absolute)
        {
            int m = ((absolute % MinutesPerDay) + MinutesPerDay) % MinutesPerDay;
            return (m / 60) * 100 + m % 60;
        }

        /// <summary>
        /// When an order posted on <paramref name="postedDay"/> sails. A departure time before noon belongs to the night
        /// after the deadline day (02:00 is "Tuesday night" to a player, though the game calls it Wednesday).
        /// </summary>
        public static int Departure(int postedDay, int leadDays, int departureHhmm)
        {
            if (leadDays < 0) leadDays = 0;
            int day = postedDay + leadDays + (MinuteOfDay(departureHhmm) < 12 * 60 ? 1 : 0);
            return At(day, departureHhmm);
        }

        /// <summary>The first <paramref name="hhmm"/> strictly after <paramref name="after"/>.</summary>
        public static int NextAfter(int after, int hhmm)
        {
            int candidate = At(DayOf(after), hhmm);
            return candidate > after ? candidate : candidate + MinutesPerDay;
        }

        /// <summary>The boat is back at the first return time at least <paramref name="minVoyage"/> minutes after it sails.</summary>
        public static int ReturnAfter(int departedAt, int returnHhmm, int minVoyage = 180)
            => NextAfter(departedAt + minVoyage - 1, returnHhmm);

        /// <summary>The day the next order is posted: <paramref name="intervalDays"/> after the departure day, never before tomorrow.</summary>
        public static int NextOrderDay(int departedAt, int intervalDays)
        {
            if (intervalDays < 1) intervalDays = 1;
            return DayOf(departedAt) + intervalDays;
        }

        /// <summary>The curfew (21:00-05:00, CurfewManager): police patrols and the quay are watched harder.</summary>
        public static bool IsCurfew(int hhmm)
        {
            int m = MinuteOfDay(hhmm);
            return m >= 21 * 60 || m < 5 * 60;
        }

        /// <summary>"02:00", for texts.</summary>
        public static string Clock(int hhmm)
        {
            int m = MinuteOfDay(hhmm);
            return $"{m / 60:00}:{m % 60:00}";
        }

        /// <summary>"5h 20m" or "40m"; "now" when it has passed.</summary>
        public static string Left(int minutes)
        {
            if (minutes <= 0) return "now";
            int h = minutes / 60, m = minutes % 60;
            return h > 0 ? (m > 0 ? $"{h}h {m}m" : $"{h}h") : $"{m}m";
        }
    }
}
