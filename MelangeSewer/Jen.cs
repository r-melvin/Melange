using System;
using Il2CppScheduleOne.Dialogue;
using Il2CppScheduleOne.NPCs;

namespace Melange.Sewer
{
    /// <summary>
    /// Jen's key sale belongs to the quest: her "buy a sewer access key" choice is hidden until Jerry has named her, and her
    /// offer line knows why you want it. No patch: the choice is the one she adds in Start (its Conversation is her
    /// BuyKeyDialogue), shown by its plain Enabled flag; the line is her conversation's OFFER node, rewritten in the data.
    /// Her relationship and price rules (CanBuyKey, the &lt;PRICE&gt; fill-in) are untouched.
    /// </summary>
    internal static class Jen
    {
        private static DialogueController_Jen _jen;
        private static NPC _npc;
        private static DialogueController.DialogueChoice _choice;
        private static bool _rewritten;

        public static void Apply(bool sells)
        {
            try
            {
                if (_jen == null && !Find()) return;
                if (_choice != null && _choice.Enabled != sells)
                {
                    _choice.Enabled = sells;
                    Mod.Log.Msg($"Jen's key sale {(sells ? "open" : "closed")}");
                }
                if (!_rewritten) RewriteOffer();
            }
            catch (Exception e) { Mod.Log.Warning("Jen's key sale: " + e.Message); _jen = null; _choice = null; }
        }

        /// <summary>Leaving a save: her controller is a scene object and goes with it.</summary>
        public static void Forget() { _jen = null; _npc = null; _choice = null; _rewritten = false; }

        /// <summary>Her NPC, once found (for the probes' teleport).</summary>
        public static NPC Npc => _jen != null || Find() ? _npc : null;

        /// <summary>
        /// For the probes: the sale as her dialogue runs it. Shown only while the quest opens it (the choice's Enabled flag),
        /// then her own checks (relationship: CanBuyKey; cash: CheckChoice on CHOICE_CONFIRM), then her own ChoiceCallback,
        /// which takes the cash and hands over the key. <paramref name="buy"/> false only reports.
        /// </summary>
        public static string Probe(bool buy)
        {
            if (_jen == null && !Find()) return "Jen not found";
            string id = _npc != null ? _npc.ID : "?";
            float rel = _npc != null && _npc.RelationData != null ? _npc.RelationData.RelationDelta : -1f;
            float price = _jen.KeyItem != null ? _jen.KeyItem.BasePurchasePrice : -1f;
            string relWhy = null, cashWhy = null;
            bool relOk = _jen.CanBuyKey(out relWhy);
            bool cashOk = _jen.CheckChoice("CHOICE_CONFIRM", out cashWhy);
            string state = $"{id}: choice shown {_choice.Enabled}, relationship {rel:0.0}/{_jen.MinRelationToBuyKey:0.0} {(relOk ? "ok" : relWhy)}, " +
                           $"price ${price:0} {(cashOk ? "affordable" : cashWhy)}";
            if (!buy) return state;
            if (!_choice.Enabled) return state + "; not sold: the quest hasn't opened her sale";
            if (!relOk || !cashOk) return state + "; not sold";
            _jen.ChoiceCallback("CHOICE_CONFIRM");
            return state + "; sold (her ChoiceCallback)";
        }

        private static bool Find()
        {
            var registry = NPCManager.NPCRegistry;
            if (registry == null) return false;
            for (int i = 0; i < registry.Count; i++)
            {
                var npc = registry[i];
                if (npc == null || npc.DialogueHandler == null) continue;
                var jen = npc.DialogueHandler.GetComponent<DialogueController_Jen>();
                if (jen == null) continue;
                _jen = jen;
                _npc = npc;
                for (int c = 0; c < jen.Choices.Count; c++)
                {
                    var ch = jen.Choices[c];
                    if (ch != null && ch.Conversation != null && jen.BuyKeyDialogue != null && ch.Conversation.Pointer == jen.BuyKeyDialogue.Pointer)
                        _choice = ch;
                }
                if (_choice == null) Mod.Log.Warning("Jen's key choice wasn't found (her Start may not have run yet); retrying");
                else Mod.Log.Msg($"Jen found: {npc.name}, key choice '{_choice.ChoiceText}'");
                if (_choice == null) { _jen = null; _npc = null; }
                return _choice != null;
            }
            return false;
        }

        private static void RewriteOffer()
        {
            _rewritten = true;
            var nodes = _jen.BuyKeyDialogue != null ? _jen.BuyKeyDialogue.DialogueNodeData : null;
            if (nodes == null) return;
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node == null || node.DialogueNodeLabel != "OFFER") continue;
                Mod.Log.Msg($"Jen's offer was: {node.DialogueText}");
                node.DialogueText = SewerLines.JenOffer;
            }
        }
    }
}
