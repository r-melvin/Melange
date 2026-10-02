using System;
using System.Collections.Generic;
using Melange.Core;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Dialogue;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.NPCs.CharacterClasses;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI;
using S1API.Quests;
using UnityEngine;

namespace Melange.Sewer
{
    /// <summary>
    /// Runs "Down the Drain" in the game: reports facts to <see cref="SewerQuest"/> (rank, key, goblin, clues, the King's
    /// fate) and carries out the effects it returns. The host decides everything; a co-op client keeps the vanilla sewer
    /// apart from Oscar's rewritten lines (see TESTING.md).
    /// </summary>
    internal static partial class Story
    {
        /// <summary>The live quest logic on the host once a save has loaded; null otherwise (the King patch then stays out of the way).</summary>
        public static SewerQuest Quest { get; private set; }

        private const float GoblinDelay = 30f;            // seconds inside the sewer before the scripted visit
        private const float GoblinRetry = 60f;

        private static SewerManager _sewer;
        private static Talk _jerry, _frank, _king;
        private static Spot _journal, _plaque, _stash;
        private static IntPtr _goblinHooked;
        private static object _loop;
        private static float _inSewerSince = -1f, _lastGoblinTry = -1000f;
        private static bool _kingSpokeThisVisit;
        private static int _rumourRoll;
        private static readonly System.Random Rng = new System.Random();

        public static void Start()
        {
            OscarHints();
            ProbeStart();
            Events.Subscribe<SaveLoaded>(_ => OnSaveLoaded());
            Events.Subscribe<MenuLoaded>(_ => OnMenu());
            Events.Subscribe<TierReached>(e =>
            {
                if (Quest != null && SewerQuest.RankTriggers((int)e.Reached.Rank, e.Reached.Tier)) Apply(Quest.Trigger(), "rank " + e.Reached);
            });
            Events.Subscribe<MethylamineUnlocked>(_ => { if (Quest != null) Apply(Quest.Trigger(), "methylamine"); });
        }

        /// <summary>Oscar no longer hints at sewer keys: his two hint lines become smuggling-ring talk (through the hub's service).</summary>
        private static void OscarHints()
        {
            OscarDialogue.RewriteLine("sewer.keyhints", (label, original, current) =>
            {
                if (original == null) return null;
                if (original.Contains("<REGION>")) return SewerLines.OscarRegionHint;
                if (original.Contains("<NPC_DESCRIPTION>")) return SewerLines.OscarNpcHint;
                return null;
            });
            // whatever choice leads to those hints asks about a sewer key; it now asks about his stock
            OscarDialogue.RewriteChoice("sewer.keyhints", text =>
                text != null && text.IndexOf("sewer", StringComparison.OrdinalIgnoreCase) >= 0 && text.IndexOf("key", StringComparison.OrdinalIgnoreCase) >= 0
                    ? SewerLines.OscarChoice : null);
        }

        private static void OnMenu()
        {
            Quest = null;
            MelangeSewerData.Current?.Reset();
            DownTheDrain.Forget();
            _sewer = null; _jerry = _frank = _king = null; Jen.Forget(); _journal = _plaque = _stash = null;
            _goblinHooked = IntPtr.Zero; _inSewerSince = -1f; _kingSpokeThisVisit = false;
            if (_loop != null) { MelonLoader.MelonCoroutines.Stop(_loop); _loop = null; }
        }

