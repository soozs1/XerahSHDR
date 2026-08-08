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

public class SaveImageAsStreamTests
{
    // ──────────────────────────────────────────────────────────────────────
    // HDR (RgbaF16) input must always be encoded as 16-bit PNG
    // ──────────────────────────────────────────────────────────────────────

    [TestCase(EImageFormat.JPEG)]
    [TestCase(EImageFormat.GIF)]
    [TestCase(EImageFormat.WEBP)]
    [TestCase(EImageFormat.BMP)]
    public void F16Bitmap_With8BitFormat_FallsBackTo16BitPng(EImageFormat imageFormat)
    {
        using var hdr = MakeF16Bitmap((1f, 1f, 1f, 1f), (4f, 2f, 1f, 1f));
        using var stream = TaskHelpers.SaveImageAsStream(hdr, imageFormat);

        Assert.That(stream, Is.Not.Null);
        Assert16BitPng(stream.ToArray());
    }

    [Test]
    public void F16Bitmap_WithPngFormat_Returns16BitPng()
    {
        using var hdr = MakeF16Bitmap((1f, 1f, 1f, 1f), (4f, 2f, 1f, 1f));
        using var stream = TaskHelpers.SaveImageAsStream(hdr, EImageFormat.PNG);

        Assert.That(stream, Is.Not.Null);
        Assert16BitPng(stream.ToArray());
    }

    // ──────────────────────────────────────────────────────────────────────
    // PNGBitDepth.Bit16 widens SDR content to 16-bit PNG
    // ──────────────────────────────────────────────────────────────────────

    [Test]
    public void SdrBitmap_Bit16_Returns16BitPng()
    {
        using var sdr = MakeSdrBitmap(SKColors.White);

        using var stream = TaskHelpers.SaveImageAsStream(sdr, EImageFormat.PNG, PNGBitDepth.Bit16);

        Assert.That(stream, Is.Not.Null);
        Assert16BitPng(stream.ToArray());
    }

    [Test]
    public void SdrBitmap_Default_Returns8BitPng()
    {
        using var sdr = MakeSdrBitmap(SKColors.White);

        using var stream = TaskHelpers.SaveImageAsStream(sdr, EImageFormat.PNG, PNGBitDepth.Default);

        Assert.That(stream, Is.Not.Null);
        var bytes = stream.ToArray();
        Assert.That(IsPng(bytes), Is.True);
        Assert.That(GetPngBitDepth(bytes), Is.EqualTo(8));

        using var decoded = SKBitmap.Decode(SKData.CreateCopy(bytes));
        Assert.That(decoded, Is.Not.Null);
        Assert.That(decoded!.ColorType, Is.EqualTo(SKColorType.Bgra8888));
    }

    [Test]
    public void SdrBitmap_Bit16_WhitePixelIsPreserved()
    {
        using var sdr = MakeSdrBitmap(SKColors.White);

        using var stream = TaskHelpers.SaveImageAsStream(sdr, EImageFormat.PNG, PNGBitDepth.Bit16);

        Assert.That(stream, Is.Not.Null);
        var bytes = stream.ToArray();
        Assert16BitPng(bytes);

        // SKBitmap.Decode downconverts to 8-bit in this SkiaSharp build, so decode the
        // 16-bit payload explicitly as RgbaF16 to read the widened values.
        using var codec = SKCodec.Create(SKData.CreateCopy(bytes));
        Assert.That(codec, Is.Not.Null);
        using var decoded = new SKBitmap(new SKImageInfo(
            codec!.Info.Width, codec.Info.Height, SKColorType.RgbaF16, SKAlphaType.Unpremul));
        Assert.That(codec.GetPixels(decoded.Info, decoded.GetPixels()), Is.EqualTo(SKCodecResult.Success));

        var (r, g, b) = ReadPixelF16(decoded, 0);
        Assert.That(r, Is.EqualTo(1f).Within(0.01f));
        Assert.That(g, Is.EqualTo(1f).Within(0.01f));
        Assert.That(b, Is.EqualTo(1f).Within(0.01f));
    }

    [Test]
    public void SaveBitmap_Bit16_Writes16BitPngFile()
    {
        using var sdr = MakeSdrBitmap(SKColors.White);
        string tempPath = Path.Combine(Path.GetTempPath(), $"xerahs-savebitmap-{Guid.NewGuid():N}.png");

        try
        {
            ImageHelpers.SaveBitmap(sdr, tempPath, PNGBitDepth.Bit16);

            Assert.That(File.Exists(tempPath), Is.True);
            Assert16BitPng(File.ReadAllBytes(tempPath));
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private static SKBitmap MakeSdrBitmap(SKColor color)
    {
        var bitmap = new SKBitmap(2, 2, SKColorType.Bgra8888, SKAlphaType.Premul);
        bitmap.Erase(color);
        return bitmap;
    }

    /// <summary>
    /// PNG signature check: 89 50 4E 47 0D 0A 1A 0A.
    /// </summary>
    private static bool IsPng(byte[] bytes)
    {
        return bytes.Length >= 8 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
            bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;
    }

    /// <summary>
    /// IHDR is the mandatory first chunk: signature(8) + length(4) + "IHDR"(4) +
    /// width(4) + height(4) + bitDepth(1) + colorType(1), so the bit depth sits at offset 24.
    /// </summary>
    private static int GetPngBitDepth(byte[] bytes)
    {
        Assert.That(IsPng(bytes), Is.True, "Expected a PNG stream");
        return bytes[24];
    }

    private static void Assert16BitPng(byte[] bytes)
    {
        Assert.That(IsPng(bytes), Is.True, "Expected a PNG stream");
        Assert.That(GetPngBitDepth(bytes), Is.EqualTo(16), "Expected a 16-bit PNG");
    }

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

    private static (float R, float G, float B) ReadPixelF16(SKBitmap bitmap, int x)
    {
        Span<byte> span = bitmap.GetPixelSpan();
        int offset = x * 8;
        return (ReadHalf(span, offset), ReadHalf(span, offset + 2), ReadHalf(span, offset + 4));
    }

    private static ushort ReadUInt16(Span<byte> span, int offset)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset, 2));
    }

    private static float ReadHalf(Span<byte> span, int offset)
    {
        return (float)BitConverter.UInt16BitsToHalf(ReadUInt16(span, offset));
    }
}