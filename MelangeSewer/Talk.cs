using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.Dialogue;
using UnityEngine;
using UnityEngine.Events;
using GameNPC = Il2CppScheduleOne.NPCs.NPC;
using Il2CppNodes = Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Dialogue.DialogueNodeData>;
using Il2CppLinks = Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Dialogue.NodeLinkData>;
using Il2CppBranches = Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Dialogue.BranchNodeData>;

namespace Melange.Sewer
{
    /// <summary>
    /// Dialogue on a vanilla NPC (Jerry, Frank, the King), the way the game itself adds the key holder's "Do you have a
    /// sewer key?" (<c>SewerManager.SetRandomKeyPossessor</c>): a choice on the NPC's own <see cref="DialogueController"/>.
    /// Picking it starts a conversation built at that moment from the quest's state, so what they say is always current.
    /// </summary>
    /// <remarks>
    /// Conversations are built here with the game's own data types, as S1API's DialogueContainerBuilder.Build does: S1API
    /// can only build for NPCs it has a wrapper for, and the Sewer King has none. Choices inside our conversations carry
    /// labels starting "MS_", heard through the handler's own onDialogueChoiceChosen event (no patch).
    /// </remarks>
    internal sealed class Talk
    {
        public readonly GameNPC Npc;
        public readonly DialogueController Controller;
        public readonly DialogueHandler Handler;

        private readonly Dictionary<string, Action> _onLabel = new Dictionary<string, Action>();
        private readonly List<(DialogueController.DialogueChoice Choice, Func<bool> Shown)> _choices = new List<(DialogueController.DialogueChoice, Func<bool>)>();

        private Talk(GameNPC npc, DialogueController controller, DialogueHandler handler)
        {
            Npc = npc; Controller = controller; Handler = handler;
            if (Handler.onDialogueChoiceChosen == null) Handler.onDialogueChoiceChosen = new UnityEvent<string>();
            Handler.onDialogueChoiceChosen.AddListener((UnityAction<string>)new Action<string>(OnChoice));
        }

        /// <summary>Talk for an NPC, or null (with a warning) if it has no dialogue to hang choices on.</summary>
        public static Talk For(GameNPC npc, string who)
        {
            try
            {
                var handler = npc != null ? npc.DialogueHandler : null;
                var controller = handler != null ? handler.GetComponent<DialogueController>() : null;
                if (controller == null) { Mod.Log.Warning($"{who} has no dialogue controller; their sewer lines are off."); return null; }
                return new Talk(npc, controller, handler);
            }
            catch (Exception e) { Mod.Log.Warning($"{who}'s dialogue: {e.Message}"); return null; }
        }

        /// <summary>Adds a choice to the NPC's list. <paramref name="build"/> makes the conversation it opens, or returns null for none.</summary>
        public void AddChoice(string text, Func<bool> shown, Func<Conversation> build, int priority = 5)
        {
            var choice = new DialogueController.DialogueChoice { ChoiceText = text, Enabled = Safe(shown) };
            choice.onChoosen.AddListener((UnityAction)new Action(() =>
            {
                // the game starts choice.Conversation right after this listener returns, so set it here, fresh
                try { choice.Conversation = build?.Invoke(); }
                catch (Exception e) { Mod.Log.Warning($"{Npc.name} conversation: {e.Message}"); choice.Conversation = null; }
            }));
            Controller.AddDialogueChoice(choice, priority);
            _choices.Add((choice, shown));
        }

        /// <summary>Runs <paramref name="action"/> when a choice with this label is picked in one of our conversations.</summary>
        public void On(string label, Action action) => _onLabel[label] = action;

        /// <summary>Shows or hides each choice from the quest's state (the game's own show-check is an IL2CPP delegate with
        /// an out parameter, which managed code can't safely supply).</summary>
        public void Refresh()
        {
            foreach (var (choice, shown) in _choices)
            {
                try { choice.Enabled = Safe(shown); }
                catch { }
            }
        }