        private static void OnSaveLoaded()
        {
            if (!Host.IsHost) { Mod.Log.Msg("co-op client: the host runs the sewer story"); return; }
            var data = MelangeSewerData.Current;
            if (data == null) { Mod.Log.Warning("no sewer save data (S1API made none); the story is off this session"); return; }
            _sewer = NetworkSingleton<SewerManager>.Instance;
            if (_sewer == null) { Mod.Log.Warning("no SewerManager in this scene; the story is off"); return; }
            Quest = new SewerQuest(data.State);
            Mod.Log.Msg($"loaded: step {Quest.Step}, triggered {data.State.Triggered}, started {data.State.QuestStarted}, fate {data.State.Fate}, deal {data.State.Deal}");

            WireTalks();
            HookGoblin();

            // catch up on what happened while the mod wasn't looking
            var lm = NetworkSingleton<LevelManager>.Instance;
            if (lm != null && SewerQuest.RankTriggers((int)lm.Rank, lm.Tier)) Apply(Quest.Trigger(), "rank on load");
            PollFacts();

            if (data.State.QuestStarted && !data.State.Completed && DownTheDrain.Current == null) QuestManager.CreateQuest<DownTheDrain>();
            if (data.State.Completed && DownTheDrain.Current != null) { DownTheDrain.Current.Complete(); DownTheDrain.Forget(); }
            if (data.State.Fate == KingFate.Spared && data.State.Deal == KingDeal.Underboss) Managers.Register(new SewerKingManager());
            Republish(data.State);
            Refresh();
            _loop = MelonLoader.MelonCoroutines.Start(Loop());
        }

        /// <summary>Lasting facts are published again after each load (as the levels spoke does), so listeners rebuild their state.</summary>
        private static void Republish(SewerState s)
        {
            if (s.Fate == KingFate.Spared) Events.Publish(new SewerKingSpared());
            if (s.Deal != KingDeal.None && s.Fate == KingFate.Spared) Events.Publish(new SewerKingDealChosen(s.Deal == KingDeal.Underboss));
            if (s.Fate == KingFate.Defeated) Events.Publish(new SewerKingDefeated());
            if (s.RouteRevealed) Events.Publish(new BootleggersRouteRevealed(s.Fate != KingFate.Spared));
        }

        private static System.Collections.IEnumerator Loop()
        {
            while (Quest != null)
            {
                float until = Time.realtimeSinceStartup + 1f;
                while (Time.realtimeSinceStartup < until) yield return null;
                try
                {
                    PollFacts();
                    PollPlaces();
                    Refresh();
                }
                catch (Exception e) { Mod.Log.Warning("sewer loop: " + e.Message); }
            }
        }

        // ---- facts from the game ----

        private static void PollFacts()
        {
            if (Quest == null || _sewer == null) return;
            var s = Quest.State;
            if (!s.HasKey && (_sewer.IsSewerUnlocked || HoldsKey())) Apply(Quest.KeyObtained(), "key");

            var king = _sewer.SewerKingNPC;
            if (s.KingAlive && (_sewer.HasSewerKingBeenDefeated || (king != null && king.Health != null && king.Health.IsDead)))
            {
                Vector3? at = king != null ? king.transform.position : (Vector3?)null;
                Apply(Quest.KingDefeated(at?.x, at?.y, at?.z), "king down");
            }

            var goblin = _sewer.SewerGoblinNPC;
            if (goblin != null)
            {
                if (!s.GoblinMet && goblin.CurrentState != SewerGoblin.ESewerGoblinState.Inactive && goblin.TargetPlayer != null)
                    Apply(Quest.GoblinCame(), "goblin came");
                ScriptGoblin(goblin);
            }
        }

        private static bool HoldsKey()
        {
            try
            {
                var inv = PlayerSingleton<PlayerInventory>.Instance;
                return inv != null && _sewer.SewerKeyItem != null && inv.GetAmountOfItem(_sewer.SewerKeyItem.ID) > 0;
            }
            catch { return false; }
        }

