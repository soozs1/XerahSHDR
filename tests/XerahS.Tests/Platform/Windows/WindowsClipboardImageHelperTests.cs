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
using XerahS.Platform.Windows;

namespace XerahS.Tests.Platform.Windows;

[TestFixture]
public class WindowsClipboardImageHelperTests
{
    [Test]
    public void BuildDibV5_WritesExpectedHeader()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(2, 1, SKColorType.Bgra8888, SKAlphaType.Premul));
        bitmap.SetPixel(0, 0, new SKColor(255, 0, 0, 255));
        bitmap.SetPixel(1, 0, new SKColor(0, 255, 0, 128));

        byte[] dibV5 = ClipboardDibCodec.BuildDibV5(bitmap);

        Assert.That(BitConverter.ToInt32(dibV5, 0), Is.EqualTo(124));
        Assert.That(BitConverter.ToInt32(dibV5, 4), Is.EqualTo(2));
        Assert.That(BitConverter.ToInt32(dibV5, 8), Is.EqualTo(-1));
        Assert.That(BitConverter.ToUInt16(dibV5, 12), Is.EqualTo(1));
        Assert.That(BitConverter.ToUInt16(dibV5, 14), Is.EqualTo(32));
        Assert.That(BitConverter.ToUInt32(dibV5, 16), Is.EqualTo(3u));
        Assert.That(BitConverter.ToUInt32(dibV5, 20), Is.EqualTo(8));
        Assert.That(BitConverter.ToUInt32(dibV5, 40), Is.EqualTo(0x00FF0000u));
        Assert.That(BitConverter.ToUInt32(dibV5, 44), Is.EqualTo(0x0000FF00u));
        Assert.That(BitConverter.ToUInt32(dibV5, 48), Is.EqualTo(0x000000FFu));
        Assert.That(BitConverter.ToUInt32(dibV5, 52), Is.EqualTo(0xFF000000u));
        Assert.That(dibV5.Length, Is.EqualTo(124 + 8));
    }

    [Test]
    public void BuildOpaqueDib_FlattensTransparencyOnWhite()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(2, 1, SKColorType.Bgra8888, SKAlphaType.Premul));
        bitmap.SetPixel(0, 0, new SKColor(0, 0, 0, 0));
        bitmap.SetPixel(1, 0, new SKColor(255, 0, 0, 255));

        byte[] dib = ClipboardDibCodec.BuildOpaqueDib(bitmap, SKColors.White);
        using SKBitmap? decoded = ClipboardDibCodec.DecodeDib(dib);

        Assert.That(decoded, Is.Not.Null);
        AssertColor(decoded!.GetPixel(0, 0), new SKColor(255, 255, 255, 255));
        AssertColor(decoded.GetPixel(1, 0), new SKColor(255, 0, 0, 255));
    }

    [Test]
    public void BuildDibV5_RoundTripsOpaqueAndTransparentPixels()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(2, 1, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        bitmap.SetPixel(0, 0, new SKColor(0, 0, 0, 0));
        bitmap.SetPixel(1, 0, new SKColor(12, 34, 56, 255));

        byte[] dibV5 = ClipboardDibCodec.BuildDibV5(bitmap);
        using SKBitmap? decoded = ClipboardDibCodec.DecodeDibV5(dibV5);

        Assert.That(decoded, Is.Not.Null);
        Assert.That(decoded!.GetPixel(0, 0).Alpha, Is.EqualTo(0));
        AssertColor(decoded.GetPixel(1, 0), new SKColor(12, 34, 56, 255));
    }

    [Test]
    public void DecodeDib_RejectsUnsupportedCompression()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bitmap.SetPixel(0, 0, SKColors.Red);

        byte[] dib = ClipboardDibCodec.BuildOpaqueDib(bitmap, SKColors.White);
        BitConverter.GetBytes(4).CopyTo(dib, 16);

        using SKBitmap? decoded = ClipboardDibCodec.DecodeDib(dib);

        Assert.That(decoded, Is.Null);
    }

    [Test]
    public void DecodeDibV5_RejectsTruncatedPixelData()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        bitmap.SetPixel(0, 0, SKColors.Red);

        byte[] dibV5 = ClipboardDibCodec.BuildDibV5(bitmap);
        Array.Resize(ref dibV5, dibV5.Length - 1);

        using SKBitmap? decoded = ClipboardDibCodec.DecodeDibV5(dibV5);

        Assert.That(decoded, Is.Null);
    }

    [Test]
    public void BuildDibV5_FromTonemappedF16Bitmap_WritesValid32BppHeader()
    {
        using var hdrBitmap = MakeF16Bitmap((1.0f, 1.0f, 1.0f, 1.0f), (10.0f, 5.0f, 2.0f, 1.0f));
        using SKBitmap? tonemapped = HdrTonemapper.TonemapToBgra8888(hdrBitmap, HdrToneMappingMode.TonemapReinhard);

        Assert.That(tonemapped, Is.Not.Null);

        byte[] dibV5 = ClipboardDibCodec.BuildDibV5(tonemapped!);

        Assert.That(BitConverter.ToInt32(dibV5, 0), Is.EqualTo(124));
        Assert.That(BitConverter.ToInt32(dibV5, 4), Is.EqualTo(2));
        Assert.That(BitConverter.ToInt32(dibV5, 8), Is.EqualTo(-1));
        Assert.That(BitConverter.ToUInt16(dibV5, 12), Is.EqualTo(1));
        Assert.That(BitConverter.ToUInt16(dibV5, 14), Is.EqualTo(32));
        Assert.That(BitConverter.ToUInt32(dibV5, 16), Is.EqualTo(3u));
        Assert.That(dibV5.Length, Is.EqualTo(124 + tonemapped.RowBytes * tonemapped.Height));

        using SKBitmap? decoded = ClipboardDibCodec.DecodeDibV5(dibV5);
        Assert.That(decoded, Is.Not.Null);
        Assert.That(decoded!.Width, Is.EqualTo(2));
        Assert.That(decoded.Height, Is.EqualTo(1));
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

    private static void AssertColor(SKColor actual, SKColor expected, byte tolerance = 1)
    {
        Assert.That(Math.Abs(actual.Red - expected.Red), Is.LessThanOrEqualTo(tolerance));
        Assert.That(Math.Abs(actual.Green - expected.Green), Is.LessThanOrEqualTo(tolerance));
        Assert.That(Math.Abs(actual.Blue - expected.Blue), Is.LessThanOrEqualTo(tolerance));
        Assert.That(Math.Abs(actual.Alpha - expected.Alpha), Is.LessThanOrEqualTo(tolerance));
    }
}
