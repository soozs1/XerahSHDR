# HDR Capture Plan (Windows)

**Status**: Design complete; Phase 1 planned (no code changed yet)
**Date**: 2026-08-08
**Baseline version**: v0.24.18
**Source**: Codebase review of `src/platform/`, `src/desktop/core/`, `src/desktop/app/XerahS.UI/`,
`tests/XerahS.Tests/`; API verification against `Vortice.DXGI 3.8.3` and `SkiaSharp 4.151.0`
(reflection + runtime probes, see §2.4)
**Related**: `docs/ROADMAP.md` §"HDR Capture (Planned)" (L84–94), `docs/architecture/xerahs_architecture_map.md`

## Execution status

| Phase | Verdict | Notes |
|-------|---------|-------|
| Design | ✅ Complete (2026-08-08) | Capture path, capability detection, output format, tonemapping approach, risks, gates — this document |
| Phase 1 — HDR acquisition | ⏳ Planned | fp16 scRGB pipeline + per-monitor `SupportsHDR` detection |
| Phase 2 — HDR output | ⏳ Planned | 16-bit PNG save path (verified working); AVIF-via-FFmpeg stretch goal |
| Phase 3 — HDR→SDR tonemapping | ⏳ Planned | PQ/BT.2020 → sRGB, selectable in after-capture |

---

## 1. Status & Goal

### 1.1 Current state (verified)

| Area | Current state | Location |
|------|---------------|----------|
| DXGI strategy | `IDXGIOutput1.DuplicateOutput` (DXGI 1.2), staging texture `Format.B8G8R8A8_UNorm`, `SKBitmap` `Bgra8888`, row copy via `BgraRowCopyHelper.CopyRows`, cursor via `DxgiCursorCompositionHelper` | `src/platform/XerahS.Platform.Windows/Capture/DxgiCaptureStrategy.cs` L169, L213, L236–251, L271–292 |
| Capabilities | `BackendCapabilities.SupportsHDR` exists but is `false` in every backend (`DxgiCapabilitiesHelper.Create()`) | `src/platform/XerahS.Platform.Abstractions/Capture/BackendCapabilities.cs` L44–48; `DxgiCapabilitiesHelper.cs` L29 |
| Monitor info | `MonitorInfo` has no HDR field; `BitsPerPixel = 32` hardcoded | `src/platform/XerahS.Platform.Abstractions/Capture/MonitorInfo.cs` L94; `DxgiCaptureStrategy.cs` L112 |
| Production capture | The app's wired screen-capture service is `WindowsModernCaptureService` (`IScreenCaptureService`, DXGI `DuplicateOutput`, combined `Bgra8888` virtual-desktop bitmap, then crop) — used by `Program.cs` L508+, `ShareXBootstrap` L220+, `WindowsPlatform` L40+ | `src/platform/XerahS.Platform.Windows/WindowsModernCaptureService.cs` L218+ (`CaptureFullScreenDxgi`), L331 (`DuplicateOutput`) |
| Region backend wiring | `WindowsRegionCaptureBackend`/`DxgiCaptureStrategy`/`WinRTCaptureStrategy`/`GdiCaptureStrategy` have **no instantiation sites** in `XerahS.UI` yet — the new backend stack is not user-visible today | grep `new WindowsRegionCaptureBackend` |
| WinRT strategy | Stub: `GetMonitors()`/`CaptureRegionAsync` delegate to GDI; `GetCapabilities()` reports GDI fallback | `WinRTCaptureStrategy.cs` L48–82 |
| Backend selection | DXGI → WinRT → GDI, decided once at construction | `WindowsRegionCaptureBackend.SelectBestStrategy()` L55–87 |
| Save pipeline | `SKImage.FromBitmap(bmp).Encode(...)`; TIFF/AVIF silently fall back to PNG; 8-bit only | `src/desktop/core/XerahS.Core/Helpers/TaskHelpers.cs` L576–609; `src/desktop/core/XerahS.Common/Helpers/ImageHelpers.cs` L51–63 |
| PNG depth setting | `PNGBitDepth` = Default/Automatic/Bit32/Bit24 — no 16-bit | `src/desktop/core/XerahS.Common/Enums.cs` L39–45 |
| Clipboard | `CF_DIBV5` writer/reader is 32-bpp BGRA only (`bV5BitCount == 32`, `LcsSrgb`); PNG also written. On Windows the app registers `WindowsClipboardService` as the `IClipboardService` (`App.axaml.cs` L257–263); `AvaloniaClipboardService` (Avalonia `IClipboard`) is the non-Windows variant | `ClipboardDibCodec.cs` L53–90, L149; `WindowsClipboardService.cs` L227–251 |
| Editor / preview | `ShareX.ImageEditor` and `AfterCaptureViewModel` preview are 8-bit | `src/desktop/app/XerahS.UI/ViewModels/AfterCaptureViewModel.cs` L49–58 |

