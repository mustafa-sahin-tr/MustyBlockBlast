Part of #67
Depends on sub-issue 1 (#68). Independent of sub-issue 2 (#69) except for the UI trigger.

## Scope
- Three dedicated "watch ad to earn" buttons, each fixed to one power-up type (Bomb, Row Clear, Column Clear) — not a random grant, not a pre-watch choice screen.
- Each button calls `IRewardSource.RequestReward(type)` and grants inventory on success; inventory persists across app restarts (per #68).
- Ships with the stub `IRewardSource` from #68. Real ad SDK integration (provider choice: Unity LevelPlay vs AdMob) is an explicit follow-up outside this epic — see #67's "Follow-up" section, including the user's interest in multi-ad-watch rewards ("watch 2 ads to win this joker"), which is product intent for that follow-up, not this issue.

## Acceptance Criteria
Granting a reward increases power-up inventory for the specific type the button was tied to; it does not auto-apply it.

Ships player-visible value: the full earn-and-spend loop is playable end-to-end, still ad-SDK-agnostic.
