---
name: unity-workflow
description: "Full development pipeline from a GitHub issue — clarify requirements, plan implementation, execute with agents, verify with review + tests."
user-invocable: true
args: issue_number
---

# /unity-workflow — Full Development Pipeline

Argument: **$ARGUMENTS** — a GitHub issue number or URL, e.g. `12`.

This command runs a 5-phase pipeline: **Fetch → Clarify → Plan → Execute → Verify**. Each phase requires explicit user confirmation before proceeding to the next.

## Phase 0: Fetch the Issue

1. `gh issue view $ARGUMENTS --json number,title,body,labels,url,comments,state`
   - If `gh` isn't authenticated or the issue can't be found, stop and show the error.
   - If the issue is already closed, tell the user and ask whether to proceed anyway.
2. Read the title, body, labels, and comments fully — this is the feature/bug description the rest of the pipeline works from.
3. Keep the issue number and URL on hand for Phase 5's summary and any commit message (`Refs #<issue_number>` / `Closes #<issue_number>`).
4. If the issue already reads like a refined spec (problem, acceptance criteria, scope already filled in — e.g. it went through `/unity-refine` or `/unity-groom`), Phase 1 can be brief. If it's raw, run the full clarify pass below. Consider suggesting `/unity-refine <issue_number>` first when the issue is large or clearly needs product-level triage before code — but proceed here if the user wants to go straight to implementation.

## Phase 1: Clarify

Interview the user to fill any gaps the issue doesn't already answer:

1. **Core mechanic / feature purpose** — what does it do? What problem does it solve?
2. **Target platform** — mobile (iOS/Android), desktop, WebGL, VR?
3. **Performance constraints** — target FPS? Memory budget? Draw call limit?
4. **Unity subsystems involved** — physics? UI? animation? audio? networking?
5. **Integration points** — what existing systems does this touch?
6. **Acceptance criteria** — how do we know it's done? What should we test?

Produce a **Requirements Summary** with all answers consolidated. Ask the user to confirm before proceeding.

If the issue body already answers these clearly, present the summary directly for confirmation rather than asking each question individually — only ask about what's genuinely missing or ambiguous.

## Phase 2: Plan

Based on confirmed requirements:

1. **Scan the project** — use `unity-scout` (haiku) for fast codebase exploration: read CLAUDE.md, find relevant existing scripts, map assembly structure
2. **Identify subsystems** — which Unity packages and skills are involved?
3. **Assess complexity** using the model-routing skill heuristics:
   - Count estimated files to create/modify
   - Check for complexity keywords in the task description
   - Identify risk factors (serialization changes, networking, platform-specific, threading)
   - Rate as **simple** (1-2 files, no risk) / **moderate** (3-8 files, some risk) / **complex** (9+ files, high risk)
4. **Choose execution strategy** based on complexity:
   - **Simple** → `unity-coder-lite` (sonnet) — faster, cheaper
   - **Moderate** → `unity-coder` (opus) — deeper reasoning
   - **Complex** → multiple agents via `/unity-team`
   - **Specialized** → route to domain agent: `unity-prototyper`, `unity-ui-builder`, `unity-network-dev`, `unity-shader-dev`
5. **Generate implementation plan**:
   - Scripts to create/modify (with file paths and assembly placement)
   - Scene changes needed (GameObjects, components, physics layers)
   - Dependencies on existing systems
   - Risk areas (serialization, platform-specific, performance)
   - Estimated complexity and chosen agent tier with rationale

Present the plan to the user and wait for approval before executing.

## Phase 2b: Critic Review (optional)

Unless `--no-critic` is specified in the original arguments:

1. **Invoke `unity-critic`** (opus, read-only) with the approved plan
2. The critic will challenge the plan for Unity-specific gotchas, missed edge cases, over-engineering, and performance risks
3. Present the critic's challenges to the user
4. Incorporate valid challenges into the plan before executing
5. If no critical challenges are raised, proceed directly to Phase 3

## Phase 3: Execute

### Branch first

Before any file is created or edited, create and check out a dedicated branch — never implement directly on `main`:

1. `git status` — if there are uncommitted changes unrelated to this issue, stop and ask the user how to handle them (stash, commit, or abort) rather than branching over dirty state.
2. Build the branch name from the issue: `issue-<issue_number>-<kebab-case-slug-of-the-title>`, e.g. issue #1 titled "satır yok olma effekti olsun..." → `issue-1-line-clear-effect` (slug from the *gist* of the title, not a literal transliteration — short, meaningful, a handful of words).
3. `git checkout -b issue-<issue_number>-<slug>` off the current `main` (fast-forward/pull first if `main` is behind `origin/main`).
4. If a branch with that name already exists (e.g. a resumed workflow), check it out instead of creating a new one — don't error out.

Do this once per `/unity-workflow` run, before Phase 3 step 1 below. Do not push the branch or open a PR automatically — that stays a separate, explicitly-requested action per this project's push/PR safety rules.

### Implement

Follow the approved plan:

1. **Route to the appropriate agent(s)** based on the plan
2. **Write C# code** following all rules in `.claude/rules/`
3. **Set up scene elements** via MCP if needed (`batch_execute` for speed)
4. **Check console** via `read_console` after each major step for compilation errors
5. If errors are found, fix them before proceeding

Report progress at natural milestones (e.g., "Scripts written, setting up scene now...").

## Phase 4: Verify

Run the `unity-verifier` agent to perform a verify-fix loop:

1. **Review** all changed files against the unity-reviewer checklist
2. **Auto-fix** issues that are safe to fix automatically
3. **Re-verify** if fixes were applied (max 3 iterations)
4. **Run tests** via MCP if available

### Deslop Pass

After verification succeeds with no critical issues, perform a targeted code-bloat review on all files created or modified during this workflow. Specifically target:

1. **Unnecessary abstractions** — interfaces with one implementation, factory classes that create one type, wrapper classes that add no behavior
2. **Over-commenting** — comments that restate the code, obvious doc comments, commented-out code blocks
3. **Redundant error handling** — try/catch that just rethrows, null checks on values that can never be null, defensive code with no plausible failure mode
4. **Dead code** — unused private methods, unreachable branches, unused parameters
5. **Over-engineering** — generic solutions for non-generic problems, premature optimization patterns, unnecessary design patterns

Deslop rules:
- Only simplify, never add complexity
- Preserve all runtime behavior
- Do not touch code that existed before this workflow started
- If in doubt, leave it alone — false positives are worse than missed bloat
- Apply fixes directly, then re-check console via `read_console` to confirm no regressions

### Final Summary

Present a complete summary to the user:

```
## Workflow Complete

### What was built
- [list of features implemented]

### Files created/modified
- [file paths with brief descriptions]

### Verification results
- [auto-fixed issues]
- [remaining items for human review]

### Test results
- [compilation status, test pass/fail counts]

### Manual steps needed
- [any inspector assignments, scene references, etc.]

### How to test
- [step-by-step testing instructions]

### Source issue
- #<issue_number> — <url>

### Branch
- `issue-<issue_number>-<slug>` — not pushed; push and open a PR when the user asks
```

## Design Principles

- **Each phase gate requires user confirmation** — never skip ahead
- **Prefer existing patterns** — match the project's established conventions
- **Minimal viable implementation** — don't overbuild on the first pass
- **Verify everything** — the verify phase is not optional
