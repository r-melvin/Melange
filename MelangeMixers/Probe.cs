using System;
using System.Collections.Generic;
using Melange.Core;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Effects;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Management;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.UI.Management;
using Il2CppScheduleOne.UI.Stations;
using S1API.Console;
using UnityEngine;
using Il2CppList = Il2CppSystem.Collections.Generic;
using PropertyType = Il2CppScheduleOne.Property.Property;

namespace Melange.Mixers
{
    /// <summary>
    /// The in-game probes (TESTING.md P3-P8): one console command, <c>mixers &lt;what&gt;</c>, so a mix can be placed, loaded,
    /// started, finished and checked without the station screen (probe-cmds.txt, through S1API's ConsoleHelper.Submit). S1API
    /// finds and registers it. Every subcommand logs <c>PROBE &lt;what&gt;: &lt;result&gt;</c> to the Melange_Mixers logger and
    /// goes through the game's own methods (the station screen's BeginMix, the station's time-pass handler), so the spoke's
    /// patches run as in play; nothing here is needed for play. Host only.
    /// </summary>
    public sealed class MixersCommand : BaseConsoleCommand
    {
        public override string CommandWord => "mixers";
        public override string CommandDescription => "Melange Mixers probes: status, place <2|3|4>, load <product> <qty> <mixer1>[:qty] [mixer2[:qty]] [mixer3[:qty]], start, finish, " +
                                                     "name <name>, output, employee [hire|assign]";
        public override string ExampleUsage => "mixers status";

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
        /// <summary>The last mix the probe started, for <c>output</c> to compare against (the spoke forgets its chain at the finish).</summary>
        private sealed class Started { public string Guid, ProductId; public List<string> Chain; public int Quantity; public EQuality Quality; }
        private static Started _last;

        public static string Run(string what, List<string> args)
        {
            if (!Mod.Active) return "the spoke is off (Melange Core too old)";
            if (!Host.IsHost) return "host only";
            if (Il2CppScheduleOne.PlayerScripts.Player.Local == null || PropertyType.OwnedProperties == null) return "no save loaded";
            string a0 = args.Count > 0 ? args[0].ToLowerInvariant() : null;
            switch (what)
            {
                case "status": return Status();
                case "place":
                {
                    var tier = a0 == "2" ? Tiers.Two : a0 == "3" ? Tiers.Three : a0 == "4" ? Tiers.Four : null;
                    return tier == null ? "place <2|3|4>" : PlaceOnGrid(tier.Id);
                }
                case "load": return Load(args);
                case "start": return Start();
                case "finish": return Finish();
                case "name": return args.Count == 0 ? "name <name>" : NameMix(string.Join(" ", args));
                case "output": return Output();
                case "employee": return Employee(a0);
                default: return "unknown; mixers status|place|load|start|finish|name|output|employee";
            }
        }

        // ------------------------------------------------------------------ status

        private static string Status()
        {
            var parts = new List<string>();
            foreach (var m in Stations.All)
            {
                if (!m.Alive) continue;
                parts.Add(Describe(m));
            }
            return parts.Count == 0 ? "no Melange mixers placed (mixers place <2|3|4>)" : $"{parts.Count} machine(s) | " + string.Join(" | ", parts);
        }

        private static string Describe(Machine m)
        {
            var st = m.Station;
            var op = st.CurrentMixOperation;
            var slots = new List<string> { "product " + Slot(st.ProductSlot), "1 " + Slot(st.MixerSlot) };
            for (int i = 0; i < m.Extra.Count; i++) slots.Add($"{i + 2} {Slot(m.Extra[i])}");
            string chainState;
            if (op != null)
            {
                var chain = Stations.RunningChain(m, op);
                chainState = $"mixing {op.Quantity}x {op.ProductID} ({op.ProductQuality}) + {string.Join(" + ", chain)}; " +
                             $"{st.CurrentMixTime}/{st.GetMixTimeForCurrentOperation()} min, {Math.Max(0, st.GetMixTimeForCurrentOperation() - st.CurrentMixTime)} left" +
                             (st.IsMixingDone ? ", done" : "");
            }
            else
            {
                var check = Stations.Check(m, st.ProductSlot.ItemInstance?.ID, Stations.ChainFromSlots(m, st.MixerSlot.ItemInstance?.ID));
                chainState = $"idle, chain {check.State}{(check.Slot > 0 ? " (slot " + check.Slot + ")" : "")}, batch {st.GetMixQuantity()}, can start {st.CanStartMix()}";
            }
            var rec = MelangeMixersData.Current?.For(m.Guid);
            string at = st.ParentProperty != null ? st.ParentProperty.PropertyCode : "?";
            return $"{m.Tier.Name} {m.Guid} at {at}: {m.Extra.Count} extra slot(s); slots [{string.Join(", ", slots)}], output {Slot(st.OutputSlot)}; {chainState}; " +
                   $"cap {st.MaxMixQuantity} (base {m.BaseMax}), {st.MixTimePerItem} min/item (base {m.BasePerItem}), ingredient insertion {st.RequiresIngredientInsertion}; " +
                   $"saved chain {(rec == null || rec.Chain.Count == 0 ? "(none)" : string.Join(" + ", rec.Chain))}";
        }

