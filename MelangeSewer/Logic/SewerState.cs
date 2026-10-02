using System;

namespace Melange.Sewer
{
    /// <summary>What the player did with the Sewer King. Spared and Revealed can still end in Defeated.</summary>
    public enum KingFate { Unknown = 0, Spared = 1, Revealed = 2, Defeated = 3 }

    /// <summary>The spared King's offer, once taken. Only one, ever.</summary>
    public enum KingDeal { None = 0, Underboss = 1, Payout = 2 }

    /// <summary>The things that point at P.P. Hyland. Two of the three identify him.</summary>
    [Flags]
    public enum Clue { None = 0, Journal = 1, Plaque = 2, Ramble = 4 }

    /// <summary>
    /// The quest's facts, as saved. Everything the quest shows is worked out from these, never from the order things
    /// happened in: a player who found the world key early, or killed the King before Jerry said a word, still gets a
    /// quest that makes sense. Plain fields, so S1API's JSON save writes it as is.
    /// </summary>
    public sealed class SewerState
    {
        public const int CurrentVersion = 1;
        public int Version = CurrentVersion;

        /// <summary>The trigger fired: Bagman V reached, or methylamine unlocked by the cartel spoke.</summary>
        public bool Triggered;
        /// <summary>Jerry has told his story and the quest is running.</summary>
        public bool QuestStarted;
        /// <summary>The player already had a way into the sewer when Jerry told it (his "been down there already" lines).</summary>
        public bool KeyFoundEarly;
        /// <summary>The sewer is unlocked or the player holds a key.</summary>
        public bool HasKey;
        public bool JerrySpareGiven;
        /// <summary>The goblin has come for the player at least once (the scripted visit, or the game's own random one).</summary>
        public bool GoblinMet;
        /// <summary>The scripted first visit has been sent, so it is never sent twice.</summary>
        public bool GoblinScripted;
        /// <summary>The goblin took meth and went away calm, at least once.</summary>
        public bool GoblinCalmed;
        public Clue Clues;
        public bool KingMet;
        public KingFate Fate;
        /// <summary>The King turned on the player because he was attacked, not because his name was said out loud.</summary>
        public bool KingAttacked;
        public KingDeal Deal;
        public bool StashTaken;
        public bool RouteRevealed;
        public bool Completed;
        /// <summary>Where the King fell, for the stash; NaN until known.</summary>
        public float StashX = float.NaN, StashY = float.NaN, StashZ = float.NaN;

        public bool HasClue(Clue c) => (Clues & c) == c;

        public int ClueCount
        {
            get
            {
                int n = 0;
                foreach (Clue c in new[] { Clue.Journal, Clue.Plaque, Clue.Ramble })
                    if (HasClue(c)) n++;
                return n;
            }
        }

        /// <summary>The player can name him: any two clues.</summary>
        public bool Identified => ClueCount >= 2;

        public bool KingAlive => Fate != KingFate.Defeated;

        public bool StashAvailable => Fate == KingFate.Defeated && !StashTaken && Deal != KingDeal.Payout;

        public bool HasStashPosition => !float.IsNaN(StashX) && !float.IsNaN(StashY) && !float.IsNaN(StashZ);
    }
}
