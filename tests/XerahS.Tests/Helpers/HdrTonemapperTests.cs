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
    // ClampPeak
    // ──────────────────────────────────────────────────────────────────────

    [TestCase(0f, 1000f)]
    [TestCase(float.NaN, 1000f)]
    [TestCase(float.PositiveInfinity, 1000f)]
    [TestCase(float.NegativeInfinity, 1000f)]
    [TestCase(50f, 80f)]
    [TestCase(50000f, 10000f)]
    [TestCase(1000f, 1000f)]
    public void ClampPeak_ReturnsSaneRange(float peak, float expected)
    {
        Assert.That(HdrTonemapper.ClampPeak(peak), Is.EqualTo(expected));
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
    public void TonemapToBgra8888_Reinhard_SdrWhiteSurvives()
    {
        using var hdr = MakeF16Bitmap((1f, 1f, 1f, 1f), (10f, 10f, 10f, 1f));
        using var result = HdrTonemapper.TonemapToBgra8888(hdr, HdrToneMappingMode.TonemapReinhard, 1000f);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.ColorType, Is.EqualTo(SKColorType.Bgra8888));
        Assert.That(result.Width, Is.EqualTo(2));
        Assert.That(result.Height, Is.EqualTo(1));

        (byte b, byte g, byte r, byte a) white = ReadPixel(result, 0);
        (byte b, byte g, byte r, byte a) bright = ReadPixel(result, 1);

        // SDR white (L=1.0) is essentially unchanged by Reinhard at peak=1000 → ~255.
        Assert.That(white.r, Is.EqualTo(255).Within(3));
        Assert.That(white.g, Is.EqualTo(255).Within(3));
        Assert.That(white.b, Is.EqualTo(255).Within(3));

        // 10× luminance: Reinhard(10, 1000) = 9.9, whose sRGB OETF saturates the 8-bit range,
        // so it clamps to 255 and must never be crushed below SDR white.
        Assert.That(bright.r, Is.EqualTo(255));
        Assert.That(bright.g, Is.EqualTo(255));
        Assert.That(bright.b, Is.EqualTo(255));

        // Hue preserved: all channels mapped with the same scale → R ≈ G ≈ B.
        Assert.That(white.r, Is.EqualTo(white.g));
        Assert.That(white.g, Is.EqualTo(white.b));
        Assert.That(bright.r, Is.EqualTo(bright.g));
        Assert.That(bright.g, Is.EqualTo(bright.b));

        Assert.That(white.a, Is.EqualTo(255));
    }

    [Test]
    public void TonemapToBgra8888_Aces_CompressesWhite()
    {
        using var hdr = MakeF16Bitmap((1f, 1f, 1f, 1f), (10f, 10f, 10f, 1f));
        using var result = HdrTonemapper.TonemapToBgra8888(hdr, HdrToneMappingMode.TonemapAces, 1000f);

        Assert.That(result, Is.Not.Null);

        (byte b, byte g, byte r, byte a) white = ReadPixel(result, 0);
        (byte b, byte g, byte r, byte a) bright = ReadPixel(result, 1);

        // ACES(1.0) = 0.8038 → sRGB OETF(0.8038) ≈ 0.908 → ~232: the ACES preset visibly compresses SDR white.
        Assert.That(white.r, Is.EqualTo(232).Within(3));
        Assert.That(white.g, Is.EqualTo(232).Within(3));
        Assert.That(white.b, Is.EqualTo(232).Within(3));

        // ACES(10) = 1.009 → clamps to 255.
        Assert.That(bright.r, Is.EqualTo(255));
        Assert.That(bright.g, Is.EqualTo(255));
        Assert.That(bright.b, Is.EqualTo(255));

        Assert.That(white.r, Is.EqualTo(white.g));
        Assert.That(white.g, Is.EqualTo(white.b));
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