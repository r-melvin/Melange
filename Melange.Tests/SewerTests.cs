using System;
using System.Linq;
using Melange.Sewer;
using Xunit;

namespace Melange.Tests
{
    public sealed class SewerTests
    {
        private static SewerQuest Fresh() => new SewerQuest(new SewerState());

        /// <summary>Triggered, Jerry heard, key held, goblin met: standing in front of the King with no name for him.</summary>
        private static SewerQuest AtTheKing()
        {
            var q = Fresh();
            q.Trigger();
            q.TalkToJerry();
            q.KeyObtained();
            q.GoblinCame();
            return q;
        }

        private static SewerQuest Identified()
        {
            var q = AtTheKing();
            q.ClueFound(Clue.Plaque);
            q.MeetKing();
            return q;
        }

        // ---- trigger ----

        [Theory]
        [InlineData(4, 4, false)]
        [InlineData(4, 5, true)]
        [InlineData(5, 1, true)]
        [InlineData(3, 5, false)]
        [InlineData(10, 7, true)]
        public void TheRankTriggerIsBagmanV(int rank, int tier, bool triggers)
            => Assert.Equal(triggers, SewerQuest.RankTriggers(rank, tier));

        [Fact]
        public void LockedUntilTriggered()
        {
            var q = Fresh();
            Assert.Equal(SewerStep.Locked, q.Step);
            Assert.False(q.JerryOffersWarning);
            Assert.Empty(q.TalkToJerry());                     // Jerry has nothing to say yet
            Assert.False(q.State.QuestStarted);
        }

        [Fact]
        public void TriggerOffersTheQuestOnce()
        {
            var q = Fresh();
            Assert.Equal(new[] { SewerEffect.OfferQuest }, q.Trigger());
            Assert.Equal(SewerStep.TalkToJerry, q.Step);
            Assert.True(q.JerryOffersWarning);
            Assert.Empty(q.Trigger());                         // rank-ups and methylamine both fire: only the first counts
        }

        [Fact]
        public void TriggerAfterTheQuestStartedOffersNothing()
        {
            var q = Fresh();
            q.State.Triggered = false;
            q.State.QuestStarted = true;                       // an odd save: started but not triggered
            Assert.DoesNotContain(SewerEffect.OfferQuest, q.Trigger());
        }

        // ---- Jerry ----

        [Fact]
        public void JerryStartsTheQuest()
        {
            var q = Fresh();
            q.Trigger();
            Assert.Equal(new[] { SewerEffect.QuestBegins }, q.TalkToJerry());
            Assert.Equal(SewerStep.GetKey, q.Step);
            Assert.False(q.JerryOffersWarning);
            Assert.True(q.JerryKeyLeads);
            Assert.True(q.JerrySpareOffered);
            Assert.True(q.JenSells);
            Assert.False(q.State.KeyFoundEarly);
            Assert.Empty(q.TalkToJerry());
        }

        [Fact]
        public void JenDoesNotSellBeforeJerryNamesHer()
        {
            var q = Fresh();
            Assert.False(q.JenSells);
            q.Trigger();
            Assert.False(q.JenSells);
        }

        [Fact]
        public void AKeyFoundEarlySkipsTheKeyStep()
        {
            var q = Fresh();
            q.KeyObtained();                                   // the random world key, before any of this
            q.Trigger();
            Assert.Equal(SewerLines.WarningVariant.AlreadyInside, SewerLines.Variant(q.State));
            q.TalkToJerry();
            Assert.True(q.State.KeyFoundEarly);
            Assert.Equal(SewerStep.EnterSewer, q.Step);
            Assert.False(q.JerryKeyLeads);
            Assert.True(q.JerryChats);
            Assert.Equal(EntryStatus.Completed, q.Entry(SewerEntry.Key));
        }

        [Fact]
        public void JerrysSpareGivesTheKeyOnce()
        {
            var q = Fresh();
            q.Trigger();
            q.TalkToJerry();
            Assert.Equal(new[] { SewerEffect.GiveSpareKey }, q.GiveJerrysSpare());
            Assert.True(q.State.HasKey);
            Assert.True(q.State.JerrySpareGiven);
            Assert.Equal(SewerStep.EnterSewer, q.Step);
            Assert.Empty(q.GiveJerrysSpare());
        }

        [Fact]
        public void NoSpareBeforeTheQuestOrOnceAKeyIsHeld()
        {
            var q = Fresh();
            Assert.Empty(q.GiveJerrysSpare());
            q.Trigger();
            q.TalkToJerry();
            q.KeyObtained();
            Assert.False(q.JerrySpareOffered);
            Assert.Empty(q.GiveJerrysSpare());
            Assert.False(q.State.JerrySpareGiven);
        }

