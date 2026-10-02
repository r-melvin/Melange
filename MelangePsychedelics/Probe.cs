using System;
using System.Collections.Generic;
using Melange.Core;
using S1API.Console;

namespace Melange.Psychedelics
{
    /// <summary>
    /// The in-game probes (TESTING.md P2-P9): one console command, <c>psy &lt;what&gt;</c>, so each check can be typed or
    /// scripted (probe-cmds.txt, through S1API's ConsoleHelper.Submit) instead of walked. S1API finds and registers it. Every
    /// subcommand logs <c>PROBE &lt;what&gt;: &lt;result&gt;</c> to the Melange_Psychedelics logger and goes through the same
    /// code the prompts and the game's hooks use; nothing here is needed for play. Host only.
    /// </summary>
    public sealed class PsychedelicsCommand : BaseConsoleCommand
    {
        public override string CommandWord => "psy";
        public override string CommandDescription => "Melange Psychedelics probes: status, pond, catch [spot] [force], randy <terrarium|crickets|net|toad>, " +
                                                     "terra <place|add|milk|night [day]>, frame <place|add>, ana unlock, dose [design], trip <good|bad> [design]";
        public override string ExampleUsage => "psy status";

        public override void ExecuteCommand(List<string> args)
        {
            var words = new List<string>();
            if (args != null) foreach (var a in args) if (!string.IsNullOrWhiteSpace(a)) words.Add(a.Trim());
            string what = words.Count > 0 ? words[0].ToLowerInvariant() : "status";
            if (words.Count > 0) words.RemoveAt(0);
            string result;
            try { result = Probe.Run(what, words); }
            catch (Exception e) { result = "threw: " + e; }
            Mod.Log?.Msg($"PROBE {what}{(words.Count > 0 ? " " + string.Join(" ", words) : "")}: {result}");
        }
    }

    internal static class Probe
    {
        public static string Run(string what, List<string> args)
        {
            if (!Mod.Active) return "the spoke is off (Melange Core too old)";
            if (!Host.IsHost) return "host only";
            var data = MelangePsychedelicsData.Current;
            if (data == null || Il2CppScheduleOne.PlayerScripts.Player.Local == null) return "no save loaded";
            data.Normalise();
            string a0 = args.Count > 0 ? args[0].ToLowerInvariant() : null;
            switch (what)
            {
                case "status":
                    return Status(data);
                case "pond":
                    return Wild.ProbePondTime();
                case "catch":
                {
                    bool force = args.Exists(x => x.Equals("force", StringComparison.OrdinalIgnoreCase));
                    string spot = args.Find(x => !x.Equals("force", StringComparison.OrdinalIgnoreCase));
                    return Wild.ProbeCatch(spot, force);
                }
                case "randy":
                    return Stall.ProbeBuy(a0);
                case "terra":
                    switch (a0)
                    {
                        case "place": return Placed.PlaceOnGrid(Ids.Terrarium);
                        case "add": return Terraria.ProbeAdd();
                        case "milk": return Terraria.ProbeMilk();
                        case "night": return Terraria.ProbeNight(args.Count > 1 ? args[1] : null);
                        default: return "terra <place|add|milk|night [day]>";
                    }
                case "frame":
                    switch (a0)
                    {
                        case "place": return Placed.PlaceOnGrid(Ids.BlotterFrame);
                        case "add": return Frames.ProbeAdd();
                        default: return "frame <place|add>";
                    }
                case "ana":
                    return a0 == "unlock" ? Ana.ProbeUnlock() : Ana.ProbeStatus();
                case "dose":
                {
                    Design design = null;
                    if (a0 != null && (design = FindDesign(data, string.Join(" ", args))) == null) return $"no design '{string.Join(" ", args)}' ({DesignIds(data)})";
                    return Frames.ProbeDose(design);
                }
                case "trip":
                    return Trip(data, a0, args.Count > 1 ? string.Join(" ", args.GetRange(1, args.Count - 1)) : null);
                default:
                    return "unknown; psy status|pond|catch|randy|terra|frame|ana|dose|trip";
            }
        }

