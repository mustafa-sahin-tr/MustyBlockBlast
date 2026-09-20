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

3a. **Install reusable personal packages.** Source of truth:
   `github.com/mustafa-sahin-tr/mtafasahin-unity-packages` (private repo, tag-pinned releases —
   see its README for the version history). Default to the git-dependency form; only fall back to
   a copy if Unity Package Manager can't reach GitHub with this machine's git credentials.
   - Add to `Packages/manifest.json` `dependencies` (use the latest tag from the repo's README,
     e.g. `#v1.1.1` — check, don't assume it's still current):
     ```
     "com.mtafasahin.reactive": "https://github.com/mustafa-sahin-tr/mtafasahin-unity-packages.git?path=com.mtafasahin.reactive#v1.1.1",
     "com.mtafasahin.mobileservices": "https://github.com/mustafa-sahin-tr/mtafasahin-unity-packages.git?path=com.mtafasahin.mobileservices#v1.1.1"
     ```
     plus `com.unity.services.authentication` and `com.unity.services.core` if not already present.
     If the project has no Apple Sign-In plugin, remove `"AppleAuth"` from
     `Mtafasahin.MobileServices.asmdef`'s `references` (that file lives in the fetched package
     cache under `Library/PackageCache/`, not in this project — editing it there is a per-machine,
     non-persistent change; if the project genuinely never needs Apple Sign-In, it's fine to leave
     the unresolved reference, Unity only warns).
   - Add `Mtafasahin.Reactive`, and `Mtafasahin.MobileServices` if the project will wire
     auth/leaderboards/IAP/music/sfx through it, as `references` on the Gameplay-equivalent asmdef
     (asmdef references work identically regardless of whether the referenced package is git or
     embedded — no per-project edit needed here beyond adding the name).
   - If Unity MCP is connected: run a package resolve, then a forced recompile, and check the
     console for errors. **If this project previously had these packages embedded (or any package
     changed between embedded/git for a package of the same name) and the Editor was already open,
     Unity's Package Manager caches the old resolution in memory — a plain resolve/refresh will not
     pick up the change.** Tell the user to close and reopen the Unity Editor, then re-run the
     resolve + recompile + console check before reporting success.
   - Only if git access isn't available: copy `com.mtafasahin.reactive` and
     `com.mtafasahin.mobileservices` from a local clone
     (`gh repo clone mustafa-sahin-tr/mtafasahin-unity-packages`) into this project's `Packages/`
     folder instead, and reference them in `manifest.json` with a plain version string.

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
