---
name: unity-build-android
description: "Builds an Android APK/AAB via MCP for local device testing or Play Store upload. Pass dev or prod (e.g. /unity-build-android dev); asks if omitted."
user-invocable: true
args: build_type
argument-hint: "dev | prod"
---

# /unity-build-android — Build for Android

Argument: **$ARGUMENTS** — the build type:
- `dev` / `development` → Development build (Google **test** ads, for the user's own devices)
- `prod` / `production` / `release` / `store` → Production build (**LIVE** ads, for the store)
- empty or anything else → **ask the user** which one before doing anything (see "Build Type" below)

e.g. `/unity-build-android dev` or `/unity-build-android prod`.

Fixed-platform shortcut for `/unity-build Android`. Builds an installable Android package.

## Workflow

Use the `unity-build-runner` agent to:

### Step 1: Pre-Build Checks

1. **Check console** via `read_console` — abort if compilation errors exist.
2. **Check project info** via `project_info` resource — confirm current active platform and Unity version.
3. **Validate build scenes** — ensure every scene in the build list exists on disk.
4. Confirm no `#if UNITY_EDITOR`-unguarded `UnityEditor` usage snuck into runtime code.

### Step 2: Configure Android Platform

Via `manage_build`:
- Switch active platform to **Android** if not already active.
- Player settings: package name: keep the existing `com.mtafasahin.blockioblast` (matches the iOS bundle id) — do not overwrite it.
- Minimum API level: 24+ unless the project already specifies otherwise.
- IL2CPP scripting backend, ARM64 only (disable ARMv7) for a modern device build.
- Output format: ask the user once — **APK** (for direct install/sideload on a test device) or **AAB** (for Play Store upload) — if not already clear from context. Default to APK for local device testing since that matches "open and run on my device" workflows. The build type settles it in the usual case: `dev` → APK, `prod` → AAB (Play Store); ask only if the user says otherwise.
- Keystore/signing: leave as configured. If unset and the user wants a signed release build, tell them to configure a keystore first rather than generating one automatically.

### Build Type: Development vs Production — ask the user

**Resolve the type from `$ARGUMENTS` first.** If it is missing or not one of the values above, ask:
"Development build mi (test reklamları, kendi cihazın için) yoksa production build mi (canlı
reklamlar, store için)?" — recommend Development. Never pick silently: the user asked to be asked,
in case they forget to say it. Skip the question only when `$ARGUMENTS` or the request itself already
names the type ("development build al", "production build al", "prod build", "store build").

Then always pass `development` explicitly to `manage_build action:"build"` — never inherit whatever
the Build Profile checkbox happens to be:

- **Development build** (`development: "true"`) — for the user's own devices.
  `Debug.isDebugBuild` is true, so `AdMobRewardSource` and `AdMobInterstitialSource` load Google's
  **test** ad units — test creatives, no device registration needed, taps are harmless.
- **Production build** (`development: "false"`) — `Debug.isDebugBuild` is false, so the **live**
  AdMob units are used; this is the build that goes to the store.

State the build type in the report ("Development — test ads" / "Production — LIVE ads"). For a
production build, warn the user not to tap ads when testing it on their own device.

### Step 3: Build

```
manage_build action:"build" (target: Android) → trigger the build
read_console → monitor progress and catch errors
```

Output to the project's existing Android build output folder if one exists (check for a prior `Builds/Android` or similar path before creating a new one); otherwise use a sensible default and report the exact path chosen.

### Step 4: Report

- Build result: SUCCESS or FAILURE.
- Build type: Development (test ads) or Production (LIVE ads).
- Exact path to the built `.apk`/`.aab`.
- Build size.
- Any warnings from the build log.
- If failed: error details and suggested fixes (see table below).
- Explicit next step for the user: for an APK, `adb install -r <path>` with a device connected, or drag-and-drop onto an emulator; for an AAB, upload via Play Console.

## What NOT To Do

- Never modify `ProjectSettings/` files directly — use MCP (`manage_build`) only.
- Never generate or commit a keystore — that's the user's to manage.
- Never silently pick a package name — ask once, then reuse it on subsequent runs.
- Never skip the pre-build console check.

## Common Build Fixes

| Error | Fix |
|-------|-----|
| `UnityEditor` namespace | Add `#if UNITY_EDITOR` guard |
| Missing type/assembly | Check `.asmdef` references |
| Stripping removes code | Add entries to `link.xml` |
| Build too large | Run `/unity-optimize` or `analyze-build-size.sh` |
| Gradle build failed | Check Android SDK/NDK paths in Unity's External Tools preferences |
