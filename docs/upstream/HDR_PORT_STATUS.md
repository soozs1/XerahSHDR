# HDR port to upstream v0.34.1 — status

Goal: carry the HDR work of this fork (`v0.24.18` + 80 commits) onto the official
`ShareX/XerahS` release **v0.34.1**, so it can eventually be proposed upstream as a patch.

## Method

1. The local history was imported without ancestry, so the fork point was recorded with
   `git replace --graft 34736625 v0.24.18` (upstream tag `v0.24.18` = `4bbb97a`).
2. Remote `upstream = https://github.com/ShareX/XerahS`, fetched with
   `git -c submodule.recurse=false fetch --tags --no-recurse-submodules upstream develop`
   (submodule recursion fails: the submodule remotes do not contain the parent's objects).
3. A scratch worktree holds the target release: `git worktree add --detach .upstream v0.34.1`
   (excluded through `.git/info/exclude`).
4. The patch is the HDR file set of `git diff v0.24.18..HEAD` (4161 lines), applied with
   `git apply --3way` inside `.upstream`.

## Result

### Applied cleanly (16 files)

| File | Kind |
|---|---|
| `src/desktop/core/XerahS.Common/Helpers/HdrContentAnalyzer.cs` | new |
| `src/desktop/core/XerahS.Common/Helpers/HdrSdrWhiteNormalizer.cs` | new |
| `src/desktop/core/XerahS.Common/Helpers/ImageHelpers.cs` | modified (16-bit encoders) |
| `src/desktop/core/XerahS.Core/Helpers/HdrTonemapper.cs` | new |
| `src/desktop/core/XerahS.Core/Helpers/HdrTonemapVariants.cs` | new |
| `src/platform/XerahS.Platform.Abstractions/HdrToneMappingMode.cs` | new |
| `src/platform/XerahS.Platform.Windows/Capture/DisplaySdrWhiteLevelHelper.cs` | new |
| `src/platform/XerahS.Platform.Windows/Capture/DxgiHdrDuplicationHelper.cs` | new |
| `src/desktop/app/XerahS.UI/Diagnostics/HdrSelfTestRunner.cs` | new |
| `src/desktop/core/XerahS.Core/Models/TaskSettings.cs` | modified (HDR settings) |
| `src/desktop/core/XerahS.Common/Enums.cs` | modified |
| `src/desktop/app/XerahS.App/Program.cs` | modified (self-test entry) |
| `src/desktop/app/XerahS.UI/Services/MainViewModelHelper.cs` | modified (Save-As depth) |
| `src/desktop/app/XerahS.UI/Services/WorkflowOrchestrator.cs` | modified (tonemap hook) |
| `src/desktop/app/XerahS.UI/ViewModels/TaskSettingsViewModel.Image.cs` | modified |
| `src/desktop/app/XerahS.UI/Views/TaskImageSettingsPanel.axaml` | modified |

### Conflicts resolved by hand (2)

- **`TaskHelpers.cs`** — upstream added FFmpeg-based external encoders (`IsExternallyEncodedFormat`,
  `TryEncodeExternally`) and SEC-18 path containment. Resolution keeps all three: external encoder
  first, then the HDR/16-bit branch, then the path check, and passes `ImagePNGBitDepth` through.
- **`CaptureJobProcessor.cs`** — upstream switched the after-capture window to a privacy-aware
  overload. Resolution keeps upstream's call and drops our tone-mapping argument (see gap 3).

### Cannot be merged (1)

- **`WindowsModernCaptureService.cs`** — upstream reduced it from 823 to 66 lines and moved the
  DXGI pipeline to `Capture/Backends/DxgiDesktopDuplicationBackend.cs`, which tone maps HDR to
  8-bit at capture time through `DxgiHdrToneMapper` / `HdrToneMapContext`. Our version preserves
  fp16 end-to-end, so the capture side needs a **rework on top of their backend**, not a merge.
  The file was left at the upstream version in the scratch worktree.

## Gaps to close before this can build or be proposed

1. **Capture integration.** Add the SDR-white normalization (`DisplaySdrWhiteLevelHelper` +
   `HdrSdrWhiteNormalizer.NormalizeToSdrWhite`) inside `DxgiDesktopDuplicationBackend` before the
   tone mapper runs, and add an opt-in fp16 path if 16-bit output is to be kept.
2. **Localization.** `TaskImageSettingsPanel.axaml` from this fork uses `{local:Localize ...}`
   (8 usages). Upstream has no `LocalizeExtension` (`grep -c "local:Localize"` on upstream's
   `ApplicationSettingsView.axaml` = 0), so the panel must be converted back to plain strings
   (or upstream's mechanism) before it compiles there.
3. **After-capture preview tone mapping.** `IUIService.ShowAfterCaptureWindowWithPrivacyAsync` has
   no tone-mapping parameter; upstream's window ignores the selected curve. Porting it needs an
   interface change across `IUIService`, `AvaloniaUIService` and the headless services.
4. **Submodules.** Upstream v0.34.1 dropped `ShareX.VideoEditor` and added `Omacut`
   (`KovaForge/omacut`) and `native/omasnap`; neither is initialized in the scratch worktree, so
   nothing compiles there yet.
5. **Tests.** The HDR test files were not part of this patch set; they need the same treatment and
   a `dotnet test` run on the ported tree.

## What upstream already has (do not duplicate)

| Area | Upstream | This fork |
|---|---|---|
| HDR capture | `Capture/Backends/DxgiDesktopDuplicationBackend.cs`, `DxgiHdrToneMapper`, `DxgiOutputDuplicationPolicy` | fp16 preserved end-to-end |
| HDR screenshots | `HdrScreenshotColorCorrector.cs` — GDI frame + tone-mapped HDR highlights composited on top | full-frame fp16 capture |
| Output depth | 8-bit only | 8-bit SDR and 16-bit "SDR+HDR" PNG |
| Tone curves | fixed mapping in the tone mapper | None / Reinhard / ACES / BT.2446C / Hable, selectable per destination |
| SDR white | not queried; `1.0 == 80 cd/m²` assumed | `DISPLAYCONFIG_SDR_WHITE_LEVEL` per output, frame normalized to it |
| Verification | none | `--hdr-self-test` / `--hdr-capture` with numeric report |

## Expanded port: localization and send-to policies

The port covers everything this fork has that v0.34.1 does not, not just HDR.

### What upstream v0.34.1 actually has

- Localization exists only for statistics strings: `src/desktop/app/XerahS.UI/Localization/StatisticsStrings.*.resx`
  (29 files). There is no app-wide resource file, no XAML markup extension, and
  `grep -c "local:Localize"` is 0 outside the patched panel.
- Send-to policies exist as UI; our `SendToPolicyResolver` (default policy per destination +
  batch confirmation) is an addition.

### Localization patch

`git diff v0.24.18..HEAD` over the 79 files touched by the localization commits
(84c73555, 6ba2b088, 439d77db, 9c1cda0a, 6c5fa86c, bf183737, c43ea028, 16374d03, 6addc85e,
788ef2c8, 18f78258, 62c33b80, 8ff6e720, a91ca4ff, 43ab2866, e8f36788, 28c8f015, 26bbb73c,
2d9eba20, cefb8605) = 24 594 lines.

| Outcome | Files |
|---|---|
| Applied (new infrastructure, resx, view models, non-conflicting XAML) | 47 |
| Refer to files upstream deleted (onboarding redesign, `HotkeySettingsView`, `ProviderExplorerViewModel`) | 10 |
| Conflicted and left at the upstream version | 22 |

The conflicted files are the ones where upstream changed the same lines: plain strings we wrapped in
`{local:Localize ...}`, and one Avalonia rename (`Watermark` → `PlaceholderText`). Resolving them
needs a build loop, because a wrong pick silently drops either upstream's UI change or our
translation. They were therefore reverted to upstream and recorded here instead of guessed at:

```
App.axaml, App.axaml.cs, Controls/FileTypeSelectorControl.axaml,
Onboarding/OnboardingWizardWindow.axaml, Services/OcrToolService.cs,
Services/PinToScreenToolService.cs, Services/ScrollingCaptureToolService.cs,
TrayIconHelper.cs, ViewModels/RecordingViewModel.cs, ViewModels/SendToPromptViewModel.cs,
ViewModels/SettingsViewModel.Integration.cs, ViewModels/WorkflowsViewModel.cs,
Views/AfterCaptureWindow.axaml, Views/ApplicationSettingsView.axaml, Views/DebugView.axaml,
Views/Dialogs/OpenImageChoiceDialog.axaml, Views/FFmpegOptionsWindow.axaml,
Views/ImageEffectsBrowserDialog.axaml, Views/MainWindow.axaml,
Views/PinToScreenStartupDialog.axaml, Views/SendToPromptWindow.axaml, Views/TaskSettingsPanel.axaml
```

### Localization conflicts — resolved

All 22 conflicting views were resolved by keeping **upstream's** version: every one of them is a file
upstream restructured (new after-capture window, onboarding redesign, send-to API, dialog commands),
so merging our localized variant line-by-line would either drop upstream behaviour or break their
bindings. The tree is free of conflict markers and builds; the recipe to restore translations in
these views is to localize upstream's current text against the existing `Resources.resx` keys:

```
App.axaml, App.axaml.cs, Controls/FileTypeSelectorControl.axaml,
Onboarding/OnboardingWizardWindow.axaml, Services/OcrToolService.cs,
Services/PinToScreenToolService.cs, Services/ScrollingCaptureToolService.cs,
TrayIconHelper.cs, ViewModels/RecordingViewModel.cs, ViewModels/SendToPromptViewModel.cs,
ViewModels/SettingsViewModel.Integration.cs, ViewModels/WorkflowsViewModel.cs,
Views/AfterCaptureWindow.axaml, Views/ApplicationSettingsView.axaml, Views/DebugView.axaml,
Views/Dialogs/OpenImageChoiceDialog.axaml, Views/FFmpegOptionsWindow.axaml,
Views/ImageEffectsBrowserDialog.axaml, Views/MainWindow.axaml,
Views/PinToScreenStartupDialog.axaml, Views/SendToPromptWindow.axaml, Views/TaskSettingsPanel.axaml
```

Concrete upstream changes that forced this: `SendToPromptWindow` became a `UserControl`, the
send-to settings moved to `SendToFolderPolicySelectedIndex`, `OpenImageChoiceDialog` switched from
`Click` handlers to `Command` bindings, and `Watermark` was renamed to `PlaceholderText`.

### Capture integration — already upstream

Upstream queries the SDR white level itself: `DisplayConfigHelper.GetSdrWhiteScale(deviceName)` feeds
`HdrToneMapContext.SdrWhiteScale`, and `DxgiHdrToneMapper` divides by it before applying its curve.
That is the same fix this fork shipped as `DisplaySdrWhiteLevelHelper` +
`HdrSdrWhiteNormalizer`, so both files (and the unused `DxgiHdrDuplicationHelper`) were **removed**
from the port rather than kept as a second source of truth. What remains unique to this fork on the
capture path is the fp16-preserving pipeline, which is incompatible with upstream's 8-bit mapper.

### Ultra HDR JPEG output — not started

Still to do; see the plan below.

### Current state of the ported tree

- Worktree `.upstream` (detached at `v0.34.1`) holds four commits on top of the release, the latest
  being the cleanup above: **67 files, +53 194 / −589**, no conflict markers.
- **It builds:** `XerahS.UI` and `XerahS.App` compile on Linux with
  `-p:EnableWindowsTargeting=true -p:SkipBundlePlugins=true -p:EnableAppDrivenPluginBuild=false`.
  Building the UI is the meaningful gate here, because it compiles every ported XAML file.
- Compile fixes that were required (each was a genuine semantic gap, not a mechanical merge):
  - `FileHelpers.IsPathWithinDirectory` — SEC-18 helper referenced by the ported `TaskHelpers`.
  - `SecretMasker` — used by `CaptureJobProcessor`, lived only in this fork.
  - `SettingsSerializationBinder` — lives only in this fork.
  - `Localization.cs` — culture switching (`ApplyCulture`) behind the language selector.
  - `SavePathDebugDump` — Save-As diagnostics referenced by `MainViewModelHelper`.
  - `BitmapConversionHelpers.ConvertF16ToSrgb8` lives in the editor **submodule**; the self-test now
    uses `ImageHelpers.ConvertF16ToSrgb8` instead. The preview path in
    `BitmapConversionHelpers.ToAvaloniBitmap` still needs the submodule change.
  - `CaptureJobProcessor` used `result.ToneMapping` from the after-capture window; upstream's result
    tuple has no such field, so the configured mode is used (gap 3).
  - `SettingsViewModel.cs` reverted to v0.34.1: this fork and upstream have **different** send-to
    implementations (`SendToDefaultFolderPolicy` observables vs `SendToFolderPolicySelectedIndex`).
    Upstream's is kept; our send-to UI work is dropped from the port.
- Present and verified by path: `Markup/LocalizeExtension.cs`, `XerahS.Common/Resources.resx`,
  `XerahS.Core/Helpers/HdrTonemapper.cs`, `XerahS.Core/SendTo/SendToPolicyResolver.cs`,
  the HDR helpers and `Diagnostics/HdrSelfTestRunner.cs`.
- `WindowsModernCaptureService.cs` stays at the upstream version (see above).

### Remaining work on the expanded port

1. Re-apply localization to the 22 views listed above (upstream structure, our resource keys).
2. Re-apply localization to the 10 files that upstream replaced (new onboarding, hotkey view).
3. Decide whether to keep upstream's 8-bit capture or reintroduce the fp16 path; upstream's SDR
   white handling can be reused either way.
4. Add Ultra HDR JPEG output for the "SDR+HDR" depth (the only container that carries real HDR on
   Windows).
5. Runtime verification on Windows: `--hdr-self-test`, `--hdr-capture`, Save-As depth behaviour, and
   the localized UI.

## Recommended patch scope for the author

**A. Small and reviewable (propose first)**

1. `DisplaySdrWhiteLevelHelper` + `HdrSdrWhiteNormalizer` — normalizing captured scRGB frames to the
   OS SDR white level. Without it, SDR content clips and every tone curve anchors at the wrong pivot.
2. Display-referred 16-bit PNG encoding (`ImageHelpers.SaveSdr16Png`, `SaveBitmap` depth handling)
   plus the "depth applies to every source" rule.
3. The effectively-SDR bypass in the tone mapper (no curve when peak/white ≤ 1.05), verified against a
   GDI screen grab to 0.000 per region.

**B. Larger, depends on A**

4. Selectable tone curves (UI + settings + resx).
5. `--hdr-self-test` / `--hdr-capture` harness with the numeric report.

## Reproducing

```bash
git replace --graft 34736625 v0.24.18
git remote add upstream https://github.com/ShareX/XerahS.git
git -c submodule.recurse=false fetch --tags --no-recurse-submodules upstream develop
git worktree add --detach .upstream v0.34.1
git diff v0.24.18..HEAD -- <HDR files> > docs/upstream/hdr-port-v0.24.18.patch
git -C .upstream apply --3way ../docs/upstream/hdr-port-v0.24.18.patch
```