        /// <summary>The goblin's first visit, once the player has been in the sewer a little while (not the moment the door opens).</summary>
        private static void ScriptGoblin(SewerGoblin goblin)
        {
            var inSewer = _sewer.GetPlayersInSewer();
            bool anyone = inSewer != null && inSewer.Count > 0;
            if (!anyone) { _inSewerSince = -1f; return; }
            float now = Time.realtimeSinceStartup;
            if (_inSewerSince < 0f) _inSewerSince = now;
            if (!Quest.ShouldScriptGoblin || goblin.CurrentState != SewerGoblin.ESewerGoblinState.Inactive) return;
            if (now - _inSewerSince < GoblinDelay || now - _lastGoblinTry < GoblinRetry) return;
            _lastGoblinTry = now;
            for (int i = 0; i < inSewer.Count; i++)
            {
                var p = inSewer[i];
                if (!goblin.IsPlayerValidTarget(p)) continue;
                Mod.Log.Msg($"scripted goblin visit to {p.PlayerName}");
                goblin.DeployToPlayer(p);
                return;
            }
        }

        /// <summary>Hears the goblin's own "took the meth and left" callback (no patch: the game's delegate field).</summary>
        private static void HookGoblin()
        {
            try
            {
                var goblin = _sewer.SewerGoblinNPC;
                var retrieve = goblin != null ? goblin.RetrieveBehaviour : null;
                if (retrieve == null || retrieve.Pointer == _goblinHooked) return;
                Il2CppSystem.Action handler = new Action(() => { if (Quest != null) Apply(Quest.GoblinTookMeth(), "goblin took meth"); });
                retrieve.onRetrieveComplete = retrieve.onRetrieveComplete == null
                    ? handler
                    : Il2CppSystem.Delegate.Combine(retrieve.onRetrieveComplete, handler).Cast<Il2CppSystem.Action>();
                _goblinHooked = retrieve.Pointer;
            }
            catch (Exception e) { Mod.Log.Warning($"goblin calm events are off: {e.Message}"); }
        }

        /// <summary>Clues, the stash, and the King noticing a visitor.</summary>
        private static void PollPlaces()
        {
            var me = Player.Local;
            if (me == null || Quest == null) return;
            var s = Quest.State;
            var pos = me.transform.position;

            if (_journal == null) { var p = Spot.JournalPlace(_sewer); if (p.HasValue) _journal = new Spot("journal", p.Value); }
            if (_plaque == null) { var p = Spot.PlaquePlace(); if (p.HasValue) _plaque = new Spot("plaque", p.Value); }
            Find(_journal, Quest.CluesInWorld && !s.HasClue(Clue.Journal), pos, () =>
            {
                Notify(SewerLines.JournalTitle, SewerLines.JournalText);
                Apply(Quest.ClueFound(Clue.Journal), "journal");
            });
            Find(_plaque, Quest.CluesInWorld && !s.HasClue(Clue.Plaque), pos, () =>
            {
                Notify(SewerLines.PlaqueTitle, SewerLines.PlaqueText);
                Apply(Quest.ClueFound(Clue.Plaque), "plaque");
            });

            if (_stash == null && s.StashAvailable)
            {
                var at = StashPlace();
                if (at.HasValue) _stash = new Spot("stash", at.Value);
            }
            Find(_stash, Quest.State.QuestStarted && s.StashAvailable, pos, () => Apply(Quest.TakeStash(), "stash"));

            // the King no longer attacks on sight, but he does notice you
            var king = _sewer.SewerKingNPC;
            bool inOffice = king != null && king.sewerOffice != null && me.CurrentProperty != null && me.CurrentProperty.Pointer == king.sewerOffice.Pointer;
            if (!inOffice) _kingSpokeThisVisit = false;
            else if (!_kingSpokeThisVisit && s.KingAlive && Quest.KingStaysCalm && s.Fate == KingFate.Unknown && _king != null)
            {
                _kingSpokeThisVisit = true;
                _king.Say(SewerLines.KingSpotted, 4f);
            }
        }

        private static void Find(Spot spot, bool live, Vector3 player, Action found)
        {
            if (spot == null) return;
            spot.Show(live);
            if (live && spot.InReach(player)) { spot.Show(false); found(); }
        }

