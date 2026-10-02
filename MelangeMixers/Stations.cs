using System;
using System.Collections.Generic;
using HarmonyLib;
using Melange.Core;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Effects;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.Persistence.Datas;
using Il2CppScheduleOne.Product;
using UnityEngine;
using Il2CppList = Il2CppSystem.Collections.Generic;

namespace Melange.Mixers
{
    /// <summary>One placed machine: the game's station, its tier, and the extra mixer slots this spoke gave it.</summary>
    internal sealed class Machine
    {
        public MixingStation Station;
        public MixerTier Tier;
        public readonly List<ItemSlot> Extra = new List<ItemSlot>();
        /// <summary>The station's own batch cap and minutes per item before this spoke touched them (the Mk2's, or another mod's).</summary>
        public int BaseMax, BasePerItem;
        public string Guid;
        public bool Alive => Station != null && Station.Pointer != IntPtr.Zero && Station.gameObject != null;
    }

    /// <summary>The result of a machine's mix: the product going in, its effects coming out, and the product they make if it exists.</summary>
    internal readonly struct MixResult
    {
        public readonly ProductDefinition Product;
        public readonly Il2CppList.List<Effect> Effects;
        public readonly ProductDefinition Known;
        public MixResult(ProductDefinition product, Il2CppList.List<Effect> effects, ProductDefinition known) { Product = product; Effects = effects; Known = known; }
        public bool Valid => Product != null && Effects != null;
    }

    /// <summary>
    /// How a machine mixes. It stays a real Mk2 and runs the game's own mix operation (product + the slot-1 mixer, which is
    /// what gets saved), with three seams:
    /// <list type="bullet">
    /// <item>Start: the extra mixers stay in their slots, locked, until the mix ends. Every player's game then knows the order
    /// from the slots (the game's operation has no room for it), late joiners get it with the slots, and nothing is lost if a
    /// co-op client started the mix. The station's own batch cap is held at the smallest extra stack, so the game's batch, its
    /// Begin button and its chemists all count every mixer without patching the tiny methods that read them.</item>
    /// <item>Finish (host): the result is the game's single-mix calculation folded over the slots in order; only that final
    /// product is made, no recipe is recorded, then the extra mixers are used up and unlocked.</item>
    /// <item>Speed: the station's own minutes per item, the Mk2's times the tier's setting.</item>
    /// </list>
    /// Patches are on multi-line methods only (IL2CPP inlines one-liners and a patch on them silently never runs): the
    /// placement (InitializeGridItem), the start request's writer (as the hub hooks XP), and the finish request's reader.
    /// </summary>
    internal static class Stations
    {
        public const string LockReason = "Held for the mix";
        private static readonly Dictionary<IntPtr, Machine> _machines = new Dictionary<IntPtr, Machine>();
        private static float _nextTick, _nextScan;
        private static readonly HashSet<IntPtr> _warnedUnknown = new HashSet<IntPtr>();
        // the host leaves running mixes alone until the extra slots are back from the save: finishing one before would use
        // mixers that aren't there yet and then hand them back
        private static bool _restored;

        public static IEnumerable<Machine> All => _machines.Values;

        public static void Reset() { _machines.Clear(); _warnedUnknown.Clear(); _nextTick = _nextScan = 0f; _restored = false; }

        // ------------------------------------------------------------------ patches

        public static void Patch(HarmonyLib.Harmony harmony)
        {
            TryPatch(harmony, "machine slots", nameof(MixingStation.InitializeGridItem), null, nameof(AfterInitialize));
            TryPatch(harmony, "mix start", "RpcWriter___Server_SendMixingOperation_2669582547", nameof(BeforeSendMix), null);
            TryPatch(harmony, "mix finish", "RpcReader___Server_TryCreateOutputItems_2166136261", nameof(BeforeCreateOutput), null);
        }

        private static void TryPatch(HarmonyLib.Harmony harmony, string what, string method, string prefix, string postfix)
        {
            try
            {
                var target = AccessTools.Method(typeof(MixingStation), method) ?? throw new MissingMethodException(nameof(MixingStation), method);
                harmony.Patch(target,
                    prefix: prefix == null ? null : new HarmonyMethod(typeof(Stations), prefix),
                    postfix: postfix == null ? null : new HarmonyMethod(typeof(Stations), postfix));
            }
            catch (Exception e) { Mod.Log.Error($"{what}: patch failed ({e.Message}); the mixers will not work properly"); }
        }

