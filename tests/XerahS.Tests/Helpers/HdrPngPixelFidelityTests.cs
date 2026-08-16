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
using XerahS.Core;
using XerahS.Services.Abstractions;

namespace XerahS.Tests.Helpers;

/// <summary>
/// Regression guard for the 2026-08-15 "black screen with cursor" report. The black capture was
/// an uninitialized zero frame from DXGI, not the encoding path — these tests pin that the
/// HDR (RgbaF16) → 16-bit PNG pipeline itself preserves pixel data end-to-end.
/// </summary>
public class HdrPngPixelFidelityTests
{
    [Test]
    public void F16Bitmap_SavedAsBit16Png_PreservesPixelValues()
    {
        using var hdr = MakeF16Bitmap((0.5f, 0.5f, 0.5f, 1f), (1f, 1f, 1f, 1f), (0.2f, 0.2f, 0.2f, 1f));

        using var stream = TaskHelpers.SaveImageAsStream(hdr, EImageFormat.PNG, PNGBitDepth.Bit16);
        Assert.That(stream, Is.Not.Null);
        var bytes = stream!.ToArray();

        // IHDR bit depth must be 16 for the HDR path.
        Assert.That(bytes[24], Is.EqualTo(16), "PNG IHDR bit depth");

        using var codec = SKCodec.Create(SKData.CreateCopy(bytes));
        Assert.That(codec, Is.Not.Null);

        using var decoded = new SKBitmap(new SKImageInfo(
            codec!.Info.Width, codec.Info.Height, SKColorType.RgbaF16, SKAlphaType.Unpremul));
        var result = codec.GetPixels(decoded.Info, decoded.GetPixels());
        Assert.That(result, Is.EqualTo(SKCodecResult.Success));

        Span<byte> span = decoded.GetPixelSpan();
        AssertHalf(span, 0, 0.5f);
        AssertHalf(span, 1, 1f);
        AssertHalf(span, 2, 0.2f);
    }

    [Test]
    public void F16Bitmap_TonemappedReinhard_SavedAsBit16Png_DecodesNonBlack()
    {
        using var hdr = MakeF16Bitmap((0.5f, 0.5f, 0.5f, 1f), (1f, 1f, 1f, 1f), (0.2f, 0.2f, 0.2f, 1f));
        using var sdr = XerahS.Core.Helpers.HdrTonemapper.TonemapToBgra8888(hdr, HdrToneMappingMode.TonemapReinhard);
        Assert.That(sdr, Is.Not.Null);

        // The SDR conversion must keep visible content visible: non-black and order-preserving.
        var p0 = sdr!.GetPixel(0, 0);
        var p1 = sdr.GetPixel(1, 0);
        var p2 = sdr.GetPixel(2, 0);
        Assert.That(p0.Red, Is.GreaterThan(0));
        Assert.That(p1.Red, Is.GreaterThan(p0.Red));
        Assert.That(p0.Red, Is.GreaterThan(p2.Red));

        using var stream = TaskHelpers.SaveImageAsStream(sdr, EImageFormat.PNG, PNGBitDepth.Bit16);
        Assert.That(stream, Is.Not.Null);
        var bytes = stream!.ToArray();
        Assert.That(bytes[24], Is.EqualTo(16), "PNG IHDR bit depth");

        using var decoded = SKBitmap.Decode(SKData.CreateCopy(bytes));
        Assert.That(decoded, Is.Not.Null);

        var d0 = decoded!.GetPixel(0, 0);
        var d1 = decoded.GetPixel(1, 0);
        var d2 = decoded.GetPixel(2, 0);
        Assert.That(d0.Red, Is.GreaterThan(0), "decoded pixel must not be black");
        Assert.That(d1.Red, Is.GreaterThan(d0.Red));
        Assert.That(d0.Red, Is.GreaterThan(d2.Red));
    }

    private static SKBitmap MakeF16Bitmap(params (float R, float G, float B, float A)[] pixels)
    {
        var bitmap = new SKBitmap(new SKImageInfo(
            pixels.Length, 1, SKColorType.RgbaF16, SKAlphaType.Premul, SKColorSpace.CreateSrgbLinear()));
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

    private static void AssertHalf(ReadOnlySpan<byte> span, int pixelIndex, float expected)
    {
        int offset = pixelIndex * 8;
        for (int c = 0; c < 3; c++)
        {
            float actual = ReadHalf(span, offset + c * 2);
            Assert.That(actual, Is.EqualTo(expected).Within(1e-3f), $"pixel {pixelIndex} channel {c}");
        }

        float alpha = ReadHalf(span, offset + 6);
        Assert.That(alpha, Is.EqualTo(1f).Within(1e-3f), $"pixel {pixelIndex} alpha");
    }

    private static void WriteHalf(Span<byte> span, int offset, float value)
    {
        ushort bits = BitConverter.HalfToUInt16Bits((Half)value);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, 2), bits);
    }

    private static float ReadHalf(ReadOnlySpan<byte> span, int offset)
    {
        ushort bits = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset, 2));
        return (float)BitConverter.UInt16BitsToHalf(bits);
    }
}