        [Fact]
        public void KeyObtainedIsIdempotent()
        {
            var q = Fresh();
            q.KeyObtained();
            Assert.Empty(q.KeyObtained());
            Assert.True(q.State.HasKey);
        }

        // ---- the goblin ----

        [Fact]
        public void TheGoblinIsScriptedOnlyOnceInsideWithTheQuestRunning()
        {
            var q = Fresh();
            q.KeyObtained();
            Assert.False(q.ShouldScriptGoblin);                // no quest yet: the game's random hunt only
            q.Trigger();
            q.TalkToJerry();
            Assert.True(q.ShouldScriptGoblin);
            q.GoblinCame();
            Assert.False(q.ShouldScriptGoblin);
        }

        [Fact]
        public void NoScriptedGoblinWithoutAWayIn()
        {
            var q = Fresh();
            q.Trigger();
            q.TalkToJerry();
            Assert.False(q.ShouldScriptGoblin);
        }

        [Fact]
        public void MeetingTheGoblinMovesOnToTheClues()
        {
            var q = Fresh();
            q.Trigger();
            q.TalkToJerry();
            q.KeyObtained();
            Assert.Equal(SewerStep.EnterSewer, q.Step);
            Assert.Empty(q.GoblinCame());
            Assert.Equal(SewerStep.FindClues, q.Step);
            Assert.Empty(q.GoblinCame());
        }

        [Fact]
        public void CalmingTheGoblinIsPublishedOnceAndCountsAsMeetingHim()
        {
            var q = Fresh();
            Assert.Equal(new[] { SewerEffect.GoblinCalmed }, q.GoblinTookMeth());
            Assert.True(q.State.GoblinMet);
            Assert.Empty(q.GoblinTookMeth());
        }

        // ---- clues ----

        [Fact]
        public void TwoCluesIdentifyTheKing()
        {
            var q = AtTheKing();
            Assert.Empty(q.ClueFound(Clue.Journal));
            Assert.False(q.State.Identified);
            Assert.Equal(new[] { SewerEffect.KingIdentified }, q.ClueFound(Clue.Plaque));
            Assert.True(q.State.Identified);
            Assert.Equal(SewerStep.ConfrontKing, q.Step);
            Assert.Empty(q.ClueFound(Clue.Plaque));
            Assert.Empty(q.ClueFound(Clue.None));
        }

        [Fact]
        public void MeetingTheKingIsAClueOnlyOnce()
        {
            var q = AtTheKing();
            Assert.True(q.KingGreets);
            Assert.Empty(q.MeetKing());
            Assert.True(q.State.HasClue(Clue.Ramble));
            Assert.False(q.KingGreets);
            Assert.Equal(new[] { SewerEffect.KingIdentified }, q.ClueFound(Clue.Journal));
            Assert.Empty(q.MeetKing());
        }

        [Fact]
        public void NoMeetingADeadKing()
        {
            var q = AtTheKing();
            q.KingDefeated();
            Assert.Empty(q.MeetKing());
            Assert.False(q.State.KingMet);
        }

        [Fact]
        public void IdentifyingAfterTheFightIsNotNews()
        {
            var q = AtTheKing();
            q.KingAttacked();
            q.ClueFound(Clue.Journal);
            Assert.DoesNotContain(SewerEffect.KingIdentified, q.ClueFound(Clue.Plaque));
        }

        [Fact]
        public void CluesLieAroundOnlyWhileTheyMatter()
        {
            var q = Fresh();
            Assert.False(q.CluesInWorld);
            q.Trigger();
            q.TalkToJerry();
            Assert.True(q.CluesInWorld);
            q.KingDefeated();
            Assert.False(q.CluesInWorld);
        }

        // ---- the confrontation ----

        [Fact]
        public void NoConfrontationWithoutANameOrAQuest()
        {
            var q = AtTheKing();
            Assert.False(q.CanConfront);
            Assert.Empty(q.Spare());
            Assert.Empty(q.Reveal());

            var early = Fresh();
            early.ClueFound(Clue.Plaque);
            early.MeetKing();
            Assert.True(early.State.Identified);
            Assert.False(early.CanConfront);                   // knowing isn't enough: the quest must be running
        }

