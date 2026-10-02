using System.Collections.Generic;

namespace Melange.Sewer
{
    /// <summary>
    /// Everything the sewer story says, and which line fits which state. Kept here, away from the game, so the tests can
    /// check that every state has something sensible to say. The King and the goblin's relationship is hinted at several
    /// ways and confirmed by none: keep it that way when adding lines.
    /// </summary>
    public static class SewerLines
    {
        public const string QuestTitle = "Down the Drain";
        public const string QuestDescription = "Jerry hears things under the street. So, now, do you.";

        public static string EntryTitle(SewerEntry entry) => entry switch
        {
            SewerEntry.Key => "Get a sewer key: the key holder, Jen, or Jerry's spare",
            SewerEntry.Sewer => "Go down into the sewer. Take some meth",
            SewerEntry.Clues => "Find out who the Sewer King is (the manor, his notebook)",
            SewerEntry.King => "Confront the Sewer King in the Sewer Office",
            SewerEntry.Offer => "Hear the Sewer King's offer",
            SewerEntry.Stash => "Find the old man's money",
            _ => entry.ToString(),
        };

        // ---- Jerry ----

        public const string JerryWarningChoice = "You wanted a word?";
        public const string JerryKeyChoice = "About that key...";
        public const string JerryChatChoice = "Heard anything?";
        public const string JerryAccept = "I'll be careful.";
        public const string JerryGo = "Go on.";
        public const string JerryFavourAsk = "The spare. What's the favour?";
        public const string JerryFavourGive = "Here.";
        public const string JerryFavourLater = "Not now.";
        public const string JerryFavourThanks = "Good. Key's yours. Don't lose it, I'm not getting another.";
        public const string JerryFavourNoMeth = "You haven't got any. Come back when you do.";

        public enum WarningVariant { Normal, AlreadyInside, KingGone }

        public static WarningVariant Variant(SewerState s)
            => s.Fate == KingFate.Defeated ? WarningVariant.KingGone : s.HasKey ? WarningVariant.AlreadyInside : WarningVariant.Normal;

        /// <summary>Jerry's warning, one line per dialogue node, ending on the line the player accepts after.</summary>
        public static IReadOnlyList<string> JerryWarning(WarningVariant variant)
        {
            var lines = new List<string> { "You're the new cook. Don't look at me like that, everyone knows. The smell gets around." };
            switch (variant)
            {
                case WarningVariant.AlreadyInside:
                    lines.Add("And you've been down there already. Don't lie, I can see it on your shoes.");
                    break;
                case WarningVariant.KingGone:
                    lines.Add("Heard someone did for the old man in the office down there. Didn't hear who. Don't want to.");
                    break;
                default:
                    lines.Add("I sleep by the canal, right by the grate. At night there's noises down there. Not rats. Rats don't cry.");
                    break;
            }
            lines.Add("Years back a lad went down with a bag of crystal and never came up. Folks say he's still down there. Folks say a lot of things.");
            lines.Add(variant == WarningVariant.KingGone
                ? "Old P.P. Hyland built this town, then walked off with its money. Whatever he was sitting on, it's still down there."
                : "And old P.P. Hyland, the man who built this town. Walked off one day with the town's money. Never found him either. Funny, that.");
            lines.Add(variant == WarningVariant.Normal
                ? "If you're going down, take a weapon. And some meth. Trust me."
                : "Next time you go down, take a weapon. And some meth. Trust me.");
            return lines;
        }

        /// <summary>Jerry's three leads. <paramref name="possessor"/> is the game's description of the key holder ("the guy with ...").</summary>
        public static IReadOnlyList<string> JerryKeyLeads(string possessor, bool spareOffered)
        {
            var lines = new List<string>
            {
                "Three ways in. " + (string.IsNullOrEmpty(possessor)
                    ? "Somebody in town carries one. Won't say where from."
                    : Capitalise(possessor) + ". Carries one. Won't say where from."),
                "Jen sells them, to people she likes. Be someone she likes.",
            };
            lines.Add(spareOffered ? "Or I've got a spare. For a favour." : "And you've had my spare. That's your lot.");
            return lines;
        }

        public const string JerryFavour = "One bag of meth. Not for me. I leave it by the grate and the crying stops for a night or two.";

        /// <summary>What Jerry says when asked, once the quest is under way.</summary>
        public static string JerryHint(SewerStep step) => step switch
        {
            SewerStep.EnterSewer => "Grate's by the canal. Go at night if you want to meet him. Go in the day if you don't. Makes no difference, really.",
            SewerStep.FindClues => "Hyland? There's a plaque up at the manor, his face on it. And he wrote everything down. Always had a notebook. Wonder where that went.",
            SewerStep.ConfrontKing => "So you know. Don't tell me. I sleep better not knowing.",
            SewerStep.FightKing => "You poked it, didn't you. Of course you did.",
            SewerStep.HearOffer => "Whatever he's offering, take it. Or don't. I'm not your mum.",
            SewerStep.TakeStash => "If he had the town's money, it's down there. Near where he fell, I'd guess. Old men keep their money close.",
            SewerStep.Done => "Some nights I hear two voices down there. One laughing, one crying. Never the same night.",
            _ => "Nothing new. Noises. Always noises.",
        };

        // ---- Frank (rumours) ----

        public const string FrankChoice = "Heard anything lately?";

