# HDR Capture Refactor Roadmap (Method B)

Status: In progress — Phases 1–3 implemented, Phase 4–5 pending
Scope: Windows screenshot capture + HDR→SDR pipeline
Supersedes: `HDR_CAPTURE_PLAN.md` §6.2 tonemapping math (the "peak-normalized" approach was wrong; it has been replaced by white-anchored curves).

---

## 0. Progress log

- ✅ Phase 1 — `DxgiHdrDuplicationHelper` created; `DxgiCaptureStrategy` delegates to it.
- ✅ Phase 2 — `WindowsModernCaptureService.CaptureFullScreenDxgi` uses fp16 duplication + `RgbaF16` combined bitmap when HDR.
- ✅ Phase 3 — `DrawMappedTextureToCanvas` handles F16 readback; `CaptureRectAsync` preserves F16 on crop.
- ✅ Phase 4a — removed unused `peakNits`/`ClampPeak`/`Peak*` from `HdrTonemapper` and call sites.
- ✅ Phase 4.5 — exposed PNG bit depth (`ImagePNGBitDepth`) in the Image Settings Quality card.
- ✅ Phase 4b — retired unused Windows region backend + strategies (`WindowsRegionCaptureBackend`, `DxgiCaptureStrategy`, `WinRTCaptureStrategy`, `GdiCaptureStrategy`, Windows `ICaptureStrategy`).
- ✅ Auto retired — all explicit tonemap modes now apply only for SDR-only tasks; `Auto` kept as a legacy enum value and hidden from UI.
- ⏳ Phase 5 — full validation (`dotnet test` + manual HDR on/off) — pending.

---

## 1. Objective

Make Windows **screenshot** captures produce `RgbaF16` (fp16 scRGB, `1.0 == 80 cd/m²`) whenever HDR is active, so the already-implemented HDR→SDR pipeline actually runs for `PrintScreen`, `ActiveWindow`, `Region`, etc.

Today every screenshot workflow produces `Bgra8888` (8-bit), so `CaptureJobProcessor` sees `isHdr == false` and skips all HDR tone-mapping. The HDR-capable capture code exists but is not wired into the running application.

---

## 2. Established facts (do not re-derive)

These were already diagnosed and confirmed from logs:

1. **The HDR→SDR tonemapper is now correct** (white-anchored). See `XerahS.Core/Helpers/HdrTonemapper.cs`.
   - Input is scene-referred scRGB linear, `1.0 == 80 cd/m² == SDR white`.
   - `TonemapReinhardNormalized` = `sqrt(L/(1+L))`.
   - `TonemapAcesNormalized` = `ACES(sqrt(L))`.
   - `TonemapBt2446C` re-anchors BT.2446 Method C so SDR white maps to `1.0`.
   - `None` is passthrough + clamp.
   - `peakNits` is **no longer used** by the curves (kept only for API compatibility).

2. **The capture path is the bottleneck.** Runtime logs show:
   ```
   CaptureFullScreenDxgi: ...
   [HDR] ProcessAsync: ColorType=Bgra8888, isHdr=False
   ```

3. **All screenshot modes funnel into one 8-bit method.**
   See `XerahS.Core/Tasks/Pipeline/CaptureStage.cs` (`ExecuteAsync`) and
   `XerahS.Platform.Windows/WindowsModernCaptureService.cs`.

4. **The HDR-capable code is dead code.** `WindowsRegionCaptureBackend`
   (which wraps `DxgiCaptureStrategy`) is never instantiated anywhere in the
   repository. `DxgiCaptureStrategy` contains the fp16 logic we need to extract.

---

## 3. Capture dispatch map (Windows screenshots)

| Workflow | `CaptureStage` call | Final implementation | HDR today |
|---|---|---|---|
| `PrintScreen` | `ScreenCapture.CaptureFullScreenAsync` | `WindowsModernCaptureService.CaptureFullScreenDxgi` | No (8-bit) |
| `ActiveWindow` | `ScreenCapture.CaptureActiveWindowAsync` | full-screen DXGI + crop | No |
| `CustomWindow` | `CaptureActiveWindowAsync` / `CaptureWindowAsync` | full-screen DXGI + crop | No |
| `RectangleRegion` / `RectangleTransparent` | `ScreenCapture.CaptureRegionAsync` | full-screen DXGI + crop | No |
| `ActiveMonitor` | `ScreenCapture.CaptureRectAsync` | full-screen DXGI + crop | No |
| `CustomRegion` / `LastRegion` | `ScreenCapture.CaptureRectAsync` | full-screen DXGI + crop | No |

`IScreenCaptureService` is registered in `XerahS.Platform.Windows/WindowsPlatform.cs` as `WindowsModernCaptureService` (when supported), else `WindowsScreenCaptureService` (GDI, 8-bit).