        private static Vector3? StashPlace()
        {
            var s = Quest.State;
            if (s.HasStashPosition) return new Vector3(s.StashX, s.StashY, s.StashZ);
            var office = _sewer.SewerKingNPC != null ? _sewer.SewerKingNPC.sewerOffice : null;
            if (office == null) return null;
            // killed before this mod saw it: somewhere in his office (the interior spawn point, to be checked in game)
            return office.InteriorSpawnPoint != null ? office.InteriorSpawnPoint.position : office.transform.position;
        }

        // ---- dialogue ----

        private static void WireTalks()
        {
            _jerry = Talk.For(NPCManager.GetNPC("jerry_montero"), "Jerry");
            _frank = Talk.For(NPCManager.GetNPC("cranky_frank"), "Frank");
            _king = Talk.For(_sewer.SewerKingNPC, "the Sewer King");

            if (_jerry != null)
            {
                _jerry.AddChoice(SewerLines.JerryWarningChoice, () => Quest != null && Quest.JerryOffersWarning, () =>
                {
                    var sc = new Talk.Script("Melange_Sewer_Jerry_Warning");
                    string last = sc.Lines("ENTRY", SewerLines.JerryWarning(SewerLines.Variant(Quest.State)), "Go on.");
                    sc.Choice(last, "MS_JERRY_ACCEPT", SewerLines.JerryAccept);
                    return sc.Build();
                });
                _jerry.On("MS_JERRY_ACCEPT", () => Apply(Quest.TalkToJerry(), "Jerry"));

                _jerry.AddChoice(SewerLines.JerryKeyChoice, () => Quest != null && Quest.JerryKeyLeads, () =>
                {
                    var sc = new Talk.Script("Melange_Sewer_Jerry_Keys");
                    bool spare = Quest.JerrySpareOffered;
                    string last = sc.Lines("ENTRY", SewerLines.JerryKeyLeads(PossessorDescription(), spare));
                    if (spare)
                    {
                        sc.Choice(last, "MS_JERRY_FAVOUR", SewerLines.JerryFavourAsk, "FAVOUR");
                        sc.Node("FAVOUR", SewerLines.JerryFavour);
                        sc.Choice("FAVOUR", "MS_JERRY_GIVE", SewerLines.JerryFavourGive);
                        sc.Choice("FAVOUR", "MS_JERRY_LATER", SewerLines.JerryFavourLater);
                    }
                    else sc.Choice(last, "MS_JERRY_OK", "Right.");
                    return sc.Build();
                });
                _jerry.On("MS_JERRY_GIVE", PayJerry);

                _jerry.AddChoice(SewerLines.JerryChatChoice, () => Quest != null && Quest.JerryChats, () =>
                    new Talk.Script("Melange_Sewer_Jerry_Chat").Node("ENTRY", SewerLines.JerryHint(Quest.Step)).Build());
            }

            _frank?.AddChoice(SewerLines.FrankChoice, () => Quest != null, () =>
                new Talk.Script("Melange_Sewer_Frank").Node("ENTRY", SewerLines.FrankRumour(Quest.State, _rumourRoll++ + Rng.Next(3))).Build(), 0);

            if (_king != null)
            {
                _king.AddChoice(SewerLines.KingGreetChoice, () => Quest != null && Quest.KingGreets && Quest.State.KingAlive, () =>
                {
                    Apply(Quest.MeetKing(), "met the King");
                    var sc = new Talk.Script("Melange_Sewer_King_Greeting");
                    sc.Lines("ENTRY", SewerLines.KingGreeting);
                    return sc.Build();
                });

                _king.AddChoice(SewerLines.KingConfrontChoice, () => Quest != null && Quest.CanConfront, () =>
                {
                    var sc = new Talk.Script("Melange_Sewer_King_Confront");
                    string last = sc.Lines("ENTRY", SewerLines.KingConfront);
                    sc.Choice(last, "MS_KING_SPARE", SewerLines.SpareChoice, "MENTOR");
                    sc.Choice(last, "MS_KING_REVEAL", SewerLines.RevealChoice, "REVEAL");
                    sc.Node("REVEAL", SewerLines.KingRevealReply);
                    string taught = sc.Lines("MENTOR", SewerLines.KingMentor);
                    sc.Choice(taught, "MS_KING_TAUGHT", "Go on.", "OFFER");
                    OfferNodes(sc);
                    return sc.Build();
                });
                _king.On("MS_KING_SPARE", () => Apply(Quest.Spare(), "spared"));
                _king.On("MS_KING_REVEAL", () => Apply(Quest.Reveal(), "revealed"));

                _king.AddChoice(SewerLines.KingOfferChoice, () => Quest != null && Quest.KingOffers && Quest.State.KingAlive, () =>
                {
                    var sc = new Talk.Script("Melange_Sewer_King_Offer");
                    OfferNodes(sc, "ENTRY");
                    return sc.Build();
                });
                _king.On("MS_KING_UNDERBOSS", () => { Apply(Quest.ChooseDeal(KingDeal.Underboss), "underboss"); _king.Say(SewerLines.UnderbossReply); });
                _king.On("MS_KING_PAYOUT", () => { Apply(Quest.ChooseDeal(KingDeal.Payout), "payout"); _king.Say(SewerLines.PayoutReply); });

                _king.AddChoice(SewerLines.KingChatChoice, () => Quest != null && Quest.KingChats && Quest.State.KingAlive, () =>
                    new Talk.Script("Melange_Sewer_King_Chat").Node("ENTRY", SewerLines.KingChat(Quest.State.Deal)).Build());
            }
        }

