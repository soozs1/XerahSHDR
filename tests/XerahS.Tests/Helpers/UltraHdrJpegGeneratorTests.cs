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
using System.Text;
using XerahS.Common;
using XerahS.Common.Helpers;
using XerahS.Core.Helpers;

namespace XerahS.Tests.Helpers;

/// <summary>
/// Generates a standards-compliant Ultra HDR JPEG (ISO 21496-1 / Google Ultra HDR v1.0) from a
/// real HDR photo, so Windows Photos can display genuine HDR highlights on an HDR desktop and
/// the screen-capture path can be verified end-to-end. Output goes to the Windows Documents
/// folder. Skips when the HDR photo test data is not present.
///
/// File structure produced:
///   base SDR JPEG (display-referred, clamped at SDR white)
///   + APP1 XMP with hdrgm:* gain-map metadata
///   + APP2 MPF with two images (primary = base, secondary = gain map)
///   + appended grayscale gain-map JPEG
/// </summary>
public class UltraHdrJpegGeneratorTests
{
    private static readonly string PhotoDir = Path.Combine(FindRepoRoot(), "tests", "XerahS.Tests", "TestData", "HdrPhotos");
    private const string OutDir = "/mnt/c/Users/x/Documents/XerahS/HdrVariants";
    private const string SourcePhoto = "venice_sunset.hdr";
    private const string OutputName = "UltraHDR_venice_sunset.jpg";