        /// <summary>Frank's rumours for the current state; <paramref name="roll"/> picks one (any int).</summary>
        public static string FrankRumour(SewerState s, int roll)
        {
            string[] pool;
            if (s.Fate == KingFate.Defeated || s.Fate == KingFate.Revealed)
                pool = new[]
                {
                    "Heard there was a fight under the street. Heard the old man lost. Heard nothing, actually. I'm deaf when it suits me.",
                    "Crier's still down there. Louder now. Like he lost something.",
                };
            else if (s.Fate == KingFate.Spared)
                pool = new[]
                {
                    "Somebody's been fixing the lights down in the tunnels. Who does that?",
                    "Saw a man buy forty feet of hose and a fish tank. Paid in old notes. Old old.",
                };
            else if (s.GoblinMet)
                pool = new[]
                {
                    "Saw a man come out of the canal pipe once. Grey as a fish. Asked me for crystal. I gave him a sandwich. He cried.",
                    "My granddad worked for Hyland. Said he laughed at his own jokes and paid in cash. Always cash.",
                    "There's a tunnel under that manor. Everyone knows. Nobody's been down it. Well. One man.",
                };
            else if (s.QuestStarted)
                pool = new[]
                {
                    "Hyland had a plaque put up at his own house. Who does that? A man who knows he's leaving, that's who.",
                    "My granddad worked for Hyland. Said he never once came up for air.",
                };
            else if (s.Triggered)
                pool = new[] { "Jerry's been asking after you. Says you smell like a chemistry set. I said you smell like money." };
            else
                pool = new[]
                {
                    "Hyland Point. Named after a man who robbed it. Says it all.",
                    "Don't sleep near the drains. That's free advice. The rest costs.",
                };
            int i = ((roll % pool.Length) + pool.Length) % pool.Length;
            return pool[i];
        }

        // ---- the Sewer King ----

        public const string KingSpotted = "Who sent you? This is my kingdom.";
        public const string KingGreetChoice = "Who are you?";
        public static readonly string[] KingGreeting =
        {
            "Who sent you? This is my kingdom.",
            "My town. I built it. Up there. The streets, the pier, the bank. Especially the bank.",
            "They've forgotten me. Good. Now get out of my office.",
        };

        public const string KingConfrontChoice = "I know who you are, Mr Hyland.";
        public static readonly string[] KingConfront =
        {
            "Nobody's called me that in forty years. Nobody that lived long, anyway.",
            "So. What are you going to do with it?",
        };
        public const string SpareChoice = "Your secret's safe with me.";
        public const string RevealChoice = "Everyone's going to know who you are.";
        public const string KingRevealReply = "Then you don't leave.";

        /// <summary>He teaches toad farming and shows the bootleggers' route, then makes his offer.</summary>
        public static readonly string[] KingMentor =
        {
            "Then you're smarter than you look. Sit. I'll teach you something.",
            "Toads. The ugly ones in the damp. Forty years I've lived on them. Milk them, cure it, and the walls start talking.",
            "First one's down here, by the overflow. Better ones at the docks, later. Tanks and feed, there's a supplier. I'll tell you who.",
            "And there's an old route. Rum, back when rum was worth the trouble. Sewer to the pier, no streets, no police. Still dry, mostly.",
            "And leave the crier alone. He's mine.",
        };

        public const string KingOfferChoice = "About your offer.";
        public static readonly string[] KingOffer =
        {
            "Two ways this goes. I run your garden down here, the beds, the toads, whatever you grow in the dark. I take my cut.",
            "Or I pay you to forget my name, and you stay out of my kingdom. Choose.",
        };
        public const string UnderbossChoice = "Run it for me.";
        public const string PayoutChoice = "Pay me.";
        public const string LaterChoice = "I'll think about it.";
        public const string UnderbossReply = "Good. Don't make me regret it. I've outlived everyone who did.";
        public const string PayoutReply = "There. More than this town ever gave me. Now forget me.";

        public const string KingChatChoice = "How's the kingdom?";
        public static string KingChat(KingDeal deal) => deal == KingDeal.Underboss
            ? "The beds are fine. The toads are fine. Stop checking."
            : "You were paid. Go.";

        // ---- Jen ----

        /// <summary>Her offer, rewritten to fit the story; keeps the game's &lt;PRICE&gt; token, which her controller fills in.</summary>
        public const string JenOffer = "Jerry sent you. Figures. <PRICE>, and you never got it from me. If something down there asks for crystal, don't argue.";

        // ---- Oscar (his sewer hints become smuggling-ring talk) ----

        public const string OscarRegionHint = "Where do I get my stock? Boats, mostly. Friends of friends who don't like customs. Keep buying and maybe I'll introduce you.";
        public const string OscarNpcHint = "There's a man with a boat who doesn't ask questions. I don't give his name out to just anyone. Spend some money here first.";
        public const string OscarChoice = "Where do you get your stock?";

        // ---- things found ----

        public const string JournalTitle = "A waterlogged notebook";
        public const string JournalText = "'Day nine thousand and something. The boy brought crystal again. I let him. Who else has he got.' Signed P.P.H.";
        public const string PlaqueTitle = "A brass plaque";
        public const string PlaqueText = "'P.P. Hyland, founder. Gone but not forgotten.' Someone has scratched out 'gone'.";
        public const string IdentifiedTitle = "The Sewer King";
        public const string IdentifiedText = "You know who he is. So will he, when you tell him.";
        public const string StashTitle = "The founder's money";
        public const string StashText = "A rusted strongbox: old notes in Hyland Point Savings bands, and a page of the journal, a route from the sewer to the pier.";
        public const string OfferTitle = "Jerry wants a word";
        public const string OfferText = "He sleeps by the canal. He's heard about the new cook.";

        private static string Capitalise(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
