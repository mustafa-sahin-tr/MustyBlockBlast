Part of #67
Depends on sub-issue 1 (#68).

## Scope
Presentation only.

- New power-up inventory HUD with three distinct icons/counts (Bomb, Row Clear, Column Clear), following `ScoreView`/`BonusFeedbackView` patterns.
- `BoardInputView` gains a power-up input mode:
  - Selecting a power-up arms it immediately (no queueing to a future turn).
  - While armed, dragging/hovering over the board live-previews the effect — a highlighted 3x3 region for Bomb, a highlighted full row for Row Clear, a highlighted full column for Column Clear — the same interaction shape as the existing piece-drag preview.
  - Tapping a valid cell while armed applies the power-up.
  - Tapping the armed power-up's own icon again cancels the selection without consuming inventory.
- All inside the existing single InputView per architecture rules, not a second InputView.
- In Timed mode, the countdown must pause for the entire time a power-up is armed (from selection to apply/cancel) — new pause branch alongside the existing "dragging a piece never pauses, backgrounding always pauses" rules.

## Acceptance Criteria
See parent #67 items 13–19, including the negative case: using a power-up with 0 in inventory is a no-op, and power-ups aren't offered once the run is game-over.

Ships player-visible value: power-ups are playable end-to-end (assuming a way to have them, even a debug cheat — real earning is sub-issue #70).
