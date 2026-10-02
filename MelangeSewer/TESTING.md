# Melange Sewer: in-game checks

Nothing here has run in the game yet. The pure logic (`Logic/`) is covered by `Melange.Tests/SewerTests.cs`; everything
that touches the game is listed below as a probe scenario, then every assumption code could not settle.

Install: `MelangeCore.dll`, `MelangeSewer.dll`, S1API. Log lines come from the `Melange_Sewer` logger in
`MelonLoader/Latest.log`; quoted text below is what to grep for. Console commands are the game's own (`addxp`, `give`,
`teleport`, `settime`, `setrelationship`, `changecash`).

## Hand-overs (done)

1. The shared events now live in `MelangeCore/SharedEvents.cs`.
2. `OscarDialogue` patches lazily with its own Harmony id; kept as is (idempotent, in the hub's assembly).
3. The King's `OnTick` prefix stays in the spoke while it is the only patch on that method.
4. `MelangeLevels` already depended on `"MelangeCore"`.
5. `MelangeLevelsData` now resets to defaults on `MenuLoaded`, as this spoke's data does.

## Probe scenarios

Each: setup, then what to look for. "Host" means single player or the co-op host.

### P1. Load and wiring
- Setup: load any save.
- Log: `loaded: step <Step>, triggered <bool>, started <bool>, fate <Fate>, deal <Deal>`. No warnings starting
  `Jerry has no dialogue controller`, `Frank has no...`, `the Sewer King has no...`, `goblin calm events are off`,
  `the Sewer King keeps his vanilla temper`, `Oscar's line rewrites are off`, `no sewer save data`.
- Also expected once Jen's Start has run: `Jen found: <name>, key choice 'I want to buy a sewer access key'` and
  `Jen's offer was: <vanilla text>` (record the vanilla text: it was never seen in code).
- Expected for a save below Bagman V: `step Locked`. At or above: `[rank on load] OfferQuest` then `step now TalkToJerry`.

### P2. Trigger by rank
- Setup: a save below Bagman V. `addxp` until rank Bagman V (Bagman I starts at 10,250 total XP; Bagman V at 14,350).
- Log: `[rank Bagman 5] OfferQuest`, `step now TalkToJerry`; a notification "Jerry wants a word".
- Check: exactly one OfferQuest even when one award crosses several tiers.

### P3. Oscar
- Setup: talk to Oscar and pick whatever choices led to his key hints.
- Expected: his lines read `Where do I get my stock? ...` and `There's a man with a boat ...`; no region name and no NPC
  description. Any choice containing both "sewer" and "key" reads `Where do you get your stock?`.
- Record: the vanilla choice texts and node labels, and whether `ENTRY`/`NPC_HINT` are in their own conversations.

### P4. Jerry's warning (quest start)
- Setup: P2 done. Find Jerry (jerry_montero; Jerry's Tent 3:00-9:50, else the construction site). Talk.
- Expected: choice `You wanted a word?`; five lines, each moved on with `Go on.`, ending on `I'll be careful.`.
- Log on accept: `[Jerry] QuestBegins`, `step now GetKey`. Journal: quest "Down the Drain", entry "Get a sewer key..."
  active with a marker on Jerry; other entries hidden.
- Variant checks: with the sewer already unlocked, the second line is `And you've been down there already...` and the
  step goes straight to `EnterSewer`; with the King already dead, `Heard someone did for the old man...` and the step is
  `TakeStash`.

### P5. The three key leads
- Setup: P4 done, no key. Talk to Jerry: `About that key...`.
- Expected: the first line names the vanilla key holder (`Three ways in. The guy with ...`), then Jen, then the spare.
- Jen: before P4 her `I want to buy a sewer access key` choice is absent (log `Jen's key sale closed` if it was shown);
  after P4 `Jen's key sale open`, relationship rule unchanged (`setrelationship` to test), offer line reads
  `Jerry sent you. Figures. $<price>...` with the price filled in green.
- Jerry's favour: `The spare. What's the favour?` -> `Here.` with no meth: bubble `You haven't got any...`, nothing
  taken. With meth (`give` the goblin's pacify item; log its ID by checking `SewerGoblinNPC.PacifyItem.ID`, expected
  `meth`): one removed, a sewer key added, log `[Jerry's favour] GiveSpareKey`, `step now EnterSewer`; the key choice
  disappears within a second.

### P6. Scripted goblin
- Setup: P5 done (key held, sewer unlocked), no goblin met. Enter the sewer and stay 30 s.
- Log: `scripted goblin visit to <player>` then `[goblin came]`, `step now FindClues`. Vanilla log
  `Sewer Goblin deploying to player`.
- Hold meth: vanilla `Sewer Goblin beginning retrieve!`, then `[goblin took meth] GoblinCalmed`. Re-check the next random
  visit still happens (vanilla cooldown 12 h).
- If he never comes: the attempt repeats every 60 s while in the sewer; record why (`IsPlayerValidTarget` false inside
  the office property, for one).

### P7. Clues
- Setup: P6 done. Journal: follow the "Find out who..." marker into the sewer (the middle `SewerMushrooms` location).
  Plaque: Hyland Manor's `SpawnPoint`.
- Expected: a small flat box at each; walking within 2.5 m shows a notification and logs `[journal]` / `[plaque]`.
  The second clue (any two of journal, plaque, the King's rambling) logs `KingIdentified`, `step now ConfrontKing`.
- Record: both positions (log `Spot.Position`), whether they are reachable and visible, and whether the manor spawn point
  is outside the gate.

### P8. The King is calm
- Setup: enter the Sewer Office (passcode) with the King alive, any quest state.
- Expected: no attack; a bubble `Who sent you? This is my kingdom.` once per visit. Talking: `Who are you?` gives three
  lines and logs `[met the King]` (plus `KingIdentified` if a physical clue was already found).
- Hit him once: log `[King attacked] KingRevealed`; he fights back and keeps fighting after leaving and re-entering.

### P9. Spare path
- Setup: identified (P7/P8), King alive. `I know who you are, Mr Hyland.` -> `Your secret's safe with me.`
- Log: `[spared] KingSpared`, `[spared] RouteRevealed`, `step now HearOffer`. Five mentor lines, then the offer.
- `Run it for me.`: `[underboss] UnderbossHired`, `QuestComplete` (if the goblin was met), bubble reply; `Managers.Get("sewer.king")`
  is non-null (and after a reload). `Pay me.` instead: `[payout] PayHushMoney`, `paid 10000 cash`, cash +$10,000.
- `I'll think about it.` ends with no deal; `About your offer.` brings it back.
- Reload: `loaded: ... fate Spared, deal Underboss`, the King still calm, `How's the kingdom?` available.

### P10. Reveal and the stash
- Setup: identified, King alive. Confront -> `Everyone's going to know who you are.`
- Expected: `Then you don't leave.`; when the conversation closes, `the Sewer King turns on the player`; he attacks.
- Kill him: `[king down] KingDefeated`, `step now TakeStash`; vanilla `HasSewerKingBeenDefeated` true after a reload.
  A box where he fell; walking to it: `PayStash`, `RouteRevealed`, `paid 25000 cash`, `QuestComplete`.
- Knocking him out (not killing) also counts: the game's own flag uses `onDieOrKnockedOut`.

### P11. Saves and order
- Save mid-quest (each step), reload: the same `step` in `loaded:`; the journal shows the same entries; no duplicate
  "Down the Drain" quest. Load a save without sewer data straight after: `step Locked` (the Reset guard).
- A King killed before the mod was installed: on load `[king down] KingDefeated`; the stash box at his last position
  (or the office's interior spawn point); after Jerry, step `TakeStash`.

### P12. Co-op (two clients)
- Host: everything above. Client: log `co-op client: the host runs the sewer story`; Oscar's rewritten lines appear; the
  King does not attack on sight (his tick runs only on the server, which is the host); Jerry/Frank/King show no new
  choices; Jen's sale is vanilla on the client.

## Assumptions not verified (conservative choice in brackets)

- **Choices on vanilla NPCs.** S1API cannot build dialogue for NPCs it has no wrapper for (the Sewer King has none), so all
  conversations are built from the game's own data types (`Conversation`, `DialogueNodeData`, `NodeLinkData`, as S1API's
  own builder does) and attached as `DialogueController.DialogueChoice`s, the way the game adds the key holder's question.
  [Unverified: that a runtime `ScriptableObject.CreateInstance<Conversation>()` plays through `DialogueHandler.StartDialogue`
  on IL2CPP. S1API does the same for its custom NPCs.]
- **Choice visibility** uses the choice's `Enabled` flag, refreshed every second, not the game's `shouldShowCheck`
  delegate (an IL2CPP delegate type; not attempted). A choice can lag a state change by up to a second.
- **Choices inside our conversations** are heard through `DialogueHandler.onDialogueChoiceChosen` (UnityEvent<string>).
  [Assumed every handler invokes it; NPC subclasses of the handler might not call base.]
- **NPC ids**: Jerry `jerry_montero` (S1API's list maps it to `ScheduleOne.NPCs.Jerry`), Frank `cranky_frank`. Jen is
  found by her `DialogueController_Jen` component, Oscar by `DialogueController_Oscar`, the King through
  `SewerManager.SewerKingNPC`. [Whether this Jerry is the one in "Jerry's Tent", unchecked.]
- **The King has a DialogueController.** [If not, P8/P9 log `the Sewer King has no dialogue controller` and the confront
  can't happen; the fallback would be talking to him through a world-space prompt.]
- **Oscar's hints**: identified by the original text containing `<REGION>` or `<NPC_DESCRIPTION>` (from his controller's
  code), not by label alone, because the generic `ENTRY` label is shared with his greeting. His choice text that asks about
  keys was never seen: the rewrite matches "sewer" and "key". [If his choice says something else, it stays vanilla.]
- **Jen's OFFER node** is the only line rewritten; other nodes in her BuyKeyDialogue keep vanilla text. The rewrite edits
  a shared ScriptableObject in memory for the session (harmless: the sale only opens during the quest).
- **Jerry's favour** counts items with the goblin's `PacifyItem.ID` exactly, like the goblin does (mixed meth with its own
  ID doesn't count). [Unverified that `PacifyItem` is base meth.]
- **Clue and stash spots** are proximity pick-ups with a small marker (no collider), not interactables: a working
  `InteractableObject` needs the interaction layer and collider set-up, which code can't check. Positions: the middle
  `SewerMushrooms.MushroomLocations` entry (sewer floor), Hyland Manor's `SpawnPoint` (assumed outside its door), the
  King's death position (or the office's `InteriorSpawnPoint`). The town-hall portrait and the manor tunnel from the
  plan are not built: no positions for them exist in code.
- **The goblin's first visit** uses `SewerGoblin.DeployToPlayer` after 30 s in the sewer, retried every 60 s.
  "Met" is read from the goblin attacking a target (`CurrentState != Inactive`), "calmed" from
  `SewerGoblinRetrieveBehaviour.onRetrieveComplete` (an `Il2CppSystem.Action` field, combined, not patched).
  [Assumed the retrieve completes on the host.]
- **The King's attack trigger**: `SewerKing.OnTick` is a multi-line override and `NPC.OnTick` is empty, so a prefix that
  returns false skips only the office trigger. An attack by the player is detected as "fighting while we said calm".
  [Assumed the NPC's own reaction to being hit enables its CombatBehaviour.]
- **Defeat** is the game's `HasSewerKingBeenDefeated` (set by `onDieOrKnockedOut`) or `Health.IsDead`, polled.
- **Notifications** are sent with a null icon. [If the game rejects it, the text is logged instead.]
- **Cash** goes to the host's own cash balance (`MoneyManager.ChangeCashBalance`): $10,000 hush money, $25,000 stash.
  Values are a first guess for a Bagman-to-Enforcer player.
- **The underboss** is registered as a hub `Manager` (`sewer.king`, cut 0.25, loyalty 1) whose daily chores do nothing
  yet: the production he would run belongs to other spokes. Loyalty slipping is not built.
- **Toad farming** is only taught in dialogue and announced (`SewerKingSpared`); the psychedelics spoke does the rest.
- **Co-op**: the story runs on the host only; quest state is not networked to clients, and client players can't progress
  it. [Conservative: no client-side state at all.]
- **S1API quest restore**: S1API recreates an active "Down the Drain" on load; `DownTheDrain.Sync` overwrites its entry
  states from the spoke's data every second. [Assumed the restore happens before the hub's `SaveLoaded`; if not, a second
  quest could be created: P11 checks for duplicates.]

## Results so far

- **P1-P3 passed in game** (2026-10-02, slot 1, IL2CPP): loaded at step Locked with no wiring warnings; Jen found and her
  key sale closed; her vanilla offer line is "I wanna help you, but I'll be out of a job if the mayor finds out. I'll need
  <PRICE> for it to be worth the risk."; slot 1's King was already dead (the game's flag), so the fate is KingDefeated;
  reaching Bagman V offered the quest exactly once (step TalkToJerry); Oscar's ENTRY and NPC_HINT lines are rewritten.
- P4 onwards need dialogue with Jerry and walking the sewer: not yet scripted.
