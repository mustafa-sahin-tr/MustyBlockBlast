---
name: unity-refine
description: "Refines a raw GitHub issue into an implementation-ready spec — problem, acceptance criteria, scope, affected systems, risks, open questions — and optionally proposes a sub-issue breakdown."
user-invocable: true
args: issue_number
---

# /unity-refine — Refine a GitHub Issue

Argument: **$ARGUMENTS** — a GitHub issue number or URL, e.g. `12`.

This command does not write code. It delegates to the `unity-business-analyst` agent to turn a raw issue into something `/unity-feature` or `/unity-fix` can implement without guessing.

## Phase 0: Fetch the Issue

1. `gh issue view $ARGUMENTS --json number,title,body,labels,url,comments,state`
   - If `gh` isn't authenticated or the issue can't be found, stop and show the error.
   - If the issue is already closed, tell the user and ask whether to proceed anyway.
2. Read the title, body, labels, and every comment fully before doing anything else.

## Phase 1: Refine

Spawn the `unity-business-analyst` agent with the issue number and ask it to ground itself in `docs/game-design.md`, `CLAUDE.md`, `.claude/rules/architecture.md`, and the relevant code under `Assets/Scripts/Core`, `Gameplay`, `Presentation` before writing anything — never let it guess at current behavior.

Have it rewrite the issue body into this structure:

```markdown
## Problem
[what's broken or missing, in plain terms]

## User Story
As a <player/designer>, I want <thing> so that <benefit>.

## Acceptance Criteria
1. [ ] [specific, testable condition — concrete values, not vague language]
2. [ ] [include at least one negative test: what should NOT happen]

## Out of Scope
- [explicit exclusions]

## Affected Systems
| System / Assembly | Direction | Notes |
|---|---|---|
| e.g. MustyBlockBlast.Gameplay / BoardSystem | read+write | ... |

## Risks
- [technical risk, serialization risk, perf risk — whatever applies]

## Open Questions
- [anything genuinely unclear]
```

- Every Open Question goes to the **user** — never fill it in with a guess.
- If the issue clearly doesn't fit in one PR, the agent should propose a vertically-sliced sub-issue breakdown: each sub-issue independently shippable, plus a suggested implementation order and dependency notes.

## Phase 2: Report and Approve

Show the user:
- The filled-in issue body
- Proposed sub-issues, if any (title + short body each)
- Any open questions still unresolved

**Wait for explicit approval before writing anything to GitHub.**

## Phase 3: Write (only after approval)

- `gh issue edit <issue_number> --body-file <tmp file>` to update the body
- For each approved sub-issue: `gh issue create --title "..." --body "Part of #<issue_number>\n\n..."`, then add it as a checklist item on the parent via another `gh issue edit`
- Never run a GitHub-writing command before the user approves

## Notes

Once refined (and split, if needed), implement a ready sub-issue with `/unity-feature <issue_number>` or fix a bug with `/unity-fix <issue_number>`.