        // A placed station learns its item here, on every player's game, before the host sends its slots: so the extra slots
        // exist, at the same indexes everywhere, before any slot data addresses them.
        private static void AfterInitialize(MixingStation __instance)
        {
            try { Attach(__instance); }
            catch (Exception e) { Mod.Log.Warning("could not fit a mixer's extra slots: " + e.Message); }
        }

        // The start request, on the player's game that starts the mix (the host's for a chemist), after the game has taken the
        // product and slot-1 mixer and before the request goes out.
        private static void BeforeSendMix(MixingStation __instance, MixOperation operation)
        {
            try
            {
                var m = Get(__instance);
                if (m == null || operation == null || string.IsNullOrEmpty(operation.ProductID)) return;
                var chain = ChainFromSlots(m, operation.IngredientID);
                var check = Check(m, operation.ProductID, chain);
                int q = check.Ready ? MixChain.StartQuantity(operation.Quantity, ExtraQuantities(m)) : 0;
                if (q < operation.Quantity) Refund(m, operation, operation.Quantity - q);
                if (q <= 0)
                {
                    // the game treats an operation without a product as none, on every player's game
                    operation.ProductID = string.Empty;
                    Mod.Log.Msg($"{m.Tier.Name}: start refused ({check.State}{(check.Slot > 0 ? " in slot " + check.Slot : "")}); inputs returned");
                    return;
                }
                operation.Quantity = q;
                var owner = __instance.NetworkObject;
                foreach (var slot in m.Extra) slot.ApplyLock(owner, LockReason);
                if (Host.IsHost) Remember(m, chain);
                Mod.Log.Msg($"{m.Tier.Name}: mixing {q}x {operation.ProductID} + {string.Join(" + ", chain)}");
            }
            catch (Exception e) { Mod.Log.Warning("mix start: " + e.Message); }
        }

        // The finish request, on the host. A machine never takes the game's own path: it would make product + slot-1 mixer and
        // record that as a recipe.
        private static bool BeforeCreateOutput(MixingStation __instance)
        {
            var m = Get(__instance);
            if (m == null) return true;
            try { Complete(m); }
            catch (Exception e) { Mod.Log.Warning("mix finish: " + e.Message); }
            return false;
        }

        // ------------------------------------------------------------------ machines

        /// <summary>The machine for a station, or null for a vanilla station.</summary>
        public static Machine Get(MixingStation station)
        {
            if (station == null) return null;
            if (_machines.TryGetValue(station.Pointer, out var m)) return m.Alive ? m : null;
            return Attach(station);
        }

        private static Machine Attach(MixingStation station)
        {
            var tier = Tiers.ById(station?.ItemInstance?.ID);
            if (tier == null) return null;
            if (_machines.TryGetValue(station.Pointer, out var known) && known.Alive) return known;
            var m = new Machine { Station = station, Tier = tier, BaseMax = station.MaxMixQuantity, BasePerItem = station.MixTimePerItem, Guid = station.GUID.ToString() };
            var owner = station.TryCast<IItemSlotOwner>() ?? new IItemSlotOwner(station.Pointer);
            Il2CppSystem.Action changed = new Action(() => SlotsChanged(m));
            for (int i = 0; i < tier.ExtraSlots; i++)
            {
                var slot = new ItemSlot(true);
                slot.AddFilter(new ItemFilter_MixingIngredient());
                slot.SetSlotOwner(owner);                        // appends to the station's ItemSlots: indexes 3 and up
                station.InputSlots.Add(slot);                    // handlers stock it; per-slot filters pin each mixer
                station.MixerSlot.SiblingSet?.AddSlot(slot);     // the filter panel's "apply to siblings" expects a set
                slot.onItemDataChanged = Combine(slot.onItemDataChanged, changed);
                m.Extra.Add(slot);
            }
            station.ProductSlot.onItemDataChanged = Combine(station.ProductSlot.onItemDataChanged, changed);
            station.MixerSlot.onItemDataChanged = Combine(station.MixerSlot.onItemDataChanged, changed);
            _machines[station.Pointer] = m;
            Refresh(m);
            Mod.Log.Msg($"{tier.Name} {m.Guid}: {tier.ExtraSlots} extra mixer slot(s); Mk2 base {m.BasePerItem} min/item, batch {m.BaseMax}");
            return m;
        }

