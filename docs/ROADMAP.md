# XerahS - Project Status and Roadmap

## Current Status (Updated 2026-08-14)

Core desktop workflow (capture -> annotate/edit -> save/copy/upload) is implemented and active.

This roadmap has been reconciled against the current codebase, including the `ImageEditor` submodule (`ShareX.ImageEditor`) and current release/build automation.

## Completed Highlights

- [x] Reimagined desktop UI and workflow-based capture pipeline
- [x] Region capture annotation overlay backed by `ShareX.ImageEditor` types
- [x] Annotation toolset and image effects integrated through the editor submodule
- [x] Smart Eraser drawing path and color sampling logic implemented
- [x] Export flow: copy to clipboard, save, save-as, and upload-to-host task routing
- [x] Dynamic uploader/plugin architecture with provider catalog and instance manager
- [x] File-type routing for upload destinations
- [x] System tray integration with recording-aware state
- [x] Multi-platform release packaging workflow (Windows, Linux x64/arm64, macOS x64/arm64)
- [x] App icon assets and packaging scripts for desktop platforms

## ImageEditor Integration Status

- [x] `ShareX.ImageEditor` is referenced by UI/Core/RegionCapture projects
- [x] Theme synchronization between XerahS app theme and editor theme manager
- [x] Legacy image-effect preset namespace compatibility (`ShareX.Editor`/`XerahS.Editor` -> `ShareX.ImageEditor`)
- [x] Region capture overlay compatibility toolbar (`XerahS.RegionCapture.UI.Controls.AnnotationToolbar`) kept locally for upstream parity gaps
- [ ] Full regression parity test coverage for prior in-repo editor behavior (legacy editor tests are currently excluded in `tests/XerahS.Tests/XerahS.Tests.csproj`)

## Roadmap

### Phase 7: Stabilization and Distribution (Current)

- [x] Export and destination integration
- [x] Clipboard copy path through platform services
- [x] Quick save and save-as flows
- [x] Upload-to-host integration with provider selection/fallback pipeline
- [x] Distribution baseline (icons/assets, tray, release workflows)
- [ ] End-to-end verification matrix for annotation tools across desktop platforms
- [ ] End-to-end verification matrix for image effects across desktop platforms
- [ ] Native app interoperability validation (copy/paste in external apps)
- [x] Linux packaging and Linux capture/hotkey orchestration test coverage
- [ ] macOS on-device validation for capture/hotkeys/clipboard/permissions
- [ ] macOS platform completion for remaining stubs/TODOs (ScreenCaptureKit strategy internals, window service gaps, OCR implementation)
- [x] Restore or replace editor regression scenarios removed during upstream ImageEditor swap
- [x] Complete remaining after-upload automation gaps (URL shortener and related tasks)

### Phase 8: Security Hardening (Planned)

Full plan: [Security Remediation Plan (August 2026)](planning/SECURITY_REMEDIATION_PLAN_AUG_2026.md). Findings are tracked as `SEC-NN`.

- [x] Phase 0 (emergency): constrain settings deserialization types with the existing `KnownTypesSerializationBinder` (SEC-01, SEC-02)
- [x] Phase 0: scheme allowlist for all `ISystemService.OpenUrl` paths + fix `IsValidURL` bypasses (SEC-04)
- [x] Phase 0: reject plugin `ProviderId` collisions/mismatches; scope `ISecretStore` per provider (SEC-05)
- [x] Phase 0: MCP server on loopback by default, key moved to `SecretStore`, constant-time comparison (SEC-06)
- [x] Phase 1: secret encryption decision (Option B: `SecretStore` migration) and backup-zip leak fix (SEC-03)
- [x] Phase 1: settings validation limits + fail-loud loading (SEC-12)
- [x] Phase 1: FFmpeg/external-process argument safety via `ProcessArgumentQuoter` (SEC-08, SEC-15)
- [x] Phase 1: custom uploader hardening — scheme allowlist, response size cap, regex timeout, single validation choke point (SEC-09)
- [x] Phase 1: mobile deep-link/share import requires no auto-activation (SEC-10)
- [x] Phase 2: FFmpeg/update download integrity (hash pins + Authenticode), macOS Keychain via ArgumentList, plugin `.deps.json` containment, remaining LOW items (SEC-07, SEC-11, SEC-13/14/16-20)

### Distribution and Packaging (Community Issues)

Windows package managers (manifests already exist under `build/windows/`; publication is the remaining work):