### 1.2 Goal

On Windows, capture an HDR-capable display in either of two variants on user demand:

1. **True HDR capture** — save/copy the actual HDR content (high dynamic range + wide color gamut
   preserved, not clamped).
2. **HDR→SDR conversion** — tonemap the HDR frame to an SDR image suitable for the 8-bit editor,
   clipboard, and SDR-only viewers.

### 1.3 Two capture pipelines (important for Phase 1 scoping)

The repo has two parallel capture stacks. This plan's design (and the ROADMAP framing) targets the
`Capture/` backend stack (`WindowsRegionCaptureBackend` → `DxgiCaptureStrategy`), but the app's
*currently wired* capture path is `WindowsModernCaptureService` (`IScreenCaptureService`). Phase 1
must therefore include an explicit wiring decision:

- **Option 1 (recommended)**: implement fp16 in `DxgiCaptureStrategy` as designed below **and wire
  `WindowsRegionCaptureBackend` as the region-capture backend** (or port the fp16 logic into
  `WindowsModernCaptureService.CaptureFullScreenDxgi`), so the feature is user-visible.
- **Option 2**: implement fp16 only in `DxgiCaptureStrategy` — keeps the design contained but
  produces no end-user feature until the backend is wired.

This decision is tracked as `HDR-01` scope item in §4.5.

### 1.4 Scope

- **In scope**: Windows desktop (Windows 10 1703+ for the DXGI 1.5 path; the app already targets
  `net10.0-windows10.0.26100.0` and requires Windows 10 minimum). Still-image capture only,
  per §1.3 wiring decision.
- **Out of scope**: macOS (`ScreenCaptureKit`/`Quartz`), Linux (`X11`/`Wayland Portal`) — all
  backends stay `SupportsHDR = false`; this plan does not change them.
- **Out of scope**: screen recording (`FFmpegRecordingService`, `WindowsGraphicsCaptureSource`),
  legacy `WindowsScreenCaptureService` GDI path, scrolling capture. Noted in risks (§7.8) because
  the same color pipelines may be reused later.

---

## 2. Capture path decision

### 2.1 Option A — DXGI 1.5 `IDXGIOutput6` + `IDXGIOutput5.DuplicateOutput1` (recommended)

Native API: `IDXGIOutput5::DuplicateOutput1(pDevice, 0, SupportedFormatsCount,
pSupportedFormats, &duplication)` — requests a desktop duplication whose frames come back in one
of the caller-supplied formats. Requesting `DXGI_FORMAT_R16G16B16A16_FLOAT` yields **fp16 scRGB**
frames on HDR outputs (values exceed 1.0 for super-white, full BT.2020 gamut, PQ-coded by the
compositor).

Verified Vortice.DXGI 3.8.3 surface (reflection probe, `~/.nuget/packages/vortice.dxgi/3.8.3/`):

- `IDXGIOutput5.DuplicateOutput1(IUnknown device, Format[] supportedFormats)` → `IDXGIOutputDuplication`
  (also `Span<Format>` and explicit-count overloads). Throws on failure, like the existing
  `IDXGIOutput1.DuplicateOutput` call at `DxgiCaptureStrategy.cs` L316.
- `IDXGIOutput6.Description1` → `OutputDescription1` struct with fields `BitsPerColor`,
  `ColorSpace` (`ColorSpaceType`), `MinLuminance`, `MaxLuminance`, `MaxFullFrameLuminance`,
  plus `RedPrimary`/`GreenPrimary`/`BluePrimary`/`WhitePoint` arrays.
- `IDXGIOutput6.CheckHardwareCompositionSupport()` → `HardwareCompositionSupportFlags`.
- Enums live in the `Vortice.DirectX` assembly but are in the `Vortice.DXGI` namespace:
  `ColorSpaceType.RgbFullG2084NoneP2020 = 12` (HDR10/PQ), `Format.R16G16B16A16_Float = 10`.

**Correction to earlier assumptions**: `CheckColorSpaceSupport` is **not** on `IDXGIOutput` — it
exists on `IDXGISwapChain3` (for swap-chain HDR, not capture). The output-side HDR signal is
`IDXGIOutput6.Description1.ColorSpace` (plus `BitsPerColor`/`MaxLuminance`). `IDXGIOutput4`
has `CheckOverlayColorSpaceSupport(Format, ColorSpaceType, IUnknown)` but it is unnecessary for
Desktop Duplication.

