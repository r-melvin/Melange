using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne.Dialogue;
using Il2CppScheduleOne.NPCs;
using UnityEngine.Events;

namespace Melange.Core
{
    /// <summary>
    /// Oscar's dialogue, for every spoke. The hub owns the one patch on his controller (<c>DialogueController_Oscar.ModifyDialogueText</c>,
    /// a multi-line override, so IL2CPP keeps it callable); spokes register rewrites of his lines and choices, and extra
    /// choices of their own. The sewer spoke turns his sewer-key hints into smuggling-ring talk; the smuggling spoke adds
    /// the smuggler's introduction.
    /// </summary>
    /// <remarks>
    /// Rewrites run in ascending order, each seeing the text the previous one left, so a later spoke (higher order) can
    /// refine an earlier one. The patch is applied on first use, so the hub pays nothing when no spoke asks.
    /// </remarks>
    public static class OscarDialogue
    {
        /// <summary>(label, the node's text before the game filled it in, the text so far) -> new text, or null to leave it.</summary>
        public delegate string LineRewrite(string label, string original, string current);

        private sealed class Entry<T> { public string Id; public int Order; public T Fn; }
        private sealed class ExtraChoice
        {
            public string Id; public string Text; public Func<bool> Shown; public Action Chosen; public int Priority;
            public DialogueController.DialogueChoice Live;
        }

        private static readonly List<Entry<LineRewrite>> LineRewrites = new List<Entry<LineRewrite>>();
        private static readonly List<Entry<Func<string, string>>> ChoiceRewrites = new List<Entry<Func<string, string>>>();
        private static readonly List<ExtraChoice> Choices = new List<ExtraChoice>();
        /// <summary>Choice texts as the game shipped them, so a rewrite always starts from the original, however often it runs.</summary>
        private static readonly Dictionary<IntPtr, string> OriginalChoiceText = new Dictionary<IntPtr, string>();
        private static bool _started;
        private static DialogueController _controller;

        /// <summary>Rewrites lines Oscar is about to say. Replaces any rewrite with the same id.</summary>
        public static void RewriteLine(string id, LineRewrite rewrite, int order = 0)
        {
            Start();
            LineRewrites.RemoveAll(e => e.Id == id);
            LineRewrites.Add(new Entry<LineRewrite> { Id = id, Order = order, Fn = rewrite });
            LineRewrites.Sort((a, b) => a.Order.CompareTo(b.Order));
        }

        /// <summary>
        /// Rewrites the text of the choices the player sees with Oscar (his own conversations' choices and his generic list):
        /// text -> new text, or null to leave it. Applied when a save loads, to the game's dialogue data.
        /// </summary>
        public static void RewriteChoice(string id, Func<string, string> rewrite, int order = 0)
        {
            Start();
            ChoiceRewrites.RemoveAll(e => e.Id == id);
            ChoiceRewrites.Add(new Entry<Func<string, string>> { Id = id, Order = order, Fn = rewrite });
            ChoiceRewrites.Sort((a, b) => a.Order.CompareTo(b.Order));
            ApplyChoiceRewrites();
        }

        /// <summary>
        /// Adds a choice to Oscar's list, shown while <paramref name="shown"/> says so (checked about once a second).
        /// <paramref name="chosen"/> runs when the player picks it; use <see cref="Say"/> for his answer.
        /// </summary>
        public static void AddChoice(string id, string text, Func<bool> shown, Action chosen, int priority = 0)
        {
            Start();
            Remove(id);
            var c = new ExtraChoice { Id = id, Text = text, Shown = shown, Chosen = chosen, Priority = priority };
            Choices.Add(c);
            Install(c);
        }

        public static void Remove(string id)
        {
            LineRewrites.RemoveAll(e => e.Id == id);
            ChoiceRewrites.RemoveAll(e => e.Id == id);
            foreach (var c in Choices.FindAll(c => c.Id == id))
            {
                if (c.Live != null) c.Live.Enabled = false;   // the game keeps the choice object; hiding it is enough
                Choices.Remove(c);
            }
        }

        /// <summary>Oscar says something in a speech bubble (after a choice, which ends the conversation).</summary>
        public static void Say(string text, float seconds = 5f)
        {
            MelonLoader.MelonCoroutines.Start(SayAfter(text, seconds));
        }

        private static System.Collections.IEnumerator SayAfter(string text, float seconds)
        {
            // the game closes the conversation right after the choice callback; speak once it has
            float until = UnityEngine.Time.realtimeSinceStartup + 0.3f;
            while (UnityEngine.Time.realtimeSinceStartup < until) yield return null;
            try
            {
                var oscar = FindController();
                oscar?.GetComponent<DialogueHandler>()?.ShowWorldspaceDialogue(text, seconds);
            }
            catch (Exception e) { Core.Log?.Warning("Oscar can't speak: " + e.Message); }
        }

        // ---- the patch and the wiring ----