    [Test]
    public void Generate_UltraHdrJpeg_FromRealHdrPhoto()
    {
        string source = Path.Combine(PhotoDir, SourcePhoto);
        if (!File.Exists(source))
        {
            Assert.Ignore($"missing HDR photo: {source}");
        }

        Directory.CreateDirectory(OutDir);
        string outputPath = Path.Combine(OutDir, OutputName);

        using var hdr = HdrPhotoLoader.LoadRadianceHdr(source);
        var (white, _) = HdrContentAnalyzer.Analyze(hdr);

        // SDR base: display-referred clamp at estimated SDR white.
        using var baseBmp = RenderClampedSdr(hdr, white);
        // Gain map: log2(HDR/SDR) normalized to [0, maxBoost].
        var (gainMap, gainMapMax) = BuildGainMap(hdr, white);

        byte[] baseJpeg = EncodeJpeg(baseBmp, 92);
        byte[] gainJpeg = EncodeGainMapJpeg(gainMap, hdr.Width, hdr.Height, 90);

        byte[] xmp = BuildXmpPacket(gainMapMax);

        // Compose: base prefix + XMP APP1 + MPF APP2 + base suffix (SOS..EOI) + gain JPEG.
        int sos = FindSosOffset(baseJpeg);
        Assert.That(sos, Is.GreaterThan(0), "SOS marker not found in base JPEG");

        byte[] mpfPlaceholder = BuildMpfSegment(0, (uint)gainJpeg.Length, 0);
        int prefixLen = sos;
        int xmpSegLen = xmp.Length;
        int mpfSegLen = mpfPlaceholder.Length;
        int suffixLen = baseJpeg.Length - sos;

        // Primary image size includes everything up to the gain-map SOI.
        uint primarySize = (uint)(prefixLen + xmpSegLen + mpfSegLen + suffixLen);
        // MPF offsets are relative to the TIFF header start (8 bytes into the APP2 segment:
        // marker 2 + length 2 + "MPF\0" 4).
        uint tiffAbs = (uint)(prefixLen + xmpSegLen + 8);
        uint secondaryOffset = primarySize - tiffAbs;

        byte[] mpf = BuildMpfSegment(primarySize, (uint)gainJpeg.Length, secondaryOffset);

        using var outStream = new MemoryStream(prefixLen + xmpSegLen + mpfSegLen + suffixLen + gainJpeg.Length);
        outStream.Write(baseJpeg, 0, prefixLen);
        outStream.Write(xmp, 0, xmp.Length);
        outStream.Write(mpf, 0, mpf.Length);
        outStream.Write(baseJpeg, sos, suffixLen);
        outStream.Write(gainJpeg, 0, gainJpeg.Length);
        File.WriteAllBytes(outputPath, outStream.ToArray());

        // ── structural verification ───────────────────────────────────────────
        byte[] written = File.ReadAllBytes(outputPath);
        int mpfAbs = written.AsSpan().IndexOf("MPF\0"u8);
        Assert.That(mpfAbs, Is.GreaterThan(0), "MPF signature missing");
        using var reader = new BinaryReader(new MemoryStream(written));
        reader.BaseStream.Position = mpfAbs + 4;
        Assert.That(reader.ReadByte(), Is.EqualTo((byte)'M'));
        Assert.That(reader.ReadByte(), Is.EqualTo((byte)'M'), "MPF must be big-endian");
        reader.ReadUInt16(); // 42
        uint ifdOffset = BinaryPrimitives.ReadUInt32BigEndian(ReadBytes(reader, 4));
        Assert.That(ifdOffset, Is.EqualTo(8u));

        reader.BaseStream.Position = mpfAbs + 4 + ifdOffset;
        int tagCount = BinaryPrimitives.ReadUInt16BigEndian(ReadBytes(reader, 2));
        Assert.That(tagCount, Is.EqualTo(3));

        uint entryOffset = 0;
        for (int t = 0; t < tagCount; t++)
        {
            ushort tag = BinaryPrimitives.ReadUInt16BigEndian(ReadBytes(reader, 2));
            BinaryPrimitives.ReadUInt16BigEndian(ReadBytes(reader, 2));
            BinaryPrimitives.ReadUInt32BigEndian(ReadBytes(reader, 4));
            byte[] value = ReadBytes(reader, 4);
            if (tag == 0xB002)
            {
                entryOffset = BinaryPrimitives.ReadUInt32BigEndian(value);
            }
        }

        Assert.That(entryOffset, Is.EqualTo(50u), "MPEntry offset must follow the reference layout");

        reader.BaseStream.Position = mpfAbs + 4 + entryOffset;
        uint entry0Attr = BinaryPrimitives.ReadUInt32BigEndian(ReadBytes(reader, 4));
        uint entry0Size = BinaryPrimitives.ReadUInt32BigEndian(ReadBytes(reader, 4));
        uint entry0Offset = BinaryPrimitives.ReadUInt32BigEndian(ReadBytes(reader, 4));
        ReadBytes(reader, 4);

        uint entry1Attr = BinaryPrimitives.ReadUInt32BigEndian(ReadBytes(reader, 4));
        uint entry1Size = BinaryPrimitives.ReadUInt32BigEndian(ReadBytes(reader, 4));
        uint entry1Offset = BinaryPrimitives.ReadUInt32BigEndian(ReadBytes(reader, 4));

        uint gainAbs = (uint)(mpfAbs + 4 + entry1Offset);
        Assert.Multiple(() =>
        {
            Assert.That(entry0Attr, Is.EqualTo(0x00030000u), "primary MP entry attribute");
            Assert.That(entry0Offset, Is.EqualTo(0u));
            Assert.That(entry0Size, Is.EqualTo(primarySize), "primary image size");
            Assert.That(entry1Attr, Is.EqualTo(0u), "secondary MP entry attribute");
            Assert.That(entry1Size, Is.EqualTo((uint)gainJpeg.Length), "gain map size");
            Assert.That(gainAbs, Is.EqualTo(primarySize), "gain map must start right after the base image");
            Assert.That(written[gainAbs], Is.EqualTo(0xFF), "gain map SOI marker");
            Assert.That(written[gainAbs + 1], Is.EqualTo(0xD8), "gain map SOI marker");
        });

        string xmpText = Encoding.ASCII.GetString(written);
        Assert.That(xmpText, Does.Contain("hdrgm:Version=\\\"1.0\\\"") | Does.Contain("hdrgm:Version=\"1.0\""), "XMP gain-map version");
        Assert.That(xmpText, Does.Contain("hdrgm:GainMapMax"), "XMP GainMapMax");
        Assert.That(xmpText, Does.Contain("http://ns.adobe.com/hdr-gain-map/1.0/"), "XMP gain-map namespace");

        TestContext.Progress.WriteLine(
            $"Ultra HDR JPEG written: {outputPath}\n  {hdr.Width}x{hdr.Height}, sdrWhite={white:F3}, gainMapMax={gainMapMax:F3} (max boost {MathF.Pow(2f, gainMapMax):F1}x), base={baseJpeg.Length}B, gain={gainJpeg.Length}B, total={written.Length}B");
    }

    // ── rendering ────────────────────────────────────────────────────────────

