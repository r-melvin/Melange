using System;
using System.Collections.Generic;
using System.Text;
using Melange.Core;
using S1API.Console;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Dialogue;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.PlayerScripts;
using UnityEngine;
using GameNPC = Il2CppScheduleOne.NPCs.NPC;

namespace Melange.Sewer
{
    /// <summary>
    /// The in-game probes (TESTING.md, P4 on): one console command, <c>sewer &lt;what&gt;</c>, so the quest can be walked
    /// through by a script (probe-cmds.txt) instead of by hand. S1API finds and registers it. Each subcommand logs
    /// <c>PROBE &lt;what&gt;: &lt;result&gt;</c> to the Melange_Sewer logger, with the quest step and every effect and shared event
    /// since the last probe. Dialogue steps pick our own choices the way the dialogue menu does (only when the menu would
    /// show them) and run the same handlers; nothing here is needed for play. Host only.
    /// </summary>
    public sealed class SewerCommand : BaseConsoleCommand
    {
        public override string CommandWord => "sewer";
        public override string CommandDescription => "Melange Sewer probes: status, trigger, jerry, keys, favour, frank, jen [buy], goblin, meet, spare, reveal, deal <underboss|payout>, chat <jerry|king>, defeat, tp <place>";
        public override string ExampleUsage => "sewer status";

        public override void ExecuteCommand(List<string> args)
        {
            string what = args != null && args.Count > 0 ? args[0] : "status";
            string arg = args != null && args.Count > 1 ? args[1] : null;
            string result;
            try { result = Story.Probe(what, arg); }
            catch (Exception e) { result = "threw: " + e; }
            Mod.Log.Msg($"PROBE {what}{(arg != null ? " " + arg : "")}: {result}");
        }
    }

    internal static partial class Story
    {
        /// <summary>Effects applied and shared events published since the last probe (filled in play too; drained by probes).</summary>
        private static readonly List<string> _heard = new List<string>();
        private const int HeardMax = 50;

        private static void Heard(string what)
        {
            if (_heard.Count >= HeardMax) _heard.RemoveAt(0);
            _heard.Add(what);
        }

        /// <summary>Listens to the sewer's shared events so the probes can say what was published (the hub has no "listen to all").</summary>
        private static void ProbeStart()
        {
            Events.Subscribe<GoblinCalmed>(_ => Heard("event GoblinCalmed"));
            Events.Subscribe<SewerKingSpared>(_ => Heard("event SewerKingSpared"));
            Events.Subscribe<SewerKingRevealed>(e => Heard($"event SewerKingRevealed(attacked {e.Attacked})"));
            Events.Subscribe<SewerKingDefeated>(_ => Heard("event SewerKingDefeated"));
            Events.Subscribe<SewerKingDealChosen>(e => Heard($"event SewerKingDealChosen(underboss {e.Underboss})"));
            Events.Subscribe<BootleggersRouteRevealed>(e => Heard($"event BootleggersRouteRevealed(fromJournal {e.FromJournal})"));
            Events.Subscribe<MenuLoaded>(_ => _heard.Clear());
        }

        public static string Probe(string what, string arg)
        {
            if (!Mod.Active) return "the mod is off";
            if (!Host.IsHost) return "host only";
            if (Quest == null || _sewer == null) return "no save loaded (or the story is off: see the load log)";
            string result = Run(what, arg);
            // a moment for anything the action set going (the loop polls facts every second): the next probe reports it
            string since = _heard.Count == 0 ? "none" : string.Join("; ", _heard);
            _heard.Clear();
            return $"{result} || step {Quest.Step}; since last probe: {since}";
        }