        private static void Start()
        {
            if (_started) return;
            _started = true;
            try
            {
                var harmony = new HarmonyLib.Harmony("melange.core.oscar");
                var target = AccessTools.Method(typeof(DialogueController_Oscar), nameof(DialogueController_Oscar.ModifyDialogueText), new[] { typeof(string), typeof(string) })
                    ?? throw new MissingMethodException(nameof(DialogueController_Oscar), "ModifyDialogueText");
                harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(OscarDialogue), nameof(BeforeModify)),
                    postfix: new HarmonyMethod(typeof(OscarDialogue), nameof(AfterModify)));
            }
            catch (Exception e) { Core.Log?.Warning($"Oscar's line rewrites are off: {e.Message}"); }
            Events.Subscribe<SaveLoaded>(_ => OnSaveLoaded());
            Events.Subscribe<MenuLoaded>(_ => { _controller = null; foreach (var c in Choices) c.Live = null; OriginalChoiceText.Clear(); });
            MelonLoader.MelonCoroutines.Start(RefreshLoop());
        }

        private static void BeforeModify(string dialogueText, ref string __state) => __state = dialogueText;

        private static void AfterModify(string dialogueLabel, string __state, ref string __result)
        {
            if (LineRewrites.Count == 0) return;
            string text = __result;
            foreach (var r in LineRewrites)
            {
                try
                {
                    string next = r.Fn(dialogueLabel, __state, text);
                    if (next != null) text = next;
                }
                catch (Exception e) { Core.Log?.Warning($"Oscar line rewrite {r.Id} threw: {e.Message}"); }
            }
            __result = text;
        }

        private static void OnSaveLoaded()
        {
            _controller = FindController();
            if (_controller == null) { Core.Log?.Warning("Oscar's dialogue controller wasn't found; his extra choices are off this session."); return; }
            foreach (var c in Choices) Install(c);
            ApplyChoiceRewrites();
        }

        private static DialogueController FindController()
        {
            if (_controller != null) return _controller;
            var registry = NPCManager.NPCRegistry;
            if (registry == null) return null;
            for (int i = 0; i < registry.Count; i++)
            {
                var npc = registry[i];
                if (npc == null || npc.DialogueHandler == null) continue;
                var oscar = npc.DialogueHandler.GetComponent<DialogueController_Oscar>();
                if (oscar != null) return oscar;
            }
            return null;
        }

        private static void Install(ExtraChoice c)
        {
            if (c.Live != null || _controller == null) return;
            try
            {
                var choice = new DialogueController.DialogueChoice { ChoiceText = c.Text, Enabled = Shown(c) };
                choice.onChoosen.AddListener((UnityAction)new Action(() =>
                {
                    try { c.Chosen?.Invoke(); }
                    catch (Exception e) { Core.Log?.Warning($"Oscar choice {c.Id} threw: {e.Message}"); }
                }));
                _controller.AddDialogueChoice(choice, c.Priority);
                c.Live = choice;
            }
            catch (Exception e) { Core.Log?.Warning($"Oscar choice {c.Id} not added: {e.Message}"); }
        }

        private static bool Shown(ExtraChoice c)
        {
            try { return c.Shown == null || c.Shown(); }
            catch { return false; }
        }

        /// <summary>
        /// Choice visibility follows the spoke's state. The game's own show-check is an IL2CPP delegate with an out parameter,
        /// which managed code can't safely supply, so the plain Enabled flag is kept current instead.
        /// </summary>
        private static System.Collections.IEnumerator RefreshLoop()
        {
            while (true)
            {
                float until = UnityEngine.Time.realtimeSinceStartup + 1f;
                while (UnityEngine.Time.realtimeSinceStartup < until) yield return null;
                foreach (var c in Choices)
                {
                    try { if (c.Live != null) c.Live.Enabled = Shown(c); }
                    catch { c.Live = null; }                       // the scene went away under it
                }
            }
        }

        private static void ApplyChoiceRewrites()
        {
            if (_controller == null || ChoiceRewrites.Count == 0) return;
            try
            {
                var list = _controller.Choices;
                if (list != null)
                    for (int i = 0; i < list.Count; i++)
                    {
                        var ch = list[i];
                        if (ch == null || IsOurs(ch)) continue;
                        ch.ChoiceText = Rewrite(ch.Pointer, ch.ChoiceText);
                    }
                var handler = _controller.GetComponent<DialogueHandler>();
                var containers = handler != null ? handler.dialogueContainers : null;
                if (containers == null) return;
                for (int i = 0; i < containers.Count; i++)
                {
                    var nodes = containers[i]?.DialogueNodeData;
                    if (nodes == null) continue;
                    for (int n = 0; n < nodes.Count; n++)
                    {
                        var choices = nodes[n]?.choices;
                        if (choices == null) continue;
                        for (int k = 0; k < choices.Length; k++)
                            if (choices[k] != null) choices[k].ChoiceText = Rewrite(choices[k].Pointer, choices[k].ChoiceText);
                    }
                }
            }
            catch (Exception e) { Core.Log?.Warning("Oscar choice rewrites: " + e.Message); }
        }

        private static bool IsOurs(DialogueController.DialogueChoice ch)
        {
            foreach (var c in Choices) if (c.Live != null && c.Live.Pointer == ch.Pointer) return true;
            return false;
        }

        private static string Rewrite(IntPtr key, string current)
        {
            if (!OriginalChoiceText.TryGetValue(key, out var original)) OriginalChoiceText[key] = original = current;
            string text = original;
            foreach (var r in ChoiceRewrites)
            {
                try
                {
                    string next = r.Fn(text);
                    if (next != null) text = next;
                }
                catch (Exception e) { Core.Log?.Warning($"Oscar choice rewrite {r.Id} threw: {e.Message}"); }
            }
            return text;
        }
    }
}
