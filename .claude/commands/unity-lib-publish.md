---
name: unity-lib-publish
description: "Publishes local edits made to an embedded mtafasahin-unity-packages package: verifies with this project's tests, pushes a new tagged version to the shared repo, then switches this project back to that tag as a git dependency and commits/pushes both repos."
user-invocable: true
---

# /unity-lib-publish — Ship local package edits back to the shared repo

Arguments: `<package-name>` (e.g. `com.mtafasahin.reactive`) or `all`; optional
trailing `major`/`minor`/`patch` to control the version bump (default `patch`).
Default package scope to `all` if omitted.

Counterpart to `/unity-lib-dev`. Run this once local edits to an embedded
shared package are ready. It does NOT ask for confirmation at each step —
invoking this command IS the confirmation; it ends with two pushes (the
shared packages repo, and this project). Report exactly what was pushed
where when done.

## Steps

1. **Find embedded shared packages.** Scan this project's `Packages/` for
   folders matching known shared package names (currently
   `com.mtafasahin.reactive`, `com.mtafasahin.mobileservices` — but check
   `~/.claude/resources/mtafasahin-unity-packages/` for the authoritative,
   current list of folder names rather than trusting this hardcoded pair,
   since the repo may have grown a new package since this command was
   written). Filter to the requested package(s); if none match, say so and
   stop — there's nothing in local-edit mode to publish.

2. **Refresh the local packages-repo clone.**
   `~/.claude/resources/mtafasahin-unity-packages` should exist (from prior
   use). If missing, `gh repo clone mustafa-sahin-tr/mtafasahin-unity-packages
   ~/.claude/resources/mtafasahin-unity-packages`. If present, check
   `git status` there is clean, then `git pull`. If it has uncommitted
   changes of its own, stop and report rather than overwriting or mixing them
   with this publish.

3. **Verify with this project's tests, before touching anything.** Run
   `mcp__unityMCP__run_tests` (EditMode). If any test fails other than the
   known pre-existing, unrelated
   `MustyBlockBlast.Tests.EditMode.ClassicModeExtrasGatingTests.OnRunStarted_InClassicModeWithARealDuration_StartsTheClock`
   failure, stop and report the failures — do not publish on top of a broken
   local state. (If that known failure has since been fixed, don't treat its
   absence as a problem either — just don't require it to still be failing.)

4. **For each target package, diff and decide.** Compare
   `Packages/<name>/` in this project against
   `~/.claude/resources/mtafasahin-unity-packages/<name>/` in the refreshed
   clone (a plain recursive diff of `.cs` files is enough; `.meta` files
   rarely need comparing). No differences → skip this package, note "nothing
   to publish" for it.

5. **Copy changes into the packages-repo clone.** For each package with real
   changes, copy this project's `Packages/<name>/` tree over the clone's
   `<name>/` folder (whole-folder copy is simplest and correct here — the
   `.meta` files should already match since this project's copy originated
   from a git checkout of that same repo).

6. **Bump the version.** In the clone, edit each changed package's
   `package.json` `version` field: read the current value, apply a semver
   bump (`patch` by default, or `major`/`minor` per the command argument —
   reset lower components on a major/minor bump, per normal semver rules).

7. **Update the README.** If the clone's `README.md` has a "Versions"
   section (or similar changelog-style list), add a line for the new version
   summarizing what changed, inferred from the diff. Keep it short — one or
   two lines, in the same style as existing entries.

8. **Commit, tag, push the packages repo.** In the clone:
   `git add -A`, commit with a message describing the actual change (derive
   it from the diff — don't write a generic "update" message), then
   `git tag -a v<new-version> -m "..."` (use the highest bumped version if
   multiple packages changed with different versions — the repo tag is
   shared across all packages it contains), then `git push` and
   `git push origin v<new-version>`.

9. **Switch this project back to the git dependency.** For each published
   package: `git rm -r Packages/<name>` in this project (removes the
   embedded copy), then edit `Packages/manifest.json` to replace that
   package's entry with
   `https://github.com/mustafa-sahin-tr/mtafasahin-unity-packages.git?path=<name>#v<new-version>`.

10. **Re-resolve and re-verify.** Run `mcp__unityMCP__manage_packages`
    `resolve_packages`, then `mcp__unityMCP__refresh_unity` (force, compile,
    wait_for_ready), then check `read_console` for errors, then
    `get_package_info` for each published package to confirm `source` is
    `"Git"` at the new version. **If Unity still reports the old (embedded)
    resolution — a known caching issue when a package's source type changes
    while the Editor is open — tell the user to close and reopen Unity, then
    re-run this verification before continuing.** Re-run the EditMode tests
    once more to confirm the git-sourced package behaves identically to the
    embedded copy that was just tested.

11. **Commit and push this project.** `git add` the manifest, lock file, and
    the embedded-folder removal; commit with a message referencing the new
    package version(s); `git push`.

12. **Report.** Summarize: which package(s) were published, old → new
    version, the packages-repo commit/tag, and this project's commit — with
    both push confirmations (or exactly what didn't get pushed and why, if
    something stopped the flow early at steps 2, 3, or 10).
