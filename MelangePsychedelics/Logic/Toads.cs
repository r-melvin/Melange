using System;
using System.Collections.Generic;

namespace Melange.Psychedelics
{
    /// <summary>Where a toad came from. It sets the best venom it can give (see <see cref="Venom"/>).</summary>
    public enum ToadOrigin
    {
        /// <summary>Bought round the back of Randy's: stressed, kept in a box all day.</summary>
        BlackMarket = 0,
        /// <summary>Born in one of the player's terrariums: steady, never wild-quality.</summary>
        Bred = 1,
        /// <summary>Caught at the pond.</summary>
        Wild = 2,
        /// <summary>Caught in the sewer: the best stock in town.</summary>
        Sewer = 3,
    }

    /// <summary>One toad living in a terrarium. Plain fields so the save writes it as it is.</summary>
    public sealed class Toad
    {
        public int Id;
        public ToadOrigin Origin;
        /// <summary>Times milked so far; at the setting's limit the toad shrivels to dust.</summary>
        public int Milkings;
        /// <summary>The in-game day it was last milked (-1 never), so it gives venom once a day.</summary>
        public int LastMilkedDay = -1;
        /// <summary>Days in a row it has been fed. Good care raises its venom.</summary>
        public int CareStreak;
        /// <summary>Days in a row it has gone without food. Too many and it dies.</summary>
        public int HungryDays;
        /// <summary>Whether it ate at the last daily feed.</summary>
        public bool FedToday;
    }

    /// <summary>One terrarium's live roster. The game saves the terrarium's storage slots (crickets, venom); the toads are ours.</summary>
    public sealed class Terrarium
    {
        public List<Toad> Toads = new List<Toad>();
        /// <summary>Days a fed pair has been together towards the next birth.</summary>
        public int BreedProgress;
        public int NextId = 1;
        /// <summary>The last in-game day fed, so a repeated day event (a reload, two subscribers) doesn't feed twice.</summary>
        public int LastFedDay = -1;

        public int Count => Toads.Count;

        /// <summary>Puts a toad in, if there is room. Returns false when full.</summary>
        public bool Release(ToadOrigin origin, TerrariumRules rules)
        {
            if (Toads.Count >= rules.Capacity) return false;
            Toads.Add(new Toad { Id = NextId++, Origin = origin });
            return true;
        }
    }

    /// <summary>The terrarium's numbers, from the settings (sanitised so a bad value can't break a save).</summary>
    public sealed class TerrariumRules
    {
        public const int DefaultCapacity = 6, DefaultMilkingsBeforeDust = 10, DefaultFeedPerToad = 1, DefaultBreedDays = 3, DefaultStarveDays = 4;

        public int Capacity = DefaultCapacity;
        public int MilkingsBeforeDust = DefaultMilkingsBeforeDust;
        /// <summary>Cricket portions one toad eats a day.</summary>
        public int FeedPerToad = DefaultFeedPerToad;
        /// <summary>Days a fed pair takes to produce a toadlet.</summary>
        public int BreedDays = DefaultBreedDays;
        /// <summary>Days without food before a toad dies.</summary>
        public int StarveDays = DefaultStarveDays;
        /// <summary>The Sewer King taught the player (he was spared): better care and quicker breeding.</summary>
        public bool Mentor;

        public static TerrariumRules Sanitised(int capacity, int milkingsBeforeDust, int feedPerToad, int breedDays, int starveDays, bool mentor)
            => new TerrariumRules
            {
                Capacity = Math.Max(2, Math.Min(24, capacity)),
                MilkingsBeforeDust = Math.Max(1, Math.Min(100, milkingsBeforeDust)),
                FeedPerToad = Math.Max(0, Math.Min(10, feedPerToad)),
                BreedDays = Math.Max(1, Math.Min(30, breedDays)),
                StarveDays = Math.Max(1, Math.Min(30, starveDays)),
                Mentor = mentor,
            };

        /// <summary>Breeding days after the mentor's tip (a day quicker, never under one).</summary>
        public int EffectiveBreedDays => Mentor ? Math.Max(1, BreedDays - 1) : BreedDays;
    }

    public sealed class FeedReport
    {
        public int FeedEaten, Fed, Hungry, Starved, Born;
        public bool Skipped;
    }

    public sealed class MilkReport
    {
        /// <summary>One venom unit per toad milked, by quality tier.</summary>
        public List<int> Tiers = new List<int>();
        public int AlreadyMilked, Dust;
    }

    /// <summary>The terrarium's day and its milking. Pure, run on the host only.</summary>
    public static class Husbandry
    {
        public const int MaxCareStreak = 30;

