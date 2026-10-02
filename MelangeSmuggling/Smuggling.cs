using System;
using System.Collections.Generic;
using Melange.Core;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.UI;
using UnityEngine;

namespace Melange.Smuggling
{
    /// <summary>
    /// Runs the boat in the game: feeds the clock, Oscar's checkouts, the player's deliveries and Dafydd's conversations
    /// to the rules in Logic/, and carries out what they decide (texts, payments, the boat sailing and coming back). The
    /// host decides everything; a co-op client sees the boat and Dafydd, gets his texts through the game's networking, and
    /// is told to leave the loading to the host.
    /// </summary>
    internal static class Smuggling
    {
        /// <summary>The save's state on the host once a save has loaded; null otherwise (clients, the menu).</summary>
        public static SmugglingState State { get; private set; }

        private static SmugglingRules _rules = new SmugglingRules();
        private static readonly List<(ImportOffer Offer, string Name)> _imports = new List<(ImportOffer, string)>();
        private static readonly System.Random Rng = new System.Random();
        private static object _loop;
        private static bool _noProductSaid;

        public static void Start()
        {
            OscarLead.Start();
            Events.Subscribe<SaveLoaded>(_ => OnSaveLoaded());
            Events.Subscribe<MenuLoaded>(_ => OnMenu());
            // a lasting fact from the sewer spoke, published when it happens and again after every load
            Events.Subscribe<BootleggersRouteRevealed>(e =>
            {
                var s = MelangeSmugglingData.Current?.State;
                if (s == null || !Host.IsHost) return;
                if (!s.RouteKnown) Mod.Log.Msg($"the bootleggers' route is known ({(e.FromJournal ? "the journal" : "the King showed it")}): deliveries that way carry no police risk");
                s.RouteKnown = true;
                s.RouteFromJournal = e.FromJournal;
            });
            Events.Subscribe<DayPassed>(e => { if (State != null) Tanker.DayPassed(State, _rules, Rank(), e.Day, Rng); });
        }

        private static void OnMenu()
        {
            State = null;
            MelangeSmugglingData.Current?.ResetToDefaults();
            if (_loop != null) { MelonLoader.MelonCoroutines.Stop(_loop); _loop = null; }
            Boat.Forget();
            Route.Forget();
            OscarLead.Forget();
            Tanker.Forget();
            Dafydd.Forget();
            _noProductSaid = false;
            _forcedRisk = null;
            LastRoll = "no delivery yet";
        }

        private static void OnSaveLoaded()
        {
            if (!Host.IsHost)
            {
                Mod.Log.Msg("co-op client: the host runs the boat");
                Boat.Place(true, () => Notify("Turnip Night", Lines.HostOnly));
                return;
            }
            var data = MelangeSmugglingData.Current;
            if (data == null) { Mod.Log.Warning("no smuggling save data (S1API made none); the boat is off this session"); return; }
            data.State.Repair();
            State = data.State;
            _rules = Settings.Rules();
            BuildImports();

            Unlock.EnsureThreshold(State, _rules, Rng);
            if (Unlock.CatchUp(State)) Mod.Log.Msg("the purchase count had already passed the threshold: unlocked on load");
            OscarLead.Hook();
            Boat.Place(State.Boat == BoatPhase.Moored, Interacted);
            if (Dafydd.Instance == null) Mod.Log.Msg("Dafydd isn't spawned yet; he's wired up when S1API makes him");
            EnsureDafydd();

            Mod.Log.Msg($"loaded: Oscar {State.QualifyingPurchases}/{State.UnlockThreshold}, unlocked {State.Unlocked}, route {State.RouteKnown}, " +
                        $"reputation {State.Reputation}, boat {State.Boat}, order {(State.Order == null ? "none" : Orders.Describe(State.Order))}, aboard {State.UnitsAboard}, runs {State.RunsPaid}/{State.RunsSailed}");
            _loop = MelonLoader.MelonCoroutines.Start(Loop());
        }