        private static Il2CppSystem.Action Combine(Il2CppSystem.Action existing, Il2CppSystem.Action add)
            => existing == null ? add : Il2CppSystem.Delegate.Combine(existing, add).Cast<Il2CppSystem.Action>();

        private static void SlotsChanged(Machine m)
        {
            if (!m.Alive) return;
            Refresh(m);
            if (Host.IsHost) Snapshot(m);
        }

        /// <summary>The station's batch cap and speed from its slots and the settings; on every player's game (all derived from shared state).</summary>
        public static void Refresh(Machine m)
        {
            var st = m.Station;
            var chain = ChainFromSlots(m, st.MixerSlot.ItemInstance?.ID);
            var check = Check(m, st.ProductSlot.ItemInstance?.ID, chain);
            st.MaxMixQuantity = MixChain.BatchCap(m.BaseMax, ExtraQuantities(m), check.Ready);
            st.MixTimePerItem = Timing.MixTimePerItem(m.BasePerItem, Settings.TimeMultiplier(m.Tier));
        }

        public static List<int> ExtraQuantities(Machine m)
        {
            var q = new List<int>(m.Extra.Count);
            foreach (var s in m.Extra) q.Add(s.Quantity);
            return q;
        }

        /// <summary>The mixers in slot order: slot 1 (the game's, or the operation's when it has already been taken), then the extras.</summary>
        public static List<string> ChainFromSlots(Machine m, string firstMixer)
        {
            var chain = new List<string>(m.Tier.Mixers) { firstMixer };
            foreach (var s in m.Extra) chain.Add(s.ItemInstance?.ID);
            return chain;
        }

        public static ChainCheck Check(Machine m, string productId, List<string> chain)
        {
            if (!string.IsNullOrEmpty(productId) && !IsVanillaProduct(productId)) return new ChainCheck(ChainState.ProductNotVanilla);
            return MixChain.Check(productId, chain, m.Tier.Mixers, IsMixingIngredient);
        }

        private static bool IsVanillaProduct(string id)
        {
            var def = Il2CppScheduleOne.Registry.GetItem(id)?.TryCast<ProductDefinition>();
            if (def == null) return false;
            var t = def.DrugType;
            return t == EDrugType.Marijuana || t == EDrugType.Methamphetamine || t == EDrugType.Cocaine || t == EDrugType.Shrooms;
        }

        private static bool IsMixingIngredient(string id)
        {
            var def = Il2CppScheduleOne.Registry.GetItem(id)?.TryCast<PropertyItemDefinition>();
            var pm = NetworkSingleton<ProductManager>.Instance;
            return def != null && pm != null && pm.ValidMixIngredients.Contains(def) && def.Properties != null && def.Properties.Count > 0;
        }

        // ------------------------------------------------------------------ the mix

        /// <summary>The running mix's chain: the host's record from the start, else the locked slots (a co-op client, or a lost record).</summary>
        public static List<string> RunningChain(Machine m, MixOperation op)
        {
            if (Host.IsHost)
            {
                var r = MelangeMixersData.Current?.For(m.Guid);
                if (r != null && SaveRules.ChainFits(r.Chain, m.Tier.Mixers) && r.Chain[0] == op.IngredientID) return new List<string>(r.Chain);
            }
            return ChainFromSlots(m, op.IngredientID);
        }

        /// <summary>The game's single mix, once per mixer in order: exactly what that many Mk2 passes give.</summary>
        public static MixResult Result(string productId, IEnumerable<string> chain)
        {
            var product = Il2CppScheduleOne.Registry.GetItem(productId)?.TryCast<ProductDefinition>();
            if (product == null) return default;
            var mixers = new List<Effect>();
            foreach (var id in chain)
            {
                var mixer = string.IsNullOrEmpty(id) ? null : Il2CppScheduleOne.Registry.GetItem(id)?.TryCast<PropertyItemDefinition>();
                if (mixer == null || mixer.Properties == null || mixer.Properties.Count == 0) break;   // an incomplete chain mixes what it has
                mixers.Add(mixer.Properties[0]);
            }
            var type = product.DrugType;
            var effects = MixChain.Fold(product.Properties, mixers, (current, effect) => EffectMixCalculator.MixProperties(current, effect, type));
            var known = NetworkSingleton<ProductManager>.Instance?.GetKnownProduct(type, effects);
            return new MixResult(product, effects, known);
        }

        // the game's own clock, so another mod that changes mix times (Production Expansion Reborn patches this getter) stays in step
        public static bool IsDone(MixingStation st, MixOperation op) => op != null && st.IsMixingDone;