        private static string Run(string what, string arg)
        {
            switch (what)
            {
                case "status": return Status();
                case "trigger":
                    // what reaching Bagman V (or methylamine) does
                    Apply(Quest.Trigger(), "probe: trigger");
                    return "triggered " + Quest.State.Triggered;
                case "jerry":
                    return Converse(_jerry, "Jerry", SewerLines.JerryWarningChoice, "MS_JERRY_ACCEPT");
                case "keys":
                    return Converse(_jerry, "Jerry", SewerLines.JerryKeyChoice, null);
                case "favour":
                    return Converse(_jerry, "Jerry", SewerLines.JerryKeyChoice, "MS_JERRY_GIVE") + $"; key items held {KeysHeld()}";
                case "frank":
                    return Converse(_frank, "Frank", SewerLines.FrankChoice, null);
                case "jen":
                    return Jen.Probe(arg == "buy") + $"; key items held {KeysHeld()}";
                case "goblin":
                    return Goblin();
                case "meet":
                    return Converse(_king, "the King", SewerLines.KingGreetChoice, null);
                case "spare":
                    return Converse(_king, "the King", SewerLines.KingConfrontChoice, "MS_KING_SPARE");
                case "reveal":
                    return Converse(_king, "the King", SewerLines.KingConfrontChoice, "MS_KING_REVEAL");
                case "deal":
                    string label = arg switch { "underboss" => "MS_KING_UNDERBOSS", "payout" => "MS_KING_PAYOUT", _ => null };
                    if (label == null) return "deal <underboss|payout>";
                    // the offer comes straight after sparing him, or later through "About your offer."; both run the same handler
                    return Converse(_king, "the King", SewerLines.KingOfferChoice, label) + $"; manager {(Managers.Get(SewerKingManager.ManagerId) != null ? "registered" : "none")}";
                case "chat":
                    if (arg == "king") return Converse(_king, "the King", SewerLines.KingChatChoice, null);
                    if (arg == "jerry") return Converse(_jerry, "Jerry", SewerLines.JerryChatChoice, null);
                    return "chat <jerry|king>";
                case "defeat":
                    return Defeat();
                case "tp":
                    return Teleport(arg);
                default:
                    return "unknown; try status, trigger, jerry, keys, favour, frank, jen [buy], goblin, meet, spare, reveal, deal <underboss|payout>, chat <jerry|king>, defeat, tp <jerry|frank|jen|king|office|sewer|journal|plaque|stash>";
            }
        }

        private static string Status()
        {
            var s = Quest.State;
            var king = _sewer.SewerKingNPC;
            string kingState = king == null ? "missing"
                : $"health {(king.Health != null ? king.Health.Health : -1f):0}/{(king.Health != null ? king.Health.MaxHealth : -1f):0}, dead {king.Health != null && king.Health.IsDead}, " +
                  $"knocked out {king.Health != null && king.Health.IsKnockedOut}, fighting {king.Behaviour != null && king.Behaviour.CombatBehaviour != null && king.Behaviour.CombatBehaviour.Enabled}, " +
                  $"game says defeated {_sewer.HasSewerKingBeenDefeated}, stays calm {Quest.KingStaysCalm}";
            return $"step {Quest.Step}; triggered {s.Triggered}, started {s.QuestStarted}, key early {s.KeyFoundEarly}, key {s.HasKey}, spare given {s.JerrySpareGiven}, " +
                   $"goblin met {s.GoblinMet} calmed {s.GoblinCalmed}, clues {s.Clues} (identified {s.Identified}), King met {s.KingMet}, fate {s.Fate}, attacked {s.KingAttacked}, " +
                   $"deal {s.Deal}, stash taken {s.StashTaken} (available {s.StashAvailable}{(s.HasStashPosition ? $" at {s.StashX:0.0},{s.StashY:0.0},{s.StashZ:0.0}" : "")}), " +
                   $"route {s.RouteRevealed}, completed {s.Completed}; journal quest {(DownTheDrain.Current != null ? "live" : "none")}; " +
                   $"sewer unlocked {_sewer.IsSewerUnlocked}, key items held {KeysHeld()}; Jen {Jen.Probe(false)}; King {kingState}; {Goblin()}; " +
                   $"manager {(Managers.Get(SewerKingManager.ManagerId) != null ? "registered" : "none")}; player at {Where()}";
        }

        private static string Goblin()
        {
            var g = _sewer.SewerGoblinNPC;
            var inSewer = _sewer.GetPlayersInSewer();
            float since = _inSewerSince < 0f ? -1f : Time.realtimeSinceStartup - _inSewerSince;
            return g == null ? "goblin missing"
                : $"goblin {g.CurrentState}, target {(g.TargetPlayer != null ? g.TargetPlayer.PlayerName : "none")}, pacify item {(g.PacifyItem != null ? g.PacifyItem.ID : "?")}, " +
                  $"players in sewer {(inSewer != null ? inSewer.Count : 0)}, in sewer for {since:0}s, scripted visit due {Quest.ShouldScriptGoblin}";
        }

