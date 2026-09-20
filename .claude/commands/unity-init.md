---
name: unity-init
description: "Scans a Unity project and generates a tailored CLAUDE.md with detected configuration, packages, render pipeline, and recommended skills."
user-invocable: true
---

# /unity-init — Project Setup

Scan this Unity project and generate a tailored CLAUDE.md configuration.

## Steps

1. **Read project info** via MCP `project_info` resource to get Unity version, platform, and state.

2. **Scan Packages/manifest.json** to detect installed packages:
   - Render pipeline (URP, HDRP, or Built-in)
   - Input System, Addressables, Cinemachine, Timeline, TextMeshPro
   - Networking (Netcode, Mirror, Photon, Fish-Net)
   - Third-party (DOTween, UniTask, VContainer, Zenject, Odin)

3. **Scan for assembly definitions** (`.asmdef` files) — map the project's assembly structure.

3a. **Install reusable personal packages.** Check whether
   `~/.claude/resources/mtafasahin-unity-packages/README.md` exists locally.
   - If it does not, clone it from GitHub first (this machine's `gh`/git credentials own
     the repo, so a plain clone works):
     `gh repo clone mustafa-sahin-tr/mtafasahin-unity-packages ~/.claude/resources/mtafasahin-unity-packages`.
     If that clone fails (no network, no `gh` auth, repo renamed), say so and skip this step
     rather than blocking the rest of `/unity-init`.
   - If it already exists locally, run `git -C ~/.claude/resources/mtafasahin-unity-packages pull`
     first so the copy reflects the latest pushed version, not a stale one from a previous project.
   - Once the local copy exists, offer to install `com.mtafasahin.reactive` and
     `com.mtafasahin.mobileservices` (skip any package whose folder already exists under this
     project's `Packages/`):
   - Copy `~/.claude/resources/mtafasahin-unity-packages/com.mtafasahin.reactive` and
     `.../com.mtafasahin.mobileservices` into this project's `Packages/` folder.
   - Add both to `Packages/manifest.json` `dependencies` (version `"1.0.0"`), plus
     `com.unity.services.authentication` and `com.unity.services.core` if not already present.
     If the project has no Apple Sign-In plugin, remove `"AppleAuth"` from
     `Mtafasahin.MobileServices.asmdef`'s `references`.
   - From the assembly scan in step 3, find the project's Gameplay/Systems assembly name and,
     if any EditMode test sets `ReactiveProperty<T>.Value` directly, its EditMode test assembly
     name. In the copied `com.mtafasahin.reactive/Runtime/AssemblyInfo.cs`, replace
     `__GAMEPLAY_ASMDEF__` with the former and `__EDITMODE_TESTS_ASMDEF__` with the latter
     (delete that `InternalsVisibleTo` line entirely if there is no such test assembly).
   - Add the new package asmdef names (`Mtafasahin.Reactive`, and `Mtafasahin.MobileServices` if
     the project will wire auth/leaderboards/IAP/music/sfx through it) as `references` on the
     Gameplay-equivalent asmdef.
   - If Unity MCP is connected, run a package resolve + forced recompile and check the console
     for errors before reporting success.

4. **Scan for scenes** — list all `.unity` files in `Assets/`.

5. **Check existing CLAUDE.md** — if one exists, preserve user customizations.

6. **Generate CLAUDE.md** with:
   - Project overview (Unity version, render pipeline, detected packages)
   - Assembly structure
   - Scene list
   - References to rules files (`.claude/rules/*.md`)
   - Recommended skills based on detected packages
   - MCP integration notes
   - Key conventions summary

7. **Report** what was detected and configured, including whether the reusable packages from
   step 3a were installed (and if not, why — e.g. resource folder missing, or already present).
   Suggest next steps:
   - Review and customize the generated CLAUDE.md
   - Install unity-mcp if not already present
   - Try `/unity-audit` for a full project health check

## Output

Present the results in a clear summary table showing what was detected and which skills are recommended.
