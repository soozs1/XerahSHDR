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

using System.Buffers.Binary;
using System.Text;
using NUnit.Framework;
using SkiaSharp;
using XerahS.Common;
using XerahS.Common.Helpers;

namespace XerahS.Tests.Helpers;

/// <summary>
/// Guards the production Ultra HDR JPEG writer ("SDR+HDR" depth saved as .jpg).
/// A 16-bit PNG cannot carry values above display white, so this is the only container
/// in the pipeline that keeps real HDR highlights for a photo viewer.
/// </summary>
public class UltraHdrJpegWriterTests
{
    [Test]
    public void TrySave_F16Bitmap_WritesUltraHdrStructure()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jpg");
        try
        {
            // 8x8 mid-grey with a single 4.0 highlight; white is passed explicitly because a
            // percentile estimate on a handful of pixels is meaningless.
            using var hdr = MakeF16Bitmap(8, 8, 0.05f, highlight: 4f);

            bool saved = UltraHdrJpegWriter.TrySave(hdr, path, sdrWhite: 0.2f);
            Assert.That(saved, Is.True, "writer must report success");

            byte[] bytes = File.ReadAllBytes(path);
            Assert.That(bytes[0], Is.EqualTo(0xFF));
            Assert.That(bytes[1], Is.EqualTo(0xD8), "file must start with a JPEG SOI");

            string text = Encoding.ASCII.GetString(bytes);
            Assert.That(text, Does.Contain("http://ns.adobe.com/hdr-gain-map/1.0/"), "XMP gain-map namespace");
            Assert.That(text, Does.Contain("hdrgm:GainMapMax"), "XMP GainMapMax");
            Assert.That(bytes.AsSpan().IndexOf("MPF\0"u8), Is.GreaterThan(0), "APP2 MPF segment");

            // The gain map is appended after the primary image and must be a JPEG as well.
            int mpfAbs = bytes.AsSpan().IndexOf("MPF\0"u8);
            using var reader = new BinaryReader(new MemoryStream(bytes));
            reader.BaseStream.Position = mpfAbs + 4 + 50; // MPEntry offset from the TIFF header
            reader.ReadUInt32(); // primary attribute
            uint primarySize = BinaryPrimitives.ReadUInt32BigEndian(reader.ReadBytes(4));
            Assert.That(primarySize, Is.LessThan((uint)bytes.Length), "primary image must be smaller than the file");
            Assert.That(bytes[primarySize], Is.EqualTo(0xFF), "gain map SOI marker");
            Assert.That(bytes[primarySize + 1], Is.EqualTo(0xD8), "gain map SOI marker");

            // A plain JPEG decoder must still show the SDR base (that is what non-HDR viewers show).
            using var decoded = SKBitmap.Decode(SKData.CreateCopy(bytes));
            Assert.That(decoded, Is.Not.Null, "base image must decode as a normal JPEG");
            Assert.That(decoded!.Width, Is.EqualTo(8));
            Assert.That(decoded.Height, Is.EqualTo(8));
            var baseMid = decoded.GetPixel(1, 1); // (0,0) is the highlight
            Assert.That(baseMid.Red, Is.EqualTo(137).Within(6), "0.05 linear on 0.2 white decodes to sRGB 137");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void SaveBitmap_Bit16JpegPath_WritesUltraHdr()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jpg");
        try
        {
            using var hdr = MakeF16Bitmap(8, 8, 0.05f, highlight: 4f);

            // The production entry point: "SDR+HDR" depth + a .jpg target.
            ImageHelpers.SaveBitmap(hdr, path, PNGBitDepth.Bit16);

            byte[] bytes = File.ReadAllBytes(path);
            Assert.That(bytes[1], Is.EqualTo(0xD8), "JPEG SOI");
            Assert.That(Encoding.ASCII.GetString(bytes), Does.Contain("hdrgm:GainMapMax"), "must be Ultra HDR, not a plain JPEG");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void TrySave_SdrBitmap_ReturnsFalse()
    {
        using var sdr = new SKBitmap(new SKImageInfo(4, 4, SKColorType.Bgra8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()));
        Assert.That(UltraHdrJpegWriter.TrySave(sdr, Path.Combine(Path.GetTempPath(), "never-written.jpg")), Is.False);
    }

    private static SKBitmap MakeF16Bitmap(int w, int h, float baseValue, float highlight)
    {
        var bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.RgbaF16, SKAlphaType.Premul, SKColorSpace.CreateSrgbLinear()));
        Span<byte> span = bitmap.GetPixelSpan();
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float v = x == 0 && y == 0 ? highlight : baseValue;
                int o = y * bitmap.RowBytes + x * 8;
                WriteHalf(span, o, v);
                WriteHalf(span, o + 2, v);
                WriteHalf(span, o + 4, v);
                WriteHalf(span, o + 6, 1f);
            }
        }

        return bitmap;
    }

    private static void WriteHalf(Span<byte> span, int offset, float value)
    {
        ushort bits = BitConverter.HalfToUInt16Bits((Half)value);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, 2), bits);
    }
}