Requirements: Windows 10 1703+ (Creators Update) — already satisfied; XerahS supports Windows 10.
No new dependencies, no CsWinRT involvement, synchronous readback like today.

### 2.2 Option B — Windows.Graphics.Capture (WinRT)

- The project already has CsWinRT infrastructure and a working WGC implementation **for screen
  recording**: `Microsoft.Windows.CsWinRT` is referenced in
  `src/platform/XerahS.Platform.Windows/XerahS.Platform.Windows.csproj`, and
  `src/platform/XerahS.Platform.Windows/Recording/WindowsGraphicsCaptureSource.cs` implements
  `Direct3D11CaptureFramePool.Create(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size)`
  (L252), `SoftwareBitmap.CreateCopyFromSurfaceAsync` (L431), `DispatcherQueue` marshaling (L87).
- HDR would mean `DirectXPixelFormat.R16G16B16A16Float` frame pools + `SoftwareBitmap` with a
  float pixel format, plus the same async frame-pump and STA/DQA plumbing.
- **Why not now**: still-capture via WGC is more moving parts than `DuplicateOutput1` (frame pool
  lifecycle, event-driven frames, WinRT interop on the capture thread), and the existing
  `WinRTCaptureStrategy` stub delegates to GDI anyway. WGC adds no capability that the DXGI 1.5
  path lacks for still images.

### 2.3 Recommendation

> **Adopt Option A (DXGI 1.5 `IDXGIOutput6`/`IDXGIOutput5.DuplicateOutput1`, fp16 scRGB) as the
> primary HDR capture path.** Keep the existing 8-bit pipeline untouched for SDR displays and as
> fallback. Track Option B (WGC) as a future path that reuses the recording-side
> `WindowsGraphicsCaptureSource` plumbing; do not implement it in these phases.

Fallback matrix (per monitor, decided in `GetMonitors()`):

| Monitor signal | Capture path |
|----------------|--------------|
| `ColorSpace == RgbFullG2084NoneP2020` (HDR10) | fp16 scRGB path (Phase 1) |
| SDR (`RgbFullG22NoneP709` or anything else) | existing 8-bit `DuplicateOutput` path — unchanged |
| DXGI unavailable / duplication init fails | GDI (unchanged) |

### 2.4 How the API/format claims in this document were verified

- Vortice.DXGI 3.8.3 method/enum surface: reflection probe against the package DLLs in
  `~/.nuget/packages/vortice.dxgi/3.8.3/` and `vortice.directx/3.8.3/` (see §2.1).
- SkiaSharp capacity claims (16-bit PNG, AVIF/JPEG-XL encoder availability, color-managed
  `ReadPixels`, PQ/2020 color space construction): runtime probe with a scratch console app using
  `SkiaSharp 4.151.0` (results cited in §5.1 and §6.2). The probe project was deleted after use.

---

## 3. Capability detection

### 3.1 Detection logic

New static helper `DxgiHdrDetectionHelper` (proposed:
`src/platform/XerahS.Platform.Windows/Capture/DxgiHdrDetectionHelper.cs`):

```
static bool TryGetHdrInfo(IDXGIOutput output, out bool isHdr, out HdrDisplayInfo info)
{
    using var output6 = output.QueryInterface<IDXGIOutput6>();   // may throw → return false
    OutputDescription1 desc1 = output6.Description1;
    isHdr = desc1.ColorSpace == ColorSpaceType.RgbFullG2084NoneP2020;
    // HdrDisplayInfo (record): BitsPerColor, MaxLuminance, MaxFullFrameLuminance,
    // RedPrimary/GreenPrimary/BluePrimary/WhitePoint
}
```

- `QueryInterface<IDXGIOutput6>` may fail on old drivers/WARP → treat as SDR (no exception).
- `IDXGIOutput6` availability implies Windows 10 1703+; on older Windows the query fails and the
  existing path is used.

### 3.2 Surface changes

