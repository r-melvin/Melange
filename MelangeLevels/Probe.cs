using System;
using System.Collections.Generic;
using Melange.Core;
using S1API.Console;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Law;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.PlayerScripts;

namespace Melange.Levels
{
    /// <summary>
    /// The in-game probes (README, Testing): one console command, <c>levels &lt;what&gt;</c>, so the checks can be typed or
    /// scripted (probe-cmds.txt). S1API finds and registers it. Every subcommand logs <c>PROBE &lt;what&gt;: &lt;result&gt;</c> to
    /// the Melange_Levels logger and goes through the same code as play; nothing here is needed for play. Host only.
    /// </summary>
    public sealed class LevelsCommand : BaseConsoleCommand
    {
        public override string CommandWord => "levels";
        public override string CommandDescription => "Melange Levels probes: status, prestige <n>, offer <rush|warehouse|police>, cooldown <rush|warehouse|police|all>, pursuit <none|investigating|arresting>, curfew";
        public override string ExampleUsage => "levels status";

        public override void ExecuteCommand(List<string> args)
        {
            string what = args != null && args.Count > 0 ? args[0] : "status";
            string arg = args != null && args.Count > 1 ? args[1] : null;
            string result;
            try { result = LevelsProbe.Run(what, arg); }
            catch (Exception e) { result = "threw: " + e; }
            Mod.Log.Msg($"PROBE {what}{(arg != null ? " " + arg : "")}: {result}");
        }
    }

    internal static class LevelsProbe
    {
        private const string CurfewState = "DisobeyingCurfew";

        public static string Run(string what, string arg)
        {
            if (!Mod.Active) return "the mod is off";
            if (!Host.IsHost) return "host only";
            var data = MelangeLevelsData.Current;
            if (data == null) return "no save loaded (or no levels data)";
            switch (what)
            {
                case "status":
                    return Status(data);
                case "prestige":
                    int n = 1;
                    if (arg != null && !int.TryParse(arg, out n)) return "prestige <n>: not a number";
                    data.Prestige += n;
                    Events.Publish(new PrestigeChanged(data.Prestige));
                    return $"Prestige now {data.Prestige}";
                case "offer":
                    return Offer(data, arg);
                case "cooldown":
                    // a test bypass, not play: forgets when the offer was last bought so it can be bought again today
                    if (arg == "all") data.OffersLastUsed.Clear();
                    else if (Kind(arg) is OfferKind k) data.OffersLastUsed.Remove(k.ToString());
                    else return "cooldown <rush|warehouse|police|all>";
                    return "cleared; " + Cooldowns(data);
                case "pursuit":
                    return Pursuit(arg);
                case "curfew":
                    return Curfew();
                default:
                    return "unknown; try status, prestige <n>, offer <rush|warehouse|police>, cooldown <kind|all>, pursuit <none|investigating|arresting>, curfew";
            }
        }

        private static OfferKind? Kind(string arg) => arg switch
        {
            "rush" => OfferKind.RushOrder,
            "warehouse" => OfferKind.WarehouseDiscount,
            "police" => OfferKind.PoliceLookAway,
            _ => null,
        };

        private static string Status(MelangeLevelsData data)
        {
            var lm = NetworkSingleton<LevelManager>.Instance;
            string rank = lm != null ? $"{lm.Rank} {lm.Tier}" : "?";
            int placed = 0;
            var where = new List<string>();
            foreach (var kv in data.EmployeeSlotsPlaced) { placed += kv.Value; where.Add($"{kv.Key}+{kv.Value}"); }
            return $"day {OfferActions.Today()} {Clock(OfferActions.Now())}; rank {rank}; rewarded up to {data.RewardedUpTo / 1000}:{data.RewardedUpTo % 1000}; " +
                   $"slots earned {data.EmployeeSlotsEarned}, placed {placed} [{string.Join(", ", where)}]; candidates {data.UnderbossCandidates}; " +
                   $"discount step {data.DiscountStep} ({BulkDiscount.PercentFor(data.DiscountStep):0}%); Prestige {data.Prestige}; {Cooldowns(data)}; " +
                   $"warehouse day {data.WarehouseDiscountDay}; lenient until {Until(data.LenientUntil)}, police offer active {OfferActions.PoliceLenient()}; {Police()}";
        }