        private static void OfferNodes(Talk.Script sc, string first = "OFFER")
        {
            string last = sc.Lines(first, SewerLines.KingOffer);
            sc.Choice(last, "MS_KING_UNDERBOSS", SewerLines.UnderbossChoice);
            sc.Choice(last, "MS_KING_PAYOUT", SewerLines.PayoutChoice);
            sc.Choice(last, "MS_KING_LATER", SewerLines.LaterChoice);
        }

        private static string PossessorDescription()
        {
            try { return _sewer.GetSewerKeyPossessor()?.NPCDescription; }
            catch { return null; }
        }

        /// <summary>Jerry's favour: one of whatever calms the goblin (its pacify item, meth), for his spare key.</summary>
        private static void PayJerry()
        {
            try
            {
                var inv = PlayerSingleton<PlayerInventory>.Instance;
                var item = _sewer.SewerGoblinNPC != null ? _sewer.SewerGoblinNPC.PacifyItem : null;
                if (inv == null || item == null || inv.GetAmountOfItem(item.ID) == 0) { _jerry.Say(SewerLines.JerryFavourNoMeth); return; }
                inv.RemoveAmountOfItem(item.ID, 1);
                Apply(Quest.GiveJerrysSpare(), "Jerry's favour");
                _jerry.Say(SewerLines.JerryFavourThanks);
            }
            catch (Exception e) { Mod.Log.Warning("Jerry's favour: " + e.Message); }
        }

        // ---- effects ----

        private static void Apply(List<SewerEffect> effects, string why)
        {
            if (Quest == null) return;
            foreach (var fx in effects)
            {
                Mod.Log.Msg($"[{why}] {fx}");
                Heard($"[{why}] {fx}");
                try { Do(fx); }
                catch (Exception e) { Mod.Log.Error($"sewer effect {fx}: {e}"); }
            }
            if (effects.Count > 0) Mod.Log.Msg($"step now {Quest.Step}");
            Refresh();
        }