        [Fact]
        public void SparingHimTeachesToadsAndShowsTheRoute()
        {
            var q = Identified();
            Assert.Equal(new[] { SewerEffect.KingSpared, SewerEffect.RouteRevealed }, q.Spare());
            Assert.Equal(KingFate.Spared, q.State.Fate);
            Assert.Equal(SewerStep.HearOffer, q.Step);
            Assert.True(q.KingOffers);
            Assert.True(q.KingStaysCalm);
            Assert.Empty(q.Spare());
            Assert.Empty(q.Reveal());                          // the choice is made
        }

        [Fact]
        public void RevealingHimSetsHimOnThePlayer()
        {
            var q = Identified();
            Assert.Equal(new[] { SewerEffect.KingRevealed, SewerEffect.KingHostile }, q.Reveal());
            Assert.Equal(SewerStep.FightKing, q.Step);
            Assert.False(q.KingStaysCalm);
            Assert.False(q.State.KingAttacked);
            Assert.Empty(q.Reveal());
            Assert.Empty(q.KingAttacked());                    // already fighting
        }

        [Fact]
        public void HeStaysCalmUntilRevealedOrAttacked()
        {
            var q = Fresh();
            Assert.True(q.KingStaysCalm);                      // even before the quest: no more attacks on sight
            Assert.Equal(new[] { SewerEffect.KingRevealed }, q.KingAttacked());
            Assert.True(q.State.KingAttacked);
            Assert.False(q.KingStaysCalm);
        }

        [Fact]
        public void AttackingTheUnderbossLosesHim()
        {
            var q = Identified();
            q.Spare();
            q.ChooseDeal(KingDeal.Underboss);
            var fx = q.KingAttacked();
            Assert.Equal(new[] { SewerEffect.UnderbossLost, SewerEffect.KingRevealed }, fx);
            Assert.Equal(KingFate.Revealed, q.State.Fate);
        }

        // ---- the deal ----

        [Fact]
        public void TheUnderbossDealEndsTheQuest()
        {
            var q = Identified();
            q.Spare();
            Assert.Equal(new[] { SewerEffect.UnderbossHired, SewerEffect.QuestComplete }, q.ChooseDeal(KingDeal.Underboss));
            Assert.Equal(KingDeal.Underboss, q.State.Deal);
            Assert.Equal(SewerStep.Done, q.Step);
            Assert.True(q.KingChats);
            Assert.False(q.KingOffers);
        }

        [Fact]
        public void ThePayoutIsOneTime()
        {
            var q = Identified();
            q.Spare();
            Assert.Equal(new[] { SewerEffect.PayHushMoney, SewerEffect.QuestComplete }, q.ChooseDeal(KingDeal.Payout));
            Assert.Empty(q.ChooseDeal(KingDeal.Payout));
            Assert.Empty(q.ChooseDeal(KingDeal.Underboss));
            Assert.Equal(KingDeal.Payout, q.State.Deal);
        }

        [Fact]
        public void NoDealWithoutSparingHimOrForNothing()
        {
            var q = Identified();
            Assert.Empty(q.ChooseDeal(KingDeal.Payout));
            q.Spare();
            Assert.Empty(q.ChooseDeal(KingDeal.None));
            Assert.Equal(KingDeal.None, q.State.Deal);
        }

        [Fact]
        public void ADealBeforeMeetingTheGoblinWaitsForHim()
        {
            var q = Fresh();
            q.Trigger();
            q.TalkToJerry();
            q.KeyObtained();
            q.ClueFound(Clue.Plaque);
            q.MeetKing();                                      // straight to the office, goblin never seen
            q.Spare();
            Assert.DoesNotContain(SewerEffect.QuestComplete, q.ChooseDeal(KingDeal.Payout));
            Assert.Equal(SewerStep.EnterSewer, q.Step);
            Assert.Equal(new[] { SewerEffect.QuestComplete }, q.GoblinCame());
        }

        // ---- the fight and the stash ----

        [Fact]
        public void KillingHimLeavesTheStash()
        {
            var q = Identified();
            q.Reveal();
            Assert.Equal(new[] { SewerEffect.KingDefeated }, q.KingDefeated(1f, 2f, 3f));
            Assert.True(q.State.HasStashPosition);
            Assert.Equal(2f, q.State.StashY);
            Assert.Equal(SewerStep.TakeStash, q.Step);
            Assert.Equal(EntryStatus.Active, q.Entry(SewerEntry.Stash));
            Assert.Empty(q.KingDefeated());
            Assert.Equal(new[] { SewerEffect.PayStash, SewerEffect.RouteRevealed, SewerEffect.QuestComplete }, q.TakeStash());
            Assert.Empty(q.TakeStash());
            Assert.Equal(SewerStep.Done, q.Step);
        }