| File | Change |
|------|--------|
| `src/platform/XerahS.Platform.Abstractions/Capture/MonitorInfo.cs` | Add `public bool IsHDR { get; init; }` (and optionally `public double MaxLuminanceNits { get; init; }`, `public double MaxFullFrameLuminanceNits { get; init; }` for Phase 3 metadata) |
| `src/platform/XerahS.Platform.Windows/Capture/DxgiCaptureStrategy.cs` | Populate `IsHDR` in `GetMonitors()` from `IDXGIOutput6.Description1` (L99–113 block); stop hardcoding `BitsPerPixel = 32` (L112) |
| `src/platform/XerahS.Platform.Windows/Capture/DxgiCapabilitiesHelper.cs` | `Create()` currently hardcodes `SupportsHDR = false` (L29). Change to `Create(MonitorInfo[] monitors)` (or an `IReadOnlyList<bool>`) and set `SupportsHDR = monitors.Any(m => m.IsHDR)`. Keep the parameterless overload for callers that cannot enumerate (e.g. tests) |
| `src/platform/XerahS.Platform.Windows/Capture/WindowsRegionCaptureBackend.cs` | `GetCapabilities()` (L108–112) already delegates to the strategy — no change needed once the DXGI strategy reports correctly |
| `RegionCaptureOptions` (`src/platform/XerahS.Platform.Abstractions/Capture/RegionCaptureOptions.cs`) | `ColorType` (SKColorType) already exists (L56) and defaults to `Bgra8888`. Add `public bool RequestHdr { get; init; }` (or treat `ColorType == RgbaF16` as the request) — Phase 1 hook |

### 3.3 Where the UI shows capabilities

`BackendCapabilities` flows: `IRegionCaptureBackend.GetCapabilities()` →
`RegionCaptureOrchestrator.GetCapabilities()` (`src/desktop/core/XerahS.Core/Services/RegionCaptureOrchestrator.cs`
L165–169) → `RegionCaptureService.GetCapabilities()` (`src/desktop/app/XerahS.UI/Services/RegionCaptureService.cs`
L133–139). Today this is **not rendered anywhere** in the UI (no `GetCapabilities()` call sites in
`XerahS.UI` views). Proposed UI surface for HDR status:

- Capture overlay / region window: an HDR badge next to the monitor / capture info when
  `SupportsHDR` is true and the selected monitor `IsHDR`.
- After-capture window: a small "(HDR)" label when the captured bitmap is `RgbaF16` and a
  tonemapping choice (Phase 3, §6.5).
- Debug/troubleshooting copy already available via `ToString()` on `BackendCapabilities`.

---

## 4. Phase 1 — HDR acquisition (fp16 scRGB pipeline)

Goal: on an HDR10 monitor, capture frames as `RgbaF16` `SKBitmap` with a linear extended-range
color space; everything else is unchanged.

### 4.1 Duplication setup (`DxgiCaptureStrategy.InitializeDuplication`, L294–334)

- Query `IDXGIOutput6` for detection; query `IDXGIOutput5` for duplication.
- If the monitor is HDR: `var dup = output5.DuplicateOutput1(device, new[] { Format.R16G16B16A16_Float });`
- If SDR: current `output1.DuplicateOutput(device)` — unchanged.
- Store the format choice per monitor in `DxgiMonitorContext` (`DxgiCaptureStrategy.cs` L404–418):
  add `public bool IsHdr { get; init; }` (or store the `Format`).

### 4.2 Readback (`CaptureRegionInternal`, L154–267)

- Staging texture description (L207–218): `Format = Format.R16G16B16A16_Float` instead of
  `B8G8R8A8_UNorm` when HDR. Everything else (staging usage, `CopySubresourceRegion`, `Map`)
  stays identical.
- `SKBitmap` construction (L236–240): `SKColorType.RgbaF16`, `SKAlphaType.Premul`,
  `SKColorSpace.CreateSrgbLinear()` (verified present in SkiaSharp 4.151).
  - Note: scRGB is linear; `CreateSrgbLinear()` is the closest standard Skia color space with
    sRGB primaries and linear transfer, which matches DXGI scRGB values (R=1.0 ⇔ 80 cd/m²).
    `TODO: verify` — confirm the compositor's fp16 output is scRGB (linear sRGB primaries) vs
    BT.2020; if it is BT.2020-linear, use `SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Linear,
    SKColorSpaceXyz.Rec2020)` instead (both exist in 4.151).
- Row copy: `BgraRowCopyHelper.CopyRows` (L245–251) is format-agnostic (byte-pointer based);
  pass `bytesPerRow = sourceWidth * 8` (fp16 × 4 channels) instead of `sourceWidth * 4`.
  No change to the helper itself.
- Rotation: `BitmapRotationHelper.RotateClockwise` (`src/desktop/core/XerahS.Common/BitmapRotationHelper.cs`
  L36+) **hardcodes `SKColorType.Bgra8888`** for the result (L54). Add an overload that preserves
  the source `ColorType`/`ColorSpace` (or generalize to `source.Info`).
- `CapturedBitmap` (`CapturedBitmap.cs`) needs no change: it already exposes `ColorType` (L65) and
  carries the `SKBitmap` as-is.

### 4.3 Cursor composition (fp16)

`DxgiCursorCompositionHelper.TryCompositeCursor` (`DxgiCursorCompositionHelper.cs` L53–100) draws
a GDI 8-bit ARGB cursor onto the bitmap via `canvas.DrawBitmap`. On an F16 canvas:

- Skia color management converts the 8-bit sRGB cursor to the canvas' linear color space when
  drawing (F16 canvas + `SKColorSpace.CreateSrgbLinear()`), which is the correct linear-space
  add. `TODO: verify` — confirm the decoded cursor bitmap carries an sRGB color space in Skia 4.151
  (default for `SKBitmap.Decode`); if not, attach `SKColorSpace.CreateSrgb()` explicitly.
- Visual check: after tonemapping, the cursor must match the SDR-path cursor in brightness;
  a too-dark/too-bright cursor means the sRGB→linear conversion was skipped or applied twice.
  This is exactly what the manual gate in §8.4 checks.

### 4.4 SDR monitors and multi-monitor regions

- SDR monitor → existing 8-bit path, byte-for-byte unchanged.
- Cross-monitor region (HDR + SDR): `RegionCaptureOrchestrator.StitchCaptures`
  (`RegionCaptureOrchestrator.cs` L205–236) currently creates a `Bgra8888` result (L210–214).
  Phase 1 scope: **capture each monitor in its own format and tonemap-to-SDR before stitching**
  (i.e. the HDR part goes through the Phase 3 path, then stitches as 8-bit). True mixed
  fp16 stitching is deferred; note in risks (§7.4).

### 4.5 `SupportsHDR` wiring

- `DxgiCapabilitiesHelper.Create(monitors)` per §3.2.
- `WindowsRegionCaptureBackend` already selects DXGI first; no strategy swap needed — the DXGI
  strategy handles both HDR and SDR monitors internally.

### 4.6 Phase 1 acceptance criteria

- HDR monitor → `SKBitmap.ColorType == RgbaF16`, values > 1.0 for bright content, no clipping.
- SDR monitor → `Bgra8888`, identical output to today.
- `GetMonitors()[i].IsHDR` correct; `GetCapabilities().SupportsHDR == true` iff any HDR monitor.

---

## 5. Phase 2 — HDR output (save to disk + clipboard)

### 5.1 Format candidates (with SkiaSharp 4.151 results)

| Candidate | Status | Evidence / plan |
|-----------|--------|-----------------|
| **16-bit PNG** | ✅ **Works today** | Runtime probe: `SKImage.FromBitmap(RgbaF16).Encode(SKEncodedImageFormat.Png, 100)` emits IHDR `bitDepth=16, colorType=6` (RGBA) + `iCCP` (linear sRGB profile) + `sBIT`. Same for `Rgba16161616` and `Rgba1010102` sources. No new dependency, no encoder code. |
| AVIF | ❌ Encoder unavailable in SkiaSharp | `SKEncodedImageFormat.Avif` exists in the enum but `Encode(Avif, …)` returns `null` at runtime in 4.151 (decode-only). Would need an FFmpeg encode job (`FFmpegDownloader` exists at `src/desktop/core/XerahS.Common/FFmpegDownloader.cs`; download path used by `TaskSettingsViewModel.FFmpeg.cs` L176+) or a native libavif wrapper. Real HDR10 container (PQ/2020, 10-bit) — stretch goal. **Security constraint (SEC-07/SEC-08): any FFmpeg encode job MUST reuse the pinned `FFmpegDownloader` (SHA-256-verified download) and build arguments via `ProcessArgumentQuoter`/`ArgumentList` — never a raw `Process.Start` with interpolated args.** |
| JPEG XR | ❌ Not in SkiaSharp | No encoder/decoder in SkiaSharp. Dropped. |
| JPEG XL | ❌ Encoder unavailable | Enum has `Jpegxl` but `Encode` returns `null` in 4.151. Dropped for now. |

**Recommendation**: Phase 2 default = **16-bit PNG** for "true HDR" saves. It preserves the
fp16 values and the linear extended-range content; caveat: it is *not* a standard HDR10 display
container (PNG has no PQ/BT.2020 chunk standard, so most viewers render it as linear sRGB or
sRGB-gamma). It is the honest, dependency-free HDR-preserving format today. AVIF-via-FFmpeg is
the follow-up if true HDR10 files are required.

### 5.2 Save-path changes

- `EImageFormat` (`src/desktop/core/XerahS.Services.Abstractions/EImageFormat.cs`): no new member
  needed for the default. Add `PNGBitDepth.Bit16` (`src/desktop/core/XerahS.Common/Enums.cs` L39–45)
  so users can opt into 16-bit PNG explicitly for SDR captures too.