        private static void BuildImports()
        {
            _imports.Clear();
            foreach (var offer in Settings.Catalogue())
            {
                if (!Cargo.Exists(offer.ItemId)) { Mod.Log.Warning($"import {offer.ItemId}: no such item in this game; left out"); continue; }
                _imports.Add((offer, Cargo.NameOf(offer.ItemId)));
            }
        }

        private static System.Collections.IEnumerator Loop()
        {
            int beat = 0;
            while (State != null)
            {
                float until = Time.realtimeSinceStartup + 1f;
                while (Time.realtimeSinceStartup < until) yield return null;
                if (State == null) yield break;
                try
                {
                    EnsureDafydd();
                    Route.Tick();
                    Tanker.Tick(State);
                    if (beat++ % 2 == 0) Advance();
                    Boat.SetMessage(Prompt());
                }
                catch (Exception e) { Mod.Log.Warning("smuggling loop: " + e.Message); }
            }
        }

        // ---- the clock ----

        private static int Now() => NetworkSingleton<TimeManager>.Instance.GetTotalMinSum();
        private static int TimeOfDay() => NetworkSingleton<TimeManager>.Instance.CurrentTime;

        private static int Rank()
        {
            try { return (int)NetworkSingleton<LevelManager>.Instance.Rank; }
            catch { return 0; }
        }

        private static void Advance()
        {
            var events = Voyage.Tick(State, Now(), Rank(), Cargo.Market, _rules, Rng);
            foreach (var e in events)
            {
                Mod.Log.Msg($"voyage: {e}");
                try { Carry(e); }
                catch (Exception ex) { Mod.Log.Error($"voyage event {e}: {ex}"); }
            }
        }

        private static void Carry(VoyageEvent e)
        {
            var dafydd = Dafydd.Instance;
            switch (e.Kind)
            {
                case VoyageEventKind.OrderPosted:
                    _noProductSaid = false;
                    dafydd?.TextOrder(e.Order, Now());
                    break;
                case VoyageEventKind.NoProduct:
                    if (!_noProductSaid) dafydd?.Text(Lines.NoProduct);
                    _noProductSaid = true;
                    break;
                case VoyageEventKind.Departed:
                    Boat.Sail();
                    if (e.Settlement.Payout > 0f)
                        NetworkSingleton<MoneyManager>.Instance.CreateOnlineTransaction("Turnip Night", e.Settlement.Payout, 1f, $"Export run #{e.Order.Id}");
                    dafydd?.Text(Lines.Departed(e.Settlement));
                    break;
                case VoyageEventKind.Returned:
                    Boat.Show(true);
                    dafydd?.Text(Lines.Returned(e.ImportsLanded));
                    break;
            }
        }

        // ---- Oscar ----

        public static void OscarPurchase(float spend)
        {
            if (!Host.IsHost) return;
            var s = State;
            if (s == null) return;
            bool justUnlocked = Unlock.RecordPurchase(s, spend, _rules, Rng);
            Mod.Log.Msg($"Oscar checkout ${spend:0}: {(Unlock.Counts(spend, _rules.QualifyingSpend) ? "counts" : "too small")}, {s.QualifyingPurchases}/{s.UnlockThreshold}");
            if (!justUnlocked) return;
            OscarDialogue.Say(Lines.OscarLead, 8f);
            Introduce();
        }

        /// <summary>
        /// S1API may spawn its NPCs after the save reports loaded, so Dafydd is wired (and his first text sent) whenever he
        /// turns up, not only at load. Both are idempotent.
        /// </summary>
        private static void EnsureDafydd()
        {
            var d = Dafydd.Instance;
            if (d == null || State == null) return;
            d.Wire(_imports);
            if (State.Unlocked && !State.Introduced) Introduce();
        }

        private static void Introduce()
        {
            var d = Dafydd.Instance;
            if (State.Introduced || d == null) return;              // sent once he exists (EnsureDafydd retries)
            State.Introduced = true;
            d.Text(Lines.Intro);
            Mod.Log.Msg("Dafydd's number passed on");
        }

