using System.Collections.Generic;

namespace Melange.Sewer
{
    /// <summary>The quest's current objective, worked out from the facts in <see cref="SewerState"/>.</summary>
    public enum SewerStep
    {
        /// <summary>Not yet: the player hasn't reached Bagman V and nobody has unlocked methylamine.</summary>
        Locked,
        TalkToJerry,
        GetKey,
        /// <summary>Go down and meet the goblin (the scripted first visit).</summary>
        EnterSewer,
        FindClues,
        ConfrontKing,
        /// <summary>He knows you know, or you hit him: he's hostile and alive.</summary>
        FightKing,
        HearOffer,
        TakeStash,
        Done,
    }

    /// <summary>What the game side must do after a transition. Each is returned once, by the transition that caused it.</summary>
    public enum SewerEffect
    {
        /// <summary>The trigger fired before the quest began: tell the player Jerry wants a word.</summary>
        OfferQuest,
        QuestBegins,
        GiveSpareKey,
        GoblinCalmed,
        KingIdentified,
        KingSpared,
        /// <summary>He turned hostile (his name said aloud, or attacked); see <see cref="SewerState.KingAttacked"/>.</summary>
        KingRevealed,
        /// <summary>Set him on the player: only after the reveal, since an attacked King is already fighting.</summary>
        KingHostile,
        KingDefeated,
        UnderbossHired,
        /// <summary>The King was underboss and is no longer (attacked or killed).</summary>
        UnderbossLost,
        PayHushMoney,
        PayStash,
        RouteRevealed,
        QuestComplete,
    }

    /// <summary>The quest's journal entries, in the order they are listed.</summary>
    public enum SewerEntry { Key, Sewer, Clues, King, Offer, Stash }

    /// <summary>An entry's state, mirroring the game's quest states without depending on them.</summary>
    public enum EntryStatus { Inactive, Active, Completed }

    /// <summary>
    /// The "Down the Drain" state machine. Transitions change <see cref="State"/> and return what the game must do; the
    /// game side only reports facts (a rank reached, a key held, a clue found) and carries out effects. A transition that
    /// doesn't apply in the current state does nothing and returns no effects, so repeated or late reports are harmless.
    /// </summary>
    public sealed class SewerQuest
    {
        /// <summary>High-Quality Pseudo unlocks from Shirley at Bagman V (rank 4, tier 5).</summary>
        public const int TriggerRank = 4, TriggerTier = 5;
        public const float HushMoney = 10000f, StashCash = 25000f;

        public SewerState State { get; }

        public SewerQuest(SewerState state) => State = state ?? new SewerState();

        public static bool RankTriggers(int rank, int tier) => Melange.Core.Ranks.Compare(rank, tier, TriggerRank, TriggerTier) >= 0;

        public SewerStep Step
        {
            get
            {
                var s = State;
                if (s.Completed) return SewerStep.Done;
                if (!s.QuestStarted) return s.Triggered ? SewerStep.TalkToJerry : SewerStep.Locked;
                if (!s.HasKey && s.Fate != KingFate.Defeated) return SewerStep.GetKey;
                switch (s.Fate)
                {
                    case KingFate.Defeated:
                        if (s.StashAvailable) return SewerStep.TakeStash;
                        return s.GoblinMet ? SewerStep.Done : SewerStep.EnterSewer;
                    case KingFate.Revealed:
                        return SewerStep.FightKing;
                    case KingFate.Spared:
                        if (s.Deal == KingDeal.None) return SewerStep.HearOffer;
                        return s.GoblinMet ? SewerStep.Done : SewerStep.EnterSewer;
                }
                if (!s.GoblinMet) return SewerStep.EnterSewer;
                return s.Identified ? SewerStep.ConfrontKing : SewerStep.FindClues;
            }
        }

        // ---- what's on offer now (the game shows or hides dialogue choices from these) ----

