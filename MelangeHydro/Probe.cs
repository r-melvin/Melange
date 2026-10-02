using System;
using System.Collections.Generic;
using Melange.Core;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.Growing;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI.Management;
using S1API.Console;
using UnityEngine;
using Il2CppList = Il2CppSystem.Collections.Generic;
using PropertyType = Il2CppScheduleOne.Property.Property;

namespace Melange.Hydro
{
    /// <summary>
    /// The in-game probes (TESTING.md P3-P10, P14): one console command, <c>hydro &lt;what&gt;</c>, so sowing, growing,
    /// harvesting, curing and the botanist's training and bulk assignment can be typed or scripted (probe-cmds.txt, through
    /// S1API's ConsoleHelper.Submit) instead of waited for. S1API finds and registers it. Every subcommand logs
    /// <c>PROBE &lt;what&gt;: &lt;result&gt;</c> to the Melange_Hydro logger and goes through the game's own methods (the
    /// pot's per-minute update, the sow task's PlantSeed_Server, each bud's Harvest, Manny's hire) and the spoke's own (the
    /// training choice, the clipboard's fill-all), so the spoke's patches run as in play. Nothing here is needed for play.
    /// Host only.
    /// </summary>
    public sealed class HydroCommand : BaseConsoleCommand
    {
        public override string CommandWord => "hydro";
        public override string CommandDescription => "Melange Hydro probes: status, place <hydro|aero|pump>, sow [seed id], grow <hours>, harvest [hydro|aero], cure <hours>, " +
                                                     "botanist [hire], train <16|24>, assign all";
        public override string ExampleUsage => "hydro status";

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
        public const string DefaultSeed = "ogkushseed";

        public static string Run(string what, List<string> args)
        {
            if (!Mod.Active) return "the spoke is off (Melange Core too old)";
            if (!Host.IsHost) return "host only";
            if (Player.Local == null || PropertyType.OwnedProperties == null || MelangeHydroData.Current == null) return "no save loaded";
            string a0 = args.Count > 0 ? args[0].ToLowerInvariant() : null;
            switch (what)
            {
                case "status": return Status();
                case "place":
                {
                    string id = a0 == "hydro" ? Units.HydroSection.Id : a0 == "aero" ? Units.AeroSection.Id : a0 == "pump" ? Units.Pump.Id : null;
                    return id == null ? "place <hydro|aero|pump>" : PlaceOnGrid(id);
                }
                case "sow": return Sow(args.Count > 0 ? args[0] : DefaultSeed);
                case "vanilla": return Vanilla(a0, args.Count > 1 ? args[1] : DefaultSeed);
                case "grow": return float.TryParse(a0, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float gh) && gh > 0f ? Grow(gh) : "grow <hours>";
                case "harvest": return a0 == null || a0 == "hydro" || a0 == "aero" ? Harvest(a0 == "hydro" ? HoleKind.Hydro : a0 == "aero" ? HoleKind.Aero : HoleKind.None) : "harvest [hydro|aero]";
                case "cure": return float.TryParse(a0, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ch) && ch > 0f ? Cure(ch) : "cure <hours>";
                case "botanist": return a0 == "hire" ? Hire(Site(), EEmployeeType.Botanist) : BotanistReport();
                case "train":
                    return a0 == "16" ? Train(TrainingLevel.Hydroponics) : a0 == "24" ? Train(TrainingLevel.Aeroponics) : "train <16|24> (16 hydroponics, 24 aeroponics)";
                case "assign": return a0 == "all" ? AssignAll() : "assign all";
                default: return "unknown; hydro status|place|sow|grow|harvest|cure|botanist|train|assign";
            }
        }

        // ------------------------------------------------------------------ status

