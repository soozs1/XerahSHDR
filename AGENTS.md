# XerahS Agent Instructions

**XerahS** - The Avalonia UI implementation of ShareX.  
**Copyright (c) 2007-2026 ShareX Team.**

> **Single Source of Truth**: Start here. Agent-specific compatibility files should defer to this document instead of restating repository policy.

## Critical Instructions
1. **Build Integrity**
   - `dotnet build` must pass with **0 errors** before any push.
   - Exception: if the only tracked change is a version-only bump in the root `Directory.Build.props`, a fresh `dotnet build` is not required before push. In that case, verify the diff is limited to the intended version change and keep the commit prefix aligned with the new version.
   - **Never** disable `<TreatWarningsAsErrors>`. Fix the warnings.
   - **Target Framework**: `net10.0-windows10.0.26100.0` (do not use `net10.0-windows` alone).
   - **SkiaSharp**: keep all `SkiaSharp` and `SkiaSharp.NativeAssets.*` packages aligned to the same version in root `Directory.Packages.props` (currently managed **SkiaSharp 4.151.0** plus matching **SkiaSharp.NativeAssets.Linux 4.151.0** and **SkiaSharp.NativeAssets.Win32 4.151.0**). The managed and native versions must match exactly; Avalonia 12.1.1's transitives pull `SkiaSharp.NativeAssets.Linux` 3.119.4 by default, and a managed/native split crashes startup with `SkiaSharpVersion.CheckNativeLibraryCompatible` ("Supported versions of the native libSkiaSharp library are in the range [151.0, 152.0)"). Do not reintroduce the legacy `2.88.9` pin.

2. **Build Timeouts**
   - Do not enforce a fixed single-build time limit; packaging and release builds can legitimately take longer on slower PCs.
   - If a build appears stalled rather than merely slow, stop it, clear locks, prefer `-m:1`, kill stale `dotnet` processes, then retry.
   - See [Building Android](.ai/skills/build-android/SKILL.md) for Android-specific lock handling and single-node builds.

3. **Shell Best Practices**
   - Do not use `&&` in this PowerShell environment.
   - Use `;` for unconditional sequencing or `if ($?) { ... }` for conditional execution.
   - Example: `git add .; if ($?) { git commit -m "..." }`

4. **Git Workflow**
   - Sequence: stage (`git add .`) -> commit -> push.
   - Commit format: `[vX.Y.Z] [Type] Use concise description`.
   - **Version prefix must use the next unreleased XerahS app version.** Read root `Directory.Build.props` `<Version>` and compare it with the highest existing XerahS tag (for example `git tag --sort=-v:refname | Select-Object -First 1`). Never use a version prefix that is lower than or equal to the latest tag. If the root version is not ahead of the latest tag, bump `Directory.Build.props` first, then use that bumped version in commit prefixes.
   - Exception: when committing inside shared library repos/submodules such as `ShareX.ImageEditor` (and other libraries shared with ShareX), omit the version prefix and use `[Type] Use concise description` because those commits must not carry the XerahS app version.
   - **Do not create branches** (local or remote) unless a human explicitly requests it. Stay on the current branch; do not invent feature/fix/chore branches, worktree branches, or `cursor/*` branches on your own.
   - If verification passes and the user did not ask to pause, execute the workflow without waiting for extra permission.