- `TaskHelpers.SaveImageAsStream` (`TaskHelpers.cs` L576–609): the existing
  `SKImage.FromBitmap(bmp).Encode(Png, 100)` already emits 16-bit PNG for an `RgbaF16` bitmap
  (verified). Required changes:
  1. Do **not** route HDR bitmaps through the JPEG/GIF/WEBP branches (they clamp to 8-bit);
     force PNG for `RgbaF16` input regardless of `ImageFormat` (or tonemap first — see Phase 3).
  2. Honor `PNGBitDepth.Bit16` by converting SDR sources to `Rgba16161616`/`RgbaF16` before encode.
- `ImageHelpers.SaveBitmap` (`src/desktop/core/XerahS.Common/Helpers/ImageHelpers.cs` L51–63):
  same `Encode` path — no change needed for fp16→PNG; verify `GetEncodedFormat` mapping (L263–275)
  stays PNG for `.png`.
- Upload path (`UploadJobProcessor.cs` L570–574) uses `SaveImageAsStream` — gains 16-bit PNG for
  free once the above is in.

### 5.3 Clipboard strategy

Current behavior (`WindowsClipboardService.SetImage`, L227–251): writes `CfPng` + `CF_DIBV5`
(32-bpp BGRA, `BI_BITFIELDS`, `LcsSrgb` — `ClipboardDibCodec.BuildDibV5` L53–90) + `CF_DIB`.
`DecodeDibV5` rejects any `bV5BitCount != 32` (L149).

**DIBV5 64-bpp/float?** Windows supports `bV5BitCount = 64` with `BI_BITFIELDS` and 16-bit/float
channel masks on paper, but consumers (Explorer thumbnails, Office, browsers, most apps) do not
render HDR DIBV5 correctly, and the current codec is hard-wired to 32-bpp. **Recommendation:
clipboard copies are always tonemapped to SDR (Phase 3) and written through the existing 8-bit
path.** The already-written `CfPng` entry is 8-bit today; extend `CfPng` to carry the 16-bit PNG
when the capture is HDR *in addition to* the tonemapped SDR DIBV5, so HDR-aware consumers can
read the HDR payload while everything else sees the SDR copy. On Windows the clipboard is
`WindowsClipboardService.SetImage` (`WindowsClipboardService.cs` L227–251); the non-Windows
`AvaloniaClipboardService` (`src/desktop/app/XerahS.UI/Services/AvaloniaClipboardService.cs`, Avalonia
`IClipboard` path) is unchanged since HDR is Windows-only.

---

## 6. Phase 3 — HDR→SDR tonemapping

### 6.1 Library reality

SkiaSharp has **no tone-mapping operator** (no Reinhard/ACES/display-mapping API). It does have
(verified in 4.151):

- `SKColorSpaceTransferFn.Pq` / `.Hlg` / `.Rec2020` / `.Srgb` / `.Linear`;
- `SKColorSpaceXyz.Rec2020` / `.Srgb`;
- `SKColorSpace.CreateRgb(SKColorSpaceTransferFn, SKColorSpaceXyz)` — builds a real HDR10 space;
- `SKPixmap.ReadPixels(SKImageInfo, IntPtr, …)` performs **color-managed conversion** between
  spaces (verified: a PQ/2020 pixel at ~1000 nits reads as linear sRGB ≈ 4.9–12.5 depending on
  peak, and clamps to 255 in sRGB 8888);
- `SKColorFilter.CreateSrgbToLinearGamma()` / `CreateLinearToSrgbGamma()` / `CreateColorMatrix(float[])`.

### 6.2 Recommended algorithm — manual pipeline (deterministic, unit-testable)

New helper `HdrTonemapper` (proposed: `src/desktop/core/XerahS.Core/Helpers/HdrTonemapper.cs`;
operates on raw F16 pixels, no Skia draw calls):

1. **PQ EOTF** (SMPTE ST 2084): `L = ((max(V^(1/m2) − c1, 0)) / (c2 − c3·V^(1/m2)))^(1/m1)`
   with the standard constants (m1 = 2610/16384, m2 = 2523/32, c1 = 3424/4096, c2 = 2413/128,
   c3 = 2392/128). V is the normalized fp16 value.
2. **Color primaries**: BT.2020 → sRGB linear matrix (3×3). (If the capture is scRGB as in §4.2,
   the matrix is identity-ish; keep the matrix explicit so either source works.)
