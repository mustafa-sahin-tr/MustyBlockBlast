# Blockio Blast: Time Rush — Game Design

Genre: block-placement puzzle (Block Blast / 1010! family). Portrait mobile, iOS + Android.
This document is the single source of truth for game rules. Code must match it; when they
disagree, fix one of them deliberately rather than letting them drift.

## Core loop

1. Three pieces are offered in a tray.
2. The player drags a piece onto the 8×8 board. It may be placed anywhere its cells are all empty.
3. Any row or column that becomes completely full is cleared.
4. When all three tray pieces have been placed, three new pieces are drawn.
5. The game ends when none of the remaining tray pieces fit anywhere on the board.

There is **no gravity**. Placed blocks never move or fall. This is the defining difference
from Tetris and the reason the game is about spatial planning rather than reaction.

## Board

- Fixed 8×8 grid, origin bottom-left, `(0,0)` to `(7,7)`
- Each cell is either empty or occupied by a colour id
- Colour is cosmetic only — it never affects placement or clearing

## Pieces

Pieces are **not rotatable**. The offered orientation is the only orientation. This is the
primary source of difficulty and must not be softened without an explicit design decision.

**The single exception** is the Rotate power-up (see "Power-ups"), which is exactly such an
explicit decision: it is earned, charged for, limited by inventory, and scoped to a piece
sitting in the **dock**. It is never available mid-drag and never applies to a piece already
on the board. Rotation remains something the player has to spend to get, not a free move —
so the "one orientation per offer" difficulty still holds for ordinary play.

Piece set (each defined as a set of cell offsets):

| Family | Shapes |
|---|---|
| Single | 1×1 |
| Lines | 1×2, 1×3, 1×4, 1×5 and their vertical counterparts |
| Squares | 2×2, 3×3 |
| Corners (L) | 2×2 corner in 4 orientations, 3×3 corner in 4 orientations |
| T / S / Z | Standard 4-cell T, S and Z tetrominoes in their common orientations |

Each distinct orientation is a **separate piece definition** — "L rotated 90°" is simply a
different piece. This keeps placement logic trivial and data-driven.

Because every orientation is already authored, the piece set is **closed under rotation**: the
90° turn of any piece is itself a piece in the set. The Rotate power-up is defined on top of
that — it swaps the dock slot to the piece that already describes the turned shape, rather than
rewriting a piece's offsets. A piece's identity therefore always matches its shape, which is
what keeps everything keyed on piece id (objectives, families) correct.

Three pieces — 1×1, 2×2 and 3×3 — are **fully symmetrical**: their 90° turn is themselves, so
they have no second orientation and Rotate has nothing to do to them.

### Drawing pieces