        [Fact]
        public void NoStashWhileHeLives()
        {
            var q = Identified();
            Assert.Empty(q.TakeStash());
            Assert.False(q.State.StashTaken);
        }

        [Fact]
        public void APaidOffKingKilledLaterHasNoStashLeft()
        {
            var q = Identified();
            q.Spare();
            q.ChooseDeal(KingDeal.Payout);
            q.KingAttacked();
            q.KingDefeated();
            Assert.False(q.State.StashAvailable);
            Assert.Empty(q.TakeStash());
        }

        [Fact]
        public void KillingTheUnderbossLosesHimAndLeavesTheStash()
        {
            var q = Identified();
            q.Spare();
            q.ChooseDeal(KingDeal.Underboss);
            q.KingAttacked();
            Assert.Equal(new[] { SewerEffect.KingDefeated }, q.KingDefeated());   // lost already on the attack
            Assert.True(q.State.StashAvailable);
        }

        [Fact]
        public void KillingTheUnderbossOutrightLosesHim()
        {
            var q = Identified();
            q.Spare();
            q.ChooseDeal(KingDeal.Underboss);
            Assert.Equal(new[] { SewerEffect.UnderbossLost, SewerEffect.KingDefeated }, q.KingDefeated());
        }

        [Fact]
        public void TheRouteIsRevealedOnlyOnce()
        {
            var q = Identified();
            q.Spare();
            q.KingAttacked();
            q.KingDefeated();
            Assert.DoesNotContain(SewerEffect.RouteRevealed, q.TakeStash());
        }

        [Fact]
        public void AKingAlreadyDeadEndsOnTheStashAndTheGoblin()
        {
            var q = Fresh();
            q.KingDefeated();                                  // a save where he died before this mod
            Assert.False(q.State.HasStashPosition);
            q.Trigger();
            Assert.Equal(SewerLines.WarningVariant.KingGone, SewerLines.Variant(q.State));
            q.TalkToJerry();
            Assert.False(q.JerryKeyLeads);                     // no key needed to reach a dead man's money
            Assert.Equal(SewerStep.TakeStash, q.Step);
            Assert.Equal(EntryStatus.Completed, q.Entry(SewerEntry.Key));
            Assert.Equal(EntryStatus.Completed, q.Entry(SewerEntry.King));
            q.TakeStash();
            Assert.Equal(SewerStep.EnterSewer, q.Step);
            Assert.True(q.ShouldScriptGoblin);
            Assert.Equal(new[] { SewerEffect.QuestComplete }, q.GoblinCame());
        }

        [Fact]
        public void TheQuestCompletesOnlyOnce()
        {
            var q = Identified();
            q.Spare();
            q.ChooseDeal(KingDeal.Underboss);
            Assert.True(q.State.Completed);
            var fx = q.KingAttacked();
            Assert.DoesNotContain(SewerEffect.QuestComplete, fx);
            Assert.DoesNotContain(SewerEffect.QuestComplete, q.KingDefeated());
            Assert.Equal(SewerStep.Done, q.Step);
        }

        [Fact]
        public void NothingCompletesBeforeTheQuestStarts()
        {
            var q = Fresh();
            q.KeyObtained();
            q.GoblinCame();
            q.KingDefeated();
            Assert.False(q.State.Completed);
            Assert.Equal(SewerStep.Locked, q.Step);
        }

        // ---- journal entries ----

        [Fact]
        public void EntriesAreHiddenBeforeTheQuest()
        {
            var q = Fresh();
            q.Trigger();
            foreach (SewerEntry e in Enum.GetValues(typeof(SewerEntry)))
                Assert.Equal(EntryStatus.Inactive, q.Entry(e));
        }