- [ ] WinGet: submit/refresh manifests to `microsoft/winget-pkgs` (template: `build/windows/winget/manifests/`, generator: `generate-winget.ps1`); consider automation via `wingetcreate` on release
- [ ] Scoop: host a bucket (e.g. `ShareX/xerahs-scoop`) or submit to `ScoopInstaller/Extras`; keep `build/windows/scoop/scoop.json` synced per release
- [ ] Chocolatey: register on the Community Repository, add `CHOCO_API_KEY` secret, automate the existing `push_package` step of `package-chocolatey.yml` on release

Linux repositories (auto-updates; deb/rpm/flatpak are already built per release):

- [ ] Publish on Flathub (flatpak bundle is already produced by `release-build-all-platforms.yml`) — highest-leverage Linux channel
- [ ] Evaluate Open Build Service (OBS) as a single source for deb/rpm repos (Ubuntu/Debian, Fedora, openSUSE) instead of separate PPA/COPR
- [ ] Keep the community AUR package `xerahs-git` linked in README; consider an official versioned `xerahs` AUR package

### Send-to Polish (post-XIP0057)

XIP0055/XIP0057 shipped the full Send-to behavior control (remembered choices, folder policies, naming parity, batch policies). Remaining polish:

- [x] Replace the remembered large-image-batch toast stop with an interactive confirmation dialog when the app can present one safely
- [x] Make default folder/batch policies editable in settings (today they are initialized to safe values; settings surface only lists/clears remembered rules)

### HDR Capture (Planned)

Goal: HDR-aware screenshots on Windows, in two variants: (1) save/copy true HDR content, (2) HDR-to-SDR conversion. Current state: all capture backends report `SupportsHDR = false`; `DxgiCaptureStrategy` uses DXGI 1.2 `IDXGIOutput1.DuplicateOutput` (8-bit only); `WinRTCaptureStrategy` is a stub falling back to GDI.

- [x] Design doc / XIP: choose capture path — DXGI 1.5 `IDXGIOutput6.DuplicateOutput1` (fp16 scRGB) vs Windows.Graphics.Capture; define HDR clipboard strategy (CF_DIBV5 is 8-bit; decide between HDR-capable format on disk and tonemapped clipboard copy)
- [x] Phase 1: HDR frame acquisition on HDR-capable displays (fp16 scRGB pipeline) + capability detection (`SupportsHDR` reporting, per-monitor)
- [x] Phase 2: HDR output — save to disk in an HDR-capable container (candidates: JPEG XR / AVIF / 16-bit PNG with color metadata), copy-to-clipboard behavior per design doc
- [x] Phase 3: HDR-to-SDR conversion (tonemapping with display metadata: BT.2020/PQ -> sRGB), selectable in after-capture
- [ ] Note: annotation/editor pipeline (`ShareX.ImageEditor`) is 8-bit; HDR content routed into the editor must be tonemapped first until the editor gains wide-gamut support

### Localization (Planned)

Goal: localize the desktop UI into the first tranche of languages after HDR lands. Scope: zh-CN (Chinese), ru (Russian), de (German), es (Spanish).

- [x] Audit hardcoded UI strings (XAML, view models, tooltips, tray menu, notifications) and extract them into resource files (`resx` or Avalonia `ResourceDictionary`); keep English as the fallback invariant language
- [x] Wire culture switching (app-level `CurrentUICulture` + restart or live retheming) and persist the user's language choice in settings
- [x] Translate the four target locales — zh-CN, ru, de, es;
- [x] Localize date/number formatting where culture-sensitive (history timestamps, file sizes, upload dates)
- [ ] Add a QA pass for text overflow / layout breakage in RTL-adjacent or long-word locales (de compound words, ru/zh string growth)

## Known Issues / Notes

- Editor regression suites were restored during the upstream `ShareX.ImageEditor` swap and now exercise the live canvas history contract (`tests/XerahS.Tests/Editor/EditorHistoryEffectsTests.cs`, `tests/XerahS.Tests/Editor/EditorRotateAnnotationsTests.cs`).
- macOS platform layer still contains MVP stubs and TODOs in capture/window/OCR paths (for example `src/platform/XerahS.Platform.MacOS/Capture/ScreenCaptureKitStrategy.cs` and `src/platform/XerahS.Platform.MacOS/MacOSWindowService.cs`).
- After-upload URL shortener automation is implemented in `src/desktop/core/XerahS.Core/Tasks/Processors/UploadJobProcessor.cs`.