        private static string Status()
        {
            var parts = new List<string>();
            var sections = new List<Hole>(Holes.All);
            parts.Add($"{sections.Count} hole(s)");
            foreach (var h in sections) if (h.Alive) parts.Add(DescribeHole(h));
            parts.Add(Pumps.Describe());
            var bots = new List<string>();
            foreach (var b in Courses.Botanists()) bots.Add(DescribeBotanist(b));
            parts.Add(bots.Count == 0 ? "no botanists" : string.Join("; ", bots));
            return string.Join(" | ", parts);
        }

        private static string DescribeHole(Hole h)
        {
            var pot = h.Pot;
            int index = Grouping.IndexInFrame(h, out var f);
            string frame = index >= 0 && f != null ? $" (hole {index} of frame {f.Guid})" : "";
            float light = pot.GetAverageLightExposure(out float lightMult);
            float temp = pot.GetTemperatureGrowthMultiplier();
            string soil = pot.CurrentSoil == null ? "no medium" : $"{pot.CurrentSoil.ID} {pot.NormalizedSoilAmount:P0}, {pot._remainingSoilUses} use(s) left";
            string plant = "empty";
            var p = pot.Plant;
            if (p != null)
            {
                string cure = Plants.Describe(p);
                plant = $"{p.SeedDefinition?.ID} {p.NormalizedGrowthProgress:P1} grown, quality {p.QualityLevel:0.###} ({ItemQuality.GetQuality(p.QualityLevel)}), " +
                        $"buds {p.ActiveHarvestables?.Count ?? 0} of {p.FinalGrowthStage?.GrowthSites?.Length ?? 0} sites, yield x{p.YieldMultiplier:0.###}" +
                        (cure != null ? $", curing {cure}" : "");
            }
            var additives = new List<string>();
            var applied = pot.AppliedAdditives;
            for (int i = 0; applied != null && i < applied.Count; i++) if (applied[i] != null) additives.Add(applied[i].ID);
            return $"{h.Unit.Name} {h.Guid}{frame} at {pot.ParentProperty?.PropertyCode}: {plant}; water {pot._currentMoistureAmount:0.##}/{pot.MoistureCapacity} " +
                   $"({pot.NormalizedMoistureAmount:P0}, drain {pot._moistureDrainPerHour:0.###}/h); {soil}; light {light:0.##} x{lightMult:0.##}; " +
                   $"speed: temperature x{temp:0.###} (hole factor {Settings.Speed(h.Kind):0.##} inside), pot x{pot.GrowSpeedMultiplier:0.###}, total x{temp * light * lightMult * pot.GrowSpeedMultiplier:0.###}; " +
                   $"pot yield x{pot.YieldMultiplier:0.###}{(additives.Count > 0 ? "; additives " + string.Join(", ", additives) : "")}";
        }

        private static string DescribeBotanist(Botanist b)
        {
            var assigns = b.configuration?.Assigns;
            int holes = 0, n = assigns?.SelectedObjects?.Count ?? 0;
            for (int i = 0; i < n; i++) if (Holes.Get(assigns.SelectedObjects[i]?.TryCast<Pot>()) != null) holes++;
            return $"{b.FullName} ({b.GUID}) at {b.AssignedProperty?.PropertyCode}: training {Training.Describe(Courses.Level(b))}, pot limit {b.MaxAssignedPots}, " +
                   $"clipboard {n}/{assigns?.MaxItems} ({holes} hole(s)), hire price now ${Courses.HirePrice(b):N0}";
        }

        // ------------------------------------------------------------------ sow, grow, harvest, cure

