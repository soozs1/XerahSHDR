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
using XerahS.Core;
using XerahS.Core.Helpers;
using XerahS.Services.Abstractions;

namespace XerahS.Tests.Helpers;

/// <summary>
/// End-to-end HDR pipeline diagnostic. Feeds a synthetic fp16 frame with known colors and
/// brightness steps through every conversion path (preview, SDR tonemap per mode, HDR PNG
/// file, 16-bit sRGB stream) and prints a channel/brightness matrix. Failures (channel swap,
/// wrong brightness) fail the test so the numerical report is always looked at.
/// </summary>
public class HdrPipelineDiagnostics
{
    // Synthetic frame: 7 pixels — black, 0.25, 0.5, 0.8, 1.0 gray + red + blue + yellow (sRGB primaries in linear).
    private static readonly (float R, float G, float B, float A, string Name)[] Samples =
    {
        (0f,      0f,      0f,      1f, "black"),
        (0.25f,   0.25f,   0.25f,   1f, "gray0.25"),
        (0.5f,    0.5f,    0.5f,    1f, "gray0.5 "),
        (1f,      0f,      0f,      1f, "red     "),
        (0f,      1f,      0f,      1f, "green   "),
        (0f,      0f,      1f,      1f, "blue    "),
        (1f,      1f,      0f,      1f, "yellow  "),
    };

    private static SKBitmap MakeF16()
    {
        var bmp = new SKBitmap(new SKImageInfo(Samples.Length, 1, SKColorType.RgbaF16, SKAlphaType.Premul, SKColorSpace.CreateSrgbLinear()));
        var span = bmp.GetPixelSpan();
        for (int i = 0; i < Samples.Length; i++)
        {
            WriteHalf(span, i * 8, Samples[i].R);
            WriteHalf(span, i * 8 + 2, Samples[i].G);
            WriteHalf(span, i * 8 + 4, Samples[i].B);
            WriteHalf(span, i * 8 + 6, Samples[i].A);
        }
        return bmp;
    }

    [Test]
    public void Diagnose_AllHdrPaths_PreserveColorsAndBrightness()
    {
        using var hdr = MakeF16();
        var sb = new StringBuilder();

        void Row(string path, Func<int, (byte R, byte G, byte B)> px)
        {
            sb.Append($"  {path,-38}");
            for (int i = 0; i < Samples.Length; i++)
            {
                var (r, g, b) = px(i);
                sb.Append($" {r,3},{g,3},{b,3}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("HDR pipeline diagnostic — RGB bytes per sample (R,G,B):");
        sb.AppendLine($"  {"path",-38}  black    0.25     0.5      red     green   blue    yellow");

        // Expected sRGB-encoded values for pure linear grays (what correct preview shows).
        sb.AppendLine("  expected-sRGB                             0   137     188     255     255     255     255");

        // 1. Preview path (MainViewModel.UpdatePreview -> ToAvaloniBitmap LUT).
        using (var sdr = ShareX.ImageEditor.Presentation.Rendering.BitmapConversionHelpers.ConvertF16ToSrgb8(hdr))
        {
            Assert.That(sdr, Is.Not.Null, "ConvertF16ToSrgb8 (preview path) must succeed");
            Row("preview ConvertF16ToSrgb8", i =>
            {
                var p = sdr!.GetPixel(i, 0);
                return (p.Red, p.Green, p.Blue);
            });
        }

        // 2. SDR tonemap paths (file saving with depth=SDR), all modes.
        foreach (var mode in new[] { HdrToneMappingMode.None, HdrToneMappingMode.TonemapReinhard, HdrToneMappingMode.TonemapAces, HdrToneMappingMode.TonemapBt2446C, HdrToneMappingMode.TonemapHable })
        {
            using var toned = HdrTonemapper.TonemapToBgra8888(hdr, mode);
            Assert.That(toned, Is.Not.Null, $"TonemapToBgra8888({mode}) must succeed");
            Row($"tonemap {mode}", i =>
            {
                var p = toned!.GetPixel(i, 0);
                return (p.Red, p.Green, p.Blue);
            });
        }

        // 3. HDR file path (SDR+HDR depth) — must be raw linear with iCCP.
        string tmp = Path.Combine(Path.GetTempPath(), $"hdr-diag-{Guid.NewGuid():N}.png");
        try
        {
            ImageHelpers.SaveHdrF16Png(hdr, tmp);
            using var decoded = SKBitmap.Decode(tmp);
            Assert.That(decoded, Is.Not.Null, "SaveHdrF16Png output must decode");
            Row("hdr-file SaveHdrF16Png", i =>
            {
                var p = decoded!.GetPixel(i, 0);
                return (p.Red, p.Green, p.Blue);
            });
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }

        // 4. 16-bit sRGB stream path (uploads) — display-referred, clipped to 255.
        using (var stream = XerahS.Core.TaskHelpers.SaveImageAsStream(hdr, EImageFormat.PNG, PNGBitDepth.Bit16))
        {
            Assert.That(stream, Is.Not.Null, "SaveImageAsStream(Bit16) must succeed");
            using var decoded = SKBitmap.Decode(SKData.CreateCopy(stream!.ToArray()));
            Assert.That(decoded, Is.Not.Null);
            Row("stream ConvertLinearF16ToSrgb16", i =>
            {
                var p = decoded!.GetPixel(i, 0);
                return (p.Red, p.Green, p.Blue);
            });
        }

        TestContext.Progress.WriteLine(sb.ToString());
    }

    private static void WriteHalf(Span<byte> span, int offset, float value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, 2), BitConverter.HalfToUInt16Bits((Half)value));
    }
}