        private static void Do(SewerEffect fx)
        {
            var s = Quest.State;
            switch (fx)
            {
                case SewerEffect.OfferQuest: Notify(SewerLines.OfferTitle, SewerLines.OfferText); break;
                case SewerEffect.QuestBegins: if (DownTheDrain.Current == null) QuestManager.CreateQuest<DownTheDrain>(); break;
                case SewerEffect.GiveSpareKey: PlayerSingleton<PlayerInventory>.Instance.AddItemToInventory(_sewer.SewerKeyItem.GetDefaultInstance()); break;
                case SewerEffect.GoblinCalmed: Events.Publish(new GoblinCalmed()); break;
                case SewerEffect.KingIdentified: Notify(SewerLines.IdentifiedTitle, SewerLines.IdentifiedText); break;
                case SewerEffect.KingSpared: Events.Publish(new SewerKingSpared()); break;
                case SewerEffect.KingRevealed: Events.Publish(new SewerKingRevealed(s.KingAttacked)); break;
                case SewerEffect.KingHostile: MelonLoader.MelonCoroutines.Start(SetKingOnPlayer()); break;
                case SewerEffect.KingDefeated: Events.Publish(new SewerKingDefeated()); break;
                case SewerEffect.UnderbossHired:
                    Managers.Register(new SewerKingManager());
                    Events.Publish(new SewerKingDealChosen(true));
                    break;
                case SewerEffect.UnderbossLost: Managers.Unregister(SewerKingManager.ManagerId); break;
                case SewerEffect.PayHushMoney:
                    Pay(SewerQuest.HushMoney);
                    Events.Publish(new SewerKingDealChosen(false));
                    break;
                case SewerEffect.PayStash:
                    Pay(SewerQuest.StashCash);
                    Notify(SewerLines.StashTitle, SewerLines.StashText);
                    break;
                case SewerEffect.RouteRevealed: Events.Publish(new BootleggersRouteRevealed(s.Fate != KingFate.Spared)); break;
                case SewerEffect.QuestComplete:
                    Sync();
                    DownTheDrain.Current?.Complete();
                    DownTheDrain.Forget();
                    break;
            }
        }

        public static void OnKingAttacked() { if (Quest != null) Apply(Quest.KingAttacked(), "King attacked"); }

        /// <summary>After the reveal: once the conversation has closed, the King goes for the player, as his own tick would have.</summary>
        private static System.Collections.IEnumerator SetKingOnPlayer()
        {
            var king = _sewer != null ? _sewer.SewerKingNPC : null;
            float until = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < until && king != null && king.DialogueHandler != null && king.DialogueHandler.IsDialogueInProgress)
                yield return null;
            try
            {
                if (king == null || king.Health.IsDead || Player.Local == null) yield break;
                KingPatch.SettingHostile = true;
                king.Behaviour.CombatBehaviour.SetTargetAndEnable_Server(Player.Local.NetworkObject);
                Mod.Log.Msg("the Sewer King turns on the player");
            }
            finally { KingPatch.SettingHostile = false; }
        }

        private static void Pay(float amount)
        {
            NetworkSingleton<MoneyManager>.Instance.ChangeCashBalance(amount, true, true);
            Mod.Log.Msg($"paid {amount} cash");
        }

        private static void Notify(string title, string text)
        {
            try { Singleton<NotificationsManager>.Instance.SendNotification(title, text, null, 6f, true); }
            catch (Exception e) { Mod.Log.Msg($"{title}: {text} (notification failed: {e.Message})"); }
        }

        // ---- keeping the game's view current ----

        private static void Refresh()
        {
            if (Quest == null) return;
            _jerry?.Refresh();
            _frank?.Refresh();
            _king?.Refresh();
            Jen.Apply(Quest.JenSells);
            Sync();
        }

        private static void Sync()
        {
            DownTheDrain.Current?.Sync(Quest, entry => entry switch
            {
                SewerEntry.Key => _jerry != null ? _jerry.Npc.transform.position : (Vector3?)null,
                SewerEntry.Clues => !Quest.State.HasClue(Clue.Plaque) && _plaque != null ? _plaque.Position
                                    : _journal != null ? _journal.Position : (Vector3?)null,
                SewerEntry.King or SewerEntry.Offer => _sewer?.SewerKingNPC != null ? _sewer.SewerKingNPC.transform.position : (Vector3?)null,
                SewerEntry.Stash => _stash?.Position,
                _ => null,
            });
        }
    }
}