        private static string Slot(ItemSlot s)
        {
            if (s == null) return "-";
            var inst = s.ItemInstance;
            string lockText = s.IsLocked ? $" [locked: {s.ActiveLock?.LockReason}]" : "";
            if (inst == null) return "empty" + lockText;
            var q = inst.TryCast<QualityItemInstance>();
            return $"{s.Quantity}x {inst.ID}{(q != null ? " " + q.Quality : "")}{lockText}";
        }

        // ------------------------------------------------------------------ load, start, finish

        private static Machine Nearest()
        {
            var me = Il2CppScheduleOne.PlayerScripts.Player.Local;
            Machine best = null; float bestD = float.MaxValue;
            foreach (var m in Stations.All)
            {
                if (!m.Alive) continue;
                float d = me == null ? 0f : (m.Station.transform.position - me.transform.position).sqrMagnitude;
                if (d < bestD) { best = m; bestD = d; }
            }
            return best;
        }

        /// <summary>Fills the nearest machine's slots from nothing: new item instances straight into its slots (the slots' own setters, networked).</summary>
        private static string Load(List<string> args)
        {
            if (args.Count < 3 || !int.TryParse(args[1], out int qty) || qty <= 0) return "load <product> <qty> <mixer1>[:qty] [mixer2[:qty]] [mixer3[:qty]]";
            var m = Nearest();
            if (m == null) return "no Melange mixer placed";
            var st = m.Station;
            if (st.CurrentMixOperation != null) return $"{m.Tier.Name} is mixing; finish it first";
            var product = Il2CppScheduleOne.Registry.GetItem(args[0])?.TryCast<ProductDefinition>();
            if (product == null) return $"'{args[0]}' is not a product";
            var mixers = args.GetRange(2, args.Count - 2);
            if (mixers.Count > m.Tier.Mixers) return $"{m.Tier.Name} takes {m.Tier.Mixers} mixer(s), got {mixers.Count}";
            var defs = new List<PropertyItemDefinition>();
            var counts = new List<int>();
            for (int i = 0; i < mixers.Count; i++)
            {
                // "cuke:7" puts 7 in that slot (the batch is the smallest stack); plain "cuke" takes the product's quantity
                string id = mixers[i]; int n = qty;
                int colon = id.LastIndexOf(':');
                if (colon > 0 && int.TryParse(id.Substring(colon + 1), out int own) && own > 0) { n = own; id = id.Substring(0, colon); }
                var d = Il2CppScheduleOne.Registry.GetItem(id)?.TryCast<PropertyItemDefinition>();
                if (d == null) return $"'{id}' is not a mixing ingredient";
                defs.Add(d); counts.Add(n);
                mixers[i] = n == qty ? id : $"{id} ({n})";
            }
            var discarded = new List<string>();
            var all = new List<ItemSlot> { st.ProductSlot, st.MixerSlot };
            all.AddRange(m.Extra);
            all.Add(st.OutputSlot);
            foreach (var s in all)
            {
                if (s.IsLocked) s.RemoveLock();
                if (s.ItemInstance != null) { discarded.Add($"{s.Quantity}x {s.ItemInstance.ID}"); s.ClearStoredInstance(); }
            }
            st.ProductSlot.SetStoredItem(product.GetDefaultInstance(qty));
            for (int i = 0; i < defs.Count; i++)
            {
                var slot = i == 0 ? st.MixerSlot : m.Extra[i - 1];
                slot.SetStoredItem(defs[i].GetDefaultInstance(counts[i]));
            }
            Stations.Refresh(m);
            return $"{m.Tier.Name} {m.Guid}: loaded {qty}x {product.ID} + {string.Join(" + ", mixers)}" +
                   (discarded.Count > 0 ? $" (cleared {string.Join(", ", discarded)})" : "") + " | " + Describe(m);
        }

