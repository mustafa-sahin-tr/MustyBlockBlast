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

Piece set (each defined as a set of cell offsets):

| Family | Shapes |
|---|---|
| Single | 1×1 |
| Lines | 1×2, 1×3, 1×4, 1×5 and their vertical counterparts |
| Squares | 2×2, 3×3 |
| Corners (L) | 2×2 corner in 4 orientations, 3×3 corner in 4 orientations |
| T / S / Z | Standard 4-cell T, S and Z tetrominoes in their common orientations |

Each distinct orientation is a **separate piece definition** — because rotation does not exist,
"L rotated 90°" is simply a different piece. This keeps placement logic trivial and data-driven.

### Drawing pieces

- A tray refill draws 3 pieces from the piece pool
- The draw is weighted, not uniform: large pieces (3×3, 1×5) are rarer than small ones
- **Solvability is not guaranteed.** A refill may be unplayable; that is a legitimate game over.
  (A "always offer at least one placeable piece" rule is a possible later tuning knob, but it
  changes the game's character and is deliberately excluded from v1.)

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

After each placement, and after each tray refill, check whether **any** remaining tray piece
fits **anywhere** on the board. If none does, the run ends.

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

All applied as a board mutation before the next placement:

| Power-up | Effect |
|---|---|
| Bomb | Player taps a cell; clears a 3×3 area centred on it |
| Row clear | Player taps a row; clears it entirely, full or not |
| Column clear | Player taps a column; clears it entirely, full or not |
| Joker | Player taps an **empty** cell; fills it, then clears its row and/or column if the fill completed them |
| Color Cleanser | Player taps an **occupied** cell; clears every cell on the board sharing that cell's colour |

The first three force-clear their region whether or not it is full. The joker is the
exception: it adds a cell rather than removing any, and clears only on the condition a
normal placement clears on — the line genuinely became full. Tapping an occupied cell is
not a legal joker target and costs nothing; the joker stays held and stays aimed.

Color Cleanser is the mirror image of the joker's legality rule: it needs a colour to
extract from the tapped cell, so an **empty** cell is the illegal target here — nothing
clears, nothing is spent, the cleanser stays held and stays aimed. Unlike the region-clearing
three, its cleared set can span the whole board and depends entirely on board content, not
just the tapped position.

Cleared cells score as a normal clear but **do not** advance the combo streak — power-ups
should not be a way to farm multipliers. A power-up that clears nothing scores nothing,
including a joker that only fills a cell. Bomb and Color Cleanser both pay per cell cleared,
since neither has a fixed region size; Row Clear/Column Clear pay the flat one-line rate.

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
- Piece rotation
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