        /// <summary>
        /// Picks <paramref name="choiceText"/> on the NPC as the dialogue menu would (refused if the menu wouldn't show it),
        /// which builds the conversation from the quest's state, then optionally picks <paramref name="label"/> inside it.
        /// </summary>
        private static string Converse(Talk talk, string who, string choiceText, string label)
        {
            if (talk == null) return $"{who} has no dialogue (see the load log)";
            var c = talk.Pick(choiceText, out bool shown);
            if (!shown) return $"'{choiceText}' isn't offered by {who} now";
            string lines = Lines(c);
            if (label == null) return $"'{choiceText}': {lines}";
            if (!Offers(c, label)) return $"'{choiceText}': {lines} -> {label} isn't a choice in this conversation";
            bool known = talk.Choose(label);
            return $"'{choiceText}': {lines} -> {label} {(known ? "chosen" : "unknown")}";
        }

        private static bool Offers(Conversation c, string label)
        {
            var nodes = c != null ? c.DialogueNodeData : null;
            for (int i = 0; nodes != null && i < nodes.Count; i++)
            {
                var choices = nodes[i] != null ? nodes[i].choices : null;
                for (int j = 0; choices != null && j < choices.Length; j++)
                    if (choices[j] != null && choices[j].ChoiceLabel == label) return true;
            }
            return false;
        }

        private static string Lines(Conversation c)
        {
            var nodes = c != null ? c.DialogueNodeData : null;
            if (nodes == null) return "(no conversation)";
            var sb = new StringBuilder();
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] == null) continue;
                if (sb.Length > 0) sb.Append(" / ");
                sb.Append(nodes[i].DialogueNodeLabel).Append(": ").Append(nodes[i].DialogueText);
            }
            return sb.ToString();
        }

        /// <summary>The King dies to the game's own damage path (lethal), as from a fight; the loop then notices, as in play.</summary>
        private static string Defeat()
        {
            var king = _sewer.SewerKingNPC;
            if (king == null || king.Health == null) return "no King";
            if (king.Health.IsDead) return "already dead";
            king.Health.TakeDamage(king.Health.MaxHealth * 10f, true);
            return $"dead {king.Health.IsDead}; the loop reports it within a second";
        }

        private static int KeysHeld()
        {
            try
            {
                var inv = PlayerSingleton<PlayerInventory>.Instance;
                return inv != null && _sewer.SewerKeyItem != null ? (int)inv.GetAmountOfItem(_sewer.SewerKeyItem.ID) : -1;
            }
            catch { return -1; }
        }

        private static string Where()
        {
            var me = Player.Local;
            if (me == null) return "?";
            var p = me.transform.position;
            return $"({p.x:0.0},{p.y:0.0},{p.z:0.0}) in {(me.CurrentProperty != null ? me.CurrentProperty.PropertyName : "no property")}";
        }

        /// <summary>Moves the player where a step needs them; walking-up pick-ups (clues, the stash) then happen as in play.</summary>
        private static string Teleport(string place)
        {
            Vector3? to = place switch
            {
                "jerry" => NearNpc(_jerry?.Npc),
                "frank" => NearNpc(_frank?.Npc),
                "jen" => NearNpc(Jen.Npc),
                "king" => NearNpc(_sewer.SewerKingNPC),
                "office" => Office(),
                "sewer" or "journal" => _journal?.Position ?? Spot.JournalPlace(_sewer),
                "plaque" => _plaque?.Position ?? Spot.PlaquePlace(),
                "stash" => _stash?.Position ?? (Quest.State.StashAvailable ? StashPlace() : null),
                _ => null,
            };
            if (place == null) return "tp <jerry|frank|jen|king|office|sewer|journal|plaque|stash>";
            if (!to.HasValue) return $"no position for '{place}' (unknown place, or not there yet)";
            var move = PlayerSingleton<PlayerMovement>.Instance;
            if (move == null) return "no player movement";
            move.Teleport(to.Value + Vector3.up * 0.1f, true);
            return $"to {place} {to.Value.x:0.0},{to.Value.y:0.0},{to.Value.z:0.0}";
        }

        private static Vector3? NearNpc(GameNPC npc)
        {
            if (npc == null) return null;
            var t = npc.transform;
            return t.position + t.forward * 1.5f;
        }

        private static Vector3? Office()
        {
            var office = _sewer.SewerKingNPC != null ? _sewer.SewerKingNPC.sewerOffice : null;
            if (office == null) return null;
            return office.InteriorSpawnPoint != null ? office.InteriorSpawnPoint.position : office.transform.position;
        }
    }
}