        // ---- the player's side ----

        public static void Answer(int orderId, bool accept)
        {
            if (!Host.IsHost || State == null) return;
            var o = State.Order;
            bool hadProduct = State.UnitsAboard > 0;
            if (!Voyage.Answer(State, orderId, accept, Now(), _rules))
            {
                if (!accept && hadProduct && o != null && o.Id == orderId) Dafydd.Instance?.Text(Lines.DeclineTooLate);
                return;
            }
            Mod.Log.Msg($"order #{orderId} {(accept ? "accepted" : "declined")}");
            Dafydd.Instance?.Text(accept ? Lines.AcceptReply : Lines.DeclineReply);
        }

        public static string StatusLine()
        {
            if (!Host.IsHost) return Lines.HostOnly;
            if (State == null || !State.Unlocked) return Lines.Stranger;
            return Lines.Status(State, Now());
        }

        /// <summary>One delivery: everything matching on the player and in the trunk by the boat, one police roll.</summary>
        public static string LoadHold()
        {
            if (!Host.IsHost) return Lines.HostOnly;
            var s = State;
            if (s == null || !s.Unlocked) return Lines.Stranger;
            if (s.Boat == BoatPhase.Away) return Lines.AtSea;
            if (s.Order == null) return Lines.NoOrder;
            int now = Now();
            var picks = Cargo.Plan(s, now, _rules);
            int units = 0;
            foreach (var p in picks) units += p.Units;
            if (units <= 0) return Lines.NothingToLoad;

            bool via = ViaRoute();
            float risk = Risk.PoliceRisk(units, TimeOfDay(), via, _rules);
            float? forced = _forcedRisk;                             // TEST ONLY: the probe's "risk" override, one delivery
            _forcedRisk = null;
            if (forced.HasValue) risk = Risk.Forced(forced.Value, via, _rules);
            Cargo.Take(picks);
            units = 0;
            foreach (var p in picks) units += p.Units;              // a stack that couldn't be taken counts for nothing
            double roll = Rng.NextDouble();
            bool seized = Risk.Seized(risk, roll);
            LastRoll = $"roll {roll:0.000} vs risk {risk:0.000}{(forced.HasValue ? $" (FORCED street {forced.Value:0.00}, test only)" : "")}, " +
                       $"route known {s.RouteKnown}, via {via}, {(seized ? "SEIZED" : "passed")}";
            Mod.Log.Msg($"delivery police roll: {LastRoll}");
            if (seized)
            {
                s.Seizures++;
                Mod.Log.Msg($"delivery of {units} seized (risk {Risk.Percent(risk)})");
                Notify("Seized", Lines.Seized(units));
                return Lines.Seized(units);
            }
            foreach (var p in picks) Voyage.Load(s, p.ProductId, p.ProductName, p.Quality, p.Units);
            Mod.Log.Msg($"loaded {units} (risk {Risk.Percent(risk)}{(via ? ", via the route" : "")}), {s.UnitsAboard}/{s.Order.Units} aboard");
            return Lines.Loaded(units, s.UnitsAboard, s.Order.Units, risk, via);
        }

        /// <summary>TEST ONLY: a street risk the next delivery uses instead of the computed one (set by the probe's "risk").</summary>
        private static float? _forcedRisk;

        /// <summary>The last delivery's police roll, for the probes.</summary>
        internal static string LastRoll { get; private set; } = "no delivery yet";

        private static bool ViaRoute() => State != null && Risk.ViaRoute(State.RouteKnown, Route.SecondsSinceOnRoute, Route.DroveSince, _rules);

        public static string CollectImports()
        {
            if (!Host.IsHost) return Lines.HostOnly;
            var s = State;
            if (s == null || !s.Unlocked) return Lines.Stranger;
            if (s.ImportsWaiting.Count == 0) return Lines.NothingWaiting;
            int given = 0, left = 0;
            foreach (var line in s.ImportsWaiting.ToArray())
            {
                int n = Cargo.Give(line.ItemId, line.Quantity);
                Imports.Collected(s, line.ItemId, n);
                given += n;
                left += line.Quantity;                               // what's left of this line after collecting
            }
            Mod.Log.Msg($"imports collected: {given}, {left} still waiting");
            return given == 0 ? "No room on you, butt." : Lines.Collected(given, left);
        }