    private static SKBitmap RenderClampedSdr(SKBitmap hdr, float white)
    {
        int w = hdr.Width, h = hdr.Height;
        var result = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque, SKColorSpace.CreateSrgb()));
        ReadOnlySpan<byte> src = hdr.GetPixelSpan();
        Span<byte> dst = result.GetPixelSpan();
        int srcStride = hdr.RowBytes, dstStride = result.RowBytes;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int so = y * srcStride + x * 8;
                int doff = y * dstStride + x * 4;
                float r = MathF.Min(ReadHalf(src, so) / white, 1f);
                float g = MathF.Min(ReadHalf(src, so + 2) / white, 1f);
                float b = MathF.Min(ReadHalf(src, so + 4) / white, 1f);
                dst[doff] = ToByte(SrgbOetf(b));
                dst[doff + 1] = ToByte(SrgbOetf(g));
                dst[doff + 2] = ToByte(SrgbOetf(r));
                dst[doff + 3] = 255;
            }
        }

        return result;
    }

    private static (byte[] Pixels, float GainMapMax) BuildGainMap(SKBitmap hdr, float white)
    {
        int w = hdr.Width, h = hdr.Height;
        ReadOnlySpan<byte> src = hdr.GetPixelSpan();
        int stride = hdr.RowBytes;

        // GainMapMax = log2(peak / white), clamped to a sane range.
        float peak = 0f;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int so = y * stride + x * 8;
                float lum = MathF.Max(ReadHalf(src, so), MathF.Max(ReadHalf(src, so + 2), ReadHalf(src, so + 4)));
                if (lum > peak) peak = lum;
            }
        }

        float gainMapMax = MathF.Max(MathF.Log2(MathF.Max(peak / MathF.Max(white, 1e-3f), 1.0001f)), 0.01f);
        var pixels = new byte[w * h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int so = y * stride + x * 8;
                float r = ReadHalf(src, so);
                float g = ReadHalf(src, so + 2);
                float b = ReadHalf(src, so + 4);
                float hdrLum = MathF.Max(r, MathF.Max(g, b));
                float sdrLum = MathF.Min(hdrLum / white, 1f);

                float logGain = 0f;
                if (sdrLum > 1e-4f && hdrLum > 0f)
                {
                    logGain = MathF.Log2(hdrLum / (sdrLum * white));
                }

                float normalized = Math.Clamp(logGain / gainMapMax, 0f, 1f);
                pixels[y * w + x] = (byte)MathF.Round(normalized * 255f);
            }
        }

        return (pixels, gainMapMax);
    }

    private static byte[] EncodeGainMapJpeg(byte[] pixels, int w, int h, int quality)
    {
        var gray = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        Span<byte> dst = gray.GetPixelSpan();
        int stride = gray.RowBytes;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                byte v = pixels[y * w + x];
                int o = y * stride + x * 4;
                dst[o] = v;
                dst[o + 1] = v;
                dst[o + 2] = v;
                dst[o + 3] = 255;
            }
        }

        using var data = gray.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data!.ToArray();
    }

    private static byte[] EncodeJpeg(SKBitmap bitmap, int quality)
    {
        using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data!.ToArray();
    }

    // ── metadata ─────────────────────────────────────────────────────────────

    private static byte[] BuildXmpPacket(float gainMapMax)
    {
        string gm = gainMapMax.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);
        string xmp =
            "<?xpacket begin=\"\uFEFF\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>" +
            "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">" +
            "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">" +
            "<rdf:Description rdf:about=\"\" " +
            "xmlns:hdrgm=\"http://ns.adobe.com/hdr-gain-map/1.0/\" " +
            "hdrgm:Version=\"1.0\" " +
            $"hdrgm:GainMapMin=\"0.0000\" hdrgm:GainMapMax=\"{gm}\" hdrgm:Gamma=\"1.0000\" " +
            "hdrgm:OffsetSDR=\"0.015625\" hdrgm:OffsetHDR=\"0.015625\" " +
            $"hdrgm:HDRCapacityMin=\"0.0000\" hdrgm:HDRCapacityMax=\"{gm}\" " +
            "hdrgm:BaseRenditionIsHDR=\"False\"/>" +
            "</rdf:RDF></x:xmpmeta><?xpacket end=\"w\"?>";

        byte[] xmpBytes = Encoding.UTF8.GetBytes(xmp);
        byte[] prefix = Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0");
        int payload = prefix.Length + xmpBytes.Length;
        var segment = new byte[2 + 2 + payload];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        segment[2] = (byte)((payload + 2) >> 8);
        segment[3] = (byte)((payload + 2) & 0xFF);
        prefix.CopyTo(segment, 4);
        xmpBytes.CopyTo(segment, 4 + prefix.Length);
        return segment;
    }

    /// <summary>Builds the APP2 MPF segment (big-endian TIFF, reference layout).</summary>
    private static byte[] BuildMpfSegment(uint primarySize, uint secondarySize, uint secondaryOffset)
    {
        const int payloadLen = 86; // "MPF\0" (4) + TIFF (82)
        var segment = new byte[2 + 2 + payloadLen];
        segment[0] = 0xFF;
        segment[1] = 0xE2;
        segment[2] = (byte)((payloadLen + 2) >> 8);
        segment[3] = (byte)((payloadLen + 2) & 0xFF);

        int p = 4;
        "MPF\0"u8.CopyTo(segment.AsSpan(p)); p += 4;

        // TIFF header (big-endian).
        segment[p++] = (byte)'M';
        segment[p++] = (byte)'M';
        WriteU16Be(segment, ref p, 0x002A);
        WriteU32Be(segment, ref p, 8);          // index IFD offset
        WriteU16Be(segment, ref p, 3);          // tag count

        // 0xB000 MPFVersion (UNDEFINED, 4) = "0100"
        WriteU16Be(segment, ref p, 0xB000);
        WriteU16Be(segment, ref p, 7);
        WriteU32Be(segment, ref p, 4);
        segment[p++] = (byte)'0';
        segment[p++] = (byte)'1';
        segment[p++] = (byte)'0';
        segment[p++] = (byte)'0';

        // 0xB001 NumberOfImages (LONG, 1) = 2
        WriteU16Be(segment, ref p, 0xB001);
        WriteU16Be(segment, ref p, 4);
        WriteU32Be(segment, ref p, 1);
        WriteU32Be(segment, ref p, 2);

        // 0xB002 MPEntry (UNDEFINED, 32) at offset 50
        WriteU16Be(segment, ref p, 0xB002);
        WriteU16Be(segment, ref p, 7);
        WriteU32Be(segment, ref p, 32);
        WriteU32Be(segment, ref p, 50);

        // Attribute IFD offset (0 = none).
        WriteU32Be(segment, ref p, 0);

        // Primary entry.
        WriteU32Be(segment, ref p, 0x00030000);  // JPEG format + primary type
        WriteU32Be(segment, ref p, primarySize);
        WriteU32Be(segment, ref p, 0);
        WriteU16Be(segment, ref p, 0);
        WriteU16Be(segment, ref p, 0);

        // Secondary (gain map) entry.
        WriteU32Be(segment, ref p, 0);           // JPEG format
        WriteU32Be(segment, ref p, secondarySize);
        WriteU32Be(segment, ref p, secondaryOffset);
        WriteU16Be(segment, ref p, 0);
        WriteU16Be(segment, ref p, 0);

        Assert.That(p, Is.EqualTo(segment.Length), "MPF layout size");
        return segment;
    }

    private static int FindSosOffset(byte[] jpeg)
    {
        int pos = 2; // after SOI
        while (pos + 4 <= jpeg.Length)
        {
            if (jpeg[pos] != 0xFF)
            {
                pos++;
                continue;
            }

            byte marker = jpeg[pos + 1];
            if (marker == 0xDA)
            {
                return pos;
            }

            if (marker == 0xFF)
            {
                pos++;
                continue;
            }

            if (marker is >= 0xD0 and <= 0xD9 || marker == 0x01)
            {
                pos += 2;
                continue;
            }

            int len = (jpeg[pos + 2] << 8) | jpeg[pos + 3];
            pos += 2 + len;
        }

        return -1;
    }

    private static void WriteU16Be(byte[] buffer, ref int pos, ushort value)
    {
        buffer[pos++] = (byte)(value >> 8);
        buffer[pos++] = (byte)value;
    }

    private static void WriteU32Be(byte[] buffer, ref int pos, uint value)
    {
        buffer[pos++] = (byte)(value >> 24);
        buffer[pos++] = (byte)(value >> 16);
        buffer[pos++] = (byte)(value >> 8);
        buffer[pos++] = (byte)value;
    }

    private static byte[] ReadBytes(BinaryReader reader, int count) => reader.ReadBytes(count);

    private static float ReadHalf(ReadOnlySpan<byte> span, int offset)
    {
        ushort bits = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset, 2));
        float v = (float)BitConverter.UInt16BitsToHalf(bits);
        return float.IsFinite(v) ? MathF.Max(v, 0f) : 0f;
    }

    private static float SrgbOetf(float linear)
    {
        if (linear <= 0.0031308f) return 12.92f * linear;
        return 1.055f * MathF.Pow(linear, 1f / 2.4f) - 0.055f;
    }

    private static byte ToByte(float v) => (byte)MathF.Round(Math.Clamp(v, 0f, 1f) * 255f);

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "XerahS.sln")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }
        return dir ?? throw new DirectoryNotFoundException("repo root not found");
    }
}