        /// <summary>
        /// Sows the first empty hole as the sow task's success does (SowSeedTask.Success: the seed out of the pockets,
        /// Pot.PlantSeed_Server, the soil packed), after the pot's own CanAcceptSeed check.
        /// </summary>
        /// <summary>A vanilla Grow Tent beside the holes, for comparison: place, sow, harvest (bud by bud, as the player's clicks).</summary>
        private static string Vanilla(string step, string seedId)
        {
            if (step == "place") return PlaceOnGrid("growtent");
            Pot pot = null;
            foreach (var p in UnityEngine.Object.FindObjectsOfType<Pot>())
            {
                if (p == null || Holes.Get(p) != null) continue;
                if (step == "sow" && p.Plant == null && p.CurrentSoil == null)
                {
                    var soil = Il2CppScheduleOne.Registry.GetItem("soil")?.TryCast<SoilDefinition>();   // plain soil, as a player would pour
                    if (soil != null) { p.SetSoil(soil); p.SetSoilAmount(p.SoilCapacity); p.SetRemainingSoilUses(1); p.SyncSoilData(); p.SetMoistureAmount(p.MoistureCapacity); p.SyncMoistureData(); }
                }
                if (step == "sow" ? p.Plant == null && p.CanAcceptSeed(out _) : p.Plant != null && p.IsReadyForHarvest(out _)) { pot = p; break; }
            }
            if (pot == null) return $"no vanilla pot ready to {step}";
            if (step == "sow") { pot.PlantSeed_Server(seedId, 0f); pot.SetSoilState(Pot.ESoilState.Packed); return $"sowed {seedId} in vanilla {pot.gameObject.name}"; }
            if (step != "harvest") return "vanilla <place|sow|harvest> [seed]";
            var plant = pot.Plant;
            int buds = 0; string stopped = null;
            var active = new List<int>();
            for (int i = 0; i < plant.ActiveHarvestables.Count; i++) active.Add(plant.ActiveHarvestables[i]);
            foreach (int index in active)
            {
                var site = plant.FinalGrowthStage.GrowthSites[index];
                var harvestable = site == null ? null : site.GetComponentInChildren<PlantHarvestable>(true);
                if (harvestable == null) { stopped = $"site {index} has no harvestable"; break; }
                try { harvestable.Harvest(true); buds++; }
                catch (Exception e) { stopped = $"site {index} ({site.name}) threw {e.Message.Split('\n')[0]}"; break; }
            }
            return $"vanilla {pot.gameObject.name}: harvested {buds} of {active.Count}{(stopped != null ? "; stopped: " + stopped : "")}";
        }

        private static string Sow(string seedId)
        {
            var seed = Il2CppScheduleOne.Registry.GetItem(seedId)?.TryCast<SeedDefinition>();
            if (seed == null) return $"'{seedId}' is not a seed";
            Hole target = null; string why = "no holes";
            foreach (var h in Holes.All)
            {
                if (!h.Alive || h.Pot.Plant != null) continue;
                if (h.Pot.CanAcceptSeed(out string reason)) { target = h; break; }
                why = reason;
            }
            if (target == null) return $"no hole can take a seed ({why})";
            var inv = PlayerSingleton<PlayerInventory>.Instance;
            bool had = inv != null && inv.GetAmountOfItem(seed.ID) > 0;
            if (had) inv.RemoveAmountOfItem(seed.ID, 1);
            target.Pot.PlantSeed_Server(seed.ID, 0f);
            target.Pot.SetSoilState(Pot.ESoilState.Packed);
            return $"PlantSeed_Server({seed.ID}) in {target.Unit.Name} {target.Guid}{(had ? "; one seed taken from the pockets" : "; no seed in the pockets, none taken")} | {DescribeHole(target)}";
        }