        /// <summary>
        /// Starts the nearest machine the way the station screen's Begin does when the station takes no ingredient task
        /// (MixingStationInterface.BeginMix: take the batch from the product and slot-1 mixer, SendMixingOperation), so the
        /// spoke's start prefix runs. The screen's Station is pointed at the machine for the call and put back after.
        /// </summary>
        private static string Start()
        {
            var m = Nearest();
            if (m == null) return "no Melange mixer placed";
            var st = m.Station;
            if (st.CurrentMixOperation != null) return $"{m.Tier.Name} is already mixing";
            Stations.Refresh(m);
            if (!st.CanStartMix()) return $"the game's CanStartMix is false (batch {st.GetMixQuantity()}, cap {st.MaxMixQuantity}, output {Slot(st.OutputSlot)}) | {Describe(m)}";
            var ui = Singleton<MixingStationInterface>.InstanceExists ? Singleton<MixingStationInterface>.Instance : null;
            if (ui == null) return "the station screen (MixingStationInterface) is not loaded";
            if (ui.IsOpen) return "close the station screen first";
            var started = new Started
            {
                Guid = m.Guid, ProductId = st.ProductSlot.ItemInstance.ID, Chain = Stations.ChainFromSlots(m, st.MixerSlot.ItemInstance?.ID),
                Quality = st.ProductSlot.ItemInstance.TryCast<QualityItemInstance>()?.Quality ?? EQuality.Standard,
            };
            int batch = st.GetMixQuantity();
            var previous = ui.Station;
            try { ui.Station = st; ui.BeginMix(); }
            finally { ui.Station = previous; }
            var op = st.CurrentMixOperation;
            if (op == null) return $"BeginMix ran (batch {batch}) but the station has no operation (the spoke refused it? see its log) | {Describe(m)}";
            started.Quantity = op.Quantity;
            _last = started;
            string note = st.RequiresIngredientInsertion ? " (this station's screen would run the ingredient task first; BeginMix is what it ends in)" : "";
            return $"MixingStationInterface.BeginMix: batch {batch}, operation {op.Quantity}x {op.ProductID} + {op.IngredientID}{note} | {Describe(m)}";
        }

        /// <summary>
        /// Fast-forwards the nearest machine's mix through the game's own time-skip handler (MixingStation.OnTimePass with the
        /// minutes left): on the host it raises MixingDone_Networked, whose MixingDone asks for TryCreateOutputItems, where the
        /// spoke's finish prefix makes the output. Those are network messages, so the output lands a frame or two later.
        /// </summary>
        private static string Finish()
        {
            var m = Nearest();
            if (m == null) return "no Melange mixer placed";
            var st = m.Station;
            var op = st.CurrentMixOperation;
            if (op == null) return $"{m.Tier.Name} is not mixing | {Describe(m)}";
            int total = st.GetMixTimeForCurrentOperation(), left = total - st.CurrentMixTime;
            if (left > 0)
            {
                st.OnTimePass(left);
                return $"OnTimePass({left}): {st.CurrentMixTime}/{total} min, done {st.IsMixingDone}; output follows MixingDone_Networked -> TryCreateOutputItems (check with 'mixers output')";
            }
            st.TryCreateOutputItems();
            return $"already done ({st.CurrentMixTime}/{total} min); TryCreateOutputItems requested again (a new result waits for 'mixers name <name>')";
        }

        /// <summary>Names a finished new result through the spoke's naming (what its prompt calls), which then asks for the output.</summary>
        private static string NameMix(string name)
        {
            var m = Nearest();
            if (m == null) return "no Melange mixer placed";
            var op = m.Station.CurrentMixOperation;
            if (op == null || !m.Station.IsMixingDone) return $"{m.Tier.Name} has no finished mix to name";
            var res = Stations.Result(op.ProductID, Stations.RunningChain(m, op));
            if (res.Known != null) return $"the result is already a product ({res.Known.Name}); nothing to name";
            Stations.Name(m, name);
            return $"Stations.Name('{name}') ran; output follows TryCreateOutputItems (check with 'mixers output')";
        }

        // ------------------------------------------------------------------ output