        public static string BuyImport(ImportOffer offer, string name)
        {
            if (!Host.IsHost) return Lines.HostOnly;
            var s = State;
            if (s == null || !s.Unlocked) return Lines.Stranger;
            if (!Imports.Open(s, _rules)) return s.Boat == BoatPhase.Away ? Lines.AtSea : Lines.ImportsClosed;
            float price = Imports.CratePrice(offer, s.Reputation, _rules);
            var money = NetworkSingleton<MoneyManager>.Instance;
            if (money == null || money.cashBalance < price) return Lines.ImportNoCash + $" (${price:0})";
            money.ChangeCashBalance(-price, true, true);
            Imports.AddOrdered(s, offer, name, price);
            Mod.Log.Msg($"import ordered: {offer.Crate} x {offer.ItemId} for ${price:0}");
            return Lines.ImportBought(name, offer.Crate) + $" ${price:0}.";
        }

        // ---- the boat's prompt ----

        private static void Interacted()
        {
            string said = State != null && State.ImportsWaiting.Count > 0 ? CollectImports() : LoadHold();
            if (Dafydd.Instance != null) Dafydd.Instance.Say(said);
            else Notify("Turnip Night", said);
        }

        private static string Prompt()
        {
            var s = State;
            if (s == null || !s.Unlocked) return "Somebody's speedboat";
            if (s.ImportsWaiting.Count > 0) return "Collect your crates";
            if (s.Order == null) return "Turnip Night's boat (no order on)";
            var picks = Cargo.Plan(s, Now(), _rules);
            int units = 0;
            foreach (var p in picks) units += p.Units;
            if (units == 0) return $"Turnip Night's boat ({s.UnitsAboard}/{s.Order.Units} aboard)";
            bool via = ViaRoute();
            float risk = Risk.PoliceRisk(units, TimeOfDay(), via, _rules);
            return $"Load {units} units ({Risk.Percent(risk)} police risk{(via ? ", the old way" : "")})";
        }

        // ---- probes (SmugglingCommand) ----

        public const string ProbeUsage = "status, oscar <spend>, unlock, order, accept, decline, load, sail, return, collect, imports, import <item> [crates], " +
                                         "route [known|unknown|walked], risk <0..1|off> (test only), tanker, boat";