`IRegionCaptureBackend` (`WindowsRegionCaptureBackend` → `DxgiCaptureStrategy` with HDR) is a separate, unused backend.

Out of scope: screen recording (`WGC`/`FFmpeg`) and scrolling capture.

---

## 4. Design decision (Method B)

Extract the HDR/fp16 duplication logic from `DxgiCaptureStrategy` into a shared,
reusable helper, and use that helper inside the real full-screen capture path
(`WindowsModernCaptureService.CaptureFullScreenDxgi`).

Key idea: `CaptureFullScreenDxgi` already handles multi-monitor, rotation, and
cursor composition. We only need to make its per-output duplication and the
combined bitmap color-type HDR-aware. `SKCanvas` can draw `Bgra8888` source onto
an `RgbaF16` surface, which makes mixed HDR/SDR monitors tractable.

### 4.1 Shared helper (new file)

Create `XerahS.Platform.Windows/Capture/DxgiHdrDuplicationHelper.cs`:

- `static bool IsOutputHdr(IDXGIOutput output)`
  - Move logic from `DxgiCaptureStrategy.IsOutputHdr`.
  - True iff `IDXGIOutput6.Description1.ColorSpace == RgbFullG2084NoneP2020`.
  - Swallow QI/`GetDesc1` failures and return `false`.

- `static IDXGIOutputDuplication CreateDuplication(IDXGIOutput output, ID3D11Device device, out bool isHdr)`
  - If `IsOutputHdr`: `IDXGIOutput5.DuplicateOutput1(device, [R16G16B16A16_FLOAT])`.
    - Confirm `Description.ModeDescription.Format == R16G16B16A16_FLOAT`; otherwise dispose and fall back.
  - Else: `IDXGIOutput1.DuplicateOutput(device)` (8-bit).
  - On fp16 exception: log + 8-bit fallback (`isHdr=false`), matching RISK-06.

- Optionally: a helper to build the readback bitmap from a mapped staging box:
  - `static SKBitmap CreateReadbackBitmap(int w, int h, bool isHdr)` → `RgbaF16` or `Bgra8888`.
  - `static int BytesPerPixel(bool isHdr)` → `8` or `4`.

### 4.2 Integrate into `WindowsModernCaptureService.CaptureFullScreenDxgi`

Current state (8-bit only):

- `output.DuplicateOutput(device)` (plain 8-bit).
- `combinedBitmap = new SKBitmap(..., SKColorType.Bgra8888, ...)`.
- `DrawMappedTextureToCanvas(...)` hardcodes `Bgra8888` and `4` bytes/px.

Required changes:

1. Extend the per-output tuple (or a parallel structure) to carry `bool IsHdr`.
   `EnumerateOutputs` currently returns `(output, adapter, bounds, rotation, deviceName, dxgiRotation)`.
2. During duplication setup, call the shared `CreateDuplication(...)` and record `IsHdr`.
3. Compute `anyHdr = activeDuplications.Any(d => d.IsHdr)`.
4. Create `combinedBitmap` as:
   - `RgbaF16` + `SKColorSpace.CreateSrgbLinear()` when `anyHdr`;
   - `Bgra8888` otherwise (existing behavior).
5. In `DrawMappedTextureToCanvas`, make source bitmap color-type aware:
   - `isHdr ? RgbaF16 : Bgra8888`;
   - bytes-per-row/bytes-per-pixel `isHdr ? 8 : 4`.
   - Copying already happens via `BgraRowCopyHelper.CopyRows`; pass the correct
     `bytesPerPixel` (rename/generalize if the helper name is misleading, but it
     is just a row-copy primitive).

Note: for an SDR output drawn into an `RgbaF16` combined surface, create the
per-output source as `Bgra8888` and let `SKCanvas` convert on draw. This keeps
SDR content correct while HDR outputs contribute `>1.0` scRGB values.

### 4.3 Cursor composition

`DxgiCursorCompositionHelper.TryCompositeCursor(combinedBitmap, ...)` draws via
`SKCanvas`, so drawing onto an `RgbaF16` surface should work. Verify visually
that the cursor is not blown out/clamped when composited onto F16.

---

## 5. Phases

### Phase 1 — Extract shared helper
- Create `DxgiHdrDuplicationHelper.cs`.
- Move `IsOutputHdr` and the fp16 duplication/fallback logic out of `DxgiCaptureStrategy`.
- Keep `DxgiCaptureStrategy` compiling (delegate to the helper) so this phase is verifiable.
- Acceptance: helper compiles; no behavior change yet.

### Phase 2 — Wire HDR into the real full-screen path
- Modify `WindowsModernCaptureService.CaptureFullScreenDxgi` and `EnumerateOutputs`.
- Track `IsHdr` per output; choose `RgbaF16`/`Bgra8888` for the combined bitmap.
- Use the shared `CreateDuplication` for each output.
- Acceptance:
  - HDR active → `CaptureJobProcessor` logs `ColorType=RgbaF16, isHdr=True`.
  - HDR off / fp16 rejected → still `Bgra8888`, no regression.

