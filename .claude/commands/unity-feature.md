---
name: unity-feature
description: "Plans and implements a Unity feature from a GitHub issue — fetches the issue, identifies subsystems, loads skills, writes code, sets up scene elements via MCP."
user-invocable: true
args: issue_number
---

# /unity-feature — Implement a Feature from a GitHub Issue

Argument: **$ARGUMENTS** — a GitHub issue number (optionally followed by `--quick`), e.g. `42` or `42 --quick`.

## Agent Routing

- Default: use `unity-coder` agent (opus — full architectural reasoning)
- If `$ARGUMENTS` contains `--quick`: use `unity-coder-lite` agent (sonnet — faster, for simple additions)
- Strip the `--quick` flag, leaving just the issue number

## Phase 0: Fetch the Issue

1. Extract the issue number from `$ARGUMENTS` (strip `--quick` if present). If what remains isn't a plain number, stop and tell the user to pass a GitHub issue number (e.g. `/unity-feature 42`).
2. Fetch it: `gh issue view <issue_number> --json number,title,body,url,state,labels`
   - If `gh` is not authenticated or the issue can't be found, stop and report the error rather than guessing at a feature description.
   - If the issue is already closed, tell the user and ask whether to proceed anyway.
3. Use the issue's **title** and **body** as the feature description for Phase 1 — this replaces any free-text description. Note the issue's labels if they hint at scope (e.g. `bug` vs `enhancement`).
4. Keep the issue number and URL on hand — reference it in the Phase 3 summary and in any commit message (`Refs #<issue_number>` or `Closes #<issue_number>` if the user commits).

## Phase 1: Plan

1. **Analyze the feature** — identify which Unity subsystems are involved:
   - Input System? Physics? Animation? UI? Audio? Networking?
   - Which existing scripts/systems does this integrate with?

2. **Identify required scripts** — what new scripts to create, what existing ones to modify.

3. **Identify scene changes** — what GameObjects, components, or scene setup is needed.

4. **Present the plan** to the user before implementing. Include:
   - Scripts to create/modify
   - Scene changes via MCP
   - Dependencies on existing systems
   - Estimated complexity (simple / moderate / complex)

## Phase 2: Implement

1. **Write C# code** using the `unity-coder` agent:
   - Follow all rules in `.claude/rules/`
   - Place scripts in correct assembly definition
   - Use `[SerializeField]` for inspector configuration
   - Add `[Header]` attributes for organization

2. **Set up scene elements** via MCP:
   - Create GameObjects with `batch_execute`
   - Configure components
   - Set up physics layers if needed

3. **Check console** via `read_console` for compilation errors.

## Phase 3: Verify

1. Verify no console errors via `read_console`
2. Summarize what was created/modified
3. Explain how to test the feature
4. Note any manual steps needed (e.g., assigning references in Inspector)
5. Reference the source issue (`#<issue_number>`, its URL) in the summary so the user can link it in their commit/PR

## Phase 4: Auto-Verify (Optional)

After implementation, offer to run the `unity-verifier` agent for a verify-fix loop:
- Reviews all changed files for serialization safety, performance, and Unity-specific pitfalls
- Auto-fixes safe issues (missing FormerlySerializedAs, CompareTag, cached GetComponent, etc.)
- Re-verifies up to 3 iterations until clean
- Reports remaining items that require human judgment

Suggest: "Would you like me to run a verification pass on the changes?"
