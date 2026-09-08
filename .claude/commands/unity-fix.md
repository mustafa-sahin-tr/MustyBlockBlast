---
name: unity-fix
description: "Diagnoses and fixes a Unity bug from a GitHub issue — reads console errors, checks common causes, applies targeted fix, verifies via MCP."
user-invocable: true
args: issue_number
---

# /unity-fix — Diagnose and Fix a Bug from a GitHub Issue

Argument: **$ARGUMENTS** — a GitHub issue number (optionally followed by `--quick`), e.g. `17` or `17 --quick`.

## Agent Routing

- Default: use `unity-fixer` agent (opus — deep investigation)
- If `$ARGUMENTS` contains `--quick`: use `unity-fixer-lite` agent (sonnet — for obvious fixes)
- Strip the `--quick` flag, leaving just the issue number

## Phase 0: Fetch the Issue

1. Extract the issue number from `$ARGUMENTS` (strip `--quick` if present). If what remains isn't a plain number, stop and tell the user to pass a GitHub issue number (e.g. `/unity-fix 17`).
2. Fetch it: `gh issue view <issue_number> --json number,title,body,url,state,labels,comments`
   - If `gh` is not authenticated or the issue can't be found, stop and report the error rather than guessing at a bug description.
   - If the issue is already closed, tell the user and ask whether to proceed anyway.
3. Use the issue's **title** and **body** as the bug description for the workflow below — this replaces any free-text description. Pull any stack trace, repro steps, or error text directly out of the body/comments.
4. Keep the issue number and URL on hand — reference it in step 5's explanation and in any commit message (`Fixes #<issue_number>` if the user commits).

## Workflow

0. **Branch first** — never fix directly on `main`:
   - `git status`; if there are uncommitted changes unrelated to this issue, stop and ask the user how to handle them (stash, commit, or abort) instead of branching over dirty state.
   - Branch name: `issue-<issue_number>-<kebab-case-slug-of-the-title>` (short, meaningful — the gist of the title, not a literal transliteration).
   - `git checkout -b issue-<issue_number>-<slug>` off the current `main` (pull first if `main` is behind `origin/main`). If that branch already exists (a resumed run), check it out instead of erroring.
   - Do not push the branch or open a PR automatically — that's a separate, explicitly-requested step.

Use the selected fixer agent to:

1. **Gather evidence:**
   - Read Unity console via `read_console` MCP for errors, warnings, stack traces
   - Search the codebase for the error message or related code
   - Parse the issue body/comments for file name, line number, and error type

2. **Diagnose** — check these common Unity causes in order:
   - NullReferenceException → missing reference, destroyed object, execution order
   - Missing Script → file/class name mismatch, asmdef issue
   - Serialization data loss → field renamed without FormerlySerializedAs
   - Coroutine stopped → SetActive(false) or Destroy
   - Physics not working → wrong layers, missing collider/rigidbody
   - Build failure → UnityEditor in runtime, platform defines

3. **Fix** — apply the minimal targeted fix. Don't refactor surrounding code.

4. **Verify:**
   - Check console via `read_console` — error should be gone
   - If it was a serialization issue, warn about data that may need re-configuration
   - If it was a build issue, suggest running `/unity-build` to verify

5. **Explain** what caused the bug and how the fix prevents recurrence. Reference the source issue (`#<issue_number>`, its URL) and the branch created in step 0.
