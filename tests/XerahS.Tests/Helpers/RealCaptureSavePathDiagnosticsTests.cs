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

using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using NUnit.Framework;
using ShareX.ImageEditor.Presentation.Rendering;
using SkiaSharp;
using System.Buffers.Binary;
using System.Text;
using XerahS.Common;
using XerahS.Core.Helpers;

namespace XerahS.Tests.Helpers;

/// <summary>
/// Real-capture round-trip diagnostics. Uses the actual 16-bit raw HDR capture
/// (20260819T2228) saved by the pipeline and the 8-bit Save-As export of the SAME
/// capture (image_none.png). Replays the exact conversion chains and compares
/// per-pixel on flat regions, so the numbers (not eyes) decide whether:
///   a) the Save-As export equals a deterministic chain of the raw capture (and which),
///   b) the Avalonia RTB render path (EditorView.GetSnapshot) reintroduces a color shift.
/// See AGENTS.md §"HDR" — never judge color by eye; always run this matrix first.
/// </summary>
public class RealCaptureSavePathDiagnosticsTests
{
    private static readonly string TestDataDir = FindRepoRoot("XerahS.sln");

    private static string FindRepoRoot(string marker)
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, marker)))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }
        if (dir == null)
        {
            throw new DirectoryNotFoundException($"Could not find {marker} above {AppContext.BaseDirectory}");
        }
        return Path.Combine(dir, "tests", "XerahS.Tests", "TestData", "HdrCapture");
    }

    private static string Raw16Path => Path.Combine(TestDataDir, "raw16_20260819T2228.png");
    private static string SaveNonePath => Path.Combine(TestDataDir, "save_none_20260819T2228.png");
    private static string Pipeline8Path => Path.Combine(TestDataDir, "pipeline8_20260819T2226.png");
    private static string SaveMethodCPath => Path.Combine(TestDataDir, "save_methodC_20260819T2226.png");

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Decodes the pipeline's 16-bit HDR PNG (raw linear u16 BE samples, iCCP linear)
    /// back into an RgbaF16 bitmap.</summary>
    private static SKBitmap LoadRaw16ToF16(string path)
    {
        var bytes = File.ReadAllBytes(path);
        int pos = 8;
        int width = 0, height = 0, bitDepth = 0, colorType = 0;
        byte[] idat = Array.Empty<byte>();
        while (pos + 8 <= bytes.Length)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(pos, 4));
            string name = Encoding.ASCII.GetString(bytes, pos + 4, 4);
            if (name == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(pos + 8, 4));
                height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(pos + 12, 4));
                bitDepth = bytes[pos + 16];
                colorType = bytes[pos + 17];
            }
            else if (name == "IDAT")
            {
                var merged = new byte[idat.Length + len];
                Buffer.BlockCopy(idat, 0, merged, 0, idat.Length);
                Buffer.BlockCopy(bytes, pos + 8, merged, idat.Length, len);
                idat = merged;
            }
            pos += 12 + len;
            if (name == "IEND") break;
        }

        Assert.That(bitDepth, Is.EqualTo(16));
        Assert.That(colorType, Is.EqualTo(6), "RGBA");

        var raw = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(new MemoryStream(idat), System.IO.Compression.CompressionMode.Decompress))
        {
            zlib.CopyTo(raw);
        }
        byte[] scanlines = raw.ToArray();

        var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.RgbaF16, SKAlphaType.Premul, SKColorSpace.CreateSrgbLinear()));
        Span<byte> dst = bmp.GetPixelSpan();
        int dstStride = bmp.RowBytes;
        int rowBytes = width * 8;
        for (int y = 0; y < height; y++)
        {
            int rowStart = y * (rowBytes + 1);
            for (int x = 0; x < width; x++)
            {
                for (int c = 0; c < 4; c++)
                {
                    ushort u = (ushort)((scanlines[rowStart + 1 + x * 8 + c * 2] << 8) | scanlines[rowStart + 1 + x * 8 + c * 2 + 1]);
                    ushort half = BitConverter.HalfToUInt16Bits((Half)(u / 65535f));
                    BinaryPrimitives.WriteUInt16LittleEndian(dst.Slice(y * dstStride + x * 8 + c * 2, 2), half);
                }
            }
        }
        return bmp;
    }

    private static SKBitmap Load8(string path) => SKBitmap.Decode(path);

    private static (byte R, byte G, byte B)[] ToRgbArray(SKBitmap bmp)
    {
        var result = new (byte R, byte G, byte B)[bmp.Width * bmp.Height];
        if (bmp.ColorType == SKColorType.Bgra8888)
        {
            Span<byte> span = bmp.GetPixelSpan();
            int stride = bmp.RowBytes;
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    int o = y * stride + x * 4;
                    result[y * bmp.Width + x] = (span[o + 2], span[o + 1], span[o]);
                }
            }
        }
        else
        {
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    var p = bmp.GetPixel(x, y);
                    result[y * bmp.Width + x] = (p.Red, p.Green, p.Blue);
                }
            }
        }
        return result;
    }

    private static string Compare(string label, (byte R, byte G, byte B)[] got, (byte R, byte G, byte B)[] expected, int w, int h)
    {
        var sb = new StringBuilder();
        sb.AppendLine(label);
        // flat mask on BOTH
        static bool[] Flat((byte R, byte G, byte B)[] px, int w, int h)
        {
            var mask = new bool[px.Length];
            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    int o = y * w + x;
                    var c = px[o];
                    bool same = true;
                    for (int dy = -1; dy <= 1 && same; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            var n = px[(y + dy) * w + (x + dx)];
                            if (n.R != c.R || n.G != c.G || n.B != c.B) { same = false; break; }
                        }
                    }
                    mask[o] = same;
                }
            }
            return mask;
        }
        bool[] mA = Flat(got, w, h), mB = Flat(expected, w, h);
        long n = 0, sum = 0, brSum = 0;
        var buckets = new long[16 * 3]; // per-channel, bucket = value/16
        for (int i = 0; i < got.Length; i++)
        {
            if (!(mA[i] && mB[i])) continue;
            var a = got[i]; var b = expected[i];
            sum += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
            brSum += b.R - b.B;
            n++;
            buckets[(a.R >> 4) * 3 + 0] += a.R - b.R;
            buckets[(a.G >> 4) * 3 + 1] += a.G - b.G;
            buckets[(a.B >> 4) * 3 + 2] += a.B - b.B;
        }
        var cnt = new long[16];
        for (int i = 0; i < 16; i++) cnt[i] = 1;
        sb.AppendLine($"  flat pixels: {n}; mean|dRGB| = {(n > 0 ? (double)sum / n : 0):F3}; mean R-B(expected) = {(n > 0 ? (double)brSum / n : 0):+0.0}");
        return sb.ToString();
    }

    private static void AssertDataAvailable()
    {
        bool ok = Directory.Exists(TestDataDir)
            && File.Exists(Raw16Path)
            && File.Exists(SaveNonePath);
        if (!ok)
        {
            Assert.Ignore($"Real capture test data not present in {TestDataDir}. " +
                "These diagnostics require the actual 16-bit raw capture + Save-As export " +
                "(see AGENTS.md §HDR) and are skipped on fresh clones.");
        }
    }

    // ── tests ────────────────────────────────────────────────────────────────

    [Test]
    public void Diagnose_WhichChain_MatchesTheRealSaveAs()
    {
        AssertDataAvailable();
        Assert.That(File.Exists(Raw16Path), Is.True, $"missing test data {Raw16Path}");
        Assert.That(File.Exists(SaveNonePath), Is.True, $"missing test data {SaveNonePath}");

        using var raw = LoadRaw16ToF16(Raw16Path);
        using var save = Load8(SaveNonePath);
        Assert.That(save.Width, Is.EqualTo(raw.Width));
        Assert.That(save.Height, Is.EqualTo(raw.Height));
        var expected = ToRgbArray(save);

        var sb = new StringBuilder();
        sb.AppendLine($"=== Real capture {raw.Width}x{raw.Height}: which chain produces image_none.png? ===");

        // 1. TonemapToBgra8888 with every mode.
        foreach (var mode in new[] { HdrToneMappingMode.None, HdrToneMappingMode.TonemapReinhard, HdrToneMappingMode.TonemapAces, HdrToneMappingMode.TonemapBt2446C })
        {
            using var toned = HdrTonemapper.TonemapToBgra8888(raw, mode);
            Assert.That(toned, Is.Not.Null);
            sb.Append(Compare($"tonemap {mode}", ToRgbArray(toned!), expected, raw.Width, raw.Height));
        }

        // 2. Pure sRGB OETF (preview LUT).
        using (var lut = BitmapConversionHelpers.ConvertF16ToSrgb8(raw))
        {
            Assert.That(lut, Is.Not.Null);
            sb.Append(Compare("ConvertF16ToSrgb8", ToRgbArray(lut!), expected, raw.Width, raw.Height));
        }

        TestContext.Progress.WriteLine(sb.ToString());
    }

    [AvaloniaTest]
    public void Diagnose_AvaloniaRtbRender_OfPreview_MatchesRealSaveAs()
    {
        AssertDataAvailable();
        Assert.That(File.Exists(Raw16Path), Is.True);
        Assert.That(File.Exists(SaveNonePath), Is.True);

        using var raw = LoadRaw16ToF16(Raw16Path);
        using var save = Load8(SaveNonePath);
        var expected = ToRgbArray(save);

        var sb = new StringBuilder();
        sb.AppendLine($"=== Avalonia RTB render of the tonemapped preview vs image_none.png ===");

        foreach (var mode in new[] { HdrToneMappingMode.None, HdrToneMappingMode.TonemapReinhard, HdrToneMappingMode.TonemapAces, HdrToneMappingMode.TonemapBt2446C })
        {
            using var toned = HdrTonemapper.TonemapToBgra8888(raw, mode)!;

            // Recreate exactly what the editor does: SKBitmap -> Avalonia WriteableBitmap.
            using var wb = (WriteableBitmap)BitmapConversionHelpers.ToAvaloniBitmap(toned);
            var image = new Image { Source = wb, Width = wb.PixelSize.Width, Height = wb.PixelSize.Height };

            // Measure/layout as the editor's GetSnapshot does.
            image.Measure(global::Avalonia.Size.Infinity);
            image.Arrange(new global::Avalonia.Rect(0, 0, wb.PixelSize.Width, wb.PixelSize.Height));

            using var rtb = new RenderTargetBitmap(new global::Avalonia.PixelSize(wb.PixelSize.Width, wb.PixelSize.Height), new global::Avalonia.Vector(96, 96));
            rtb.Render(image);
            using var sk = BitmapConversionHelpers.ToSKBitmap(rtb);
            using var encoded = sk.Encode(SKEncodedImageFormat.Png, 100);
            var pngPath = Path.Combine(Path.GetTempPath(), $"rtb-{mode}.png");
            File.WriteAllBytes(pngPath, encoded.ToArray());

            sb.Append(Compare($"rtb-render({mode})", ToRgbArray(sk), expected, raw.Width, raw.Height));
        }

        // Also render the LUT-converted preview (what depth=Bit16 shows while editing).
        using (var lut = BitmapConversionHelpers.ConvertF16ToSrgb8(raw)!)
        using (var wb = (WriteableBitmap)BitmapConversionHelpers.ToAvaloniBitmap(lut))
        {
            var image = new Image { Source = wb, Width = wb.PixelSize.Width, Height = wb.PixelSize.Height };
            image.Measure(global::Avalonia.Size.Infinity);
            image.Arrange(new global::Avalonia.Rect(0, 0, wb.PixelSize.Width, wb.PixelSize.Height));
            using var rtb = new RenderTargetBitmap(new global::Avalonia.PixelSize(wb.PixelSize.Width, wb.PixelSize.Height), new global::Avalonia.Vector(96, 96));
            rtb.Render(image);
            using var sk = BitmapConversionHelpers.ToSKBitmap(rtb);
            sb.Append(Compare("rtb-render(ConvertF16ToSrgb8)", ToRgbArray(sk), expected, raw.Width, raw.Height));
        }

        TestContext.Progress.WriteLine(sb.ToString());
    }
}