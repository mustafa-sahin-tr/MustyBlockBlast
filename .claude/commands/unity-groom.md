---
name: unity-groom
description: "Takes a raw GitHub issue end-to-end through prioritization, refinement, optional sub-issue breakdown, and optional mockup before implementation."
user-invocable: true
args: issue_number
---

# /unity-groom — Groom a GitHub Issue

Argument: **$ARGUMENTS** — a GitHub issue number or URL, e.g. `12`.

Get a raw issue ready for implementation. This project has dedicated `unity-product-owner` and `unity-business-analyst` agents — delegate to them for Phases 1 and 2 rather than reasoning through their steps yourself, and stop wherever a judgment call needs the user. There is no separate designer agent; Phase 3's mockup pass is handled inline.

## Phase 0: Fetch the Issue

`gh issue view $ARGUMENTS --json number,title,body,labels,url,comments,state`

If `gh` isn't authenticated or the issue can't be found, stop and show the error. Read the title, body, labels, and comments fully before proceeding.

## Phase 1: Prioritize

Spawn the `unity-product-owner` agent with the fetched issue content and `docs/game-design.md`'s core loop (8x8 board, 3-piece tray, no gravity, line clears, endless play + high score, one-step undo, two power-ups) as context. Ask it to return: **DO NOW / DO LATER / WON'T DO**, a one-line reason, and any priority ordering if multiple issues are in play.

If the call is DO LATER or WON'T DO, stop here and tell the user — don't keep grooming something not worth doing yet. Continue only if the user overrides.

## Phase 2: Refine

Spawn the `unity-business-analyst` agent with the issue number. It will:

- Ground itself in the codebase (`Assets/Scripts/Core`, `Gameplay`, `Presentation`) and `.claude/rules/architecture.md` — verify as-is behavior by reading code, never guess.
- Fill in: Problem, User Story, Acceptance Criteria (concrete, testable, include a negative test), Out of Scope, Affected Systems, Risks, Open Questions.
- Propose a vertically-sliced sub-issue breakdown if the issue doesn't fit in one PR, with a suggested implementation order and dependency notes.

Every Open Question the agent surfaces goes to the user — don't answer on its behalf.

## Phase 3: Mockup (only if UI-facing)

If the issue involves anything the player sees, run the same pass as `/unity-mockup`:

- Match the existing visual direction (an established design canvas, `BlockPalette.asset`, or existing `Presentation/Views/` styling) before drawing anything new — ask for a direction first if nothing established exists.
- Build via the `design` skill, covering the states the issue implies.
- Write a short design note per screen.

Skip this phase — and say why — if the issue is purely logic/systems/tooling with no visual surface.

## Phase 4: Report

Present one consolidated summary:

- Priority call + reasoning
- Filled-in issue body
- Sub-issue breakdown, if any
- Mockup canvas link + notes, if any
- Remaining open questions

**Wait for explicit approval before writing anything to GitHub.**

## Phase 5: Write (only after approval)

- `gh issue edit <issue_number> --body-file <tmp file>` — update the issue body
- `gh issue create --title "..." --body "Part of #<issue_number>\n\n..."` for each approved sub-issue, then add it to the parent's checklist via another `gh issue edit`
- `gh issue comment <issue_number> --body-file <tmp file>` — attach the mockup link/notes, if produced
- Never run a GitHub-writing command before the user approves

## Notes

This command writes no code and creates no Unity assets by itself. It prepares an issue for `/unity-feature <issue_number>` or `/unity-fix <issue_number>`.