        /// <summary>
        /// What came out, next to what the same mixers give applied one after another on a vanilla Mk2: each pass is the game's
        /// EffectMixCalculator.MixProperties on the previous pass's effects, its product the game's GetKnownProduct, and the
        /// game's recorded recipe for that product + mixer (ProductManager.GetRecipe) when it has one.
        /// </summary>
        private static string Output()
        {
            var m = Nearest();
            if (m == null) return "no Melange mixer placed";
            var st = m.Station;
            var inst = st.OutputSlot.ItemInstance;
            string got = "output empty";
            ProductDefinition outDef = null;
            if (inst != null)
            {
                outDef = inst.Definition?.TryCast<ProductDefinition>();
                var q = inst.TryCast<QualityItemInstance>();
                got = $"output {st.OutputSlot.Quantity}x {inst.Name} ({inst.ID}){(q != null ? ", " + q.Quality : "")}, effects [{Effects(outDef?.Properties)}]";
            }
            if (st.CurrentMixOperation != null) got += $"; still has an operation ({(st.IsMixingDone ? "done, waiting: new result needs a name" : "running")})";
            if (_last == null || _last.Guid != m.Guid) return got + "; no probe-started mix on this machine to compare with";

            var pm = NetworkSingleton<ProductManager>.Instance;
            var product = Il2CppScheduleOne.Registry.GetItem(_last.ProductId)?.TryCast<ProductDefinition>();
            if (product == null || pm == null) return got + $"; product {_last.ProductId} not found";
            var type = product.DrugType;
            var effects = product.Properties;
            string currentId = product.ID;
            var steps = new List<string>();
            foreach (var id in _last.Chain)
            {
                var mixer = string.IsNullOrEmpty(id) ? null : Il2CppScheduleOne.Registry.GetItem(id)?.TryCast<PropertyItemDefinition>();
                if (mixer == null || mixer.Properties == null || mixer.Properties.Count == 0) { steps.Add($"+ {id}: not a mixer"); break; }
                var recipe = currentId == null ? null : pm.GetRecipe(currentId, id);
                effects = EffectMixCalculator.MixProperties(effects, mixer.Properties[0], type);
                var known = pm.GetKnownProduct(type, effects);
                string recipeText = recipe == null ? "no recipe" : $"recipe -> {recipe.Product?.Item?.ID}";
                steps.Add($"+ {id} -> {(known != null ? known.Name : "(new)")} [{Effects(effects)}] ({recipeText})");
                currentId = known?.ID;
            }
            var expected = pm.GetKnownProduct(type, effects);
            bool same = outDef != null && SameEffects(outDef.Properties, effects);
            string verdict = inst == null ? "NO OUTPUT" : same ? "MATCH" : "MISMATCH";
            string qty = inst == null ? "" : st.OutputSlot.Quantity == _last.Quantity ? $", quantity {_last.Quantity} as started" : $", quantity {st.OutputSlot.Quantity} but {_last.Quantity} were started";
            return $"{got}; sequential Mk2 passes from {product.Name}: {string.Join(" ", steps)}; expected {(expected != null ? expected.Name + " (" + expected.ID + ")" : "a new product")} " +
                   $"[{Effects(effects)}]: {verdict}{qty}";
        }

        private static string Effects(Il2CppList.List<Effect> list)
        {
            if (list == null) return "";
            var names = new List<string>();
            for (int i = 0; i < list.Count; i++) if (list[i] != null) names.Add(list[i].Name);
            return string.Join(", ", names);
        }

        private static bool SameEffects(Il2CppList.List<Effect> a, Il2CppList.List<Effect> b)
        {
            if (a == null || b == null || a.Count != b.Count) return false;
            var ids = new HashSet<string>();
            for (int i = 0; i < a.Count; i++) ids.Add(a[i]?.ID);
            for (int i = 0; i < b.Count; i++) if (!ids.Contains(b[i]?.ID)) return false;
            return true;
        }

        // ------------------------------------------------------------------ chemists