        /// <summary>
        /// Finishes a done mix on the host if its result is a known product: the output, at the input's quality; no recipe (the
        /// game's recipes are product + one mixer only); then the extra mixers are used and unlocked. An unknown result waits for a
        /// player to name it, as the game's own station does.
        /// </summary>
        public static void Complete(Machine m)
        {
            if (!Host.IsHost || !_restored) return;
            var st = m.Station;
            var op = st.CurrentMixOperation;
            if (!IsDone(st, op)) return;
            var chain = RunningChain(m, op);
            if (!SaveRules.ChainFits(chain, m.Tier.Mixers))
                Mod.Log.Warning($"{m.Tier.Name} {m.Guid}: the mix's mixers are not all known ({string.Join(", ", chain)}); finishing with those that are");
            var res = Result(op.ProductID, chain);
            if (!res.Valid) { Mod.Log.Warning($"{m.Tier.Name} {m.Guid}: product '{op.ProductID}' not found; mix left as it is"); return; }
            if (res.Known == null)
            {
                if (_warnedUnknown.Add(st.Pointer)) Mod.Log.Msg($"{m.Tier.Name} {m.Guid}: new mix, waiting for a player to name it");
                return;
            }
            _warnedUnknown.Remove(st.Pointer);
            var output = res.Known.GetDefaultInstance(op.Quantity)?.TryCast<QualityItemInstance>();
            if (output == null) { Mod.Log.Warning($"{m.Tier.Name}: could not make {res.Known.ID}"); return; }
            output.SetQuality(op.ProductQuality);
            st.OutputSlot.AddItem(output);
            int mixers = 0;
            foreach (var id in chain) if (!string.IsNullOrEmpty(id)) mixers++;
            var pm = NetworkSingleton<ProductManager>.Instance;
            if (SaveRules.MayRecordGameRecipe(mixers) && pm.GetRecipe(op.ProductID, op.IngredientID) == null)
                pm.SendMixRecipe(op.ProductID, op.IngredientID, res.Known.ID);      // only ever a plain single mix
            int quantity = op.Quantity;
            st.SetMixOperation(null, null, 0);
            for (int i = 0; i < m.Extra.Count; i++)
            {
                var slot = m.Extra[i];
                string want = i + 1 < chain.Count ? chain[i + 1] : null;
                if (slot.ItemInstance != null && slot.ItemInstance.ID == want) slot.ChangeQuantity(-Math.Min(quantity, slot.Quantity));
                else if (want != null) Mod.Log.Warning($"{m.Tier.Name}: slot {i + 2} no longer holds {want}; nothing taken from it");
                Unlock(m, slot);
            }
            Forget(m);
            Mod.Log.Msg($"{m.Tier.Name}: {quantity}x {res.Product.Name} + {string.Join(" + ", chain)} -> {res.Known.Name}");
        }

        /// <summary>
        /// Names a new mix's result (on the player's game that named it, as the game does) and creates it the way the game
        /// creates a named mix, but from the final effects: a vanilla product (drug type, effect IDs, appearance), so the save
        /// loads it without the mod. The game's naming path can't be used: it re-mixes product + one mixer.
        /// </summary>
        public static void Name(Machine m, string name)
        {
            var st = m.Station;
            var op = st.CurrentMixOperation;
            if (op == null) return;
            var res = Result(op.ProductID, RunningChain(m, op));
            if (!res.Valid) return;
            if (res.Known == null)
            {
                var pm = NetworkSingleton<ProductManager>.Instance;
                if (!ProductManager.IsMixNameValid(name)) name = Singleton<Il2CppScheduleOne.UI.NewMixScreen>.Instance.GenerateUniqueName(null, res.Product.DrugType);
                string id = SaveRules.UniqueId(SaveRules.MixId(name), x => ProductExists(pm, x));
                var ids = new Il2CppList.List<string>();
                foreach (var e in res.Effects) ids.Add(e.ID);
                NetworkSingleton<Il2CppScheduleOne.Levelling.LevelManager>.Instance.AddXP(80);   // what the game gives for a new mix
                switch (res.Product.DrugType)
                {
                    case EDrugType.Marijuana: pm.CreateWeed_Server(name, id, EDrugType.Marijuana, ids, WeedDefinition.GetAppearanceSettings(res.Effects)); break;
                    case EDrugType.Methamphetamine: pm.CreateMeth_Server(name, id, EDrugType.Methamphetamine, ids, MethDefinition.GetAppearanceSettings(res.Effects)); break;
                    case EDrugType.Cocaine: pm.CreateCocaine_Server(name, id, EDrugType.Cocaine, ids, CocaineDefinition.GetAppearanceSettings(res.Effects)); break;
                    case EDrugType.Shrooms: pm.CreateShroom_Server(name, id, EDrugType.Shrooms, ids, ShroomDefinition.GetAppearanceSettings(res.Effects)); break;
                    default: Mod.Log.Warning($"{m.Tier.Name}: drug type {res.Product.DrugType} can't be named here"); return;
                }
                Mod.Log.Msg($"{m.Tier.Name}: named '{name}' ({id}): {res.Effects.Count} effects");
            }
            st.TryCreateOutputItems();
        }