        /// <summary>
        /// Runs the game's per-minute update (Pot.OnMinPass: water drain, Plant.MinPass) on every pot with a growing plant at the
        /// owned properties, hours x 60 times, so the hole's speed (in the temperature multiplier) and curing apply as they do
        /// with the clock. Vanilla pots run too, for comparison. The clock itself does not move.
        /// </summary>
        private static string Grow(float hours)
        {
            int minutes = Mathf.RoundToInt(hours * 60f);
            var pots = new List<Pot>();
            foreach (var pot in Holes.PotsAtOwnedProperties()) if (pot.Plant != null && !pot.Plant.IsFullyGrown) pots.Add(pot);
            if (pots.Count == 0) return "no growing plants";
            var start = new float[pots.Count];
            var grownAt = new int[pots.Count];
            for (int i = 0; i < pots.Count; i++) { start[i] = pots[i].Plant.NormalizedGrowthProgress; grownAt[i] = -1; }
            for (int m = 1; m <= minutes; m++)
                for (int i = 0; i < pots.Count; i++)
                {
                    var pot = pots[i];
                    if (pot == null || pot.Pointer == IntPtr.Zero) continue;
                    pot.OnMinPass();
                    if (grownAt[i] < 0 && pot.Plant != null && pot.Plant.IsFullyGrown) grownAt[i] = m;
                }
            var parts = new List<string>();
            for (int i = 0; i < pots.Count; i++)
            {
                var pot = pots[i];
                var h = Holes.Get(pot);
                string name = h != null ? $"{h.Unit.Name} {h.Guid}" : $"vanilla {pot.ItemInstance?.ID} {pot.GUID}";
                var p = pot.Plant;
                float rate = minutes > 0 && p != null ? (p.NormalizedGrowthProgress - start[i]) / minutes : 0f;
                string eta = grownAt[i] >= 0 ? $"fully grown after {grownAt[i]} min ({grownAt[i] / 60f:0.00} h)" :
                             rate > 0f && p != null ? $"about {(1f - p.NormalizedGrowthProgress) / rate / 60f:0.00} h more at this rate" : "not growing";
                parts.Add($"{name}: {p?.SeedDefinition?.ID} {start[i]:P1} -> {(p == null ? 0f : p.NormalizedGrowthProgress):P1}, {eta}, " +
                          $"base growth time {p?.GrowthTime} h, water {pot.NormalizedMoistureAmount:P0}");
            }
            bool eod = NetworkSingleton<Il2CppScheduleOne.GameTime.TimeManager>.Instance?.IsEndOfDay ?? false;
            return $"{minutes} x Pot.OnMinPass{(eod ? " (it is 4:00, end of day: the game grows nothing now)" : "")}: " + string.Join("; ", parts);
        }

        /// <summary>
        /// Harvests the first fully grown hole (of a kind, if given) bud by bud as the player's clicks do (PlantHarvestable.Harvest: the product into
        /// the pockets, Pot.SetHarvestableActive_Server), after the harvest task's own room check.
        /// </summary>
        private static string Harvest(HoleKind kind)
        {
            Hole target = null;
            foreach (var h in Holes.All)
                if (h.Alive && (kind == HoleKind.None || h.Kind == kind) && h.Pot.Plant != null && h.Pot.IsReadyForHarvest(out _)) { target = h; break; }
            if (target == null) return $"no {(kind == HoleKind.None ? "" : kind + " ")}hole is ready for harvest";
            var plant = target.Pot.Plant;
            string cure = Plants.Describe(plant);
            float quality = plant.QualityLevel;
            var active = new List<int>();
            for (int i = 0; i < plant.ActiveHarvestables.Count; i++) active.Add(plant.ActiveHarvestables[i]);
            int sites = plant.FinalGrowthStage.GrowthSites.Length;
            var inv = PlayerSingleton<PlayerInventory>.Instance;
            int buds = 0, items = 0; string product = null, tier = null, stopped = null;
            foreach (int index in active)
            {
                var site = plant.FinalGrowthStage.GrowthSites[index];
                var harvestable = site == null ? null : site.GetComponentInChildren<PlantHarvestable>(true);
                if (harvestable == null) { stopped = $"site {index} has no harvestable"; break; }
                var preview = plant.GetHarvestedProduct(harvestable.ProductQuantity);
                if (inv != null && !inv.CanItemFitInInventory(preview, harvestable.ProductQuantity)) { stopped = "pockets full"; break; }
                product ??= preview.ID;
                tier ??= preview.TryCast<QualityItemInstance>()?.Quality.ToString();
                try { harvestable.Harvest(true); }
                catch (Exception e)
                {
                    stopped = $"site {index} ({site.name}, parent pot {(harvestable.GetComponentInParent<Pot>() != null)}, plant {(harvestable.GetComponentInParent<Plant>() != null)}) threw {e.GetType().Name}: {e.Message.Split('\n')[0]}";
                    break;
                }
                buds++; items += harvestable.ProductQuantity;
            }
            int ownSites = target.Kind == HoleKind.Aero && Settings.ExtraSites ? sites - Units.AeroKind.ExtraBudSites : sites, extra = 0;
            foreach (int i in active) if (i >= ownSites) extra++;
            return $"{target.Unit.Name} {target.Guid}: harvested {buds} of {active.Count} bud(s) ({extra} on the spoke's extra sites; {sites} sites) = {items}x {product}, " +
                   $"quality {quality:0.###} -> {tier}{(cure != null ? $"; curing at harvest: {cure}" : "")}{(stopped != null ? "; stopped: " + stopped : "")}";
        }

