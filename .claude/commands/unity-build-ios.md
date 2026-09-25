---
name: unity-build-ios
description: "Builds/exports the Xcode project for iOS via MCP, so it can be opened and run on a device from Xcode. Pass dev or prod (e.g. /unity-build-ios dev); asks if omitted."
user-invocable: true
args: build_type
argument-hint: "dev | prod"
---

# /unity-build-ios — Export the Xcode Project

Argument: **$ARGUMENTS** — the build type:
- `dev` / `development` → Development build (Google **test** ads, for the user's own devices)
- `prod` / `production` / `release` / `store` → Production build (**LIVE** ads, for the store)
- empty or anything else → **ask the user** which one before doing anything (see "Build Type" below)

e.g. `/unity-build-ios dev` or `/unity-build-ios prod`.

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
- Player settings: keep the existing bundle identifier (`com.mtafasahin.blockioblast`, read it from ProjectSettings first) unless the user asks to change it — do not silently overwrite it.
- Minimum iOS version: 15.0+ unless the project already specifies otherwise (check current settings first, don't downgrade).
- Target devices: iPhone (confirm with user if iPad support is also expected).
- Signing team ID: leave as configured in Xcode/Unity — this command does not manage signing. If unset, note it in the report rather than guessing a team ID.

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

### Step 3: Build (Export Xcode Project)

```
manage_build action:"build" (target: iOS) → export the Xcode project
read_console → monitor progress and catch errors
```

Export to the project's existing iOS build output folder if one exists (check for a prior `Builds/iOS` or similar path before creating a new one); otherwise use a sensible default and report the exact path chosen.

### Step 3.5: CocoaPods (`pod install`)

Unity's iOS export regenerates the `Podfile` via EDM4U on every run whenever pod-based
plugins are present (this project pulls in `Google-Mobile-Ads-SDK` and
`GoogleUserMessagingPlatform` for AdMob/UMP). The exported `.xcodeproj` alone does **not**
link those pods — opening it directly fails at link time with errors like
`Undefined symbol: _CGSizeFromGADAdSize`.

After a successful export, if `<output_path>/Podfile` exists:
- Before running `pod install`, ensure the Podfile has a `post_install` hook that forces
  every pod's `IPHONEOS_DEPLOYMENT_TARGET` up to the project's minimum (read from the
  `platform :ios, 'X.Y'` line, default 15.0). EDM4U regenerates the Podfile from scratch
  on every export with no such hook, and pods like `Google-Mobile-Ads-SDK` /
  `GoogleUserMessagingPlatform` ship a lower deployment target (commonly 12.0) than Xcode
  now accepts — without the hook, Xcode reports "The iOS deployment target
  'IPHONEOS_DEPLOYMENT_TARGET' is set to 12.0, but the range of supported deployment
  target versions is 15.0 to ...". Append (don't replace) something like:
  ```ruby
  post_install do |installer|
    installer.pods_project.targets.each do |target|
      target.build_configurations.each do |config|
        deployment_target = config.build_settings['IPHONEOS_DEPLOYMENT_TARGET']
        if deployment_target.nil? || deployment_target.to_f < 15.0
          config.build_settings['IPHONEOS_DEPLOYMENT_TARGET'] = '15.0'
        end
      end
    end
  end
  ```
  Skip this if the hook is already present (don't duplicate it).
- Run `pod install` in the output folder.
- If `pod` isn't installed on this machine, don't fail the build — report that CocoaPods
  needs a one-time install (`brew install cocoapods`) and that `pod install` must then be
  run manually in the output folder.
- If no `Podfile` exists, skip this step silently — nothing to install.

This step is explicitly allowed even though the command otherwise avoids `xcodebuild`/signing —
`pod install` only resolves dependencies into the already-exported project, it does not
build, archive, or sign anything.

### Step 4: Report

- Build result: SUCCESS or FAILURE.
- Build type: Development (test ads) or Production (LIVE ads).
- Exact path to the exported Xcode project.
- Any warnings from the build log.
- `pod install` outcome: ran successfully / skipped (no Podfile) / needs manual CocoaPods install.
- If failed: error details and suggested fixes (see table below).
- Explicit next step for the user: open `.xcworkspace` if `pod install` ran (or the project already has pods), otherwise `.xcodeproj` — "Open `<path>/Unity-iPhone.xcworkspace` in Xcode, select your device, and press Run."

## What NOT To Do

- Never modify `ProjectSettings/` files directly — use MCP (`manage_build`) only.
- Never attempt to invoke `xcodebuild`, archive, sign, or install to a device — that part is manual, in Xcode, by design (per the user's workflow). `pod install` is the one exception, see Step 3.5.
- Never change the bundle identifier or signing configuration without being asked.
- Never skip the pre-build console check.
- Never let a `pod install` failure or missing CocoaPods install mask the underlying Xcode export result — report them separately.

## Common Build Fixes

| Error | Fix |
|-------|-----|
| `UnityEditor` namespace | Add `#if UNITY_EDITOR` guard |
| Missing type/assembly | Check `.asmdef` references |
| Stripping removes code | Add entries to `link.xml` |
| `Undefined symbol: _CGSizeFromGADAdSize` (or other GAD/UMP symbols) | `Podfile` pods weren't installed — run `pod install` in the exported folder (see Step 3.5), then open `.xcworkspace` |
| `IPHONEOS_DEPLOYMENT_TARGET is set to 12.0, but the range of supported deployment target versions is 15.0 to ...` | A pod's build settings weren't raised to the project minimum — add the `post_install` hook from Step 3.5 to the Podfile and re-run `pod install` |
| Xcode signing errors after export | Not this command's job — open Xcode, fix signing under Signing & Capabilities |
