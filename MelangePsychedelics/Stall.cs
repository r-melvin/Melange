using System;
using S1API.Interaction;
using UnityEngine;

namespace Melange.Psychedelics
{
    /// <summary>
    /// Randy's stall, round the back of Randy's Bait &amp; Tackle at the Docks (beside the game's "Behind Randy's bait &amp;
    /// tackle" dead drop: the shop is an NPC building with no shop screen, so the stall is ours). By day: terrariums, crickets
    /// and nets. At night, for a couple of hours: toads, at a steep markup that climbs with each one sold.
    /// Each purchase is the buyer's own (cash and pockets are per player), so the stall works for co-op clients too; the
    /// night's count is kept in memory only.
    /// </summary>
    internal static class Stall
    {
        private const float ScanEvery = 1f;
        private static float _next;
        private static GameObject _root;
        private static InteractionPrompt _left, _middle, _right, _sign;
        private static int _night = -1, _sold;
        private static Mode _mode = (Mode)(-1);

        private enum Mode { Closed, Day, Night }

        public static void Reset() { _next = 0f; _root = null; _left = _middle = _right = _sign = null; _night = -1; _sold = 0; _mode = (Mode)(-1); }

        public static void Tick()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + ScanEvery;
            try
            {
                if (!Settings.Stall || Il2CppScheduleOne.PlayerScripts.Player.Local == null) return;
                if (_root == null && !Build()) { _next = Time.unscaledTime + 60f; return; }
                int day = Placed.Day, minute = Clock.ToMinutes(S1API.GameTime.TimeManager.CurrentTime);
                int night = RandysStall.NightOpen(day, minute, Settings.Night);
                if (night >= 0 && night != _night) { _night = night; _sold = 0; }
                var mode = night >= 0 ? Mode.Night : RandysStall.DayOpenAt(minute) ? Mode.Day : Mode.Closed;
                if (mode != _mode || mode == Mode.Night) Label(mode);
                _mode = mode;
            }
            catch (Exception e) { Mod.Log.Warning("Randy's stall: " + e.Message); _next = Time.unscaledTime + 30f; }
        }

        private static bool Build()
        {
            var drop = S1API.DeadDrops.DeadDropManager.Get<S1API.DeadDrops.Native.BehindRandysBaitAndTackle>();
            if (drop == null) { Mod.Log.Warning("Randy's dead drop not found; no stall this session"); return false; }
            var at = Looks.Ground(drop.Position + new Vector3(1.6f, 0f, 0f));
            _root = new GameObject("Melange Randy's stall");
            _root.transform.position = at;
            Looks.Box(_root.transform, "crates", new Vector3(1.2f, 0.8f, 0.6f), new Color(0.55f, 0.42f, 0.28f)).transform.localPosition = new Vector3(0f, 0.4f, 0f);
            Looks.Box(_root.transform, "sign", new Vector3(0.6f, 0.35f, 0.04f), new Color(0.9f, 0.85f, 0.6f)).transform.localPosition = new Vector3(0f, 1.2f, 0f);
            _left = Prompt("left", new Vector3(-0.4f, 0.85f, 0f), () => Buy(Slot.Left));
            _middle = Prompt("middle", new Vector3(0f, 0.85f, 0f), () => Buy(Slot.Middle));
            _right = Prompt("right", new Vector3(0.4f, 0.85f, 0f), () => Buy(Slot.Right));
            _sign = Prompt("sign", new Vector3(0f, 1.2f, 0f), Ask);
            Mod.Log.Msg($"Randy's stall at {at}");
            return true;
        }

        private static InteractionPrompt Prompt(string name, Vector3 local, Action act)
        {
            var go = new GameObject("Melange stall " + name);
            go.transform.SetParent(_root.transform, false);
            go.transform.localPosition = local;
            Looks.Interactable(go, Vector3.zero, new Vector3(0.38f, 0.3f, 0.7f));
            return InteractionPrompt.CreateBuilder(go).WithMessage("…").WithRange(2.5f).WithPriority(5).OnInteractionStarted(act).Build();
        }

        private enum Slot { Left, Middle, Right }

        private static (string id, string what, int qty, float price, int tier)? Offer(Slot slot)
        {
            switch (_mode)
            {
                case Mode.Day:
                    switch (slot)
                    {
                        case Slot.Left: return (Ids.Terrarium, "Terrarium", 1, Items.TerrariumPrice, -1);
                        case Slot.Middle: return (Ids.Crickets, "Crickets x5", 5, Items.CricketsPrice * 5, -1);
                        default: return (Ids.ToadNet, "Toad Net", 1, Items.NetPrice, -1);
                    }
                case Mode.Night:
                    if (slot != Slot.Middle) return null;
                    if (_sold >= RandysStall.Stock(Wild.Seed, _night)) return null;
                    return (Ids.LiveToad, "Toad", 1, RandysStall.ToadPrice(Items.ToadValue, Settings.Markup, _sold), LiveToads.ItemTier(ToadOrigin.BlackMarket));
                default: return null;
            }
        }

