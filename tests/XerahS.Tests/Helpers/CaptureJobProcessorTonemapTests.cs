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
using XerahS.Core.Tasks.Processors;

namespace XerahS.Tests.Helpers;

public class CaptureJobProcessorTonemapTests
{
    [Test]
    public void TryTonemapHdr_F16Reinhard_CreatesNewBgra8888()
    {
        using var hdr = MakeF16Bitmap((1f, 1f, 1f, 1f), (125f, 125f, 125f, 1f));

        bool converted = CaptureJobProcessor.TryTonemapHdr(
            hdr, HdrToneMappingMode.TonemapReinhard, AfterCaptureTasks.CopyImageToClipboard, out SKBitmap? tonemapped);

        Assert.That(converted, Is.True);
        Assert.That(tonemapped, Is.Not.Null);
        Assert.That(tonemapped, Is.Not.SameAs(hdr));
        Assert.That(tonemapped!.ColorType, Is.EqualTo(SKColorType.Bgra8888));
        Assert.That(tonemapped.Width, Is.EqualTo(hdr.Width));
        Assert.That(tonemapped.Height, Is.EqualTo(hdr.Height));

        tonemapped.Dispose();
    }

    [Test]
    public void TryTonemapHdr_F16Auto_WithSdrStep_CreatesNewBgra8888()
    {
        using var hdr = MakeF16Bitmap((1f, 1f, 1f, 1f));

        bool converted = CaptureJobProcessor.TryTonemapHdr(
            hdr, HdrToneMappingMode.Auto, AfterCaptureTasks.CopyImageToClipboard, out SKBitmap? tonemapped);

        Assert.That(converted, Is.True);
        Assert.That(tonemapped, Is.Not.Null);
        Assert.That(tonemapped, Is.Not.SameAs(hdr));
        Assert.That(tonemapped!.ColorType, Is.EqualTo(SKColorType.Bgra8888));

        tonemapped.Dispose();
    }

    [Test]
    public void TryTonemapHdr_F16None_ReturnsSameImage()
    {
        using var hdr = MakeF16Bitmap((1f, 1f, 1f, 1f));

        bool converted = CaptureJobProcessor.TryTonemapHdr(
            hdr, HdrToneMappingMode.None, AfterCaptureTasks.CopyImageToClipboard, out SKBitmap? tonemapped);

        Assert.That(converted, Is.False);
        Assert.That(tonemapped, Is.SameAs(hdr));
    }

    [Test]
    public void TryTonemapHdr_F16Auto_SaveOnly_ReturnsSameImage()
    {
        // Auto + save-only keeps fp16 so the 16-bit PNG path is preserved (HDR_CAPTURE_PLAN §6.3).
        using var hdr = MakeF16Bitmap((1f, 1f, 1f, 1f));

        bool converted = CaptureJobProcessor.TryTonemapHdr(
            hdr, HdrToneMappingMode.Auto, AfterCaptureTasks.SaveImageToFile, out SKBitmap? tonemapped);

        Assert.That(converted, Is.False);
        Assert.That(tonemapped, Is.SameAs(hdr));
    }

    [Test]
    public void TryTonemapHdr_SdrImage_ReturnsSameImage()
    {
        using var sdr = new SKBitmap(2, 2, SKColorType.Bgra8888, SKAlphaType.Premul);

        bool converted = CaptureJobProcessor.TryTonemapHdr(
            sdr, HdrToneMappingMode.TonemapReinhard, AfterCaptureTasks.CopyImageToClipboard, out SKBitmap? tonemapped);

        Assert.That(converted, Is.False);
        Assert.That(tonemapped, Is.SameAs(sdr));
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
}