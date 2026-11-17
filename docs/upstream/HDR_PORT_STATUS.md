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