        public bool JerryOffersWarning => State.Triggered && !State.QuestStarted;
        public bool JerryKeyLeads => State.QuestStarted && !State.HasKey && State.Fate != KingFate.Defeated;
        public bool JerrySpareOffered => JerryKeyLeads && !State.JerrySpareGiven;
        public bool JerryChats => State.QuestStarted && !JerryKeyLeads;
        /// <summary>Jen sells keys only once Jerry has named her, and keeps selling afterwards (a co-op friend may need one).</summary>
        public bool JenSells => State.QuestStarted;
        public bool KingGreets => State.Fate == KingFate.Unknown && !State.KingMet;
        public bool CanConfront => State.QuestStarted && State.Identified && State.Fate == KingFate.Unknown;
        public bool KingOffers => State.Fate == KingFate.Spared && State.Deal == KingDeal.None;
        public bool KingChats => State.Fate == KingFate.Spared && State.Deal != KingDeal.None;
        /// <summary>The King's own "player in my office" attack stays off unless he's been revealed or attacked.</summary>
        public bool KingStaysCalm => State.Fate == KingFate.Unknown || State.Fate == KingFate.Spared;
        /// <summary>Send the goblin's scripted first visit (the game side rate-limits attempts and needs a player in the sewer).</summary>
        public bool ShouldScriptGoblin => State.QuestStarted && (State.HasKey || State.Fate == KingFate.Defeated) && !State.GoblinMet;
        /// <summary>Clue pickups exist only while they can still matter.</summary>
        public bool CluesInWorld => State.QuestStarted && State.Fate == KingFate.Unknown;

        public EntryStatus Entry(SewerEntry entry)
        {
            var s = State;
            if (!s.QuestStarted) return EntryStatus.Inactive;
            bool inside = s.HasKey || s.Fate == KingFate.Defeated;
            switch (entry)
            {
                case SewerEntry.Key:
                    return inside ? EntryStatus.Completed : EntryStatus.Active;
                case SewerEntry.Sewer:
                    if (s.GoblinMet) return EntryStatus.Completed;
                    return inside ? EntryStatus.Active : EntryStatus.Inactive;
                case SewerEntry.Clues:
                    if (s.Identified || s.Fate != KingFate.Unknown) return EntryStatus.Completed;
                    return inside ? EntryStatus.Active : EntryStatus.Inactive;
                case SewerEntry.King:
                    if (s.Fate == KingFate.Spared || s.Fate == KingFate.Defeated) return EntryStatus.Completed;
                    return s.Fate == KingFate.Revealed || (inside && s.Identified) ? EntryStatus.Active : EntryStatus.Inactive;
                case SewerEntry.Offer:
                    if (s.Deal != KingDeal.None) return EntryStatus.Completed;
                    return s.Fate == KingFate.Spared ? EntryStatus.Active : EntryStatus.Inactive;
                case SewerEntry.Stash:
                    if (s.StashTaken) return EntryStatus.Completed;
                    return s.StashAvailable ? EntryStatus.Active : EntryStatus.Inactive;
            }
            return EntryStatus.Inactive;
        }

        // ---- transitions ----

        /// <summary>Bagman V reached, or methylamine unlocked.</summary>
        public List<SewerEffect> Trigger()
        {
            var fx = new List<SewerEffect>();
            if (State.Triggered) return fx;
            State.Triggered = true;
            if (!State.QuestStarted) fx.Add(SewerEffect.OfferQuest);
            return Finish(fx);
        }

        /// <summary>The player heard Jerry out. A key already held or a King already dead are taken as they are.</summary>
        public List<SewerEffect> TalkToJerry()
        {
            var fx = new List<SewerEffect>();
            if (!State.Triggered || State.QuestStarted) return fx;
            State.QuestStarted = true;
            State.KeyFoundEarly = State.HasKey;
            fx.Add(SewerEffect.QuestBegins);
            return Finish(fx);
        }

        public List<SewerEffect> KeyObtained()
        {
            var fx = new List<SewerEffect>();
            if (State.HasKey) return fx;
            State.HasKey = true;
            return Finish(fx);
        }

        /// <summary>Jerry hands over his spare (the game checked and took his favour first).</summary>
        public List<SewerEffect> GiveJerrysSpare()
        {
            var fx = new List<SewerEffect>();
            if (!JerrySpareOffered) return fx;
            State.JerrySpareGiven = true;
            fx.Add(SewerEffect.GiveSpareKey);
            fx.AddRange(KeyObtained());
            return Finish(fx);
        }

        /// <summary>The goblin came for the player (scripted or random).</summary>
        public List<SewerEffect> GoblinCame()
        {
            var fx = new List<SewerEffect>();
            if (State.GoblinMet) return fx;
            State.GoblinMet = true;
            return Finish(fx);
        }