        private static string Status(MelangePsychedelicsData data)
        {
            int time = S1API.GameTime.TimeManager.CurrentTime;
            var designs = new List<string>();
            foreach (var d in data.Designs.Designs)
                designs.Add($"{d.Id} rep {d.Reputation:0} {DesignBook.StandingOf(d.Reputation)} ({d.GoodTrips} good/{d.BadTrips} bad, {d.SheetsPrinted} sheets{(d.Retired ? ", retired" : "")})");
            int tabs = 0, bad = 0;
            foreach (var b in data.Batches.Batches) { tabs += b.Tabs; if (b.Bad) bad++; }
            return $"day {Placed.Day} {time:D4} | {Wild.ProbeStatus()} | {Stall.ProbeStatus()} | {Terraria.ProbeStatus()} | {Frames.ProbeStatus()} | " +
                   $"designs: {string.Join(", ", designs)} | batches {data.Batches.Batches.Count} ({bad} bad, {tabs} tabs unaccounted) | {Ana.ProbeStatus()} | " +
                   $"pockets: net {Items.Count(Ids.ToadNet)}, toads {Items.Count(Ids.LiveToad)}, crickets {Items.Count(Ids.Crickets)}, cash {S1API.Money.Money.GetCashBalance():N0}";
        }

        /// <summary>
        /// A trip on a design as a sale would record it: put down to that design's oldest batch with tabs left (one tab off it;
        /// else its newest), at that batch's quality; the outcome is the one asked for. No customer, so no relationship change.
        /// </summary>
        private static string Trip(MelangePsychedelicsData data, string outcome, string designArg)
        {
            if (outcome != "good" && outcome != "bad") return "trip <good|bad> [design]";
            var design = designArg != null ? FindDesign(data, designArg) : Frames.NearestDesign();
            if (design == null && designArg == null && data.Batches.Batches.Count > 0)
                design = data.Designs.Find(data.Batches.Batches[data.Batches.Batches.Count - 1].DesignId);
            design ??= designArg == null ? data.Designs.Find(DesignBook.Presets[0].Id) : null;
            if (design == null) return $"no design '{designArg}' ({DesignIds(data)})";
            Batch batch = null;
            foreach (var b in data.Batches.Batches)
            {
                if (b.DesignId != design.Id) continue;
                if (b.Tabs > 0) { batch = b; break; }
                batch = b;
            }
            if (batch != null && batch.Tabs > 0) batch.Tabs--;
            int tier = batch?.Tier ?? Tier.Premium;
            bool good = outcome == "good";
            string note = batch == null ? " (no batch of this design: batch 0, Premium)" : batch.Bad == !good ? "" : $" (batch {batch.Id} was rolled {(batch.Bad ? "bad" : "good")})";
            return Brands.Trip(design, batch?.Id ?? 0, good, tier, null) + note + "; relationship change not applied (no customer)";
        }

        /// <summary>A design by ID ("preset:sunburst"), the part after the colon ("sunburst", "1" for painted:1) or name ("Third Eye").</summary>
        private static Design FindDesign(MelangePsychedelicsData data, string arg)
        {
            string want = Squash(arg);
            foreach (var d in data.Designs.Designs)
            {
                string id = d.Id ?? "";
                int colon = id.IndexOf(':');
                if (Squash(id) == want || (colon >= 0 && Squash(id.Substring(colon + 1)) == want) || Squash(d.Name) == want) return d;
            }
            return null;
        }

        private static string Squash(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s ?? "") if (char.IsLetterOrDigit(c) || c == ':') sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        private static string DesignIds(MelangePsychedelicsData data) => string.Join(", ", data.Designs.Designs.ConvertAll(d => d.Id));
    }
}