        /// <summary>A speech bubble, after a short wait so a conversation that's closing doesn't hide it.</summary>
        public void Say(string text, float seconds = 5f) => MelonLoader.MelonCoroutines.Start(SayLater(text, seconds));

        private System.Collections.IEnumerator SayLater(string text, float seconds)
        {
            float until = Time.realtimeSinceStartup + 0.3f;
            while (Time.realtimeSinceStartup < until) yield return null;
            try { Handler.ShowWorldspaceDialogue(text, seconds); }
            catch (Exception e) { Mod.Log.Warning("speech bubble: " + e.Message); }
        }

        private void OnChoice(string label)
        {
            if (label == null || !_onLabel.TryGetValue(label, out var action)) return;
            try { action(); }
            catch (Exception e) { Mod.Log.Error($"dialogue choice {label}: {e}"); }
        }

        private static bool Safe(Func<bool> f)
        {
            try { return f == null || f(); }
            catch { return false; }
        }

        // ---- building conversations ----

        /// <summary>A conversation in the making: nodes by label, choices that lead on or end it.</summary>
        public sealed class Script
        {
            private readonly Conversation _c;
            private readonly Dictionary<string, DialogueNodeData> _nodes = new Dictionary<string, DialogueNodeData>();
            private readonly Dictionary<string, List<DialogueChoiceData>> _choices = new Dictionary<string, List<DialogueChoiceData>>();
            private readonly List<(string From, string Choice, string To)> _links = new List<(string, string, string)>();

            public Script(string name)
            {
                _c = ScriptableObject.CreateInstance<Conversation>();
                _c.name = name;
            }

            public Script Node(string label, string text)
            {
                _nodes[label] = new DialogueNodeData { Guid = Guid.NewGuid().ToString(), DialogueNodeLabel = label, DialogueText = text, Position = Vector2.zero };
                _choices[label] = new List<DialogueChoiceData>();
                return this;
            }

            /// <summary>A choice on node <paramref name="from"/>; <paramref name="to"/> null ends the conversation.</summary>
            public Script Choice(string from, string label, string text, string to = null)
            {
                var data = new DialogueChoiceData { Guid = Guid.NewGuid().ToString(), ChoiceLabel = label, ChoiceText = text, ShowWorldspaceDialogue = true };
                _choices[from].Add(data);
                if (to != null) _links.Add((from, data.Guid, to));
                return this;
            }

            /// <summary>
            /// Lines said one after another, starting at node <paramref name="first"/>; each but the last moves on with
            /// <paramref name="next"/>. Returns the last node's label, for its own choices.
            /// </summary>
            public string Lines(string first, IReadOnlyList<string> lines, string next = "...")
            {
                string label = first;
                for (int i = 0; i < lines.Count; i++)
                {
                    Node(label, lines[i]);
                    if (i == lines.Count - 1) break;
                    string following = $"{first}_{i + 1}";
                    Choice(label, $"MS_NEXT_{first}_{i}", next, following);
                    label = following;
                }
                return label;
            }

            public Conversation Build()
            {
                var nodes = new Il2CppNodes();
                foreach (var kv in _nodes)
                {
                    var list = _choices[kv.Key];
                    var arr = new Il2CppReferenceArray<DialogueChoiceData>(list.Count);
                    for (int i = 0; i < list.Count; i++) arr[i] = list[i];
                    kv.Value.choices = arr;
                    nodes.Add(kv.Value);
                }
                var links = new Il2CppLinks();
                foreach (var (from, choice, to) in _links)
                {
                    if (!_nodes.TryGetValue(from, out var a) || !_nodes.TryGetValue(to, out var b)) continue;
                    links.Add(new NodeLinkData { BaseDialogueOrBranchNodeGuid = a.Guid, BaseChoiceOrOptionGUID = choice, TargetNodeGuid = b.Guid });
                }
                _c.DialogueNodeData = nodes;
                _c.NodeLinks = links;
                _c.BranchNodeData = new Il2CppBranches();
                _c.SetAllowExit(true);
                return _c;
            }
        }
    }
}