3. **Tonemap curve** in linear space: default **Reinhard** `L' = L / (1 + L·k)` with
   `k = 1/peak` where `peak` comes from `OutputDescription1.MaxFullFrameLuminance`/`MaxLuminance`
   (fallback 1000 nits); **clamp `peak` to a sane range (e.g. (80, 10000)) and reject 0/NaN/Inf**
   so a malformed driver value cannot leak NaN/Inf into saved files; optional **ACES fitted**
   curve as an alternative preset.
   > **TODO HDR-02 (unit normalization, for Phase 2 integration):** the pixel values coming
   > from the fp16 capture are scRGB-linear where `1.0 == 80 cd/m²`, while `peak` is in nits.
   > `TonemapReinhard(L, peak)` must receive both in the same units — either convert pixels to
   > nits (`L_nits = L_scRGB * 80`) or normalize `peak` to scRGB units (`peak_scRGB = peak/80`)
   > before applying the curve, otherwise sub-peak HDR highlights map to ~255 with no visible
   > compression. Decide the normalization at the integration point and pin it with a numeric
   > regression test (e.g. 10.0 scRGB == 800 nits at peak 1000 must not saturate to 255).
4. **sRGB OETF** (standard piecewise) → 8-bit `Bgra8888` output.

Constants and the curve are pure functions → fully unit-testable (§8.2).

**Alternative hook (fallback/preview)**: `SKPixmap.ReadPixels` from the HDR10 space
(`CreateRgb(Pq, Rec2020)`) into `Bgra8888`/sRGB gives a color-managed but highlight-clamping
conversion — acceptable for preview, not for final output.

### 6.3 Where it hooks in

`CaptureJobProcessor.ProcessAsync` (`src/desktop/core/XerahS.Core/Tasks/Processors/CaptureJobProcessor.cs`
L62–156) is the single choke point. Current order: ShowAfterCaptureWindow (L67) → AddImageEffects
(L99) → AnnotateMedia (L120) → SaveImageToFile (L136) → CopyImageToClipboard (L143) →
UploadImageToHost (L152).

Insert **one** tonemap decision after capture and before the SDR-only steps:

```
if (image.ColorType == SKColorType.RgbaF16)
{
    if (mode == Auto && saveOnly)        → keep fp16 (Phase 2 writes 16-bit PNG)
    if (mode == Auto && copy/editor/etc) → tonemap to Bgra8888 once, reuse for all SDR steps
    if (mode == Tonemap)                 → tonemap immediately
    if (mode == None)                    → keep fp16 (clipboard/editor get clamped 8-bit — see risks)
}
```

The "keep fp16" branch must still hand the editor/clipboard a tonemapped copy when those steps
are selected; the "auto" mode does exactly that.

### 6.4 After-capture UI

- `AfterCaptureViewModel` (`src/desktop/app/XerahS.UI/ViewModels/AfterCaptureViewModel.cs`)
  already takes the `SKBitmap` and builds the preview via `BitmapConversionHelpers.ToAvaloniBitmap`
  (L53) — must receive the **tonemapped** bitmap (the ViewModel/UI stays 8-bit).
- Add a tonemapping selector to the after-capture window when the capture is HDR:
  `None / Auto / Tonemap (Reinhard) / Tonemap (ACES)` (new `ObservableProperty` on
  `AfterCaptureViewModel`, surfaced in `Views/AfterCaptureWindow.axaml`).
- Persist an override in `TaskSettingsImage` (`src/desktop/core/XerahS.Core/Models/TaskSettings.cs`
  L236–246): `public HdrToneMappingMode ImageHdrToneMapping = HdrToneMappingMode.Auto;` with a new
  enum in `XerahS.Common/Enums.cs`.

### 6.5 Defaults

| Action | Default |
|--------|---------|
| Save to file (HDR monitor) | keep HDR → 16-bit PNG (Auto) |
| Copy to clipboard | tonemap to SDR (Auto) |
| Open in editor / preview / effects | tonemap to SDR (Auto) |
| Upload | follows the image format; if format is 8-bit → tonemap first (note: 16-bit PNG upload is rejected or re-encoded by some hosts — compatibility, see RISK-07) |

Override via `ImageHdrToneMapping` in settings and per-capture in the after-capture window.

---

## 7. Risks & limitations