        /// <summary>Advances the first curing plant's curve hour by hour (Plants.AgeCure: its curing clock moved back, then one Plant.MinPass).</summary>
        private static string Cure(float hours)
        {
            Hole target = null;
            foreach (var h in Holes.All)
                if (h.Alive && h.Kind == HoleKind.Aero && h.Pot.Plant != null && h.Pot.Plant.IsFullyGrown) { target = h; break; }
            if (target == null) return "no fully grown plant in an aero hole";
            if (!Settings.Curing) return "curing is off in the settings";
            var steps = new List<string> { "now " + (Plants.Describe(target.Pot.Plant) ?? "(not curing yet)") };
            int total = Mathf.RoundToInt(hours * 60f);
            for (int done = 0; done < total;)
            {
                int step = Math.Min(60, total - done);
                string s = Plants.AgeCure(target.Pot.Plant, target, step);
                if (s == null) { steps.Add("the plant is not curing (no curing step ran)"); break; }
                done += step;
                steps.Add($"+{done / 60f:0.#} h: {s} ({ItemQuality.GetQuality(target.Pot.Plant.QualityLevel)})");
            }
            return $"{target.Unit.Name} {target.Guid}: " + string.Join("; ", steps);
        }

        // ------------------------------------------------------------------ botanists

        /// <summary>The property with holes (the first found), else the first owned property that isn't the RV.</summary>
        private static PropertyType Site()
        {
            foreach (var h in Holes.All) if (h.Alive && h.Pot.ParentProperty != null) return h.Pot.ParentProperty;
            var owned = PropertyType.OwnedProperties;
            for (int i = 0; owned != null && i < owned.Count; i++) if (owned[i] != null && owned[i].PropertyCode != "rv") return owned[i];
            return null;
        }

        private static Botanist BotanistAt(PropertyType prop)
        {
            if (prop?.Employees == null) return null;
            for (int i = 0; i < prop.Employees.Count; i++)
            {
                var b = prop.Employees[i]?.TryCast<Botanist>();
                if (b != null) return b;
            }
            return null;
        }

        private static string BotanistReport()
        {
            var prop = Site();
            if (prop == null) return "no owned property";
            var b = BotanistAt(prop);
            int holes = 0;
            foreach (var h in Holes.All) if (h.Alive && h.Pot.ParentProperty != null && h.Pot.ParentProperty.Pointer == prop.Pointer) holes++;
            string staff = $"{prop.PropertyCode}: {prop.Employees?.Count ?? 0}/{prop.EmployeeCapacity} employees, {holes} hole(s)";
            if (b == null) return $"no botanist at {staff}; 'hydro botanist hire' hires one through Manny";
            return $"{staff}; {DescribeBotanist(b)}";
        }

