# End-to-End Verification Matrix

Manual verification checklist for the desktop annotation toolset and image-effects pipeline across platforms. Record results per platform with `[x]` (pass) / `[ ]` (not verified) and note failures inline.

> The annotation tools and image effects live in the `ShareX.ImageEditor` submodule and are surfaced through XerahS's region-capture editor.

## Annotation Tools

| Tool | Category | Windows | Linux | macOS | Notes |
|---|---|---|---|---|---|
| Arrow | Shapes | [ ] | [ ] | [ ] | |
| Crop | Shapes | [ ] | [ ] | [ ] | |
| Cursor | Shapes | [ ] | [ ] | [ ] | |
| CutOut | Shapes | [ ] | [ ] | [ ] | |
| Ellipse | Shapes | [ ] | [ ] | [ ] | |
| Emoji | Shapes | [ ] | [ ] | [ ] | |
| Freehand | Shapes | [ ] | [ ] | [ ] | |
| Image | Shapes | [ ] | [ ] | [ ] | |
| Line | Shapes | [ ] | [ ] | [ ] | |
| Rectangle | Shapes | [ ] | [ ] | [ ] | |
| Smart Eraser | Shapes | [ ] | [ ] | [ ] | |
| Number | Text | [ ] | [ ] | [ ] | |
| Speech Balloon | Text | [ ] | [ ] | [ ] | |
| Text | Text | [ ] | [ ] | [ ] | |
| Blur | Effects | [ ] | [ ] | [ ] | |
| Highlight | Effects | [ ] | [ ] | [ ] | |
| Magnify | Effects | [ ] | [ ] | [ ] | |
| Pixelate | Effects | [ ] | [ ] | [ ] | |
| Spotlight | Effects | [ ] | [ ] | [ ] | |

Cross-cutting checks for each tool: draw/render, move/resize, color/thickness, undo/redo, and persistence through rotate/flip (see the editor regression tests in `tests/XerahS.Tests/Editor/`).

## Image Effects

The effect catalog is grouped by category. Each row should be verified for: apply → preview → confirm → export, plus undo/redo.

### Adjustments (31)

| Effect | Windows | Linux | macOS | Notes |
|---|---|---|---|---|
| Alpha | [ ] | [ ] | [ ] | |
| Auto Contrast | [ ] | [ ] | [ ] | |
| Black & White | [ ] | [ ] | [ ] | |
| Brightness | [ ] | [ ] | [ ] | |
| Channel Swap | [ ] | [ ] | [ ] | |
| Clarity | [ ] | [ ] | [ ] | |
| Color Balance | [ ] | [ ] | [ ] | |
| Colorize | [ ] | [ ] | [ ] | |
| Color Matrix | [ ] | [ ] | [ ] | |
| Contrast | [ ] | [ ] | [ ] | |
| Cross Process | [ ] | [ ] | [ ] | |
| Dehaze | [ ] | [ ] | [ ] | |
| Duotone Gradient Map | [ ] | [ ] | [ ] | |
| Exposure | [ ] | [ ] | [ ] | |
| Film Emulation | [ ] | [ ] | [ ] | |
| Gamma | [ ] | [ ] | [ ] | |
| Grayscale | [ ] | [ ] | [ ] | |
| Hue | [ ] | [ ] | [ ] | |
| Invert | [ ] | [ ] | [ ] | |
| Levels | [ ] | [ ] | [ ] | |
| Lomo | [ ] | [ ] | [ ] | |
| Polaroid | [ ] | [ ] | [ ] | |
| Posterize | [ ] | [ ] | [ ] | |
| Replace Color | [ ] | [ ] | [ ] | |
| Saturation | [ ] | [ ] | [ ] | |
| Sepia | [ ] | [ ] | [ ] | |
| Shadows & Highlights | [ ] | [ ] | [ ] | |
| Solarize | [ ] | [ ] | [ ] | |
| Split Toning | [ ] | [ ] | [ ] | |
| Temperature & Tint | [ ] | [ ] | [ ] | |
| Threshold | [ ] | [ ] | [ ] | |
| Vibrance | [ ] | [ ] | [ ] | |

### Drawings (13)

| Effect | Windows | Linux | macOS | Notes |
|---|---|---|---|---|
| Confetti | [ ] | [ ] | [ ] | |
| Draw Background | [ ] | [ ] | [ ] | |
| Draw Background Image | [ ] | [ ] | [ ] | |
| Draw Checkerboard | [ ] | [ ] | [ ] | |
| Draw Image | [ ] | [ ] | [ ] | |
| Draw Line | [ ] | [ ] | [ ] | |
| Draw Particles | [ ] | [ ] | [ ] | |
| Draw Shape | [ ] | [ ] | [ ] | |
| Draw Text | [ ] | [ ] | [ ] | |
| Fake Cursor | [ ] | [ ] | [ ] | |
| Gradient Overlay | [ ] | [ ] | [ ] | |
| Snowflakes | [ ] | [ ] | [ ] | |
| Text Watermark | [ ] | [ ] | [ ] | |

### Filters and Manipulations

Representative subset; the full catalog spans ~230 effect classes. Verify at least the following, then spot-check the remainder.

| Effect | Windows | Linux | macOS | Notes |
|---|---|---|---|---|
| Add Noise (Filter) | [ ] | [ ] | [ ] | |
| Anaglyph 3D (Filter) | [ ] | [ ] | [ ] | |
| Anamorphic Lens Flare (Filter) | [ ] | [ ] | [ ] | |
| Anime Speed Lines (Filter) | [ ] | [ ] | [ ] | |
| Flip / Rotate (Manipulation) | [ ] | [ ] | [ ] | |
| Resize / Canvas (Manipulation) | [ ] | [ ] | [ ] | |

## Platform Notes

- **Windows**: primary target; SkiaSharp native assets are `win-x64`.
- **Linux**: `linux-x64`; capture falls back to FFmpeg `x11grab`/portal where applicable.
- **macOS**: requires on-device validation (Screen Recording permission); capture paths still contain MVP stubs — see `docs/ROADMAP.md` Known Issues.
