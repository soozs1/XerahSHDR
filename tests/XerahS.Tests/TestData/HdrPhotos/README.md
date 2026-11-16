# HDR photo test data

Real HDR photographs (Radiance RGBE `.hdr`) used by `HdrPhotoVariantTests` to exercise the
HDR→SDR tone-mapping curves on genuine extended-range content (peaks 5–55× SDR white), unlike
screenshots which are usually fully SDR.

All files are **CC0 (public domain)** from [Polyhaven](https://polyhaven.com/hdris) — 1k HDRIs.

| file | source |
|---|---|
| `venice_sunset.hdr` | https://dl.polyhaven.org/file/ph-assets/HDRIs/hdr/1k/venice_sunset_1k.hdr |
| `the_sky_is_on_fire.hdr` | https://dl.polyhaven.org/file/ph-assets/HDRIs/hdr/1k/the_sky_is_on_fire_1k.hdr |
| `studio_small_09.hdr` | https://dl.polyhaven.org/file/ph-assets/HDRIs/hdr/1k/studio_small_09_1k.hdr |
| `dikhololo_night.hdr` | https://dl.polyhaven.org/file/ph-assets/HDRIs/hdr/1k/dikhololo_night_1k.hdr |
| `kloppenheim_06.hdr` | https://dl.polyhaven.org/file/ph-assets/HDRIs/hdr/1k/kloppenheim_06_1k.hdr |

The binaries are intentionally **not committed** (see `.gitignore` in this folder); re-download
them with `wget -O <name>.hdr <url>` (or any HTTP client) when running the photo diagnostics.

The test writes rendered variants to `C:\Users\<user>\Documents\XerahS\HdrPhotoVariants\<photo>\`
on Windows machines (WSL path `/mnt/c/Users/<user>/Documents/...`) for visual assessment.