        private static string Train(TrainingLevel course)
        {
            var b = BotanistAt(Site());
            if (b == null) return "no botanist at the property ('hydro botanist hire')";
            var money = NetworkSingleton<Il2CppScheduleOne.Money.MoneyManager>.Instance;
            float before = money?.cashBalance ?? 0f;
            string r = Courses.ProbeTrain(b, course);
            return $"{b.FullName}: {r}; cash ${before:N0} -> ${(money?.cashBalance ?? 0f):N0} | {DescribeBotanist(b)}";
        }

        /// <summary>
        /// The clipboard's Reload ("every tray") for the property's botanist without the clipboard: the game's object selector,
        /// closed, is given his pot list as ObjectListFieldUI would open it (his list, limit, type requirements, filter and
        /// property), Clipboard.FillAll runs on it as the key does, and the list is submitted as the selector's callback does
        /// (ObjectListField.SetList, networked). The selector's own fields are put back after.
        /// </summary>
        private static string AssignAll()
        {
            var prop = Site();
            var b = BotanistAt(prop);
            if (b == null) return "no botanist at the property ('hydro botanist hire')";
            var field = b.configuration?.Assigns;
            if (field == null) return $"{b.FullName} has no configuration yet";
            var sel = Selector();
            if (sel == null) return "the clipboard's object selector was not found";
            if (sel.IsOpen) return "close the clipboard's selector first";
            var keep = (sel.selectedObjects, sel.maxSelectedObjects, sel.typeRequirements, sel.objectFilter, sel.targetProperty);
            int before = field.SelectedObjects.Count, added;
            try
            {
                var list = new Il2CppList.List<BuildableItem>();
                for (int i = 0; i < field.SelectedObjects.Count; i++) list.Add(field.SelectedObjects[i]);
                sel.selectedObjects = list;
                sel.maxSelectedObjects = field.MaxItems;
                sel.typeRequirements = field.TypeRequirements;
                sel.objectFilter = field.objectFilter;
                sel.targetProperty = b.AssignedProperty ?? prop;
                added = Clipboard.FillAll(sel, Courses.Level(b));
                if (added > 0) field.SetList(list, true);
            }
            finally
            {
                sel.selectedObjects = keep.Item1; sel.maxSelectedObjects = keep.Item2; sel.typeRequirements = keep.Item3;
                sel.objectFilter = keep.Item4; sel.targetProperty = keep.Item5;
            }
            if (added < 0) return $"no holes at {prop?.PropertyCode}";
            int hydro = 0, aero = 0, other = 0;
            for (int i = 0; i < field.SelectedObjects.Count; i++)
            {
                var h = Holes.Get(field.SelectedObjects[i]?.TryCast<Pot>());
                if (h == null) other++; else if (h.Kind == HoleKind.Aero) aero++; else hydro++;
            }
            return $"{b.FullName} (training {Training.Describe(Courses.Level(b))}): Clipboard.FillAll added {added}; list {before} -> {field.SelectedObjects.Count}/{field.MaxItems} " +
                   $"({hydro} hydro, {aero} aero, {other} other); bot's AssignedPots {b.configuration.AssignedPots?.Count}";
        }

        private static ObjectSelector Selector()
        {
            try { var ui = Clipboard.Ui(); if (ui != null && ui.ObjectSelector != null) return ui.ObjectSelector; } catch { }
            foreach (var x in Resources.FindObjectsOfTypeAll<ObjectSelector>())
                if (x != null && x.gameObject.scene.IsValid()) return x;
            return null;
        }