        private static string Cooldowns(MelangeLevelsData data)
        {
            int today = OfferActions.Today();
            var parts = new List<string>();
            foreach (OfferKind kind in Enum.GetValues(typeof(OfferKind)))
            {
                bool ok = Offers.Available(kind, data.Prestige, data.LastUsed(kind), today, out string why);
                parts.Add($"{kind} last day {data.LastUsed(kind)} {(ok ? "available" : why)}");
            }
            return "offers [" + string.Join(", ", parts) + "]";
        }

        /// <summary>Buys an offer the way the Connections app's button does (the same action, so the same spend check).</summary>
        private static string Offer(MelangeLevelsData data, string arg)
        {
            int before = data.Prestige;
            bool ok;
            switch (Kind(arg))
            {
                case OfferKind.RushOrder:
                    var drops = OfferActions.PendingDrops();
                    if (drops.Count == 0) return "no dead drop on its way (the app shows no button)";
                    ok = OfferActions.RushOrder(drops[0]);
                    break;
                case OfferKind.WarehouseDiscount:
                    ok = OfferActions.WarehouseDiscount();
                    break;
                case OfferKind.PoliceLookAway:
                    if (OfferActions.PoliceLenient()) return "already looking away (the app shows no button)";
                    ok = OfferActions.PoliceLookAway();
                    break;
                default:
                    return "offer <rush|warehouse|police>";
            }
            return $"{(ok ? "bought" : "refused")}; Prestige {before} -> {data.Prestige}; {Cooldowns(data)}; police offer active {OfferActions.PoliceLenient()}; {Police()}";
        }

        private static string Pursuit(string arg)
        {
            var level = arg switch
            {
                "none" => PlayerCrimeData.EPursuitLevel.None,
                "investigating" => PlayerCrimeData.EPursuitLevel.Investigating,
                "arresting" => PlayerCrimeData.EPursuitLevel.Arresting,
                _ => (PlayerCrimeData.EPursuitLevel?)null,
            };
            if (level == null) return "pursuit <none|investigating|arresting>";
            var crime = Player.Local != null ? Player.Local.CrimeData : null;
            if (crime == null) return "no local player";
            var was = crime.CurrentPursuitLevel;
            crime.SetPursuitLevel(level.Value);
            return $"{was} -> {crime.CurrentPursuitLevel}";
        }

        private static string Curfew()
        {
            var c = NetworkSingleton<CurfewManager>.Instance;
            string mgr = c == null ? "no CurfewManager" : $"enabled {c.IsEnabled}, active {c.IsCurrentlyActive}, hard {c.IsHardCurfewActive}";
            return $"{Clock(OfferActions.Now())}; {mgr}; {CurfewState} state present {HasCurfewState()}; police offer active {OfferActions.PoliceLenient()}";
        }

        private static string Police()
        {
            var me = Player.Local;
            var crime = me != null ? me.CrimeData : null;
            if (crime == null) return "no local player";
            return $"pursuit {crime.CurrentPursuitLevel}, since last body search {crime.TimeSinceLastBodySearch:0.0}s, {CurfewState} present {HasCurfewState()}";
        }

        private static bool HasCurfewState()
        {
            var seen = Player.Local != null ? Player.Local.VisualState : null;
            return seen != null && seen.GetState(CurfewState) != null;
        }

        private static string Clock(int hhmm) => $"{hhmm / 100:00}:{hhmm % 100:00}";

        private static string Until(int absMinute)
            => absMinute < 0 ? "never" : $"day {absMinute / 1440} {absMinute % 1440 / 60:00}:{absMinute % 60:00}";
    }
}
