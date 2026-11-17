#region License Information (GPL v3)

/*
    XerahS - The Avalonia UI implementation of ShareX
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using NUnit.Framework;
using SkiaSharp;
using System.Buffers.Binary;
using XerahS.Common;
using XerahS.Core.Helpers;

namespace XerahS.Tests.Helpers;

public class HdrTonemapperTests
{
    // ──────────────────────────────────────────────────────────────────────
    // PQ EOTF (SMPTE ST 2084)
    // ──────────────────────────────────────────────────────────────────────

    [Test]
    public void PqEotf_Black_IsZero()
    {
        Assert.That(HdrTonemapper.PqEotf(0f), Is.EqualTo(0f).Within(0.001f));
    }

    [Test]
    public void PqEotf_MidTone_IsAroundExpectedNits()
    {
        // V=0.5 → ~92.2 cd/m²; the exact 100 cd/m² code value is V≈0.508078 (SMPTE ST 2084).
        Assert.That(HdrTonemapper.PqEotf(0.5f), Is.InRange(85f, 98f));
        Assert.That(HdrTonemapper.PqEotf(0.5080784191f), Is.EqualTo(100f).Within(1f));
    }

    [Test]
    public void PqEotf_OneThousandNits_CodeValue()
    {
        // V≈0.751827 → ~1000 cd/m² (HDR_CAPTURE_PLAN §8.2 gate).
        Assert.That(HdrTonemapper.PqEotf(0.751827f), Is.EqualTo(1000f).Within(1f));
    }

    [Test]
    public void PqEotf_White_Is10000Nits()
    {
        Assert.That(HdrTonemapper.PqEotf(1f), Is.EqualTo(10000f).Within(50f));
    }

    [Test]
    public void PqEotf_IsMonotonic()
    {
        float previous = -1f;

        for (int i = 0; i <= 100; i++)
        {
            float luminance = HdrTonemapper.PqEotf(i / 100f);
            Assert.That(luminance, Is.GreaterThanOrEqualTo(previous));
            previous = luminance;
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // sRGB OETF
    // ──────────────────────────────────────────────────────────────────────

    [Test]
    public void SrgbOetf_Endpoints()
    {
        Assert.That(HdrTonemapper.SrgbOetf(0f), Is.EqualTo(0f).Within(1e-6f));
        Assert.That(HdrTonemapper.SrgbOetf(1f), Is.EqualTo(1f).Within(1e-6f));
    }

    [Test]
    public void SrgbOetf_LinearSegmentBoundary()
    {
        // 0.0031308 is the piecewise breakpoint; the linear branch gives ≈0.04045 there.
        Assert.That(HdrTonemapper.SrgbOetf(0.0031308f), Is.EqualTo(0.04045f).Within(0.001f));
    }

    [Test]
    public void SrgbOetf_MidValue()
    {
        Assert.That(HdrTonemapper.SrgbOetf(0.5f), Is.EqualTo(0.7353f).Within(0.001f));
    }

    // ──────────────────────────────────────────────────────────────────────
    // TonemapReinhard
    // ──────────────────────────────────────────────────────────────────────

    [Test]
    public void TonemapReinhard_NearWhite_IsNotCrushed()
    {
        // L=1 with peak=1000 → 0.999001: virtually unchanged.
        Assert.That(HdrTonemapper.TonemapReinhard(1f, 1000f), Is.EqualTo(0.999f).Within(0.001f));
    }

    [Test]
    public void TonemapReinhard_CompressesHighLuminance()
    {
        // L=1000 with peak=1000 → 500: compressed well below the raw input.
        Assert.That(HdrTonemapper.TonemapReinhard(1000f, 1000f), Is.EqualTo(500f).Within(0.01f));
    }

    [Test]
    public void TonemapReinhard_IsMonotonic()
    {
        float previous = 0f;

        for (float linear = 0f; linear <= 1000f; linear += 10f)
        {
            float mapped = HdrTonemapper.TonemapReinhard(linear, 1000f);
            Assert.That(mapped, Is.GreaterThanOrEqualTo(previous));
            previous = mapped;
        }
    }

    [Test]
    public void TonemapReinhard_InvalidPeak_PassesThrough()
    {
        Assert.That(HdrTonemapper.TonemapReinhard(0.5f, 0f), Is.EqualTo(0.5f));
        Assert.That(HdrTonemapper.TonemapReinhard(0.5f, float.NaN), Is.EqualTo(0.5f));
        Assert.That(HdrTonemapper.TonemapReinhard(0.5f, float.PositiveInfinity), Is.EqualTo(0.5f));
        Assert.That(HdrTonemapper.TonemapReinhard(0.5f, float.NegativeInfinity), Is.EqualTo(0.5f));
    }

    // ──────────────────────────────────────────────────────────────────────
    // TonemapAces
    // ──────────────────────────────────────────────────────────────────────

    [Test]
    public void TonemapAces_Black_IsZero()
    {
        Assert.That(HdrTonemapper.TonemapAces(0f), Is.EqualTo(0f).Within(1e-6f));
    }

    [Test]
    public void TonemapAces_White_IsCompressed()
    {
        // ACES(1) = 0.803797: midtones are visibly compressed.
        Assert.That(HdrTonemapper.TonemapAces(1f), Is.EqualTo(0.8038f).Within(0.001f));
    }

    [Test]
    public void TonemapAces_HighLuminance_ClampsToOne()
    {
        Assert.That(HdrTonemapper.TonemapAces(100f), Is.EqualTo(1f).Within(1e-6f));
    }

    // ──────────────────────────────────────────────────────────────────────
    // TonemapBt2446C
    // ──────────────────────────────────────────────────────────────────────

    [Test]
    public void TonemapBt2446C_Black_IsZero()
    {
        Assert.That(HdrTonemapper.TonemapBt2446C(0f), Is.EqualTo(0f).Within(1e-6f));
    }

    [Test]
    public void TonemapBt2446C_SdrWhite_MapsToWhite()
    {
        // 1.0 scRGB == 80 nits == SDR reference white; the curve is re-anchored to 1.0.
        Assert.That(HdrTonemapper.TonemapBt2446C(1f), Is.EqualTo(1f).Within(0.001f));
    }

    [Test]
    public void TonemapBt2446C_HighLuminance_RollsAboveWhite()
    {
        // 12.5 scRGB == 1000 nits must exceed SDR white; the encoder clamps it to white.
        Assert.That(HdrTonemapper.TonemapBt2446C(12.5f), Is.GreaterThan(1f));
    }

    [Test]
    public void TonemapBt2446C_IsMonotonic()
    {
        float previous = 0f;

        for (float linear = 0f; linear <= 125f; linear += 1f)
        {
            float mapped = HdrTonemapper.TonemapBt2446C(linear);
            Assert.That(mapped, Is.GreaterThanOrEqualTo(previous));
            previous = mapped;
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // TonemapHable
    // ──────────────────────────────────────────────────────────────────────

    [Test]
    public void TonemapHable_Black_IsZero()
    {
        Assert.That(HdrTonemapper.TonemapHableNormalized(0f, 1f), Is.EqualTo(0f).Within(1e-6f));
    }

    [Test]
    public void TonemapHable_SdrWhite_MapsToWhite()
    {
        // The curve is normalized so the estimated SDR white maps to 1.0.
        Assert.That(HdrTonemapper.TonemapHableNormalized(1f, 1f), Is.EqualTo(1f).Within(1e-5f));
        Assert.That(HdrTonemapper.TonemapHableNormalized(3.36f, 3.36f), Is.EqualTo(1f).Within(1e-5f));
    }

    [Test]
    public void TonemapHable_MidtonesStayBelowWhite()
    {
        // Chosen visual profile (photo assessment): mids are lifted slightly, peaks above white
        // saturate to white (the sun disc), but nothing goes negative/NaN.
        float mid = HdrTonemapper.TonemapHableNormalized(0.5f, 1f);
        Assert.That(mid, Is.GreaterThan(0.1f));
        Assert.That(mid, Is.LessThan(1f));

        float peak = HdrTonemapper.TonemapHableNormalized(55f, 1f);
        Assert.That(peak, Is.EqualTo(1f).Within(1e-6f));
    }

    [Test]
    public void TonemapHable_IsMonotonic()
    {
        float previous = 0f;

        for (float linear = 0f; linear <= 200f; linear += 0.5f)
        {
            float mapped = HdrTonemapper.TonemapHableNormalized(linear, 3.36f);
            Assert.That(mapped, Is.GreaterThanOrEqualTo(previous - 1e-6f));
            previous = mapped;
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // TonemapToBgra8888
    // ──────────────────────────────────────────────────────────────────────

    [Test]
    public void TonemapToBgra8888_NonF16Input_ReturnsNull()
    {
        using var sdr = new SKBitmap(2, 2, SKColorType.Bgra8888, SKAlphaType.Premul);

        Assert.That(HdrTonemapper.TonemapToBgra8888(sdr, HdrToneMappingMode.TonemapReinhard), Is.Null);
    }

    [Test]
    public void TonemapToBgra8888_TonemapHable_MatchesVisuallySelectedVariant()
    {
        // The production TonemapHable mode must render exactly like the "hable-norm" variant the
        // operator selected from the real HDR photo comparison (see HdrPhotoVariantTests).
        using var hdr = MakeF16Bitmap(
            (0f, 0f, 0f, 1f),
            (0.2f, 0.2f, 0.2f, 1f),
            (0.5f, 0.5f, 0.5f, 1f),
            (1f, 1f, 1f, 1f),
            (3f, 3f, 3f, 1f),
            (10f, 10f, 10f, 1f),
            (1f, 0f, 0f, 1f),
            (0f, 0f, 1f, 1f));

        var (white, _) = XerahS.Common.Helpers.HdrContentAnalyzer.Analyze(hdr);
        var variant = HdrTonemapVariants.All.First(v => v.Id == "hable-norm");

        using var expected = HdrTonemapVariants.Apply(hdr, variant, white);
        using var actual = HdrTonemapper.TonemapToBgra8888(hdr, HdrToneMappingMode.TonemapHable);

        Assert.That(expected, Is.Not.Null);
        Assert.That(actual, Is.Not.Null);

        for (int i = 0; i < hdr.Width; i++)
        {
            var e = ReadPixel(expected!, i);
            var a = ReadPixel(actual!, i);
            Assert.Multiple(() =>
            {
                Assert.That(a.R, Is.EqualTo(e.R).Within(1), $"R mismatch at {i}");
                Assert.That(a.G, Is.EqualTo(e.G).Within(1), $"G mismatch at {i}");
                Assert.That(a.B, Is.EqualTo(e.B).Within(1), $"B mismatch at {i}");
            });
        }
    }

    [Test]
    public void TonemapToBgra8888_Reinhard_MeetsNumericCriteria()
    {
        using var hdr = MakeF16Bitmap((1f, 1f, 1f, 1f), (4f, 4f, 4f, 1f));
        using var result = HdrTonemapper.TonemapToBgra8888(hdr, HdrToneMappingMode.TonemapReinhard);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.ColorType, Is.EqualTo(SKColorType.Bgra8888));
        Assert.That(result.Width, Is.EqualTo(2));
        Assert.That(result.Height, Is.EqualTo(1));

        (byte b, byte g, byte r, byte a) white = ReadPixel(result, 0);
        (byte b, byte g, byte r, byte a) peak = ReadPixel(result, 1);

        Assert.Multiple(() =>
        {
            // SDR white (1.0 scRGB = 80 nits) must stay bright, not crushed by HDR peak normalization.
            Assert.That(white.r, Is.InRange(128, 255));
            Assert.That(white.g, Is.InRange(128, 255));
            Assert.That(white.b, Is.InRange(128, 255));

            // HDR highlight (4 scRGB = 320 nits) must be visibly compressed below 255.
            Assert.That(peak.r, Is.InRange(200, 254));
            Assert.That(peak.g, Is.InRange(200, 254));
            Assert.That(peak.b, Is.InRange(200, 254));

            // Monotonic: peak is brighter than SDR white.
            Assert.That(peak.r, Is.GreaterThan(white.r));

            // Hue preserved: all channels mapped with the same scale → R ≈ G ≈ B.
            Assert.That(white.r, Is.EqualTo(white.g));
            Assert.That(white.g, Is.EqualTo(white.b));
            Assert.That(peak.r, Is.EqualTo(peak.g));
            Assert.That(peak.g, Is.EqualTo(peak.b));

            Assert.That(white.a, Is.EqualTo(255));
        });
    }

    [Test]
    public void TonemapToBgra8888_Aces_MeetsNumericCriteria()
    {
        using var hdr = MakeF16Bitmap((1f, 1f, 1f, 1f), (4f, 4f, 4f, 1f));
        using var result = HdrTonemapper.TonemapToBgra8888(hdr, HdrToneMappingMode.TonemapAces);

        Assert.That(result, Is.Not.Null);

        (byte b, byte g, byte r, byte a) white = ReadPixel(result, 0);
        (byte b, byte g, byte r, byte a) peak = ReadPixel(result, 1);

        Assert.Multiple(() =>
        {
            // SDR white (1.0 scRGB = 80 nits) must stay bright.
            Assert.That(white.r, Is.InRange(128, 255));
            Assert.That(white.g, Is.InRange(128, 255));
            Assert.That(white.b, Is.InRange(128, 255));

            // HDR highlight (4 scRGB = 320 nits) must be visibly compressed below 255.
            Assert.That(peak.r, Is.InRange(200, 254));
            Assert.That(peak.g, Is.InRange(200, 254));
            Assert.That(peak.b, Is.InRange(200, 254));

            // Monotonic: peak is brighter than SDR white.
            Assert.That(peak.r, Is.GreaterThan(white.r));

            // Hue preserved: all channels mapped with the same scale → R ≈ G ≈ B.
            Assert.That(white.r, Is.EqualTo(white.g));
            Assert.That(white.g, Is.EqualTo(white.b));
            Assert.That(peak.r, Is.EqualTo(peak.g));
            Assert.That(peak.g, Is.EqualTo(peak.b));
        });
    }

    [Test]
    public void TonemapToBgra8888_Bt2446C_MeetsNumericCriteria()
    {
        using var hdr = MakeF16Bitmap((0.5f, 0.5f, 0.5f, 1f), (1f, 1f, 1f, 1f));
        using var result = HdrTonemapper.TonemapToBgra8888(hdr, HdrToneMappingMode.TonemapBt2446C);

        Assert.That(result, Is.Not.Null);

        (byte b, byte g, byte r, byte a) mid = ReadPixel(result, 0);
        (byte b, byte g, byte r, byte a) white = ReadPixel(result, 1);

        Assert.Multiple(() =>
        {
            // SDR midtone (0.5 scRGB = 40 nits) must stay visible and not be crushed.
            Assert.That(mid.r, Is.InRange(128, 235));
            Assert.That(mid.g, Is.InRange(128, 235));
            Assert.That(mid.b, Is.InRange(128, 235));

            // SDR white (1.0 scRGB = 80 nits) maps to full white.
            Assert.That(white.r, Is.InRange(235, 255));
            Assert.That(white.g, Is.InRange(235, 255));
            Assert.That(white.b, Is.InRange(235, 255));

            Assert.That(white.r, Is.GreaterThan(mid.r));
            Assert.That(mid.r, Is.EqualTo(mid.g));
            Assert.That(mid.g, Is.EqualTo(mid.b));
            Assert.That(white.r, Is.EqualTo(white.g));
            Assert.That(white.g, Is.EqualTo(white.b));
        });
    }

    [Test]
    public void TonemapToBgra8888_IsDeterministic()
    {
        using var hdr = MakeF16Bitmap((0.5f, 0.25f, 0.125f, 1f), (125f, 1f, 0.5f, 1f));
        using var first = HdrTonemapper.TonemapToBgra8888(hdr, HdrToneMappingMode.TonemapReinhard);
        using var second = HdrTonemapper.TonemapToBgra8888(hdr, HdrToneMappingMode.TonemapReinhard);

        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.Not.Null);
        Assert.That(first.GetPixelSpan().SequenceEqual(second.GetPixelSpan()), Is.True);
    }

    [Test]
    public void TonemapToBgra8888_NonFiniteAndNegativeHalves_MapToBlack()
    {
        using var hdr = MakeF16Bitmap(
            (float.NaN, float.NaN, float.NaN, 1f),
            (float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity, 1f),
            (-1f, -1f, -1f, 1f));
        using var result = HdrTonemapper.TonemapToBgra8888(hdr, HdrToneMappingMode.TonemapReinhard);

        Assert.That(result, Is.Not.Null);

        for (int x = 0; x < 3; x++)
        {
            (byte b, byte g, byte r, byte a) pixel = ReadPixel(result, x);
            Assert.That(pixel.r, Is.EqualTo(0));
            Assert.That(pixel.g, Is.EqualTo(0));
            Assert.That(pixel.b, Is.EqualTo(0));
            Assert.That(pixel.a, Is.EqualTo(255));
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Effectively-SDR bypass
    // ──────────────────────────────────────────────────────────────────────

    [Test]
    public void TonemapToBgra8888_EffectivelySdrContent_BypassesEveryCurve()
    {
        // A desktop frame on an HDR display reaches SDR white but never above it. Running it
        // through Hable/ACES/Reinhard lifts or crushes midtones away from what the screen shows
        // (measured: +13% mean luminance for Hable), so the mapper must return the identity result.
        // Percentile-based analysis needs a realistic sample count, hence 100 pixels.
        var pixels = new (float R, float G, float B, float A)[100];
        for (int i = 0; i < pixels.Length; i++)
        {
            float v = i < 60 ? 0.2f : i < 90 ? 0.5f : 1f;
            pixels[i] = (v, v, v, 1f);
        }

        using var hdr = MakeF16Bitmap(pixels);

        using var none = HdrTonemapper.TonemapToBgra8888(hdr, HdrToneMappingMode.None);
        Assert.That(none, Is.Not.Null);

        foreach (var mode in new[]
        {
            HdrToneMappingMode.TonemapReinhard,
            HdrToneMappingMode.TonemapAces,
            HdrToneMappingMode.TonemapBt2446C,
            HdrToneMappingMode.TonemapHable,
        })
        {
            using var toned = HdrTonemapper.TonemapToBgra8888(hdr, mode);
            Assert.That(toned, Is.Not.Null, $"{mode} must produce a bitmap");

            for (int x = 0; x < hdr.Width; x++)
            {
                var expected = ReadPixel(none!, x);
                var actual = ReadPixel(toned!, x);
                Assert.That(actual, Is.EqualTo(expected), $"{mode} must be identity for effectively-SDR content at x={x}");
            }
        }
    }

    [Test]
    public void TonemapToBgra8888_RealHdrContent_StillCompresses()
    {
        // Regression guard for the bypass: content with genuine headroom (a rare specular peak
        // ~8x the SDR white) must still be tone mapped, otherwise highlights clip to white.
        var pixels = new (float R, float G, float B, float A)[100];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (0.5f, 0.5f, 0.5f, 1f);
        }
        pixels[99] = (4f, 4f, 4f, 1f);

        using var hdr = MakeF16Bitmap(pixels);

        using var none = HdrTonemapper.TonemapToBgra8888(hdr, HdrToneMappingMode.None);
        using var hable = HdrTonemapper.TonemapToBgra8888(hdr, HdrToneMappingMode.TonemapHable);
        Assert.That(none, Is.Not.Null);
        Assert.That(hable, Is.Not.Null);

        var nonePeak = ReadPixel(none!, 99);
        var hablePeak = ReadPixel(hable!, 99);
        var noneBase = ReadPixel(none!, 0);
        var hableBase = ReadPixel(hable!, 0);

        // The bypass must not engage: an HDR frame is still processed by the curve, so at least
        // one pixel has to differ from the plain clamp result. (The exact peak value is the tone
        // curve's business and is covered by the per-curve tests above.)
        Assert.That(hablePeak.Equals(nonePeak) && hableBase.Equals(noneBase), Is.False,
            "HDR content must not take the effectively-SDR identity bypass");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private static SKBitmap MakeF16Bitmap(params (float R, float G, float B, float A)[] pixels)
    {
        var bitmap = new SKBitmap(new SKImageInfo(
            pixels.Length,
            1,
            SKColorType.RgbaF16,
            SKAlphaType.Premul,
            SKColorSpace.CreateSrgbLinear()));

        Span<byte> span = bitmap.GetPixelSpan();

        for (int i = 0; i < pixels.Length; i++)
        {
            int offset = i * 8;
            WriteHalf(span, offset, pixels[i].R);
            WriteHalf(span, offset + 2, pixels[i].G);
            WriteHalf(span, offset + 4, pixels[i].B);
            WriteHalf(span, offset + 6, pixels[i].A);
        }

        return bitmap;
    }

    private static void WriteHalf(Span<byte> span, int offset, float value)
    {
        ushort bits = BitConverter.HalfToUInt16Bits((Half)value);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, 2), bits);
    }

    private static (byte B, byte G, byte R, byte A) ReadPixel(SKBitmap bitmap, int x)
    {
        Span<byte> span = bitmap.GetPixelSpan();
        int offset = x * 4;
        return (span[offset], span[offset + 1], span[offset + 2], span[offset + 3]);
    }
}