        /// <summary>Hires through Manny's own confirm (DialogueController_Fixer.Confirm: the signing fee taken, CreateNewEmployee), after his limit check.</summary>
        private static string Hire(PropertyType prop, EEmployeeType type)
        {
            if (prop == null) return "no owned property";
            int n = prop.Employees?.Count ?? 0;
            if (n >= prop.EmployeeCapacity) return $"employee limit reached at {prop.PropertyCode} ({n}/{prop.EmployeeCapacity})";
            Il2CppScheduleOne.Dialogue.DialogueController_Fixer fixer = null;
            foreach (var x in Resources.FindObjectsOfTypeAll<Il2CppScheduleOne.Dialogue.DialogueController_Fixer>())
                if (x != null && x.gameObject.scene.IsValid()) { fixer = x; break; }
            if (fixer == null) return "Manny's dialogue controller not found";
            var em = NetworkSingleton<EmployeeManager>.Instance;
            var money = NetworkSingleton<Il2CppScheduleOne.Money.MoneyManager>.Instance;
            float fee = em.GetEmployeePrefab(type).SigningFee + Il2CppScheduleOne.NPCs.CharacterClasses.Fixer.GetAdditionalSigningFee();
            float before = money.cashBalance;
            if (before < fee) return $"a {type} costs ${fee:N0}; cash ${before:N0}";
            fixer.selectedEmployeeType = type;
            fixer.selectedProperty = prop;
            fixer.Confirm();
            return $"DialogueController_Fixer.Confirm: {type} for ${fee:N0} at {prop.PropertyCode}, cash ${before:N0} -> ${money.cashBalance:N0}; the employee spawns when the server call lands (run 'hydro botanist' after a wait)";
        }

        // ------------------------------------------------------------------ placing

        /// <summary>
        /// Places one of a grid buildable on the first free tiles of an owned property other than the RV, through the game's
        /// BuildManager (as the build tool would, without taking it from the pockets).
        /// </summary>
        private static string PlaceOnGrid(string itemId)
        {
            var any = Il2CppScheduleOne.Registry.GetItem(itemId);
            if (any == null) return $"{itemId} is not registered";
            var def = any.TryCast<BuildableItemDefinition>();
            if (def == null) return $"{itemId} is not a buildable ({any.GetIl2CppType().FullName})";
            var grid0 = def.BuiltItem?.TryCast<GridItem>();
            if (grid0 == null) return $"{itemId} is not a grid item";
            int fx = grid0.FootprintX, fy = grid0.FootprintY;
            Il2CppScheduleOne.Tiles.Grid grid = null; int ox = 0, oy = 0; string where = "";
            var owned = PropertyType.OwnedProperties;
            for (int p = 0; owned != null && p < owned.Count && grid == null; p++)
            {
                var prop = owned[p];
                if (prop?.Grids == null || prop.PropertyCode == "rv") continue;
                for (int g = 0; g < prop.Grids.Count && grid == null; g++)
                {
                    var gr = prop.Grids[g];
                    for (int t = 0; gr?.Tiles != null && t < gr.Tiles.Count && grid == null; t++)
                    {
                        var tile = gr.Tiles[t];
                        bool free = true;
                        for (int dx = 0; dx < fx && free; dx++)
                            for (int dy = 0; dy < fy && free; dy++)
                            {
                                var other = gr.GetTile(new Il2CppScheduleOne.Tiles.Coordinate(tile.x + dx, tile.y + dy));
                                free = other != null && (other.OccupantTiles == null || other.OccupantTiles.Count == 0);
                            }
                        if (free) { grid = gr; ox = tile.x; oy = tile.y; where = prop.PropertyCode; }
                    }
                }
            }
            if (grid == null) return $"no free {fx}x{fy} spot for {itemId} in any owned property but the RV";
            var placed = Il2CppScheduleOne.Building.BuildManager.Instance.CreateGridItem(def.GetDefaultInstance(1), grid, new Vector2(ox, oy), 0, "");
            if (placed == null) return $"CreateGridItem returned null at {where} ({ox},{oy})";
            var at = placed.transform.position;
            return $"placed {itemId} ({fx}x{fy}) at {where} tile ({ox},{oy}), world ({at.x:0.0},{at.y:0.0},{at.z:0.0}), guid {placed.GUID}";
        }
    }
}