        private static bool ProductExists(ProductManager pm, string id)
        {
            var all = pm?.AllProducts;
            if (all == null) return false;
            for (int i = 0; i < all.Count; i++) if (all[i] != null && all[i].ID == id) return true;
            return false;
        }

        /// <summary>Gives back what the game took for a batch the extra mixers can't match.</summary>
        private static void Refund(Machine m, MixOperation op, int n)
        {
            if (n <= 0) return;
            Return(m.Station.ProductSlot, op.ProductID, n, op.ProductQuality, true);
            Return(m.Station.MixerSlot, op.IngredientID, n, default, false);
        }

        private static void Return(ItemSlot slot, string id, int n, Il2CppScheduleOne.ItemFramework.EQuality quality, bool hasQuality)
        {
            var inst = Il2CppScheduleOne.Registry.GetItem(id)?.GetDefaultInstance(n);
            if (inst == null) { Mod.Log.Warning($"could not return {n}x {id}"); return; }
            if (hasQuality) inst.TryCast<QualityItemInstance>()?.SetQuality(quality);
            if (slot.ItemInstance == null) slot.SetStoredItem(inst);
            else if (slot.ItemInstance.CanStackWith(inst, false)) slot.ChangeQuantity(n);
            else Mod.Log.Warning($"could not return {n}x {id}: its slot holds something else now");
        }

        private static void Unlock(Machine m, ItemSlot slot)
        {
            var owner = slot.ActiveLock?.LockOwner;
            if (slot.IsLocked && (owner == null || owner == m.Station.NetworkObject)) slot.RemoveLock();
        }

        // ------------------------------------------------------------------ the host's records

        private static void Remember(Machine m, List<string> chain)
        {
            var data = MelangeMixersData.Current;
            if (data == null) return;
            var r = data.For(m.Guid);
            r.Chain = new List<string>(chain);
            Snapshot(m);
        }

        private static void Forget(Machine m)
        {
            var data = MelangeMixersData.Current;
            if (data == null) return;
            data.For(m.Guid).Chain.Clear();
            Snapshot(m);
        }

        /// <summary>The extra slots in the game's own slot-set format (items and player filters), so the game's loader reads them back.</summary>
        private static void Snapshot(Machine m)
        {
            var data = MelangeMixersData.Current;
            if (data == null || !m.Alive) return;
            try { data.For(m.Guid).Slots = new ItemSet(ExtraList(m)).GetJSON(); }
            catch (Exception e) { Mod.Log.Warning($"{m.Tier.Name}: could not record its slots: {e.Message}"); }
        }

        private static Il2CppList.List<ItemSlot> ExtraList(Machine m)
        {
            var list = new Il2CppList.List<ItemSlot>();
            foreach (var s in m.Extra) list.Add(s);
            return list;
        }

        /// <summary>Before the game writes the save: every machine's slots as they are now, and no records for machines that are gone.</summary>
        public static void BeforeSave()
        {
            var data = MelangeMixersData.Current;
            if (data == null || !Host.IsHost) return;
            Scan();
            var live = new HashSet<string>();
            foreach (var m in _machines.Values) if (m.Alive) { live.Add(m.Guid); Snapshot(m); }
            data.Stations = SaveRules.Prune(data.Stations, live);
        }

