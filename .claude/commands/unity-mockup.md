---
name: unity-mockup
description: "Produces a visual mockup for a GitHub issue using the design canvas skill, matching the project's existing visual direction, then posts it to the issue after approval."
user-invocable: true
args: issue_number
---

# /unity-mockup — Mockup a GitHub Issue

Argument: **$ARGUMENTS** — a GitHub issue number or URL, e.g. `12`.

This command produces no code or Unity assets. It's for aligning on visual direction before implementation.

## Phase 0: Fetch the Issue

1. `gh issue view $ARGUMENTS --json number,title,body,labels,url,comments,state`
   - If `gh` isn't authenticated or the issue can't be found, stop and show the error.
2. If the issue isn't UI/visual in nature (pure logic, tooling, backend systems), tell the user this command doesn't apply here and stop.

## Phase 1: Ground in the Existing Visual Direction

Don't invent a new visual language — match what already exists:

- Look for an established design: a previously published design-canvas Artifact for this project, `Assets/Settings/*Palette*.asset` (e.g. `BlockPalette`), or existing styling in `Assets/Scripts/Presentation/Views/`. Read the actual colors/fonts/spacing used, not a remembered impression of them.
- If nothing established exists yet, ask the user for an aesthetic direction first — offer 2-4 genuinely different quick options — before committing to one, same as the `design` skill's own rule.

## Phase 2: Build the Mockup

- Invoke the `design` skill to build the mockup as a published Artifact canvas — portrait mobile by default, or whatever form factor the issue implies.
- Cover the states the issue implies as separate artboards where relevant (empty / filled / loading / error / game-over, etc.).
- Write a short design note per screen: layout rationale, which existing colors/components were reused, what's new, and any Unity-side implementation notes (e.g. "reuse BlockPalette accent 2" or "needs a new 9-sliced sprite").

## Phase 3: Report and Approve

Show the user the canvas link and design notes. **Wait for explicit approval before posting to GitHub.**

## Phase 4: Post (only after approval)

- `gh issue comment <issue_number> --body-file <tmp file>` with the canvas link and design notes
- Never run a GitHub-writing command before the user approves

## Notes

Once approved, implement it with `/unity-feature <issue_number>` (or `/unity-ui` for a pure UI pass).