        [Fact]
        public void EntriesFollowTheMainPath()
        {
            var q = Fresh();
            q.Trigger();
            q.TalkToJerry();
            Assert.Equal(EntryStatus.Active, q.Entry(SewerEntry.Key));
            Assert.Equal(EntryStatus.Inactive, q.Entry(SewerEntry.Sewer));
            q.KeyObtained();
            Assert.Equal(EntryStatus.Completed, q.Entry(SewerEntry.Key));
            Assert.Equal(EntryStatus.Active, q.Entry(SewerEntry.Sewer));
            Assert.Equal(EntryStatus.Active, q.Entry(SewerEntry.Clues));
            Assert.Equal(EntryStatus.Inactive, q.Entry(SewerEntry.King));
            q.GoblinCame();
            q.ClueFound(Clue.Journal);
            q.ClueFound(Clue.Plaque);
            Assert.Equal(EntryStatus.Completed, q.Entry(SewerEntry.Sewer));
            Assert.Equal(EntryStatus.Completed, q.Entry(SewerEntry.Clues));
            Assert.Equal(EntryStatus.Active, q.Entry(SewerEntry.King));
            q.Spare();
            Assert.Equal(EntryStatus.Completed, q.Entry(SewerEntry.King));
            Assert.Equal(EntryStatus.Active, q.Entry(SewerEntry.Offer));
            Assert.Equal(EntryStatus.Inactive, q.Entry(SewerEntry.Stash));
            q.ChooseDeal(KingDeal.Payout);
            Assert.Equal(EntryStatus.Completed, q.Entry(SewerEntry.Offer));
        }

        [Fact]
        public void TheKingEntryStaysOpenWhileHeFights()
        {
            var q = AtTheKing();
            q.KingAttacked();                                  // unidentified, but he's fighting
            Assert.Equal(EntryStatus.Active, q.Entry(SewerEntry.King));
            Assert.Equal(EntryStatus.Completed, q.Entry(SewerEntry.Clues));
            Assert.Equal(SewerStep.FightKing, q.Step);
            q.KingDefeated();
            Assert.Equal(EntryStatus.Completed, q.Entry(SewerEntry.King));
            Assert.Equal(EntryStatus.Inactive, q.Entry(SewerEntry.Offer));
        }

        // ---- lines ----

        [Fact]
        public void JerryHasAWarningForEveryVariantEndingOnTheAdvice()
        {
            foreach (SewerLines.WarningVariant v in Enum.GetValues(typeof(SewerLines.WarningVariant)))
            {
                var lines = SewerLines.JerryWarning(v);
                Assert.True(lines.Count >= 3);
                Assert.Contains("meth", lines.Last());
                Assert.Contains(lines, l => l.Contains("Hyland"));
            }
        }

        [Fact]
        public void JerryNamesTheKeyHolderAndJen()
        {
            var lines = SewerLines.JerryKeyLeads("the guy with the big eyebrows", true);
            Assert.StartsWith("Three ways in. The guy with the big eyebrows.", lines[0]);
            Assert.Contains(lines, l => l.Contains("Jen"));
            Assert.Contains("spare", lines.Last());
            Assert.DoesNotContain("<", string.Join(" ", SewerLines.JerryKeyLeads(null, false)));
        }

        [Fact]
        public void EveryStepHasAHintAndEveryEntryATitle()
        {
            foreach (SewerStep s in Enum.GetValues(typeof(SewerStep)))
                Assert.False(string.IsNullOrWhiteSpace(SewerLines.JerryHint(s)));
            foreach (SewerEntry e in Enum.GetValues(typeof(SewerEntry)))
                Assert.NotEqual(e.ToString(), SewerLines.EntryTitle(e));
        }

        [Fact]
        public void FrankAlwaysHasARumour()
        {
            var states = new[]
            {
                new SewerState(),
                new SewerState { Triggered = true },
                new SewerState { Triggered = true, QuestStarted = true },
                new SewerState { Triggered = true, QuestStarted = true, GoblinMet = true },
                new SewerState { Fate = KingFate.Spared },
                new SewerState { Fate = KingFate.Revealed },
                new SewerState { Fate = KingFate.Defeated },
            };
            foreach (var s in states)
                foreach (int roll in new[] { 0, 1, 7, -3, int.MinValue + 1, int.MaxValue })
                    Assert.False(string.IsNullOrWhiteSpace(SewerLines.FrankRumour(s, roll)));
        }

        [Fact]
        public void JensOfferKeepsThePriceToken() => Assert.Contains("<PRICE>", SewerLines.JenOffer);

        [Fact]
        public void NoLineSettlesWhatTheKingAndTheGoblinAre()
        {
            // the user's rule: hint, never confirm
            var all = string.Join(" ", SewerLines.KingMentor.Concat(SewerLines.KingGreeting).Concat(SewerLines.KingOffer)
                .Concat(new[] { SewerLines.JournalText, SewerLines.JerryHint(SewerStep.Done) })).ToLowerInvariant();
            foreach (var word in new[] { "son", "lover", "servant", "slave", "husband", "brother" })
                Assert.DoesNotContain(" " + word + " ", " " + all + " ");
        }
    }
}