| ID | Risk | Mitigation / note |
|----|------|-------------------|
| RISK-01 | `ShareX.ImageEditor` and the Avalonia preview are 8-bit (`Bgra8888`); HDR routed into them untonemapped looks wrong | Tonemap before editor/preview (§6.3). Editor wide-gamut support is a separate future effort. |
| RISK-02 | Intermediate conversions lose color: fp16 → 8-bit → fp16 round-trips destroy super-white and gamut | Single conversion point in `CaptureJobProcessor`; never re-encode HDR content through 8-bit. |
| RISK-03 | fp16 readback is heavier than 8-bit (2× bytes, float rows) — full-screen 4K ≈ 66 MB/frame | Keep staging textures per-monitor; measure in Phase 1 gate; region capture limits size; `MaxCaptureResolution` stays 16384. |
| RISK-04 | Mixed HDR/SDR multi-monitor regions: stitching is 8-bit today | Phase 1 tonemaps the HDR slice before stitching (§4.4); true fp16 stitching deferred. |
| RISK-05 | Windows 10 vs 11 differences: `DuplicateOutput1` fp16 + `GetDesc1` only on 1703+; WARP/remote session quirks | `IDXGIOutput6` query fails → SDR fallback; document WARP behavior in gate notes. |
| RISK-06 | Driver behavior: some drivers ignore `DuplicateOutput1` format hints or return `B8G8R8A8` frames anyway | Runtime check of `OutduplDescription` (`IDXGIOutputDuplication.Description` — vortex `OutduplDescription.ModeDescription.Format`); fall back to 8-bit path if fp16 not honored. |
| RISK-07 | 16-bit PNG is not a standard HDR10 display container (no PQ chunk) | Documented limitation; AVIF-via-FFmpeg is the stretch goal for true HDR10 files. |
| RISK-08 | Screen recording (`FFmpegRecordingService`, `WindowsGraphicsCaptureSource`) out of scope but shares color-space concepts | Reuse the approved color-space helpers later; do not couple this plan to recording changes. |
| RISK-09 | Secure desktop / UAC: desktop duplication freezes frames there — no HDR impact | Existing behavior, unchanged. |
| RISK-10 | `CheckColorSpaceSupport` misdirection (it is swap-chain-only) | Documented in §2.1; detection uses `GetDescription1().ColorSpace`. |

---

## 8. Verification gates

Every phase exits only when:

1. **Build**: `dotnet build src/desktop/XerahS.sln -m:1` — 0 errors, 0 warnings
   (`TreatWarningsAsErrors` stays on; `net10.0-windows10.0.26100.0` TFM unchanged).
2. **Unit tests** (`dotnet test tests/XerahS.Tests/XerahS.Tests.csproj -m:1`), each phase adds:
   - **Phase 1**: `DxgiHdrDetectionHelper` parsing of a synthesized `OutputDescription1`
     (SDR vs `RgbFullG2084NoneP2020`, `BitsPerColor`, `MaxLuminance` values) — COM interfaces
     are not mockable, so test the pure parsing helper, not the COM call; `BgraRowCopyHelper`
     fp16 row copy (existing `BgraRowCopyHelperTests` pattern, `bytesPerRow = width*8`);
     `BitmapRotationHelper` F16-preserving overload (extend `BitmapRotationHelperTests`);
     `DxgiCapabilitiesHelper.Create(monitors)` aggregation.
   - **Phase 2**: synthetic fp16 bitmaps → `SaveImageAsStream` PNG → assert IHDR `bitDepth=16`
     (reuse the probe technique from §2.4); clipboard: `DecodeDibV5` still rejects 64-bpp input;
     `PNGBitDepth.Bit16` path.
   - **Phase 3**: `HdrTonemapper` on known PQ values (e.g. V=0.0 → 0 nits; V≈0.7518 → ~1000 nits
     before curve; Reinhard monotonicity; BT.2020→sRGB matrix on pure primaries; sRGB OETF
     round-trip at 0/0.5/1.0).
3. **Manual checks**:
   - HDR monitor (real hardware): capture bright HDR content → fp16 bitmap > 1.0; save → 16-bit
     PNG opens without clipping; copy → tonemapped SDR; editor shows tonemapped image.
   - SDR monitor: byte-identical behavior to baseline (no regression).
   - Mixed HDR+SDR region: tonemapped stitch, no artifacts.
   - WARP / remote desktop: falls back to SDR path without crash.
4. **Docs**: `docs/ROADMAP.md` phase checkboxes and this plan updated in the same PR as code.

**Phase 1 TODO-verify experiments** (mini-tests, run before Phase 1 closes):
- scRGB vs BT.2020 source primaries of the duplicated fp16 frames (§4.2).
- Cursor color space on F16 canvas (§4.3).
- `OutduplDescription` format check on real HDR hardware (§7 RISK-06).

---

## 9. Tracking

- **Roadmap**: `docs/ROADMAP.md` §"HDR Capture (Planned)" (L84–94) — check off items per phase.
- **Commit prefixes**: `HDR-01` (Phase 1 acquisition/detection/wiring), `HDR-02` (Phase 2 output),
  `HDR-03` (Phase 3 tonemapping), e.g. `[v0.25.0] [Feature] HDR-01: fp16 scRGB capture on
  HDR10 monitors`.
- **This document**: update the Execution status table at the top as phases land; record verdicts
  and verification results exactly like `SECURITY_REMEDIATION_PLAN_AUG_2026.md`.