        public static string Probe(string what, List<string> args)
        {
            string arg = args != null && args.Count > 0 ? args[0] : null;
            if (!Host.IsHost) return "host only";
            var s = State;
            if (s == null) return "no save loaded (or no smuggling data)";
            int now = Now();
            switch (what)
            {
                case "status":
                    return $"day {Timing.DayOf(now)} {Timing.Clock(TimeOfDay())}; Oscar {s.QualifyingPurchases}/{s.UnlockThreshold}, unlocked {s.Unlocked}, introduced {s.Introduced}; " +
                           $"route {s.RouteKnown} (journal {s.RouteFromJournal}); reputation {s.Reputation}; boat {s.Boat} back {s.BoatBackAt}; next order day {s.NextOrderDay}; " +
                           $"order {(s.Order == null ? "none" : $"#{s.Order.Id} {Orders.Describe(s.Order)}, {s.Order.Answer}, sails {Timing.Left(s.Order.DepartsAt - now)}")}; " +
                           $"aboard {s.UnitsAboard} in {s.Hold.Count} lots; imports ordered {s.ImportsOrdered.Count}, waiting {s.ImportsWaiting.Count}; " +
                           $"runs {s.RunsPaid}/{s.RunsSailed}, paid ${s.TotalPaid:0}, seizures {s.Seizures}; Dafydd {(Dafydd.Instance != null ? "spawned" : "missing")}; " +
                           $"market [{string.Join(", ", Cargo.Market().ConvertAll(m => $"{m.DrugType} ${m.UnitValue:0.00}"))}]; imports offered {_imports.Count}";
                case "oscar":
                    float spend = 300f;
                    if (arg != null) float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out spend);
                    OscarPurchase(spend);
                    return $"{s.QualifyingPurchases}/{s.UnlockThreshold}, unlocked {s.Unlocked}";
                case "unlock":
                    Unlock.EnsureThreshold(s, _rules, Rng);
                    s.QualifyingPurchases = System.Math.Max(s.QualifyingPurchases, s.UnlockThreshold - 1);
                    OscarPurchase(_rules.QualifyingSpend);
                    return $"unlocked {s.Unlocked}, introduced {s.Introduced}, next order day {s.NextOrderDay}";
                case "order":
                    if (s.Order != null) return "an order is already on";
                    if (s.Boat != BoatPhase.Moored) return "the boat is at sea";
                    s.NextOrderDay = Timing.DayOf(now) - 1;           // due: the next tick posts it
                    Advance();
                    return s.Order == null ? "no order (nothing to sell?)" : $"#{s.Order.Id} {Orders.Describe(s.Order)}";
                case "accept":
                case "decline":
                    if (s.Order == null) return "no order";
                    Answer(s.Order.Id, what == "accept");
                    return s.Order == null ? "declined" : s.Order.Answer.ToString();
                case "load":
                {
                    string rollBefore = LastRoll;
                    string said = LoadHold();
                    return $"{said} [{(ReferenceEquals(rollBefore, LastRoll) ? "no roll" : LastRoll)}]";
                }
                case "collect":
                {
                    // the boat prompt's and Dafydd's "collect" path; reports what reached the pockets and the room left
                    string before = ImportList(s.ImportsWaiting);
                    var counts = new Dictionary<string, int>();
                    foreach (var l in s.ImportsWaiting) counts[l.ItemId] = Cargo.CountOnPlayer(l.ItemId);
                    int freeBefore = Cargo.FreePocketSlots();
                    string said = CollectImports();
                    var got = new List<string>();
                    foreach (var kv in counts) got.Add($"{kv.Key} {kv.Value} -> {Cargo.CountOnPlayer(kv.Key)}");
                    return $"{said} | waiting before [{before}], after [{ImportList(s.ImportsWaiting)}] | pockets [{string.Join(", ", got)}], " +
                           $"free slots {freeBefore} -> {Cargo.FreePocketSlots()}";
                }
                case "imports":
                {
                    var money = NetworkSingleton<MoneyManager>.Instance;
                    var offers = new List<string>();
                    foreach (var (offer, name) in _imports)
                        offers.Add($"{offer.ItemId} \"{name}\": crate of {offer.Crate} at ${offer.UnitPrice:0.##} each, ${Imports.CratePrice(offer, s.Reputation, _rules):0} a crate now");
                    string open = Imports.Open(s, _rules) ? "open"
                        : $"closed (unlocked {s.Unlocked}, runs paid {s.RunsPaid}/{_rules.ImportsAfterRuns} needed, boat {s.Boat})";
                    return $"{open}; reputation {s.Reputation} (discount up to {_rules.MaxImportDiscount:P0}); cash ${(money == null ? 0f : money.cashBalance):0}; " +
                           $"offered [{string.Join("; ", offers)}]; ordered [{ImportList(s.ImportsOrdered)}]; waiting [{ImportList(s.ImportsWaiting)}]";
                }
                case "import":
                {
                    if (arg == null) return "import <item> [crates]; see 'smuggling imports'";
                    int crates = 1;
                    if (args.Count > 1 && (!int.TryParse(args[1], out crates) || crates < 1 || crates > 20)) return "crates: 1-20";
                    int found = FindImport(arg);
                    if (found < 0) return $"no import '{arg}' (offered: {string.Join(", ", _imports.ConvertAll(i => i.Offer.ItemId))})";
                    var (offer, name) = _imports[found];
                    var money = NetworkSingleton<MoneyManager>.Instance;
                    float cash = money == null ? 0f : money.cashBalance;
                    var said = new List<string>();
                    // the same call as Dafydd's "Bring something back" choice (msm_imp_<i> in Dafydd.Wire), once per crate
                    for (int i = 0; i < crates; i++) said.Add(BuyImport(offer, name));
                    float after = money == null ? 0f : money.cashBalance;
                    return $"{string.Join(" | ", said)} | cash ${cash:0} -> ${after:0}; ordered [{ImportList(s.ImportsOrdered)}] (lands on the next return)";
                }
                case "risk":
                    // TEST ONLY: forces the street police risk of the next delivery (the route multiplier still applies)
                    if (arg == null) return $"forced {(_forcedRisk.HasValue ? _forcedRisk.Value.ToString("0.00") : "none")}; last {LastRoll}";
                    if (arg == "off") { _forcedRisk = null; return "forced risk cleared"; }
                    if (!float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float forced) || forced < 0f || forced > 1f)
                        return "risk <0..1|off>";
                    _forcedRisk = forced;
                    return $"TEST ONLY: the next delivery's street risk is forced to {forced:0.00} (x{_rules.RouteRiskMultiplier:0.##} via the route); one delivery, then back to normal";
                case "sail":
                    if (s.Order == null) return "no order to sail with";
                    s.Order.DepartsAt = now;
                    Advance();
                    return $"boat {s.Boat}, back at {Timing.Clock(Timing.HhmmOf(s.BoatBackAt))}";
                case "return":
                    if (s.Boat != BoatPhase.Away) return "the boat is in";
                    s.BoatBackAt = now;
                    Advance();
                    return $"boat {s.Boat}, imports waiting [{ImportList(s.ImportsWaiting)}]";
                case "route":
                    if (arg == "known") s.RouteKnown = true;     // for testing without the sewer spoke; the real fact comes from the hub event
                    else if (arg == "unknown") s.RouteKnown = false;   // TEST ONLY: undoes "known" (saved with the game)
                    else if (arg == "walked") Route.PretendWalked();   // TEST ONLY: as if just off the route on foot
                    else if (arg != null) return "route [known|unknown|walked]";
                    var me = Il2CppScheduleOne.PlayerScripts.Player.Local;
                    var p = me != null ? me.transform.position : UnityEngine.Vector3.zero;
                    return $"known {s.RouteKnown}; at ({p.x:0.0},{p.y:0.0},{p.z:0.0}) on route {Quay.OnRoute(p.x, p.y, p.z)}; " +
                           $"since {Route.SecondsSinceOnRoute:0}s, drove since {Route.DroveSince}, via {ViaRoute()}";
                case "tanker":
                    return Tanker.Force(s);
                case "boat":
                    return Boat.Describe();
                default:
                    return "unknown; try " + ProbeUsage;
            }
        }

        private static string ImportList(List<ImportLine> lines)
            => string.Join(", ", lines.ConvertAll(l => $"{l.Quantity} x {l.ItemId} (${l.Paid:0})"));

        /// <summary>An offer by item ID, else by a name that contains the words (case and spacing ignored).</summary>
        private static int FindImport(string want)
        {
            string w = want.Replace(" ", "").ToLowerInvariant();
            for (int i = 0; i < _imports.Count; i++) if (string.Equals(_imports[i].Offer.ItemId, want, StringComparison.OrdinalIgnoreCase)) return i;
            for (int i = 0; i < _imports.Count; i++) if ((_imports[i].Name ?? "").Replace(" ", "").ToLowerInvariant().Contains(w)) return i;
            return -1;
        }

        internal static void Notify(string title, string text)
        {
            try { Singleton<NotificationsManager>.Instance.SendNotification(title, text, null, 6f, true); }
            catch (Exception e) { Mod.Log.Msg($"{title}: {text} (notification failed: {e.Message})"); }
        }
    }
}