        private static void Label(Slot slot, InteractionPrompt p)
        {
            if (p == null) return;
            var o = Offer(slot);
            p.SetMessage(o == null ? (_mode == Mode.Night ? (slot == Slot.Middle ? "Sold out tonight" : "Just toads tonight") : "Closed: opens 7 am")
                                   : $"Buy {o.Value.what} (${o.Value.price:N0})");
            p.SetState(o == null ? InteractionPromptState.Invalid : InteractionPromptState.Default);
        }

        private static void Label(Mode mode)
        {
            _mode = mode;
            Label(Slot.Left, _left); Label(Slot.Middle, _middle); Label(Slot.Right, _right);
            _sign?.SetMessage(mode == Mode.Night ? "Randy (round the back)" : "Ask Randy about toads");
        }

        private static void Buy(Slot slot)
        {
            var o = Offer(slot);
            if (o == null) return;
            var (id, what, qty, price, tier) = o.Value;
            if (S1API.Money.Money.GetCashBalance() < price) { Items.Notify("Randy", $"Cash only. That's ${price:N0}."); return; }
            if (!Items.Give(Items.Make(id, qty, tier))) { Items.Notify("Randy", "Your pockets are full."); return; }
            S1API.Money.Money.ChangeCashBalance(-price, true, true);
            if (id == Ids.LiveToad) _sold++;
            Label(_mode);
            Mod.Log.Msg($"Randy's stall: bought {what} for {price:N0}");
        }

        /// <summary>Randy's advice: how toads are kept, and where the good ones are, by how far the sewer story has gone.</summary>
        private static void Ask()
        {
            var data = MelangePsychedelicsData.Current;
            var route = data?.Route(Wild.SewerUnlocked()) ?? SewerRoute.Closed;
            string where = route switch
            {
                SewerRoute.Mentor => "Heard the old man under the streets showed you his spot. Lucky. Best toads in town down there.",
                SewerRoute.Hard => "The sewer ones, by the mushrooms. Best in town. Bring a torch.",
                _ => "The pond, evenings. Mind the wildlife bloke, he does a lap. Word is the best ones are in the sewers, if you can get in.",
            };
            Items.Notify("Randy", "One tub of crickets per toad a day. Two fed toads together breed. " + where);
        }

        // ------------------------------------------------------------------ probes (Probe.cs)

        /// <summary>Runs the scan now, so a probe right after a settime sees the stall as it is.</summary>
        private static void Refresh() { _next = 0f; Tick(); }

        internal static string ProbeStatus()
        {
            if (!Settings.Stall) return "Randy's stall off";
            Refresh();
            if (_root == null) return "Randy's stall not built";
            int stock = _night >= 0 ? RandysStall.Stock(Wild.Seed, _night) : 0;
            var toad = _mode == Mode.Night ? Offer(Slot.Middle) : null;
            return $"Randy {_mode}" + (_mode == Mode.Night ? $" (night of day {_night}), toad {(toad == null ? "sold out" : $"${toad.Value.price:N0}")}, {Math.Max(0, stock - _sold)}/{stock} left tonight" : "");
        }

        /// <summary>Buys from the stall through the prompt's own handler: terrarium, crickets, net (by day) or toad (at night).</summary>
        internal static string ProbeBuy(string item)
        {
            if (!Settings.Stall) return "Randy's stall off";
            Refresh();
            if (_root == null) return "Randy's stall not built";
            Slot slot; string id;
            switch (item)
            {
                case "terrarium": slot = Slot.Left; id = Ids.Terrarium; break;
                case "crickets": slot = Slot.Middle; id = Ids.Crickets; break;
                case "net": slot = Slot.Right; id = Ids.ToadNet; break;
                case "toad": slot = Slot.Middle; id = Ids.LiveToad; break;
                default: return "randy <terrarium|crickets|net|toad>";
            }
            var o = Offer(slot);
            if (o == null || o.Value.id != id) return $"{item} not on sale now (stall {_mode}{(o == null && _mode == Mode.Night && id == Ids.LiveToad ? ", sold out" : "")})";
            float cash = S1API.Money.Money.GetCashBalance();
            int had = Items.Count(id);
            Buy(slot);
            return $"{o.Value.what} at ${o.Value.price:N0}: cash {cash:N0} -> {S1API.Money.Money.GetCashBalance():N0}, {id} in pockets {had} -> {Items.Count(id)}; {ProbeStatus()}";
        }
    }
}