### Phase 3 — Adapt readback + cursor for F16
- Make `DrawMappedTextureToCanvas` handle both `Bgra8888` and `RgbaF16`.
- Verify rotation and cursor composition on HDR output.
- Acceptance: multi-monitor + rotated output + cursor all correct on HDR.

### Phase 4 — Cleanup
- After verification, retire the unused `WindowsRegionCaptureBackend` /
  `DxgiCaptureStrategy` / `WinRTCaptureStrategy` if nothing else consumes them,
  or convert them to thin wrappers over the shared helper.
- Remove the now-unused `peakNits` parameter from
  `HdrTonemapper.TonemapToBgra8888` and the corresponding call sites, plus
  `ClampPeak` / `PeakMinNits` / `PeakMaxNits` if they become fully unused
  (keep `PeakDefaultNits` only if still needed by a default).
- Update `HDR_CAPTURE_PLAN.md` §6.2/§6.5 references to the white-anchored curves
  or link to this roadmap.

### Phase 4.5 — Expose PNG bit depth (HDR-quality save)

The save pipeline already honors `TaskSettingsImage.ImagePNGBitDepth == PNGBitDepth.Bit16`
(16-bit PNG), but the value was never exposed in the UI.

- Add `ImagePNGBitDepth` + `PngBitDepthOptions` to `TaskSettingsViewModel.Image.cs`.
- Add a "PNG bit depth" selector to the Quality card in `TaskImageSettingsPanel.axaml`.
- Localize `PNGBitDepth_Default` / `PNGBitDepth_Bit16`.
- Clarify: "save HDR quality" = `ImageHdrSaveToneMapping == None` (keep fp16 → 16-bit PNG)
  and/or `ImagePNGBitDepth == Bit16` (force 16-bit PNG).

### Phase 5 — Validation
- `dotnet test XerahS-develop/tests/XerahS.Tests --filter HdrToneMapping`
- Manual HDR-on capture:
  - log shows `ColorType=RgbaF16, isHdr=True`;
  - saved `None`/`Auto` output is 16-bit PNG (64-bit color) for HDR;
  - `Method C`/`Reinhard`/`ACES` produce visually distinct, non-crushed SDR;
  - editor HDR preview shows clear differences.
- Manual HDR-off capture: unchanged (8-bit SDR, `isHdr=False`).

---

## 6. Key files

- `XerahS-develop/src/platform/XerahS.Platform.Windows/WindowsModernCaptureService.cs` — main capture (8-bit today).
- `XerahS-develop/src/platform/XerahS.Platform.Windows/Capture/DxgiCaptureStrategy.cs` — HDR logic to extract (currently dead).
- `XerahS-develop/src/platform/XerahS.Platform.Windows/Capture/DxgiCapabilitiesHelper.cs` — `IsHdrColorSpace`.
- `XerahS-develop/src/platform/XerahS.Platform.Windows/WindowsPlatform.cs` — service registration.
- `XerahS-develop/src/desktop/core/XerahS.Core/Tasks/Pipeline/CaptureStage.cs` — workflow dispatch.
- `XerahS-develop/src/desktop/core/XerahS.Core/Helpers/HdrTonemapper.cs` — fixed tonemapper.
- `XerahS-develop/src/desktop/core/XerahS.Core/Tasks/Processors/CaptureJobProcessor.cs` — HDR→SDR pipeline + `[HDR] ProcessAsync` log.
- `XerahS-develop/src/desktop/app/XerahS.UI/ViewModels/TaskSettingsViewModel.Image.cs` — HDR preview.
- `XerahS-develop/src/desktop/app/XerahS.UI/Views/TaskImageSettingsPanel.axaml` — preview UI.
- `XerahS-develop/tests/XerahS.Tests/Helpers/HdrTonemapperTests.cs` — tonemapper tests.

---

## 7. Risks / open questions

- **Mixed HDR/SDR multi-monitor**: combined `RgbaF16` surface + canvas conversion for SDR outputs is the proposed approach; verify SDR colors are not shifted.
- **Driver rejects fp16** (`RISK-06`): must keep the 8-bit fallback path intact and logged.
- **F16 cursor compositing**: confirm cursor colors/alpha are correct on F16.
- **Rotation + F16**: `DxgiCaptureStrategy` already handles rotation with F16; reuse that behavior.

---

## 8. Do not redo

- Tonemapping curve design (already fixed).
- BT.2446 Method C implementation (already added).
- Workflow editor HDR preview (already added).
- The build/lock issue (environmental: build on a native NTFS path, not the WSL `Z:` mount).
