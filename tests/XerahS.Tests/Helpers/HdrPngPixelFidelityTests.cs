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
    public void F16Bitmap_SavedAsBit16Png_DisplaysCorrectly()
    {
        // Regression guard for the 2026-08-16 report "16-bit PNG looks darker than reality":
        // raw scRGB-linear samples written verbatim to a PNG render too dark in standard viewers.
        // The encoder must convert linear -> sRGB (display-referred) while keeping 16-bit depth.
        using var hdr = MakeF16Bitmap((0.5f, 0.5f, 0.5f, 1f), (1f, 1f, 1f, 1f), (0.2f, 0.2f, 0.2f, 1f));

        using var stream = TaskHelpers.SaveImageAsStream(hdr, EImageFormat.PNG, PNGBitDepth.Bit16);
        Assert.That(stream, Is.Not.Null);
        var bytes = stream!.ToArray();

        // IHDR bit depth must be 16 for the HDR path.
        Assert.That(bytes[24], Is.EqualTo(16), "PNG IHDR bit depth");
        // sRGB chunk, not a linear iCCP profile: samples are display-referred.
        Assert.That(HasChunk(bytes, "sRGB"), Is.True, "PNG must carry the sRGB chunk");
        Assert.That(HasChunk(bytes, "iCCP"), Is.False, "PNG must not carry a linear iCCP profile");

        // A plain 8-bit decode must show the sRGB-encoded values (what any viewer renders):
        // linear 0.5 -> 188, linear 1.0 -> 255, linear 0.2 -> 124.
        using var decoded = SKBitmap.Decode(SKData.CreateCopy(bytes));
        Assert.That(decoded, Is.Not.Null);
        Assert.That(decoded!.GetPixel(0, 0).Red, Is.EqualTo(188).Within(1), "linear 0.5 must decode to sRGB 188");
        Assert.That(decoded.GetPixel(1, 0).Red, Is.EqualTo(255), "linear 1.0 must decode to sRGB 255");
        Assert.That(decoded.GetPixel(2, 0).Red, Is.EqualTo(124).Within(1), "linear 0.2 must decode to sRGB 124");
    }

    [Test]
    public void F16Bitmap_UntaggedColorSpace_SavedAsBit16Png_DisplaysCorrectly()
    {
        // Untagged fp16 buffers are still linear scRGB in the capture pipeline; the encoder must
        // apply the sRGB OETF even when Skia cannot infer the source transfer function.
        using var hdr = MakeF16Bitmap(new[] { (0.5f, 0.5f, 0.5f, 1f) }, null);

        using var stream = TaskHelpers.SaveImageAsStream(hdr, EImageFormat.PNG, PNGBitDepth.Bit16);
        Assert.That(stream, Is.Not.Null);
        var bytes = stream!.ToArray();
        Assert.That(bytes[24], Is.EqualTo(16), "PNG IHDR bit depth");

        using var decoded = SKBitmap.Decode(SKData.CreateCopy(bytes));
        Assert.That(decoded, Is.Not.Null);
        Assert.That(decoded!.GetPixel(0, 0).Red, Is.EqualTo(188).Within(1), "untagged linear 0.5 must decode to sRGB 188");
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

    [Test]
    public void SaveBitmap_HdrSource_Bit16File_WritesHdrIccpPng()
    {
        // The FILE path for "SDR+HDR" depth must produce a true HDR PNG (raw linear + iCCP),
        // not sRGB-encoded output. The in-memory stream path (SaveImageAsStream, used for
        // uploads) intentionally stays sRGB-encoded for web compatibility.
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        try
        {
            using var hdr = MakeF16Bitmap((0.5f, 0.5f, 0.5f, 1f), (4f, 4f, 4f, 1f));
            ImageHelpers.SaveBitmap(hdr, path, PNGBitDepth.Bit16);

            var bytes = File.ReadAllBytes(path);
            Assert.That(bytes[24], Is.EqualTo(16), "file must be bit depth 16");
            Assert.That(HasChunk(bytes, "iCCP"), Is.True, "file must carry the linear iCCP profile");
            Assert.That(HasChunk(bytes, "sRGB"), Is.False, "file must not carry an sRGB chunk");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void F16Bitmap_SavedAsHdr16Png_PreservesRawLinearRange()
    {
        // "SDR+HDR" depth must write a true HDR file: raw linear samples (including > 1.0)
        // with a linear iCCP profile — NOT sRGB-encoded, NOT clipped to display white.
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        try
        {
            using var hdr = MakeF16Bitmap((0.25f, 0.25f, 0.25f, 1f), (4f, 4f, 4f, 1f));
            ImageHelpers.SaveHdrF16Png(hdr, path);

            var bytes = File.ReadAllBytes(path);
            Assert.That(bytes[24], Is.EqualTo(16), "IHDR bit depth must be 16");
            Assert.That(bytes[25], Is.EqualTo(6), "IHDR color type must be RGBA");
            Assert.That(HasChunk(bytes, "iCCP"), Is.True, "HDR PNG must carry a linear iCCP profile");
            Assert.That(HasChunk(bytes, "sRGB"), Is.False, "HDR PNG must not carry an sRGB chunk");

            // Decode pixels: first sample 0.25 -> 0.25*65535 ≈ 16384, second 4.0 -> 65535 (clamped u16).
            using var decoded = SKBitmap.Decode(SKData.CreateCopy(bytes));
            Assert.That(decoded, Is.Not.Null);
            var p0 = decoded!.GetPixel(0, 0);
            var p1 = decoded.GetPixel(1, 0);
            // A plain 8-bit viewer sees the raw linear values clamped to [0,1]; the 4.0 sample
            // must be BRIGHTER than the 0.25 sample (not sRGB-darkened 0.25 -> 137).
            Assert.That(p0.Red, Is.GreaterThan(0), "0.25 linear must not be black");
            Assert.That(p0.Red, Is.LessThan(p1.Red), "4.0 linear must be brighter than 0.25 linear");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void F16Bitmap_ConvertToSrgb8_PreservesChannelOrder()
    {
        // Regression guard for the R↔B swap in the fp16→sRGB LUT conversion (yellow→cyan,
        // red→blue). The conversion targets Bgra8888: byte0=B, byte1=G, byte2=R.
        using var hdr = MakeF16Bitmap((1f, 0f, 0f, 1f), (0f, 1f, 0f, 1f), (0f, 0f, 1f, 1f), (1f, 1f, 0f, 1f));
        using var sdr = ShareX.ImageEditor.Presentation.Rendering.BitmapConversionHelpers.ConvertF16ToSrgb8(hdr);
        Assert.That(sdr, Is.Not.Null);

        Assert.That(sdr!.GetPixel(0, 0).Red, Is.EqualTo(255).Within(2), "red pixel keeps red");
        Assert.That(sdr.GetPixel(0, 0).Blue, Is.LessThan(8), "red pixel has no blue");
        Assert.That(sdr.GetPixel(1, 0).Green, Is.EqualTo(255).Within(2), "green pixel keeps green");
        Assert.That(sdr.GetPixel(2, 0).Blue, Is.EqualTo(255).Within(2), "blue pixel keeps blue");
        Assert.That(sdr.GetPixel(2, 0).Red, Is.LessThan(8), "blue pixel has no red");
        Assert.That(sdr.GetPixel(3, 0).Red, Is.GreaterThan(200), "yellow has strong red");
        Assert.That(sdr.GetPixel(3, 0).Green, Is.GreaterThan(200), "yellow has strong green");
        Assert.That(sdr.GetPixel(3, 0).Blue, Is.LessThan(100), "yellow has no blue");
    }

    private static SKBitmap MakeF16Bitmap(params (float R, float G, float B, float A)[] pixels)
    {
        return MakeF16Bitmap(pixels, SKColorSpace.CreateSrgbLinear());
    }

    private static SKBitmap MakeF16Bitmap((float R, float G, float B, float A)[] pixels, SKColorSpace? colorSpace)
    {
        var bitmap = new SKBitmap(new SKImageInfo(
            pixels.Length, 1, SKColorType.RgbaF16, SKAlphaType.Premul, colorSpace));
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

    private static bool HasChunk(byte[] png, string chunkName)
    {
        int pos = 8;
        while (pos + 8 <= png.Length)
        {
            uint len = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos, 4));
            string name = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
            if (name == chunkName)
            {
                return true;
            }
            pos += 12 + (int)len;
        }
        return false;
    }

    private static void WriteHalf(Span<byte> span, int offset, float value)
    {
        ushort bits = BitConverter.HalfToUInt16Bits((Half)value);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, 2), bits);
    }
}
