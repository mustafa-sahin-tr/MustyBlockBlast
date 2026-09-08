---
name: unity-groom
description: "Takes a raw GitHub issue end-to-end through prioritization, refinement, optional sub-issue breakdown, and optional mockup before implementation."
user-invocable: true
args: issue_number
---

# /unity-groom — Groom a GitHub Issue

Argument: **$ARGUMENTS** — a GitHub issue number or URL, e.g. `12`.

Get a raw issue ready for implementation. This project has no separate product-owner/business-analyst/designer agents, so reason through each step yourself and stop wherever a judgment call needs the user.

## Phase 0: Fetch the Issue

`gh issue view $ARGUMENTS --json number,title,body,labels,url,comments,state`

If `gh` isn't authenticated or the issue can't be found, stop and show the error. Read the title, body, labels, and comments fully before proceeding.

## Phase 1: Prioritize

Weigh this issue against `docs/game-design.md`'s core loop (8x8 board, 3-piece tray, no gravity, line clears, endless play + high score, one-step undo, two power-ups) and the project's current state:

- Does this touch the core loop, or is it peripheral (nice-to-have, polish, tooling)?
- Is there an unmet dependency (e.g. it needs TMP Essentials imported, or an `.inputactions` asset that doesn't exist yet)?
- Make a rough call: **DO NOW / LATER / WON'T DO**, with a one-line reason.

If the call is LATER or WON'T DO, stop here and tell the user — don't keep grooming something not worth doing yet. Continue only if the user overrides.

## Phase 2: Refine

Same substance as `/unity-refine`:

- Ground in the codebase (`Assets/Scripts/Core`, `Gameplay`, `Presentation`) and `.claude/rules/architecture.md` — verify as-is behavior by reading code, never guess.
- Fill in: Problem, Acceptance Criteria (concrete, testable, include a negative test), Out of Scope, Affected Systems, Risks, Open Questions.
- Every Open Question goes to the user.
- If the issue doesn't fit in one PR, propose a vertically-sliced sub-issue breakdown with a suggested implementation order and dependency notes.

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
