using System;
using System.Collections.Generic;

namespace Melange.Psychedelics
{
    /// <summary>The game's clock: an int like 1930 for 7:30 pm. The day turns over at midnight (TimeManager: 2359 -> 0).</summary>
    public static class Clock
    {
        public const int MinutesPerDay = 1440;
        public static int ToMinutes(int hhmm) => Math.Max(0, Math.Min(23, hhmm / 100)) * 60 + Math.Max(0, Math.Min(59, hhmm % 100));
        public static int ToHhmm(int minutes) { minutes = ((minutes % MinutesPerDay) + MinutesPerDay) % MinutesPerDay; return minutes / 60 * 100 + minutes % 60; }
    }

    /// <summary>
    /// A daily window, opening at a minute of the day and lasting some minutes, possibly past midnight. Because the game's day
    /// turns over at midnight, the part after midnight belongs to the previous day's window.
    /// </summary>
    public readonly struct Window
    {
        public int OpenAt { get; }
        public int Minutes { get; }
        public Window(int openAt, int minutes) { OpenAt = openAt; Minutes = Math.Max(0, minutes); }

        /// <summary>The part on the opening day.</summary>
        public bool OpenSameDay(int minute) => minute >= OpenAt && minute < OpenAt + Minutes;
        /// <summary>The part after midnight, asked on the following day.</summary>
        public bool OpenNextDay(int minute) => OpenAt + Minutes > Clock.MinutesPerDay && minute < OpenAt + Minutes - Clock.MinutesPerDay;
        public int MinutesInto(int minute, bool nextDay) => nextDay ? minute + Clock.MinutesPerDay - OpenAt : minute - OpenAt;
    }

    /// <summary>One day at the pond: when the toads are out, how many, and on which spots.</summary>
    public sealed class PondDay
    {
        public int Day;
        public Window Window;
        /// <summary>Indices into the pond's spot list, distinct.</summary>
        public List<int> Spots = new List<int>();
    }

    /// <summary>
    /// When wild toads come out at the pond. Toads are night animals: the window opens between dusk and late evening, for two
    /// to four hours, with three to six toads. Rolled from the save's seed and the day, so every player in a co-op game sees
    /// the same pond without it being sent over the network.
    /// </summary>
    public static class PondSchedule
    {
        public const int EarliestOpen = 19 * 60, LatestOpen = 22 * 60, MinMinutes = 120, MaxMinutes = 240, MinToads = 3, MaxToads = 6;
        private const int SaltOpen = 11, SaltLength = 12, SaltCount = 13, SaltSpots = 14;

        public static PondDay For(long seed, int day, int spotCount)
        {
            int open = EarliestOpen + 15 * Dice.Range(seed, day, SaltOpen, 0, (LatestOpen - EarliestOpen) / 15);
            int minutes = 30 * Dice.Range(seed, day, SaltLength, MinMinutes / 30, MaxMinutes / 30);
            var d = new PondDay { Day = day, Window = new Window(open, minutes) };
            int n = Math.Min(Math.Max(0, spotCount), Dice.Range(seed, day, SaltCount, MinToads, MaxToads));
            d.Spots = Pick(seed, day, SaltSpots, n, spotCount);
            return d;
        }

        /// <summary>The window open now, today's or last night's running past midnight; null when the toads are in.</summary>
        public static PondDay OpenNow(long seed, int day, int minuteOfDay, int spotCount, out int minutesInto)
        {
            var today = For(seed, day, spotCount);
            if (today.Window.OpenSameDay(minuteOfDay)) { minutesInto = today.Window.MinutesInto(minuteOfDay, false); return today; }
            if (day > 0)
            {
                var yesterday = For(seed, day - 1, spotCount);
                if (yesterday.Window.OpenNextDay(minuteOfDay)) { minutesInto = yesterday.Window.MinutesInto(minuteOfDay, true); return yesterday; }
            }
            minutesInto = 0;
            return null;
        }

        /// <summary>n distinct indices from 0..count-1, deterministic (a partial Fisher-Yates on the dice).</summary>
        public static List<int> Pick(long seed, int day, long salt, int n, int count)
        {
            var all = new List<int>();
            for (int i = 0; i < count; i++) all.Add(i);
            var picked = new List<int>();
            for (int i = 0; i < n && i < count; i++)
            {
                int j = Dice.Range(seed, day, salt * 1000 + i, i, count - 1);
                (all[i], all[j]) = (all[j], all[i]);
                picked.Add(all[i]);
            }
            return picked;
        }
    }

    /// <summary>
    /// The wildlife officer's round while the toads are out: he walks the ring of spots, one lap per <see cref="LapMinutes"/>,
    /// and sees the spot he is at and its neighbours. Catching a toad on a spot he can see is an offence.
    /// </summary>
    public static class WildlifeOfficer
    {
        public const int LapMinutes = 40, Sight = 1;

        /// <summary>The spot index he is at, minutes into the window.</summary>
        public static int At(int minutesInto, int spotCount)
        {
            if (spotCount <= 0) return -1;
            int m = ((minutesInto % LapMinutes) + LapMinutes) % LapMinutes;
            return Math.Min(spotCount - 1, m * spotCount / LapMinutes);
        }

        public static bool Sees(int spot, int minutesInto, int spotCount)
        {
            int at = At(minutesInto, spotCount);
            if (at < 0) return false;
            int d = Math.Abs(spot - at);
            d = Math.Min(d, spotCount - d);                 // the spots are a ring round the pond
            return d <= Sight;
        }
    }

    /// <summary>What getting caught costs: a fine that grows with each offence that night, and a police look-in from the third.</summary>
    public static class Penalty
    {
        public const float BaseFine = 250f, MaxFine = 1000f;
        public const int PursuitFrom = 3;

        public static float Fine(int offence) => Math.Min(MaxFine, BaseFine * Math.Max(1, offence));
        public static bool Pursuit(int offence) => offence >= PursuitFrom;
    }

    /// <summary>How the player gets at the sewer's toads.</summary>
    public enum SewerRoute
    {
        /// <summary>Nobody has pointed the player at them.</summary>
        Closed = 0,
        /// <summary>The hard way: the King's dead (his journal), the goblin calmed, or Randy's tip once the sewer is open.</summary>
        Hard = 1,
        /// <summary>The spared Sewer King showed the player the spot: more toads, and he teaches care.</summary>
        Mentor = 2,
    }

    public static class SewerToads
    {
        public static SewerRoute Route(bool kingSpared, bool kingDefeated, bool goblinCalmed, bool sewerUnlocked)
        {
            if (kingSpared) return SewerRoute.Mentor;
            if (kingDefeated || goblinCalmed || sewerUnlocked) return SewerRoute.Hard;
            return SewerRoute.Closed;
        }

        /// <summary>Toads in the sewer today: two or three with the King's spot, on the hard route one, and only every other day or so.</summary>
        public static int Count(SewerRoute route, long seed, int day)
        {
            switch (route)
            {
                case SewerRoute.Mentor: return Dice.Range(seed, day, 21, 2, 3);
                case SewerRoute.Hard: return Dice.Unit(seed, day, 22) < 0.5 ? 1 : 0;
                default: return 0;
            }
        }
    }
}
