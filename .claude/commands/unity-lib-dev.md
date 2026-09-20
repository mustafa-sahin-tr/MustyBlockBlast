---
name: unity-lib-dev
description: "Switches one or all mtafasahin-unity-packages shared packages from a pinned git dependency to a locally embedded, directly-editable copy in this project, so they can be edited exactly like any other project file."
user-invocable: true
---

# /unity-lib-dev — Enter local edit mode for a shared package

Arguments: `<package-name>` (e.g. `com.mtafasahin.reactive`) or `all`. Default to `all` if omitted.

This project depends on shared packages from the private repo
`github.com/mustafa-sahin-tr/mtafasahin-unity-packages`, referenced in
`Packages/manifest.json` as `...git?path=<name>#<tag>`. That's the right
default state (one source of truth, versioned), but it's inconvenient to
edit directly — changes have to go through a separate clone. This command
flips a package into "embedded" mode: a real local copy under this
project's `Packages/`, editable and hot-reloaded exactly like any other
script in the project. Pair with `/unity-lib-publish` to push local edits
back out and return to the pinned git dependency when done.

## Steps

1. Read `Packages/manifest.json`. Find every dependency whose value is a
   `https://github.com/mustafa-sahin-tr/mtafasahin-unity-packages.git?path=<name>#<tag>`
   URL — these are the shared packages. Match the requested package name(s)
   against this set; if the argument doesn't match any, say so and stop.

2. For each target package already embedded (a real folder already exists at
   `Packages/<name>/` instead of a git URL in the manifest), skip it and note
   that it's already in local-edit mode.

3. For each remaining target package:
   - Call `mcp__unityMCP__manage_packages` with `action: "embed_package"` and
     `package: "<name>"`. This copies the currently-resolved git version from
     `Library/PackageCache/` into `Packages/<name>/` and rewrites its
     `manifest.json` entry to a plain version string automatically.
   - If `embed_package` isn't available or fails, do it manually: copy
     `Library/PackageCache/<name>@<hash>/` (find the hash via
     `get_package_info`) to `Packages/<name>/`, and change that package's
     manifest entry from the git URL to a plain version string (the version
     `get_package_info` reported).
   - Verify with `get_package_info` that `source` is now `"Embedded"`.

4. Run `mcp__unityMCP__manage_packages` `resolve_packages`, then
   `mcp__unityMCP__refresh_unity` (`mode: force`, `compile: request`,
   `wait_for_ready: true`), then check `read_console` for errors.
   **If this project had the same package embedded before and the Editor was
   already open when the source type last changed, Unity's Package Manager
   can serve a stale cached resolution** — if `get_package_info` still shows
   the old source/path after a resolve, tell the user to close and reopen
   the Unity Editor, then re-verify.

5. Report which package(s) are now embedded at `Packages/<name>/`, that they
   are plain project files now (git status in this project's repo will show
   them as untracked/new — that's expected, they aren't committed here since
   this is a temporary working state), and that `/unity-lib-publish` is the
   way back: it tests, publishes the edits to the shared repo, and restores
   the pinned git dependency.
