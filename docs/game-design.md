# MustyBlockBlast — Game Design

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

Two in v1, both applied as a board mutation before the next placement:

| Power-up | Effect |
|---|---|
| Bomb | Player taps a cell; clears a 3×3 area centred on it |
| Line clear | Player taps a row or column; clears it entirely |

Cleared cells score as a normal clear but **do not** advance the combo streak — power-ups
should not be a way to farm multipliers.

## Earning undo and power-ups

Both are earned by watching a **rewarded ad**, opt-in only. No forced interstitials in v1.

The game logic must not know that ads exist. `Core` and `Gameplay` depend on an interface
such as `IRewardSource` that grants a reward; the ad SDK lives entirely in `Presentation`
or an outer composition layer. This keeps the whole rule set testable without an SDK, and
leaves the choice of ad provider (Unity LevelPlay, AdMob) open.

**Open decision:** ad provider is not chosen yet. Nothing in v1's rules depends on it.

## Explicitly out of scope for v1

- Levels, objectives, progression map
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