5. **Agent Workflow**
   - Plan before non-trivial work. Use the host tool's plan mode when available; otherwise post a numbered plan in chat before editing.
   - For large, multi-step, parallelizable, or context-heavy work, the coordinating agent must use sub-agents when the host supports them. In Codex, use `spawn_agent` (or the host's current equivalent) for bounded side tasks instead of keeping all work in one thread.
   - Do not create competing instruction sets. Keep shared workflow rules in [Universal Agent Workflow](developers/guidelines/AGENT_WORKFLOW.md) and keep compatibility shims thin.
   - Record durable lessons in [Lessons Learnt](developers/lessons-learnt/general.md) or the nearest topic-specific lessons file.

6. **Search Tool Restrictions (known agent hangs — do not violate)**
   - **Forbidden**: the structured grep/search tool over all C# sources with alternation-heavy or escaped-bracket regexes. Confirmed to hang the agent (2026-08-15, reproduced twice):
     - `{"include_pattern": "**/*.cs", "regex": "ShowAfterCaptureWindowAsync|AfterCaptureViewModel|AfterCaptureWindow"}`
     - `{"regex": "\\[HDR\\] ProcessAsync"}` (escaped square brackets)
   - **Required alternative**: plain terminal grep scoped to a concrete directory with one simple literal pattern, e.g. `grep -rn "AfterCaptureWindow" src/desktop/app/XerahS.UI/ViewModels/` or `grep -rn -F "[HDR] ProcessAsync" src/desktop/core/XerahS.Core/Tasks/`.

7. **Commit After Major Changes**
   - Commit promptly after any major change, especially once its correctness has been verified by a build or tests. Uncommitted work is fragile: working-tree-only changes can be lost silently (deleted/restored folders, cleanup), so never end a session with a significant verified fix sitting uncommitted.
   - A local commit is always safe and reversible and does not require push permission.

8. **Cross-Platform Build Hygiene (shared Windows/WSL tree)**
   - **Mandatory cleanup before AND after every build/test (`dotnet build` / `dotnet test`, either platform) — never end a session with these files present, or the build hangs/crashes:**
     - `ShareX.VideoEditor/frontend/node_modules/.bin/` POSIX symlinks created by WSL npm — exactly 6 files: `jiti`, `nanoid`, `rolldown`, `tsc`, `tsserver`, `vite`. Windows `fs.rmdir` cannot delete them, so any copy step or `npm ci` on Windows hangs forever (symptom: copy stalls "remaining 6 files" or MSB3077 ENOTEMPTY in `ShareX.VideoEditor.csproj`).
     - Leftover test artifacts under `**/bin/**`, e.g. `tests/XerahS.Tests/bin/Debug/net10.0/<hash>/backups/2026-01/locked.zip` left behind by the Unix-only `SettingsBaseBackupDiagnosticsTests.PruneOldBackups_PerFolderDeleteFailure_FiresSettingsBackupFailed_PruneFolderPhase` test (it deliberately creates a chmod-0555 `locked.zip` and the leftover dir is `666` without the execute bit, so neither the test TearDown nor Windows can remove it). Read-only/no-execute files in `bin` block recursive deletes and output copies; `chmod 777 <dir>` first if a delete reports Permission denied.
   - **When to clean:** *before* a build/test (a previous session — possibly the other platform — left leftovers) AND *after* a build/test (the run itself recreates them: `npm ci` rewrites the `.bin` symlinks, and the backup-prune test drops `locked.zip`).
   - **Handy tool:** run `./clean-wsl-leftovers.sh` from the repo root whenever copying the tree WSL → Windows or before a Windows build — it removes the 6 `.bin` symlinks and the test `locked.zip`/`settings-backup-tests` artifacts (dry-run first, then `--apply`, or `--force` to also `chmod` locked dirs). Works both from a WSL shell and from Windows PowerShell via `bash \\wsl.localhost\<distro>\...\clean-wsl-leftovers.sh`.
   - **Mandatory HDR pipeline check before any Windows build that touches HDR:** run `dotnet test tests/XerahS.Tests/XerahS.Tests.csproj --filter HdrPipelineDiagnostics` and verify the printed matrix — all rows must keep the known colors (red=255,0,0 / green=0,255,0 / blue=0,0,255 / yellow=255,255,0) with no channel swap, and the brightness columns must match the expectations documented in the test. Numerically verify color/brightness regressions here; never judge them by eye in the built app. This test caught the R↔B channel swap and the over-bright preview regressions.
   - Never alternate full builds between WSL and native Windows on the same working tree. `ShareX.VideoEditor/frontend/node_modules` created by one platform's npm breaks the other platform's npm ci with `ENOTEMPTY` (MSB3077 in `ShareX.VideoEditor.csproj`): WSL npm writes POSIX symlinks into `node_modules/.bin/` (jiti, nanoid, rolldown, tsc, tsserver, vite) that Windows `fs.rmdir` cannot delete.
   - `ShareX.VideoEditor/scripts/ensure-webui-deps.mjs` hashes `process.platform` into the deps stamp, so the stamp is per-platform and switching platforms forces a fresh `npm ci` (which then trips on the foreign symlinks).
   - **Restoring for the next build:** the WebUI `npm run build` resolves `tsc`/`vite` through `node_modules/.bin`, so a cleaned tree must not reuse a stale deps stamp. Whenever `node_modules/.bin` is missing/empty or the tree was touched by the other platform, delete the whole `ShareX.VideoEditor/frontend/node_modules` first (use `find ShareX.VideoEditor/frontend/node_modules -depth -delete` in a script; bare `rm -rf` with globs is blocked in this environment) so the target platform's `npm ci` recreates it natively.
   - `<BuildWebUI>false</BuildWebUI>` in `ShareX.VideoEditor/backend/ShareX.VideoEditor.csproj` skips the npm steps and is safe only when `ShareX.VideoEditor/frontend/dist` is already built.
   - **Native Windows build** (from the repo root on Windows): `dotnet build XerahS.sln -m:1 -p:nodeReuse=false -p:UseSharedCompilation=false`. This is the only command that builds the full `net10.0-windows10.0.26100.0` solution; `src/desktop/XerahS.sln` is the Linux/WSL-scope desktop subset. From WSL use `~/.dotnet/dotnet` and never run a full `dotnet test` of `tests/XerahS.Tests` right before a Windows build unless the VideoEditor `node_modules` is cleaned afterwards (see above).

9. **Automated HDR self-test (Windows, real capture)**
   - Run the built app with `--hdr-self-test <outDir>` (or `--hdr-self-test=<outDir>`): `XerahS.exe --hdr-self-test C:\temp\hdr-selftest`. It shows a known-color test card, captures it through the real DXGI pipeline, and writes `report.txt` / `report.json` plus stage PNGs into the folder.
   - Exit code **0 = PASS**, 1 = FAIL. Every failure line names the stage, e.g. `tonemap-TonemapBt2446C:stripe-red-keeps-R` or `saveas-hdr:bit-depth`.
   - Prefer it over manual screenshots for any color/depth regression: it verifies channel order (red/green/blue/cyan/magenta/yellow stripes), neutral white/gray, tone-mapping clipping metrics, PNG bit depth/markers (8-bit SDR vs 16-bit linear iCCP), the real Save-As content path, and the preview-source invariant — all numerically.
   - Requires an HDR-enabled display and `UseModernCapture` (DXGI). On an SDR display `capture-is-rgba-f16` fails by design (the test reports which mode it actually captured).
   - The self-test hides the main window and uses its own single-instance mutex, so it can run while the normal app instance is open.
   - It also writes `variants/` (10 tone-map candidates: clamp / clamp at SDR white / Reinhard / ACES / BT.2446C, plain and anchored at the estimated SDR white, plus Hable and gamma) and `capture-sdr16.png` (display-referred 16-bit reference that must match the screen). Use these for visual tuning: pick the variant closest to the live screen and promote it to a production mode.
   - `report.json` records the capture profile (`sdrWhite`, `peakRatio`), per-stripe means and clipping for every variant, so tone-curve decisions stay numeric.
   - **Effectively-SDR content bypasses the tone curves:** `HdrTonemapper.TonemapToBgra8888` checks `HdrContentAnalyzer` once per frame and, when `peak/white <= 1.05` (a normal desktop, an SDR photo/video or a game UI on an HDR output), returns the identity mapping for every mode except `None` (which is identity anyway). There is nothing above SDR white to compress, and a filmic curve only moves midtones away from the screen: measured on a live HDR capture, the bypassed SDR output matched a GDI screen grab to 0.000 on every image region, while Hable lifted the mean luminance by ~13%. Real HDR frames (rare specular peaks) still go through the selected curve.
   - **Default SDR tone mapping is `TonemapHable`** — the Hable/Uncharted-2 filmic curve anchored at the capture's estimated SDR white (`HdrContentAnalyzer`). It was selected by the operator's visual assessment of real HDR photographs (CC0 Polyhaven HDRIs in `tests/XerahS.Tests/TestData/HdrPhotos`, see `HdrPhotoVariantTests`), where the variants ranked: 09 `hable-norm` > 02 `clamp-norm` > 08 `bt2446c-norm`; all others were reported too bright (05/06/03/04/01/07) or too dark (10). Do not change the curve without regenerating the photo variants and obtaining a new visual pick.
   - Variant numbering used for that assessment lives in `HdrTonemapVariants.All`: 01 clamp, 02 clamp-norm, 03 reinhard, 04 reinhard-norm, 05 aces, 06 aces-norm, 07 bt2446c, 08 bt2446c-norm, 09 hable-norm, 10 gamma-norm.
   - **16-bit PNG rule ("SDR+HDR" depth):** `ImageHelpers.SaveBitmap` always writes **display-referred 16-bit sRGB** (sRGB chunk, no `iCCP`), for HDR and SDR sources alike. Rationale, measured on this machine: the previous raw-linear + linear-`iCCP` output decodes as `Format32bppArgb` through GDI+/WIC (Photos, Explorer preview, Paint, Chromium) with the samples read verbatim — 122/158/135 instead of the correct 183/205/191 for the same frame, i.e. midtones ~35% too dark while highlights stay near white. That is exactly the reported "16-bit file is darker than the screen". Windows PNG decoders ignore `iCCP`, so a linear profile can never make a file viewable; keep `SaveHdrF16Png` (raw linear + `iCCP`) for diagnostic stage dumps only. A 16-bit PNG physically cannot carry values above display white — real HDR output needs a different container (Ultra HDR JPEG / AVIF), which is not implemented yet.
   - Implementation: `src/desktop/app/XerahS.UI/Diagnostics/HdrSelfTestRunner.cs`; entry point in `Program.cs` (`OnPostUIInitialization`).
   - **Ultra HDR JPEG for "SDR+HDR" depth into a photo file:** a 16-bit PNG physically cannot carry values above display white, so `ImageHelpers.SaveBitmap` writes an **Ultra HDR JPEG** (ISO 21496-1) when the source is `RgbaF16`, the depth is `Bit16` and the path ends in `.jpg`/`.jpeg`. `UltraHdrJpegWriter` assembles a display-referred SDR base, a log2 gain map (`GainMapMax = log2(peak/SDR white)`), the `hdrgm:*` XMP (APP1) and the MPF index (APP2); Windows Photos, Chrome and Android render the HDR range from it, while any plain JPEG decoder still shows the base. 8-bit sources keep the normal JPEG encoder. Guarded by `UltraHdrJpegWriterTests`.
   - **Depth setting applies to every source:** "SDR+HDR" (`PNGBitDepth.Bit16`) must produce a 16-bit PNG even when the working bitmap is 8-bit (SDR desktop, GDI fallback, opened SDR image). `MainViewModelHelper.SaveToPathAsync` routes any `Bit16` `.png` save through `ImageHelpers.SaveBitmap`, which widens to `Rgba16161616` tagged sRGB — the widened file keeps the exact 8-bit appearance (verified: identical mean R/G/B).
   - **scRGB normalization (SDR white):** DXGI fp16 frames are linear scRGB where `1.0 == 80 cd/m²`, but Windows composites SDR content at the configured SDR white level (commonly 200–300 cd/m² = 2.5–3.75 in scRGB). `DisplaySdrWhiteLevelHelper` (`DisplayConfigGetDeviceInfo` / `DISPLAYCONFIG_SDR_WHITE_LEVEL`) reads that level per output and `HdrSdrWhiteNormalizer.NormalizeToSdrWhite` divides every captured frame by it, so the invariant `1.0 == SDR white` holds for `HdrContentAnalyzer`, the tone curves, the preview LUT and the encoders. Without it the SDR output clips to white and every tone curve is anchored at the wrong pivot (the "washed out" / "shifted palette" reports). Never drop the normalization without replacing it with an equivalent per-output white-level source.
   - **Save-As diagnostics are opt-in:** `SavePathDebugDump` writes full-resolution stage PNGs only when `XERAHS_SAVE_DEBUG_DUMP=1` is set; unset by default because it slows saving and fills `Documents\XerahS\DebugDumps`.
   - **Screen-content mode:** `XerahS.exe --hdr-capture <outDir>` captures whatever is currently on the desktop (no test card), writes the raw fp16 dump, the production SDR and 16-bit outputs (through the real encoders), a display-referred `reference-screen-sdr16.png`, all tone-map variants under `variants/`, and `report.txt`/`report.json` with the HDR profile. Use it to verify an HDR photo/video/game against the live screen. The main window is hidden first; the app exits when done (exit code 0). The userlog records `[HDR] screen capture mode requested: <dir>` / `[HDR] starting screen capture` — if those lines are missing, the running binary predates the mode (rebuild).
   - **Real HDR test image:** `HdrPhotoLoader` decodes Radiance RGBE photos; `UltraHdrJpegGeneratorTests` writes `Documents\XerahS\HdrVariants\UltraHDR_venice_sunset.jpg` — a standards-compliant Ultra HDR JPEG (ISO 21496-1: base SDR JPEG + APP1 `hdrgm:*` XMP + APP2 MPF + appended gain-map JPEG, offsets verified against the reference libultrahdr layout). Open it in Windows Photos on an HDR display to get genuine HDR highlights for `--hdr-capture`.

10. **Single source of truth and the Windows build mirror**
   - The WSL checkout at `/home/XR/programs/XerahS-develop` is **the only repository**: git history, submodules and every source edit live here.
   - `C:\programs\XerahS-develop` is a **generated build mirror** (no `.git`, never edited by hand). It exists because the desktop target is Windows-only and `cmd.exe` cannot build inside a UNC path (`\\wsl.localhost\...`); running a `.cmd` from the WSL folder fails with "UNC paths are not supported".
   - Rebuild everything from the WSL checkout with one command: `./build-windows.sh`. It cleans WSL leftovers, syncs `src`, `tests` and the build props (excluding `bin`, `obj`, `node_modules`), writes `.xerahs-mirror`, runs `build-windows.cmd --no-pause` and prints the EXE path.
   - From Windows, `C:\programs\XerahS-develop\build-windows.cmd` performs the build without syncing and warns that the tree has no `.git`.
   - EXE: `C:\programs\XerahS-develop\src\desktop\app\XerahS.App\bin\Debug\net10.0-windows10.0.26100.0\XerahS.exe`. There is no `win-x64` folder — that only appears on `publish` with a RID.
   - **Never** name a build-script variable `OutDir`, `OutputPath` or `BaseOutputPath`: MSBuild inherits environment variables as global properties, so an `OUTDIR` variable silently redirected every assembly into `<project>\src\desktop\app\...` while the real EXE stayed stale.

11. **Upstream relationship (ShareX/XerahS)**
   - Local history was imported without ancestry. `git replace --graft 34736625 v0.24.18` records the true fork point so `git merge-base HEAD upstream/develop` works; `git replace -d 34736625` restores the flat view.
   - Remote: `upstream = https://github.com/ShareX/XerahS`. Fetch with `git -c submodule.recurse=false fetch --tags --no-recurse-submodules upstream develop` (recurse-submodules fails: the submodule remotes do not have the parent's objects).
   - As of 2026-11-17 upstream `develop` is **5230 commits ahead** of `v0.24.18` (latest tag `v0.34.1`) and a trial merge yields **76 conflicted files** across CI, onboarding UI, packaging and the editor submodule pointer. Do not attempt it as a routine merge.
   - Updating = porting, not merging: take upstream in a separate clone and re-apply the HDR patch set (`HdrSdrWhiteNormalizer`, `DisplaySdrWhiteLevelHelper`, the `HdrTonemapper` SDR bypass, the `ImageHelpers` encoders, `MainViewModelHelper` Save-As depth handling, `TaskImageSettingsPanel` + resx, `HdrSelfTestRunner`, the HDR tests and §9 here). Validate with the HDR test filter and `--hdr-self-test` before switching over.

## Adding a language

The UI is localized through satellite assemblies and the selector is **discovered at runtime**, so a
new language needs exactly **one** step and no code change:

1. Add `src/desktop/core/XerahS.Common/Resources.<culture>.resx` (for example `Resources.fr.resx`)
   next to `Resources.resx`. Copy the file, translate the values, leave keys untouched. Missing keys
   automatically fall back to the neutral (English) value, so a partial translation is valid.

The next start lists the new language: `Localization.GetAvailableCultures()` scans the satellite
assemblies next to the executable (`<culture>/XerahS.Common.resources.dll`) and the settings store the
chosen culture as a free-form string (`ApplicationConfig.LanguageCode`), not as an enum value.
`SupportedLanguage`/`ToCultureCode` remain only to read configs written by older builds.

`Localization.ApplyCulture(code)` sets `CurrentUICulture`/`CurrentCulture` for the UI and the
formatted values; `Resources.GetString(key)` and the `{local:Localize Key}` markup extension both read
through the same `ResourceManager`, so a new satellite file covers XAML and code at once.

Rules that keep translations cheap to add:

- Never hard-code user-visible text — use `Resources.GetString("Key")` in code and
  `{local:Localize Key}` in XAML. Keys are shared across views on purpose (e.g. `Common_Cancel`).
- Keep the neutral file the single source of key names; translators only copy it.
- `scripts/localize-xaml.py` re-applies translations to a ported tree by exact value match, so
  upstream text that already has a key is localized without hand-editing.

## GitHub Issues
- Do not create GitHub issues automatically when a bug or feature is discussed.
- Create or update GitHub issues only when the user explicitly asks for it.

## Documentation Index

### Shared Workflow
- [Universal Agent Workflow](developers/guidelines/AGENT_WORKFLOW.md)
- [Graphify Agent Prompt](developers/guidelines/GRAPHIFY_AGENT_PROMPT.md) — kickoff prompt + skill/tool paths for every agent using the `src/` knowledge graph
- [Multi-Agent Coordination](docs/architecture/MULTI_AGENT_COORDINATION.md)
- [Lessons Learnt](developers/lessons-learnt/general.md)

### Development
- [Coding Standards & License Headers](developers/guidelines/CODING_STANDARDS.md)
- [Release & Versioning](.ai/skills/git-workflow/SKILL.md)
- [Building Windows Executables](.ai/skills/build-windows-exe/SKILL.md)
- [Building Android (MAUI / Avalonia, adb deploy)](.ai/skills/build-android/SKILL.md)
- [Testing Guidelines](developers/guidelines/TESTING.md)
- [Documentation Standards](developers/guidelines/DOCUMENTATION_STANDARDS.md)
- [CLI Reference](developers/guidelines/CLI.md)

### Architecture
- [Porting Guide & Platform Abstractions](docs/architecture/PORTING_GUIDE.md)
- [XerahS Architecture Map](docs/architecture/xerahs_architecture_map.md)
- [src/ Knowledge Graph (graphify)](docs/architecture/graphify-out/README.md) — queryable AST graph for agent navigation (`GRAPH_REPORT.md`, `graph.json`, HTML). Skill: [`.ai/skills/graphify/SKILL.md`](.ai/skills/graphify/SKILL.md). Agent prompt: [GRAPHIFY_AGENT_PROMPT.md](developers/guidelines/GRAPHIFY_AGENT_PROMPT.md). Rebuild: `scripts/update-graphify.sh`.

### Planning
- [Roadmap & Status Snapshot](docs/planning/ROADMAP_SNAPSHOT_JAN_2025.md)
- [XIP Sync](.ai/skills/sync-xips/SKILL.md)

---

# AGENTS.md

## Git identity and wrappers (mandatory)

All git activity in this repo MUST go through a per-person wrapper. No bare `git push`.

| Agent | Wrapper |
|---|---|
| Aoife | `git-aoife` |
| Mikhail | `git-mikhail` |
| Declan | `git-declan` |
| Vladislava | `git-vladislava` |

Whoever pushes uses their own wrapper. Example: Declan pushes with `git-declan push`, Vladislava with `git-vladislava push`. Wrappers set committer identity and route the push to the correct per-person remote on the matching `github-<person>` SSH host.

Run `git-<person> whoami` to confirm identity and remote before pushing.

### Fallback when no wrapper is installed

If the matching `git-<person>` wrapper is not on `PATH` (e.g. fresh CI/sandbox/headless box), fall back to the default git auth already configured for the agent:

- Confirm the configured identity is correct for you: `git config --get user.name` and `git config --get user.email`.
- Confirm the remote is the shared `https://github.com/ShareX/XerahS.git` (or your fork): `git remote -v`.
- Push with plain `git push <remote> <branch>` (or `git push` if on a tracking branch).

The wrapper is preferred because it pins the per-person committer identity and routes to the right remote. The fallback exists only so an agent is not blocked when the wrapper is genuinely unavailable; do not "fix" the wrapper-missing case by editing git config in place unless the operator asks for it.

## Source of truth

Inherited from `/Users/mike/Projects/KovaForge/AGENTS.md`. When this file and the parent conflict, the parent wins until this file is updated to match.

## Cursor Cloud specific instructions

These notes are for Cursor Cloud agents running on the Linux VM (Ubuntu 24.04). The per-person git wrappers above do **not** apply here; cloud agents use plain `git` on `cursor/*` branches.

### Toolchain (already provisioned in the VM snapshot)
- **.NET 10 SDK** lives in `~/.dotnet` and is on `PATH`/`DOTNET_ROOT` via `~/.bashrc`. If `dotnet` is not found in a non-login shell, use the full path `~/.dotnet/dotnet`.
- **Node.js** (>= 22.12) and `npm` are preinstalled system-wide and satisfy the `ShareX.VideoEditor` frontend `engines` requirement.
- On Linux the desktop projects build as **`net10.0`** (not `net10.0-windows...`); the Windows TFM only applies on Windows. `XerahS.Platform.Linux` is pulled in automatically.

### Build / test / run (Linux desktop scope)
- Build everything runnable on Linux: `dotnet build src/desktop/XerahS.sln -m:1 -p:nodeReuse=false -p:UseSharedCompilation=false` (single-node flags avoid MSBuild lock flakiness; see README "Desktop Quick Start").
- Tests: `dotnet test tests/XerahS.Tests/XerahS.Tests.csproj`.
- Run the app: `dotnet run --project src/desktop/app/XerahS.App/XerahS.App.csproj` (add `--no-build` if already built).
- Lint/style is enforced by the build itself via `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` + Roslyn analyzers. `dotnet format --verify-no-changes` reports many pre-existing ENDOFLINE/CHARSET findings and is **not** the repo's style gate — do not treat those as regressions.

### Non-obvious gotchas
- **First build builds the VideoEditor web UI**: an MSBuild target runs `npm ci` + `vite build` in `ShareX.VideoEditor/frontend` and fails if `frontend/dist` is missing. This needs network on the first build; later builds skip it when deps are current.
- **GUI needs a display**: `XerahS.App` `Program.cs` validates `DISPLAY`/`WAYLAND_DISPLAY` on Linux and will not start headless. The cloud VM already exposes an X11 display at `DISPLAY=:1` (the desktop the computer-use tools see). Export `DISPLAY=:1` before `dotnet run` if it is unset.
- Screen recording on Linux falls back to the FFmpeg `x11grab` backend (no ScreenCast portal in the VM).
- Submodules (`ShareX.ImageEditor`, `ShareX.VideoEditor`) are required to build; `XerahS.UI`/`XerahS.CLI` reference them directly. They are refreshed by the startup update script (`git submodule update --init --recursive`).
