---
name: unity-build-ios
description: "Builds/exports the Xcode project for iOS via MCP, so it can be opened and run on a device from Xcode."
user-invocable: true
---

# /unity-build-ios — Export the Xcode Project

Fixed-platform shortcut for `/unity-build iOS`. Builds (exports) the Xcode project only —
it does **not** archive, sign, or run on a device. After the export finishes, the user opens
the generated `.xcodeproj`/`.xcworkspace` in Xcode themselves and hits Run.

## Workflow

Use the `unity-build-runner` agent to:

### Step 1: Pre-Build Checks

1. **Check console** via `read_console` — abort if compilation errors exist.
2. **Check project info** via `project_info` resource — confirm current active platform and Unity version.
3. **Validate build scenes** — ensure every scene in the build list exists on disk.
4. Confirm no `#if UNITY_EDITOR`-unguarded `UnityEditor` usage snuck into runtime code.

### Step 2: Configure iOS Platform

Via `manage_build`:
- Switch active platform to **iOS** if not already active.
- Player settings: keep the existing bundle identifier (`com.mtafasahin.mustyblockblast`) unless the user asks to change it — do not silently overwrite it.
- Minimum iOS version: 15.0+ unless the project already specifies otherwise (check current settings first, don't downgrade).
- Target devices: iPhone (confirm with user if iPad support is also expected).
- Signing team ID: leave as configured in Xcode/Unity — this command does not manage signing. If unset, note it in the report rather than guessing a team ID.

### Step 3: Build (Export Xcode Project)

```
manage_build action:"build" (target: iOS) → export the Xcode project
read_console → monitor progress and catch errors
```

Export to the project's existing iOS build output folder if one exists (check for a prior `Builds/iOS` or similar path before creating a new one); otherwise use a sensible default and report the exact path chosen.

### Step 4: Report

- Build result: SUCCESS or FAILURE.
- Exact path to the exported Xcode project.
- Any warnings from the build log.
- If failed: error details and suggested fixes (see table below).
- Explicit next step for the user: "Open `<path>/Unity-iPhone.xcworkspace` in Xcode, select your device, and press Run."

## What NOT To Do

- Never modify `ProjectSettings/` files directly — use MCP (`manage_build`) only.
- Never attempt to invoke `xcodebuild`, archive, sign, or install to a device — that part is manual, in Xcode, by design (per the user's workflow).
- Never change the bundle identifier or signing configuration without being asked.
- Never skip the pre-build console check.

## Common Build Fixes

| Error | Fix |
|-------|-----|
| `UnityEditor` namespace | Add `#if UNITY_EDITOR` guard |
| Missing type/assembly | Check `.asmdef` references |
| Stripping removes code | Add entries to `link.xml` |
| Xcode signing errors after export | Not this command's job — open Xcode, fix signing under Signing & Capabilities |