        /// <summary>The goblin took meth and left. Published once per save.</summary>
        public List<SewerEffect> GoblinTookMeth()
        {
            var fx = GoblinCame();
            if (State.GoblinCalmed) return fx;
            State.GoblinCalmed = true;
            fx.Add(SewerEffect.GoblinCalmed);
            return Finish(fx);
        }

        public List<SewerEffect> ClueFound(Clue clue)
        {
            var fx = new List<SewerEffect>();
            if (clue == Clue.None || State.HasClue(clue)) return fx;
            bool before = State.Identified;
            State.Clues |= clue;
            if (!before && State.Identified && State.Fate == KingFate.Unknown) fx.Add(SewerEffect.KingIdentified);
            return Finish(fx);
        }

        /// <summary>First words with the King: his rambling about "my town" is itself a clue.</summary>
        public List<SewerEffect> MeetKing()
        {
            var fx = new List<SewerEffect>();
            if (!State.KingAlive || State.KingMet) return fx;
            State.KingMet = true;
            fx.AddRange(ClueFound(Clue.Ramble));
            return Finish(fx);
        }

        /// <summary>Keep his secret: he teaches toad farming and shows the bootleggers' route.</summary>
        public List<SewerEffect> Spare()
        {
            var fx = new List<SewerEffect>();
            if (!CanConfront) return fx;
            State.Fate = KingFate.Spared;
            fx.Add(SewerEffect.KingSpared);
            RevealRoute(fx);
            return Finish(fx);
        }

        /// <summary>Say his name out loud: he fights to keep it.</summary>
        public List<SewerEffect> Reveal()
        {
            var fx = new List<SewerEffect>();
            if (!CanConfront) return fx;
            State.Fate = KingFate.Revealed;
            fx.Add(SewerEffect.KingRevealed);
            fx.Add(SewerEffect.KingHostile);
            return Finish(fx);
        }

        /// <summary>The King is fighting and we didn't set him on anyone: the player started it.</summary>
        public List<SewerEffect> KingAttacked()
        {
            var fx = new List<SewerEffect>();
            if (State.Fate != KingFate.Unknown && State.Fate != KingFate.Spared) return fx;
            if (State.Deal == KingDeal.Underboss) fx.Add(SewerEffect.UnderbossLost);
            State.Fate = KingFate.Revealed;
            State.KingAttacked = true;
            fx.Add(SewerEffect.KingRevealed);
            return Finish(fx);
        }

        /// <summary>The King is dead (or the game says he was defeated). The position, when known, is where his stash is.</summary>
        public List<SewerEffect> KingDefeated(float? x = null, float? y = null, float? z = null)
        {
            var fx = new List<SewerEffect>();
            if (State.Fate == KingFate.Defeated) return fx;
            // still serving only while spared: an attack has already ended the deal
            if (State.Deal == KingDeal.Underboss && State.Fate == KingFate.Spared) fx.Add(SewerEffect.UnderbossLost);
            State.Fate = KingFate.Defeated;
            if (x.HasValue && y.HasValue && z.HasValue) { State.StashX = x.Value; State.StashY = y.Value; State.StashZ = z.Value; }
            fx.Add(SewerEffect.KingDefeated);
            return Finish(fx);
        }

        public List<SewerEffect> ChooseDeal(KingDeal deal)
        {
            var fx = new List<SewerEffect>();
            if (!KingOffers || deal == KingDeal.None) return fx;
            State.Deal = deal;
            fx.Add(deal == KingDeal.Underboss ? SewerEffect.UnderbossHired : SewerEffect.PayHushMoney);
            return Finish(fx);
        }

        public List<SewerEffect> TakeStash()
        {
            var fx = new List<SewerEffect>();
            if (!State.StashAvailable) return fx;
            State.StashTaken = true;
            fx.Add(SewerEffect.PayStash);
            RevealRoute(fx);                                   // his journal is in the strongbox, and it mentions the route
            return Finish(fx);
        }

        private void RevealRoute(List<SewerEffect> fx)
        {
            if (State.RouteRevealed) return;
            State.RouteRevealed = true;
            fx.Add(SewerEffect.RouteRevealed);
        }

        /// <summary>Every transition ends here: a quest whose facts now say "done" completes, once.</summary>
        private List<SewerEffect> Finish(List<SewerEffect> fx)
        {
            if (!State.Completed && State.QuestStarted && Step == SewerStep.Done)
            {
                State.Completed = true;
                fx.Add(SewerEffect.QuestComplete);
            }
            return fx;
        }
    }
}
