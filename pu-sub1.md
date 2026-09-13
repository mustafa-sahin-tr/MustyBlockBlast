Part of #67

## Scope
Core + Gameplay only — no UI, no ad integration.

- Core: three resolvers — bomb-area-clear (3x3, bounds-clamped), forced-row-clear, forced-column-clear — each returning which cells were actually cleared (for scoring), mirroring `LineClearResolver`'s result shape. (Row Clear and Column Clear are separate power-up types, not one ambiguous "Line Clear.")
- Gameplay: `PowerUpModel` (inventory counts per type: Bomb, Row Clear, Column Clear), `PowerUpSystem` applying any of the three to `BoardModel`. Inventory is persisted (PlayerPrefs-style), surviving app restart.
- New scoring entry point that awards points for a power-up clear using the same formula as a normal clear, but does **not** touch `ScoreModel.Streak`, `MultiClearStreak`, or `CumulativeMultiClearCount`.
- `IRewardSource` interface (Gameplay, zero ad-SDK references) + a deterministic stub implementation, registered in `GameLifetimeScope` mirroring `ISfxService`/`IMusicService`. The request shape must carry *which* power-up type is being requested (rewards are fixed-per-ad-placement, not random) — grants inventory directly; no UI trigger yet (exercised via tests / a debug hook).

## Acceptance Criteria
See parent #67 items 1–12. Must include the negative cases: empty-region bomb clears nothing/scores nothing/publishes nothing; empty row/column clear is a no-op; streak is provably unchanged (not incremented, not reset) after a power-up clear.

## Out of Scope
No UI, no ad SDK. Ships no player-visible value on its own — foundation only.

Fully testable in EditMode with zero scene/MonoBehaviour, per the project's testing principle.