        /// <summary>
        /// Whether a chemist at the nearest machine's property would take it: the clipboard's station list check
        /// (ObjectSelector.IsObjectTypeValid with the chemist's Stations field: type requirements, property, ChemistConfiguration's
        /// filter) and, once assigned, whether Chemist.GetMixingStationsReadyToStart lists it. "hire" hires a chemist through
        /// Manny's own confirm (cash taken); "assign" adds the machine to the chemist's list as the clipboard's submit does
        /// (ObjectListField.SetList, networked).
        /// </summary>
        private static string Employee(string action)
        {
            var m = Nearest();
            if (m == null) return "no Melange mixer placed";
            var st = m.Station;
            var prop = st.ParentProperty;
            if (prop == null) return "the machine has no property";
            if (action == "hire") return Hire(prop, EEmployeeType.Chemist);
            Chemist chemist = null;
            for (int i = 0; prop.Employees != null && i < prop.Employees.Count && chemist == null; i++) chemist = prop.Employees[i]?.TryCast<Chemist>();
            if (chemist == null) return $"no chemist at {prop.PropertyCode} ({prop.Employees?.Count ?? 0}/{prop.EmployeeCapacity} employees); 'mixers employee hire' hires one through Manny";
            var cfg = chemist.configuration;
            var field = cfg?.Stations;
            if (field == null) return $"{chemist.FullName}: no configuration yet";
            bool typeListed = false;
            var stType = st.GetIl2CppType();
            for (int i = 0; field.TypeRequirements != null && i < field.TypeRequirements.Count; i++)
                if (field.TypeRequirements[i]?.FullName == stType.FullName) typeListed = true;
            string selector = SelectorAccepts(field, prop, st, out string selReason);
            bool filterOk = cfg.IsStationValid(st, out string filterReason);
            string assignNote = "";
            if (action == "assign" && !cfg.MixStations.Contains(st))
            {
                var list = new Il2CppList.List<BuildableItem>();
                for (int i = 0; i < field.SelectedObjects.Count; i++) list.Add(field.SelectedObjects[i]);
                if (list.Count >= field.MaxItems) assignNote = $"; not assigned: list full ({list.Count}/{field.MaxItems})";
                else { list.Add(st); field.SetList(list, true); assignNote = "; assigned with ObjectListField.SetList (as the clipboard's submit)"; }
            }
            bool assigned = cfg.MixStations.Contains(st);
            string ready = "not assigned";
            if (assigned)
            {
                var mcfg = st.Configuration?.TryCast<MixingStationConfiguration>();
                bool inList = chemist.GetMixingStationsReadyToStart().Contains(st);
                ready = $"GetMixingStationsReadyToStart lists it: {inList} (npc user {(st.NPCUserObject != null ? st.NPCUserObject.name : "none")}, player user {(st.PlayerUserObject != null ? st.PlayerUserObject.name : "none")}, CanStartMix {st.CanStartMix()}, " +
                        $"batch {st.GetMixQuantity()} vs start threshold {mcfg?.StartThrehold?.Value}, operation {(st.CurrentMixOperation != null ? "running" : "none")}, " +
                        $"assigned chemist {mcfg?.AssignedChemist?.SelectedNPC?.FullName ?? "none"})";
            }
            return $"{chemist.FullName} at {prop.PropertyCode}: station type {stType.Name} in the field's type list {typeListed}; clipboard selector would accept it: {selector}" +
                   $"{(selReason.Length > 0 ? " (" + selReason + ")" : "")}; ChemistConfiguration.IsStationValid {filterOk}{(string.IsNullOrEmpty(filterReason) ? "" : " (" + filterReason + ")")}" +
                   $"{assignNote}; stations {field.SelectedObjects.Count}/{field.MaxItems}; {ready}";
        }

        /// <summary>
        /// The clipboard's own check (ObjectSelector.IsObjectTypeValid) for this field, without opening the selector: its
        /// requirements are set from the field as ObjectListFieldUI's Open would, and put back after. Only while it is closed.
        /// </summary>
        private static string SelectorAccepts(ObjectListField field, PropertyType prop, BuildableItem item, out string reason)
        {
            reason = "";
            var sel = Selector();
            if (sel == null) return "selector not found";
            if (sel.IsOpen) return "selector open (close the clipboard)";
            var types = sel.typeRequirements; var filter = sel.objectFilter; var target = sel.targetProperty;
            try
            {
                sel.typeRequirements = field.TypeRequirements;
                sel.objectFilter = field.objectFilter;
                sel.targetProperty = prop;
                return sel.IsObjectTypeValid(item, out reason).ToString();
            }
            finally { sel.typeRequirements = types; sel.objectFilter = filter; sel.targetProperty = target; }
        }

        private static ObjectSelector Selector()
        {
            try { var ui = Singleton<ManagementInterface>.Instance; if (ui != null && ui.ObjectSelector != null) return ui.ObjectSelector; } catch { }
            foreach (var x in Resources.FindObjectsOfTypeAll<ObjectSelector>())
                if (x != null && x.gameObject.scene.IsValid()) return x;
            return null;
        }

        /// <summary>Hires through Manny's own confirm (DialogueController_Fixer.Confirm: the signing fee taken, CreateNewEmployee), after his limit check.</summary>
        internal static string Hire(PropertyType prop, EEmployeeType type)
        {
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
            return $"DialogueController_Fixer.Confirm: {type} for ${fee:N0} at {prop.PropertyCode}, cash ${before:N0} -> ${money.cashBalance:N0}; the employee spawns when the server call lands (run again after a wait)";
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