        /// <summary>Once the save has loaded (host): puts back each machine's extra slots, which the game's own data doesn't hold.</summary>
        public static void AfterLoad()
        {
            Scan();
            if (!Host.IsHost) return;
            _restored = true;
            var data = MelangeMixersData.Current;
            if (data == null) return;
            foreach (var m in _machines.Values)
            {
                if (!m.Alive || !data.Stations.TryGetValue(m.Guid, out var r) || r == null || string.IsNullOrEmpty(r.Slots)) continue;
                bool empty = true;
                foreach (var s in m.Extra) if (s.ItemInstance != null) empty = false;
                if (!empty) continue;                            // already there (a second load event)
                try
                {
                    var set = JsonUtility.FromJson(r.Slots, Il2CppInterop.Runtime.Il2CppType.Of<ItemSet>())?.TryCast<ItemSet>();
                    set?.LoadTo(ExtraList(m));
                    Refresh(m);
                    Mod.Log.Msg($"{m.Tier.Name} {m.Guid}: extra slots restored; chain {(r.Chain.Count > 0 ? string.Join(" + ", r.Chain) : "(idle)")}");
                }
                catch (Exception e) { Mod.Log.Warning($"{m.Tier.Name} {m.Guid}: could not restore its slots: {e.Message}"); }
            }
        }

        // ------------------------------------------------------------------ tick

        /// <summary>Twice a second: batch caps and speed everywhere; on the host, the extra slots' locks and finishing mixes that became known.</summary>
        public static void Tick()
        {
            float now = Time.unscaledTime;
            if (now < _nextTick) return;
            _nextTick = now + 0.5f;
            try
            {
                if (now >= _nextScan) { _nextScan = now + 5f; Scan(); }
                var dead = new List<IntPtr>();
                foreach (var kv in _machines)
                {
                    var m = kv.Value;
                    if (!m.Alive) { dead.Add(kv.Key); continue; }
                    Refresh(m);
                    Looks.Dress(m);
                    Mk2Screen.Show(m);
                    if (!Host.IsHost || !_restored) continue;
                    var op = m.Station.CurrentMixOperation;
                    if (op != null)
                    {
                        foreach (var s in m.Extra) if (!s.IsLocked) s.ApplyLock(m.Station.NetworkObject, LockReason);   // a handler's own lock can replace ours
                        var r = MelangeMixersData.Current?.For(m.Guid);
                        if (r != null && r.Chain.Count == 0) Remember(m, ChainFromSlots(m, op.IngredientID));        // a co-op client started it
                        if (IsDone(m.Station, op)) Complete(m);       // e.g. the result was named on another machine meanwhile
                    }
                    else foreach (var s in m.Extra) if (s.IsLocked && s.ActiveLock?.LockReason == LockReason) Unlock(m, s);
                }
                foreach (var p in dead) _machines.Remove(p);
            }
            catch (Exception e) { Mod.Log.Warning("mixers: " + e.Message); _nextTick = now + 10f; }
        }

        /// <summary>Finds placed machines the placement hook missed (it should miss none; this is the safety net).</summary>
        private static void Scan()
        {
            try
            {
                var props = Il2CppScheduleOne.Property.Property.OwnedProperties;
                if (props == null) return;
                for (int p = 0; p < props.Count; p++)
                {
                    var bi = props[p]?.BuildableItems;
                    if (bi == null) continue;
                    for (int i = 0; i < bi.Count; i++)
                    {
                        if (Tiers.ById(bi[i]?.ItemInstance?.ID) == null) continue;
                        var st = bi[i].TryCast<MixingStation>();
                        if (st != null && !_machines.ContainsKey(st.Pointer)) Attach(st);
                    }
                }
            }
            catch (Exception e) { Mod.Log.Warning("mixer scan: " + e.Message); }
        }
    }

    /// <summary>The Mk2's little screen shows the output it expects; the game works it out from product + slot-1 mixer, so a machine corrects it.</summary>
    internal static class Mk2Screen
    {
        public static void Show(Machine m)
        {
            try
            {
                var mk2 = m.Station.TryCast<MixingStationMk2>();
                var op = m.Station.CurrentMixOperation;
                if (mk2 == null || op == null || mk2.ScreenCanvas == null || !mk2.ScreenCanvas.enabled) return;
                var res = Stations.Result(op.ProductID, Stations.RunningChain(m, op));
                if (!res.Valid) return;
                bool known = res.Known != null;
                mk2.OutputIcon.sprite = known ? res.Known.Icon : res.Product.Icon;
                mk2.OutputIcon.color = known ? Color.white : Color.black;
                mk2.QuestionMark.gameObject.SetActive(!known);
            }
            catch { }
        }
    }
}