- A tray refill draws 3 pieces from the piece pool
- The draw is weighted, not uniform: large pieces (3×3, 1×5) are rarer than small ones
- **Solvability is not guaranteed.** A refill may be unplayable; that is a legitimate game over.
  (A "always offer at least one placeable piece" rule is a possible later tuning knob, but it
  changes the game's character and is deliberately excluded from v1.)

The **one exception** is the Reroll power-up (see "Power-ups"), and only for the set it draws:
a set the player spent an earned power-up on is retried until at least one of its three pieces
fits the board. That guarantee is scoped to Reroll alone — ordinary refills are untouched by it,
so the difficulty above still holds for ordinary play. The retry is bounded; if the bound is
exhausted the last set drawn is used as-is, which can only happen on a board no catalog piece
fits at all — a board that was already out of moves.

## Clearing

- A row or column clears when all 8 of its cells are occupied
- Rows and columns are evaluated **after the whole piece is placed**, never mid-placement
- All lines completed by a single placement clear **simultaneously**
- A cell at the intersection of a cleared row and a cleared column clears once

## Scoring

Tunable via a ScriptableObject; the values below are the starting point, not sacred.

| Event | Points |
|---|---|
| Placing a piece | +1 per cell |
| Clearing lines | `10 × lines × comboMultiplier(lines)` |

`comboMultiplier(lines)` = 1, 3, 6, 10 for 1, 2, 3, 4+ simultaneous lines — clearing several
lines at once should feel disproportionately rewarding.

### Combo streak

- A placement that clears at least one line increments the streak
- A placement that clears nothing resets the streak to 0
- Streak adds `+0.5×` to the multiplier per consecutive clearing placement, capped at `+3×`

High score is persisted locally. There is no server, no leaderboard, in v1.

## Levels & Objectives

An objective is a goal the player works towards while playing the ordinary rule set — board,
pieces, clearing and scoring are unchanged. Objectives only observe placements; they never
alter them.

**Objective types**

| Type | Qualifies when | Progress |
|---|---|---|
| Simultaneous line clear | A placement clears **exactly** the required number of lines at once | +1 per qualifying placement |
| Piece shape family | A placement uses a piece of the required shape family | +1 per qualifying placement |
| Score in a run | — | Mirrors the current run score |
| Board wipe | A placement leaves the board completely empty | +1 per qualifying placement |
| Streak threshold | — | Tracks the best combo streak reached this run (high-water mark) |
| Bomb-induced line clear | A Bomb power-up clear leaves a row or column completely empty | +1 per qualifying Bomb use |
| Row and column cross-clear | A placement clears at least one row AND at least one column simultaneously | +1 per qualifying placement |
| Clutch recovery clear | A placement clears at least one line while the board held at least the configured occupied-cell threshold immediately beforehand | +1 per qualifying placement |
| At-least line clear ("mega clear") | A placement clears **at least** the required number of lines at once | +1 per qualifying placement |
| Piece id count | A placement uses the exact catalog piece named by the objective | +1 per qualifying placement |
| Piece id line clear | A placement uses the exact catalog piece AND clears at least one line | +1 per qualifying placement |
| Four corners cleared | A placement's clear touches a board corner cell | +1 per qualifying placement |
| Center core evacuated | A placement leaves the board's centered 4×4 core completely empty | +1 per qualifying placement |
| No isolated holes streak | A placement leaves zero unreachable-from-edge empty cells | Best streak of consecutive qualifying placements |
| Line clear burst | A trailing rolling window of the configured width | Sum of lines cleared by placements still inside the window |
| Early score rush | The run is still inside the configured deadline (from run start) | Mirrors the current run score, until the deadline passes |
| Reroll save (synergy) | The Reroll power-up is spent while the dock had zero legal placements | +1 per qualifying Reroll |

The line-clear objective matches exactly, not "at least": a 3-line clear does not satisfy a
"clear 2 lines at once" objective — that is a separate, harder goal. Shape families are
`Single`, `Line`, `Square`, `Corner`, `T`, `S`, `Z`; every catalog piece belongs to exactly one,
regardless of its size or orientation, so adding a new orientation never invalidates a level.

Progress is clamped to the target, and an objective stops tracking once complete — it can never
overshoot and never completes twice.

Streak threshold tracks the best streak *reached*, not the live streak: a streak that peaks at 4
and then resets to 0 keeps its progress at 4, and a later streak of 6 reads as "best 6", never a
sum of the two. This is what lets the objective complete permanently the moment the target streak
is ever reached, even if the player's combo breaks immediately afterward.

Bomb-induced line clear is the one objective type not driven by a placement at all: it fires off
the Bomb power-up's own clear, checking whether the 3×3 blast happened to remove the last
occupied cell(s) of a row or column and leave it entirely empty. This is the *opposite* condition
from a normal line clear (which fires on a row becoming **full**, not empty), and it is
deliberately scoped to Bomb only — Row Clear/Column Clear always empty their own target line by
design, so that would never be a "surprise" worth an objective. It also never advances the streak
or combo counters, matching every other power-up clear.

Reroll save is the other objective type not driven by a placement: it fires off the Reroll
power-up's own application, checking whether the dock held zero legal placements *immediately
before* the reroll. Read at the moment Reroll is spent — a reroll used with moves already
available never qualifies, no matter how good the resulting draw is. The Hold slot's piece is
deliberately excluded from the "zero legal moves" check, which means this is honestly a "spent a
Reroll while your held piece was the only thing keeping the dock alive" signal, not a rescue from
the brink: the run's game-over check already considers the dock *and* the Hold slot together, so a
dead dock with the run still going can only mean the Hold slot already had it covered.

Row and column cross-clear is deliberately distinct from just checking `Simultaneous line
clear`'s total: two rows clearing at once and one row plus one column clearing at once both
sum to 2 lines cleared, but only the second is a "cross" — the objective needs the row/column
split, not just the total, to tell them apart.

Clutch recovery clear reads the board's occupancy **immediately after the piece lands but before
its own clears resolve** — the "how full was it right before this rescued it" moment. A packed
board that clears nothing does not qualify: pressure alone isn't the goal, clearing under
pressure is.

At-least line clear is the deliberate opposite of the exact-match line-clear objective: it is a
genuinely separate type, not a flag on `Simultaneous line clear`, so a level can ask for "clear
exactly 2" and a different level can ask for "clear 4 or more" without either rule bleeding into
the other.

Piece id objectives reference a piece by its exact catalog id (e.g. `square_3x3`, `line_h5`)
rather than its shape family — `Piece shape family` groups every size of a shape together
(`Square` covers both 2×2 and 3×3), so "place 3×3 solid blocks specifically" or "clear a line
with the I5 pentomino" need the finer-grained id, not the family. A typo'd id is caught at
authoring time: `LevelObjectiveConfig.IsValid` checks it against the real `PieceCatalog`.

Board topology objectives read the board's shape, not just what a placement cleared:

- **Four corners cleared** counts a clear that touches any corner cell. Clearing row 0 or the
  last row alone already touches two corners at once (every cell in that row is cleared,
  including both edge columns), so this reduces to checking whether the cleared row/column
  indices include either edge — no per-cell coordinate tracking needed.
- **Center core evacuated** checks the board's centered 4×4 region, independently of the whole
  board (`Board wipe`, above) or any single row/column.
- **No isolated holes streak** flood-fills from every empty edge cell to find which empty cells
  are reachable; anything empty and unreached is an isolated hole. This runs on *every*
  placement (not just clearing ones) and **after** that placement's own line clears resolved —
  a hole that opens up and is immediately closed by the same placement's clear never breaks the
  streak, matching how a normal line clear is evaluated after the whole piece lands. Like
  `Streak threshold`, progress tracks the best run ever, not the live one — a single bad
  placement drops the live streak to 0 but never erases an earlier peak.

Rolling-window / time-attack objectives measure against wall-clock time elapsed since the current
run started (`ElapsedRunSeconds`), tracked independently of Timed mode's countdown — they behave
identically in Endless and Timed mode, and are never paused by a modal being open (unlike the
Timed mode countdown, which does pause while backgrounded):

- **Line clear burst** tracks a trailing window of `WindowSeconds` width: every qualifying
  placement (one that clears at least one line) is recorded with its timestamp and line count,
  expired entries older than the window age out, and progress is the live sum of what remains
  inside it. This is **not** a high-water mark like `Streak threshold` — the running total can
  fall back down as old entries expire — but a completed objective still latches permanently via
  the same `IsComplete` mechanism every other type uses, so a burst that once reached the target
  cannot be "un-completed" by its own entries later expiring.
- **Early score rush** mirrors the run score exactly like `Score in a run`, but only while
  `ElapsedRunSeconds` is still within `WindowSeconds` of run start; once the deadline passes, the
  objective simply stops updating and freezes at its last in-window reading rather than
  completing late off a score reached after the deadline.

Both types forbid Cumulative scope for the same reason: their internal clock (`ElapsedRunSeconds`)
resets to 0 every run, so a Cumulative instance would have no coherent way to compare timestamps
across a run boundary.

Lifetime "how many pieces has the player ever placed" goals are covered by the Badges system's
`TotalPiecesPlaced` stat, not a separate objective type — a level goal and a lifetime achievement
watching the same counter would be redundant.

**Progress scopes**

- **Per-run** — progress is reset to 0 when a new run starts (i.e. after game over). The goal
  must be met inside a single run.
- **Cumulative** — progress persists across runs and is never reset at run start, within the
  current app session (no cross-restart persistence yet — see the v1 scope note below).

**v1 scope:** this is the tracking engine only. There is no level content, no objective UI and
no persistence of objective progress yet — those are tracked separately in epic "Levels &
Objectives" (sub-issues #72 and #73).

## Game over

After each placement, and after each tray refill, check whether **any** remaining tray piece —
or the piece parked in the hold slot, if there is one — fits **anywhere** on the board. If none
does, the run ends.

This check is also what powers the "no moves" hint state, so it must be cheap enough to run
every placement.

## Timed mode

Two modes are selectable: **Sınırsız** (endless, the default) and **Süreli** (timed). Switching
mode restarts the run, because a board mid-run belongs to the mode it was started in.

Timed mode layers a countdown on top of the identical endless rule set — board, pieces,
clearing and scoring are unchanged. Only the "run ends" condition gains a second trigger.

- The round length is chosen **before** a run, in Settings → Süre. The selectable list is
  5 / 10 / 15 / 20 / 25 seconds, default 15. The list lives in a `TimedModeConfig`
  ScriptableObject, so retuning it is an asset edit, not a code change.
- The countdown starts the moment a tray of three pieces is drawn, and resets to the full
  duration **only** on a tray refill — that is, once all three pieces have been placed.
  Placing an individual piece never resets it. The clock therefore measures "clear the whole
  tray in time", not "place a piece in time".
- Reaching 0 ends the run immediately with the score as it stands, through the same game-over
  path as running out of moves. There is exactly one end-of-run state; time is just another way
  to reach it.
- Dragging a piece does **not** pause the clock — holding a piece in mid-air would otherwise be
  free time. Backgrounding the app **does** pause it, and resumes with the same time remaining.
- The Süre row is greyed out and inert while endless is selected.

Endless runs are unaffected: they show no timer and are never ended by time.

High scores are not yet tracked per duration — a timed score competes with the same single
best score as an endless one. That is a known v1 simplification.

## Undo

- Depth: **1 step**. Only the most recent placement can be undone.
- Undoing restores the board, the score, the combo streak, and the tray — a full snapshot.
- Undo is consumed; a second undo requires earning another one.
- Undo is **not available** after a game over.

Implementation consequence: the game state must be snapshot-able as a value. Board, tray,
score and streak are therefore plain data in `Core`, not scattered MonoBehaviour fields.

## Power-ups

All but Rotate, Reroll and Double Multiplier are applied as a board mutation before the
next placement:

| Power-up | Effect |
|---|---|
| Bomb | Player taps a cell; clears a 3×3 area centred on it |
| Row clear | Player taps a row; clears it entirely, full or not |
| Column clear | Player taps a column; clears it entirely, full or not |
| Joker | Player taps an **empty** cell; fills it, then clears its row and/or column if the fill completed them |
| Color Cleanser | Player taps an **occupied** cell; clears every cell on the board sharing that cell's colour |
| Rotate | Player taps a **dock piece**; turns it 90° clockwise, in place, in its slot |
| Reroll | Player taps the icon; discards all three dock pieces and draws three new ones, at least one of which fits the board |
| Double Multiplier | Player taps the icon; every score gain in the run is worth **2×** for the next 15 seconds |

The first three force-clear their region whether or not it is full. The joker is the
exception: it adds a cell rather than removing any, and clears only on the condition a
normal placement clears on — the line genuinely became full. Tapping an occupied cell is
not a legal joker target and costs nothing; the joker stays held and stays aimed.

Color Cleanser is the mirror image of the joker's legality rule: it needs a colour to
extract from the tapped cell, so an **empty** cell is the illegal target here — nothing
clears, nothing is spent, the cleanser stays held and stays aimed. Unlike the region-clearing
three, its cleared set can span the whole board and depends entirely on board content, not
just the tapped position.

Rotate is the odd one out twice over. It is the **only** power-up aimed at the tray rather than
the board, and the **only explicit exception** to "pieces are not rotatable" (see "Pieces"). It
is scoped deliberately narrowly:

- It applies **only to a piece in a dock slot** — never to a piece already placed on the board,
  and never mid-drag. There is no rotate gesture while dragging.
- Aimed at a **fully symmetrical** piece (1×1, 2×2, 3×3) it is **rejected**: that piece's 90°
  turn is itself, so there is nothing to do. Nothing is spent and it stays armed to aim again —
  the same rule Joker and Color Cleanser follow for their illegal targets.
- It touches no cell, so it clears nothing, scores nothing and neither advances nor breaks the
  combo streak.
- Because it changes **which shapes** the player holds, the no-moves-left check is re-run after
  it: a rotation that leaves nothing placeable ends the run, exactly as the placement that
  exhausted the board would.

Reroll is the first of the two power-ups with **no target**. There is nothing to aim it at — the whole dock is
the subject — so it is applied on the tap that selects it rather than armed and then aimed, and it
is never an armed selection. Its other distinguishing rules:

- It discards all three dock slots, **occupied or not**. The pocket is untouched: a parked piece
  was deliberately set aside and is not on offer, so it is not part of what is discarded.
- Its draw is the game's **only solvability-guaranteed** one (see "Drawing pieces").
- It touches no cell, so it clears nothing, scores nothing and neither advances nor breaks the
  combo streak — exactly as Rotate.
- Because it changes which shapes the player holds, the no-moves-left check is re-run after it,
  again exactly as Rotate.
- It is **not** a tray refill. In Timed mode a refill restarts the countdown from full, which is
  earned by playing the whole dock out; a reroll is a discard, and granting it the same reset
  would make it a time power-up as well as a piece one.
- A drag in flight is cancelled the moment its slot is rewritten, so no drag can drop a piece the
  tray no longer offers.

Double Multiplier (the "2× frenzy") is the second targetless power-up, and the only one whose
effect is not immediate: tapping it spends the charge and opens a **15-second window**, and
everything it does happens inside that window.

- While the window is open, **every** score gain in the run is doubled — a placement's total and
  a power-up clear's alike. Power-up-sourced points are explicitly included: the rule is "every
  score gain", not "every placement".
- The doubling is applied to each event's **finished total**, after the whole additive multiplier
  stack (combo, streak, monochrome, milestone bonuses) has resolved. It is not another term in
  that stack, so it never compounds with the streak arithmetic — it simply doubles the output.
- A score event worth **0 is still worth 0**: there is no floor, so a placement or clear that
  earned nothing earns nothing during a frenzy too.
- The 15 seconds are **run seconds, not wall-clock seconds**: the window is held for exactly as
  long as the run is paused — a modal panel open (Settings/Level path/Badges), another power-up
  armed and being aimed, or the app backgrounded — the same three reasons that hold the Timed
  mode countdown, and it resumes where it left off.
- Activating it again while a window is already open **restarts** it at 15 seconds rather than
  stacking: this is a doubling, never a 4×.
- Like Rotate and Reroll it touches no cell, so it clears nothing and scores nothing at the moment
  it is spent — opening the window never pays the player for opening it.
- The window belongs to the run it was opened in: it does not survive game over or a restart.

Cleared cells score as a normal clear but **do not** advance the combo streak — power-ups
should not be a way to farm multipliers. A power-up that clears nothing scores nothing,
including a joker that only fills a cell. Bomb and Color Cleanser both pay per cell cleared,
since neither has a fixed region size; Row Clear/Column Clear pay the flat one-line rate.

## Hold slot (pocket)

Separate from the eight power-ups above: the Hold slot is not earned, has no charge or count,
is never armed, and never touches the board. It is always available, for the whole run.

A single extra slot sits beside the tray. The player drags a tray piece onto it to **park**
that piece:

- Parking into an **empty** pocket moves the piece there and empties its tray slot. The
  piece's shape and orientation are carried over unchanged (pieces are never rotated).
- Parking while the pocket is **occupied** **swaps** the two: the parked piece drops into the
  tray slot the dragged piece just left, and the dragged piece takes its place in the pocket.
  The swap is atomic — one piece in, one piece out, so the tray slot count never moves.

That swap is the only way a parked piece comes back, and it is enough: there is no separate
"take it out" gesture, and none is needed.

Parking is **not a placement**. Nothing lands on the board, so nothing scores, no line can
clear, and the combo streak is neither advanced nor broken. The pieces involved were already
drawn; they are merely somewhere else now.

The pocket is invisible to the refill rule: a refill is triggered by all **three tray slots**
being consumed, and a parked piece is not in a tray slot. A full pocket can never hold a
refill off.

Parking the **last** remaining tray piece into an empty pocket is refused. The pocket is only
ever fed from the tray and only ever emptied by the swap that refills it, so a tray emptied by
parking could never be restocked — a refill is a placement's consequence — and the run would
be stuck with nothing to drag. A swap can never hit this case.

The parked piece **does** count for the game-over check: swapping it back into a tray slot is
always legal and costs nothing, so a board that only the parked piece fits is not a dead end.

## Earning undo and power-ups

Both are earned by watching a **rewarded ad**, opt-in only. No forced interstitials in v1.

Power-ups are additionally granted outright — no ad — on two earned events: unlocking a
badge, and completing a level that authors a level-up reward. Which levels reward, and with
what, is authored per level in the level catalog rather than derived in code; the reward
belongs to the level **completed**, not the one advanced into.

The game logic must not know that ads exist. `Core` and `Gameplay` depend on an interface
such as `IRewardSource` that grants a reward; the ad SDK lives entirely in `Presentation`
or an outer composition layer. This keeps the whole rule set testable without an SDK, and
leaves the choice of ad provider (Unity LevelPlay, AdMob) open.

**Open decision:** ad provider is not chosen yet. Nothing in v1's rules depends on it.

## Explicitly out of scope for v1

- Progression map
- Free piece rotation (the earned, inventory-limited Rotate power-up is the only rotation in v1)
- Leaderboards, accounts, cloud save
- In-app purchases
- Daily rewards, streaks, live-ops

## Architecture mapping

| Layer | Owns |
|---|---|
| `Core` (no UnityEngine) | Board, Piece, placement rules, line clearing, scoring, game-over detection, snapshots |
| `Gameplay` | Run lifecycle, tray refill, undo stack, power-up application, message publishing |
| `Presentation` | Rendering, drag input, animation, audio, ads, persistence |

Every rule in this document must be verifiable by an EditMode test against `Core` alone,
with no scene and no MonoBehaviour.