        /// <summary>
        /// The daily feed, once per in-game day: each toad eats its portion while the crickets last (in roster order, so the
        /// longest-kept toads eat first); unfed toads lose their care streak and die after enough hungry days. A pair or more
        /// of fed toads with room to spare moves breeding on a day and gives a toadlet when it's due; fewer than two fed toads
        /// start breeding over, a full terrarium only waits.
        /// </summary>
        public static FeedReport Feed(Terrarium t, int feedAvailable, int day, TerrariumRules rules)
        {
            var r = new FeedReport();
            if (t.LastFedDay == day) { r.Skipped = true; return r; }
            t.LastFedDay = day;
            int feed = Math.Max(0, feedAvailable);
            var starved = new List<Toad>();
            foreach (var toad in t.Toads)
            {
                if (feed >= rules.FeedPerToad)
                {
                    feed -= rules.FeedPerToad;
                    r.FeedEaten += rules.FeedPerToad;
                    toad.FedToday = true;
                    toad.HungryDays = 0;
                    toad.CareStreak = Math.Min(MaxCareStreak, toad.CareStreak + 1);
                    r.Fed++;
                }
                else
                {
                    toad.FedToday = false;
                    toad.CareStreak = 0;
                    toad.HungryDays++;
                    r.Hungry++;
                    if (toad.HungryDays >= rules.StarveDays) starved.Add(toad);
                }
            }
            foreach (var s in starved) t.Toads.Remove(s);
            r.Starved = starved.Count;

            // the starved were all unfed, so r.Fed counts only toads still alive
            if (r.Fed < 2) t.BreedProgress = 0;
            else if (t.Toads.Count >= rules.Capacity) t.BreedProgress = Math.Min(t.BreedProgress, rules.EffectiveBreedDays - 1);
            else if (++t.BreedProgress >= rules.EffectiveBreedDays)
            {
                t.BreedProgress = 0;
                t.Toads.Add(new Toad { Id = t.NextId++, Origin = ToadOrigin.Bred });
                r.Born = 1;
            }
            return r;
        }

        /// <summary>
        /// Milks every toad not yet milked today: one venom unit each at its quality. A toad that reaches the milking limit gives
        /// its last venom and shrivels to dust.
        /// </summary>
        public static MilkReport Milk(Terrarium t, int day, TerrariumRules rules)
        {
            var r = new MilkReport();
            var dust = new List<Toad>();
            foreach (var toad in t.Toads)
            {
                if (toad.LastMilkedDay == day) { r.AlreadyMilked++; continue; }
                r.Tiers.Add(Venom.TierOf(toad, rules.Mentor));
                toad.LastMilkedDay = day;
                toad.Milkings++;
                if (toad.Milkings >= rules.MilkingsBeforeDust) dust.Add(toad);
            }
            foreach (var d in dust) t.Toads.Remove(d);
            r.Dust = dust.Count;
            return r;
        }

        /// <summary>Toads that can be milked today.</summary>
        public static int Ready(Terrarium t, int day)
        {
            int n = 0;
            foreach (var toad in t.Toads) if (toad.LastMilkedDay != day) n++;
            return n;
        }
    }

    /// <summary>
    /// Venom quality: each origin has a starting tier and a ceiling; a toad fed several days running gives a tier more, a
    /// hungry one a tier less, and a wild-caught toad gives a tier less until it has settled (two fed days). Captive-bred
    /// toads don't suffer the capture stress: steadier, but never above Premium without the mentor. The drying rack then
    /// cures the venom up a tier per half day, as it does shrooms.
    /// </summary>
    public static class Venom
    {
        public const int SettleDays = 2, WellKeptDays = 5, MentorWellKeptDays = 3;

        public static int BaseTier(ToadOrigin o) => o switch
        {
            ToadOrigin.BlackMarket => Tier.Poor,
            ToadOrigin.Bred => Tier.Standard,
            ToadOrigin.Wild => Tier.Premium,
            _ => Tier.Premium,
        };

        public static int Ceiling(ToadOrigin o, bool mentor) => Tier.Clamp(o switch
        {
            ToadOrigin.BlackMarket => Tier.Standard,
            ToadOrigin.Bred => Tier.Premium,
            ToadOrigin.Wild => Tier.Premium,
            _ => Tier.Heavenly,
        } + (mentor ? 1 : 0));

        public static int TierOf(Toad toad, bool mentor) => TierOf(toad.Origin, toad.CareStreak, toad.FedToday, mentor);

        public static int TierOf(ToadOrigin origin, int careStreak, bool fedToday, bool mentor)
        {
            int t = BaseTier(origin);
            if (careStreak >= (mentor ? MentorWellKeptDays : WellKeptDays)) t++;
            t = Math.Min(t, Ceiling(origin, mentor));          // the ceiling caps the bonus; the penalties below always bite
            if (!fedToday) t--;
            bool caught = origin == ToadOrigin.Wild || origin == ToadOrigin.Sewer || origin == ToadOrigin.BlackMarket;
            if (caught && careStreak < SettleDays) t--;
            return Tier.Clamp(t);
        }
    }

    /// <summary>
    /// A toad carried in the inventory is a quality item; the quality says where it came from, so the origin survives being
    /// carried, stored and traded without extra item data.
    /// </summary>
    public static class LiveToads
    {
        public static int ItemTier(ToadOrigin o) => o switch
        {
            ToadOrigin.BlackMarket => Tier.Poor,
            ToadOrigin.Bred => Tier.Standard,
            ToadOrigin.Wild => Tier.Premium,
            _ => Tier.Heavenly,
        };

        public static ToadOrigin OriginOf(int itemTier) => Tier.Clamp(itemTier) switch
        {
            Tier.Trash => ToadOrigin.BlackMarket,
            Tier.Poor => ToadOrigin.BlackMarket,
            Tier.Standard => ToadOrigin.Bred,
            Tier.Premium => ToadOrigin.Wild,
            _ => ToadOrigin.Sewer,
        };
    }
}
