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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using SkiaSharp;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using ShareX.ImageEditor.Presentation.Rendering;
using ShareX.ImageEditor.Presentation.ViewModels;
using XerahS.Common;
using XerahS.Common.Helpers;
using XerahS.Core;
using XerahS.Core.Helpers;
using XerahS.Platform.Abstractions;
using XerahS.UI.Services;

namespace XerahS.UI.Diagnostics;

/// <summary>
/// Automated end-to-end HDR self-test. Run with <c>XerahS.exe --hdr-self-test &lt;outDir&gt;</c>.
///
/// The test shows a known-color test card on screen, captures it through the real DXGI
/// pipeline (same service the app uses), then verifies every stage numerically:
///  1. capture is fp16 scRGB and contains the card's colors without channel swaps;
///  2. every tone-mapping mode preserves the stripe hue relations and reports clipping;
///  3. file encoders write the expected PNG depth/markers (8-bit SDR vs 16-bit linear HDR);
///  4. the real Save-As path (MainViewModelHelper + MainViewModel preview) writes content that
///     matches the capture — catching stale-preview / wrong-source regressions;
///  5. preview conversion (ConvertF16ToSrgb8) matches TonemapToBgra8888(None) within rounding.
///
/// Results are written to report.txt / report.json next to the dumped stage images.
/// Exit code 0 = all checks passed, non-zero = failures.
/// </summary>
public static class HdrSelfTestRunner
{
    private static readonly (string Name, Color Color)[] Stripes =
    {
        ("red",     Color.FromRgb(255, 0, 0)),
        ("green",   Color.FromRgb(0, 255, 0)),
        ("blue",    Color.FromRgb(0, 0, 255)),
        ("cyan",    Color.FromRgb(0, 255, 255)),
        ("magenta", Color.FromRgb(255, 0, 255)),
        ("yellow",  Color.FromRgb(255, 255, 0)),
        ("white",   Color.FromRgb(255, 255, 255)),
        ("gray50",  Color.FromRgb(128, 128, 128)),
    };

    private sealed class CardGeometry
    {
        public Window Window { get; init; } = null!;
        public int Left { get; init; }
        public int Top { get; init; }
        public int WidthPx { get; init; }
        public int HeightPx { get; init; }
    }

    public static async Task<int> RunAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var report = new List<string>();
        var metrics = new Dictionary<string, object>();
        bool pass = true;

        void Check(bool condition, string name, string detail = "")
        {
            pass &= condition;
            report.Add($"[{(condition ? "PASS" : "FAIL")}] {name}{(string.IsNullOrEmpty(detail) ? string.Empty : "  — " + detail)}");
            DebugHelper.WriteLine($"[HdrSelfTest] {(condition ? "PASS" : "FAIL")} {name} {detail}");
        }

        CardGeometry? card = null;
        try
        {
            // Hide the main window so the app UI does not cover the card.
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                {
                    lifetime.MainWindow?.Hide();
                }
            });

            card = await Dispatcher.UIThread.InvokeAsync(ShowTestCard);
            await Task.Delay(600); // let the compositor settle

            var cardRect = new SKRectI(card.Left, card.Top, card.Left + card.WidthPx, card.Top + card.HeightPx);
            using var capture = await PlatformServices.ScreenCapture.CaptureRectAsync(new SKRect(cardRect.Left, cardRect.Top, cardRect.Right, cardRect.Bottom));
            Check(capture != null, "capture-not-null");
            if (capture == null)
            {
                return Finish(outputDir, report, metrics, pass);
            }

            bool isHdr = capture.ColorType == SKColorType.RgbaF16;
            report.Add($"capture: {capture.Width}x{capture.Height} {capture.ColorType} alpha={capture.AlphaType} rect={cardRect}");
            Check(isHdr, "capture-is-rgba-f16", capture.ColorType.ToString());

            DumpCapture(capture, outputDir);

            // ── 1. Stripe colors in the raw capture ───────────────────────────
            var rawMeans = SampleStripes(capture, card, cardRect.Left, cardRect.Top);
            metrics["captureStripes"] = rawMeans.ToDictionary(kv => kv.Key, kv => kv.Value.Select(v => Math.Round(v, 4)).ToArray());
            report.Add("raw stripe means (R,G,B linear):");
            foreach (var kv in rawMeans)
            {
                report.Add($"  {kv.Key,-8} ({kv.Value[0]:F3}, {kv.Value[1]:F3}, {kv.Value[2]:F3})");
            }

            CheckStripes(rawMeans, Check, "capture");

            // ── 2. Tone mapping: hue preservation + clipping metrics ──────────
            foreach (var mode in new[]
            {
                HdrToneMappingMode.None,
                HdrToneMappingMode.TonemapReinhard,
                HdrToneMappingMode.TonemapAces,
                HdrToneMappingMode.TonemapBt2446C,
            })
            {
                using var toned = HdrTonemapper.TonemapToBgra8888(capture, mode);
                if (toned == null)
                {
                    Check(false, $"tonemap-{mode}-not-null");
                    continue;
                }

                SavePng(toned, Path.Combine(outputDir, $"tonemap-{mode}.png"));

                var means = SampleStripes(toned, card, cardRect.Left, cardRect.Top);
                var (clipFraction, meanLuminance) = AnalyzeToned(toned);
                metrics[$"tonemap-{mode}"] = new
                {
                    stripes = means.ToDictionary(kv => kv.Key, kv => kv.Value.Select(v => Math.Round(v, 4)).ToArray()),
                    clippedFraction = Math.Round(clipFraction, 5),
                    meanLuminance = Math.Round(meanLuminance, 5),
                };
                report.Add($"tonemap {mode}: meanLum={meanLuminance:F3} clipped={clipFraction:P2}");

                CheckStripes(means, Check, $"tonemap-{mode}");
            }

            // ── 3. File encoders: bit depth and markers ───────────────────────
            var settingsSdr = new TaskSettings
            {
                ImageSettings = new TaskSettingsImage
                {
                    ImagePNGBitDepth = PNGBitDepth.Default,
                    ImageHdrSaveToneMapping = HdrToneMappingMode.TonemapBt2446C,
                },
            };
            var settingsHdr = new TaskSettings
            {
                ImageSettings = new TaskSettingsImage
                {
                    ImagePNGBitDepth = PNGBitDepth.Bit16,
                    ImageHdrSaveToneMapping = HdrToneMappingMode.None,
                },
            };

            string sdrPath = Path.Combine(outputDir, "encoder-sdr.png");
            using (var sdrBitmap = HdrTonemapper.TonemapToBgra8888(capture, settingsSdr.ImageSettings.ImageHdrSaveToneMapping))
            {
                Check(sdrBitmap != null, "encoder-sdr-tonemap");
                if (sdrBitmap != null)
                {
                    ImageHelpers.SaveBitmap(sdrBitmap, sdrPath, PNGBitDepth.Default);
                    CheckPng(sdrPath, expect16Bit: false, Check, "encoder-sdr");
                }
            }

            string hdrPath = Path.Combine(outputDir, "encoder-hdr.png");
            ImageHelpers.SaveBitmap(capture, hdrPath, PNGBitDepth.Bit16);
            CheckPng(hdrPath, expect16Bit: true, Check, "encoder-hdr");

            // ── 4. Real Save-As content path ──────────────────────────────────
            var editorViewModel = new MainViewModel(ThemeService.CreateImageEditorOptions());
            editorViewModel.UpdatePreview(capture.Copy()!);

            MainViewModelHelper.LastCaptureTaskSettings = settingsSdr;
            string saveSdrPath = Path.Combine(outputDir, "saveas-sdr.png");
            string? savedSdr = await MainViewModelHelper.SaveToPathForDiagnosticsAsync(editorViewModel, saveSdrPath);
            Check(savedSdr != null && File.Exists(saveSdrPath), "saveas-sdr-file-written", savedSdr ?? "<null>");
            if (File.Exists(saveSdrPath))
            {
                CheckPng(saveSdrPath, expect16Bit: false, Check, "saveas-sdr");
                CompareWithTonemap(saveSdrPath, capture, settingsSdr.ImageSettings.ImageHdrSaveToneMapping, Check, "saveas-sdr");
            }

            MainViewModelHelper.LastCaptureTaskSettings = settingsHdr;
            string saveHdrPath = Path.Combine(outputDir, "saveas-hdr.png");
            string? savedHdr = await MainViewModelHelper.SaveToPathForDiagnosticsAsync(editorViewModel, saveHdrPath);
            Check(savedHdr != null && File.Exists(saveHdrPath), "saveas-hdr-file-written", savedHdr ?? "<null>");
            if (File.Exists(saveHdrPath))
            {
                CheckPng(saveHdrPath, expect16Bit: true, Check, "saveas-hdr");
            }

            // The preview/source must still be the capture (no stale-image regression).
            using (var sourceCopy = editorViewModel.CreateSourceImageCopyForPersistence())
            {
                string sourceSig = SavePathDebugDump.Signature(sourceCopy);
                string captureSig = SavePathDebugDump.Signature(capture);
                Check(sourceSig == captureSig, "preview-source-matches-capture", $"{sourceSig} vs {captureSig}");
                metrics["previewSignature"] = sourceSig;
                metrics["captureSignature"] = captureSig;
            }

            // ── 5. Preview conversion matches Tonemap(None) ───────────────────
            using (var lut = BitmapConversionHelpers.ConvertF16ToSrgb8(capture))
            using (var none = HdrTonemapper.TonemapToBgra8888(capture, HdrToneMappingMode.None))
            {
                if (lut != null && none != null)
                {
                    double meanDiff = MeanAbsRgbDiff(lut, none);
                    metrics["previewVsNoneMeanDiff"] = Math.Round(meanDiff, 4);
                    Check(meanDiff <= 1.5, "preview-matches-tonemap-none", $"mean|dRGB|={meanDiff:F3}");
                }
            }
        }
        catch (Exception ex)
        {
            Check(false, "self-test-exception", ex.ToString());
        }
        finally
        {
            if (card != null)
            {
                await Dispatcher.UIThread.InvokeAsync(() => card.Window.Close());
            }
        }

        return Finish(outputDir, report, metrics, pass);
    }

    // ── test card ────────────────────────────────────────────────────────────

    private static CardGeometry ShowTestCard()
    {
        var grid = new Grid();
        for (int i = 0; i < Stripes.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var border = new Border { Background = new SolidColorBrush(Stripes[i].Color) };
            Grid.SetColumn(border, i);
            grid.Children.Add(border);
        }

        var window = new Window
        {
            CanResize = false,
            Topmost = true,
            ShowInTaskbar = false,
            Width = 960,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Position = new PixelPoint(0, 0),
            Content = grid,
        };
        window.Show();

        double scale = window.RenderScaling > 0 ? window.RenderScaling : 1.0;
        return new CardGeometry
        {
            Window = window,
            Left = window.Position.X,
            Top = window.Position.Y,
            WidthPx = (int)Math.Round(window.Width * scale),
            HeightPx = (int)Math.Round(window.Height * scale),
        };
    }

    // ── sampling ─────────────────────────────────────────────────────────────

    private static Dictionary<string, double[]> SampleStripes(SKBitmap bitmap, CardGeometry card, int originX, int originY)
    {
        var result = new Dictionary<string, double[]>();
        double bandY = card.Top - originY + card.HeightPx / 2.0;
        double y0 = Math.Clamp(bandY - card.HeightPx * 0.2, 0, bitmap.Height - 2);
        double y1 = Math.Clamp(bandY + card.HeightPx * 0.2, 1, bitmap.Height - 1);

        for (int i = 0; i < Stripes.Length; i++)
        {
            double x0 = card.Left - originX + (i + 0.25) * (card.WidthPx / (double)Stripes.Length);
            double x1 = card.Left - originX + (i + 0.75) * (card.WidthPx / (double)Stripes.Length);
            var (r, g, b) = MeanRegion(bitmap, x0, y0, x1, y1);
            result[Stripes[i].Name] = new[] { r, g, b };
        }

        return result;
    }

    private static (double R, double G, double B) MeanRegion(SKBitmap bitmap, double x0, double y0, double x1, double y1)
    {
        int ix0 = Math.Clamp((int)x0, 0, bitmap.Width - 1);
        int ix1 = Math.Clamp((int)x1, ix0 + 1, bitmap.Width);
        int iy0 = Math.Clamp((int)y0, 0, bitmap.Height - 1);
        int iy1 = Math.Clamp((int)y1, iy0 + 1, bitmap.Height);

        int stepX = Math.Max(1, (ix1 - ix0) / 24);
        int stepY = Math.Max(1, (iy1 - iy0) / 8);

        double sr = 0, sg = 0, sb = 0;
        int n = 0;
        Span<byte> span = bitmap.GetPixelSpan();
        int stride = bitmap.RowBytes;

        for (int y = iy0; y < iy1; y += stepY)
        {
            for (int x = ix0; x < ix1; x += stepX)
            {
                int o = y * stride + x;
                if (bitmap.ColorType == SKColorType.RgbaF16)
                {
                    o *= 8;
                    sr += DecodeHalf(span, o);
                    sg += DecodeHalf(span, o + 2);
                    sb += DecodeHalf(span, o + 4);
                }
                else
                {
                    o *= 4;
                    sr += span[o + 2] / 255.0;
                    sg += span[o + 1] / 255.0;
                    sb += span[o] / 255.0;
                }
                n++;
            }
        }

        return n == 0 ? (0, 0, 0) : (sr / n, sg / n, sb / n);
    }

    private static double DecodeHalf(ReadOnlySpan<byte> span, int offset)
    {
        ushort bits = (ushort)(span[offset] | (span[offset + 1] << 8));
        float v = (float)BitConverter.UInt16BitsToHalf(bits);
        return float.IsFinite(v) ? Math.Clamp(v, 0f, 1f) : 0f;
    }

    // ── checks ───────────────────────────────────────────────────────────────

    private static void CheckStripes(Dictionary<string, double[]> means, Action<bool, string, string> check, string stage)
    {
        static bool Dom(double[] c, int idx, double relMargin = 0.25)
        {
            double max = Math.Max(c[0], Math.Max(c[1], c[2]));
            return max > 0.02 && c[idx] >= max * (1 - relMargin);
        }

        static bool Suppressed(double[] c, int[] idxs, double factor = 0.35)
        {
            double max = Math.Max(c[0], Math.Max(c[1], c[2]));
            foreach (int i in idxs)
            {
                if (c[i] > max * factor + 0.02) return false;
            }
            return true;
        }

        static bool Neutral(double[] c, double tolerance = 0.06)
        {
            double max = Math.Max(c[0], Math.Max(c[1], c[2]));
            double min = Math.Min(c[0], Math.Min(c[1], c[2]));
            return max > 0.02 && (max - min) <= max * tolerance + 0.01;
        }

        if (means.TryGetValue("red", out var red))
            check(Dom(red, 0) && Suppressed(red, new[] { 1, 2 }), $"{stage}:stripe-red-keeps-R", $"({red[0]:F3},{red[1]:F3},{red[2]:F3})");
        if (means.TryGetValue("green", out var green))
            check(Dom(green, 1) && Suppressed(green, new[] { 0, 2 }), $"{stage}:stripe-green-keeps-G", $"({green[0]:F3},{green[1]:F3},{green[2]:F3})");
        if (means.TryGetValue("blue", out var blue))
            check(Dom(blue, 2) && Suppressed(blue, new[] { 0, 1 }), $"{stage}:stripe-blue-keeps-B", $"({blue[0]:F3},{blue[1]:F3},{blue[2]:F3})");
        if (means.TryGetValue("cyan", out var cyan))
            check(Dom(cyan, 1) && Dom(cyan, 2) && Suppressed(cyan, new[] { 0 }), $"{stage}:stripe-cyan", $"({cyan[0]:F3},{cyan[1]:F3},{cyan[2]:F3})");
        if (means.TryGetValue("magenta", out var magenta))
            check(Dom(magenta, 0) && Dom(magenta, 2) && Suppressed(magenta, new[] { 1 }), $"{stage}:stripe-magenta", $"({magenta[0]:F3},{magenta[1]:F3},{magenta[2]:F3})");
        if (means.TryGetValue("yellow", out var yellow))
            check(Dom(yellow, 0) && Dom(yellow, 1) && Suppressed(yellow, new[] { 2 }), $"{stage}:stripe-yellow", $"({yellow[0]:F3},{yellow[1]:F3},{yellow[2]:F3})");
        if (means.TryGetValue("white", out var white))
            check(Neutral(white), $"{stage}:stripe-white-neutral", $"({white[0]:F3},{white[1]:F3},{white[2]:F3})");
        if (means.TryGetValue("gray50", out var gray))
            check(Neutral(gray), $"{stage}:stripe-gray-neutral", $"({gray[0]:F3},{gray[1]:F3},{gray[2]:F3})");
    }

    private static (double ClippedFraction, double MeanLuminance) AnalyzeToned(SKBitmap bitmap)
    {
        Span<byte> span = bitmap.GetPixelSpan();
        int stride = bitmap.RowBytes;
        int w = bitmap.Width, h = bitmap.Height;
        int step = Math.Max(1, Math.Max(w, h) / 200);
        long n = 0, clipped = 0;
        double lum = 0;

        for (int y = 0; y < h; y += step)
        {
            for (int x = 0; x < w; x += step)
            {
                int o = y * stride + x * 4;
                byte b = span[o], g = span[o + 1], r = span[o + 2];
                if (r >= 250 && g >= 250 && b >= 250) clipped++;
                lum += (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
                n++;
            }
        }

        return n == 0 ? (0, 0) : (clipped / (double)n, lum / n);
    }

    private static void CheckPng(string path, bool expect16Bit, Action<bool, string, string> check, string stage)
    {
        byte[] bytes = File.ReadAllBytes(path);
        bool is16 = bytes.Length > 25 && bytes[24] == 16;
        bool hasIccp = HasPngChunk(bytes, "iCCP");
        bool hasSrgb = HasPngChunk(bytes, "sRGB");
        check(is16 == expect16Bit, $"{stage}:bit-depth", $"16bit={is16} expected={expect16Bit}");
        if (expect16Bit)
        {
            check(hasIccp && !hasSrgb, $"{stage}:hdr-profile", $"iCCP={hasIccp} sRGB={hasSrgb}");
        }
        else
        {
            check(!is16, $"{stage}:sdr-depth", $"bitDepth={bytes[24]}");
        }
    }

    private static void CompareWithTonemap(string path, SKBitmap capture, HdrToneMappingMode mode, Action<bool, string, string> check, string stage)
    {
        using var saved = SKBitmap.Decode(path);
        using var expected = HdrTonemapper.TonemapToBgra8888(capture, mode);
        if (saved == null || expected == null)
        {
            check(false, $"{stage}:compare-decode", string.Empty);
            return;
        }

        double diff = MeanAbsRgbDiff(saved, expected);
        check(diff <= 2.0, $"{stage}:content-matches-tonemap", $"mean|dRGB|={diff:F3} (mode={mode})");
    }

    private static double MeanAbsRgbDiff(SKBitmap a, SKBitmap b)
    {
        if (a.ColorType != SKColorType.Bgra8888 || b.ColorType != SKColorType.Bgra8888)
        {
            return double.MaxValue;
        }

        Span<byte> sa = a.GetPixelSpan();
        Span<byte> sb = b.GetPixelSpan();
        int strideA = a.RowBytes, strideB = b.RowBytes;
        int w = Math.Min(a.Width, b.Width), h = Math.Min(a.Height, b.Height);
        int step = Math.Max(1, Math.Max(w, h) / 400);
        long n = 0, sum = 0;

        for (int y = 0; y < h; y += step)
        {
            for (int x = 0; x < w; x += step)
            {
                int oa = y * strideA + x * 4;
                int ob = y * strideB + x * 4;
                sum += Math.Abs(sa[oa] - sb[ob]) + Math.Abs(sa[oa + 1] - sb[ob + 1]) + Math.Abs(sa[oa + 2] - sb[ob + 2]);
                n++;
            }
        }

        return n == 0 ? double.MaxValue : sum / (double)n;
    }

    private static bool HasPngChunk(byte[] png, string chunkName)
    {
        int pos = 8;
        while (pos + 8 <= png.Length)
        {
            uint len = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos, 4));
            string name = Encoding.ASCII.GetString(png, pos + 4, 4);
            if (name == chunkName) return true;
            if (name == "IEND") break;
            pos += 12 + (int)len;
        }
        return false;
    }

    // ── io ───────────────────────────────────────────────────────────────────

    private static void DumpCapture(SKBitmap capture, string outputDir)
    {
        if (capture.ColorType == SKColorType.RgbaF16)
        {
            ImageHelpers.SaveHdrF16Png(capture, Path.Combine(outputDir, "capture-raw16.png"));
        }
        else
        {
            SavePng(capture, Path.Combine(outputDir, "capture-raw8.png"));
        }
    }

    private static void SavePng(SKBitmap bitmap, string path)
    {
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        if (data != null)
        {
            File.WriteAllBytes(path, data.ToArray());
        }
    }

    private static int Finish(string outputDir, List<string> report, Dictionary<string, object> metrics, bool pass)
    {
        report.Insert(0, pass ? "HDR SELF-TEST: PASS" : "HDR SELF-TEST: FAIL");
        string txt = string.Join(Environment.NewLine, report);
        File.WriteAllText(Path.Combine(outputDir, "report.txt"), txt);
        File.WriteAllText(
            Path.Combine(outputDir, "report.json"),
            JsonSerializer.Serialize(new { pass, lines = report, metrics }, new JsonSerializerOptions { WriteIndented = true }));
        DebugHelper.WriteLine($"[HdrSelfTest] report written to {outputDir}; result={(pass ? "PASS" : "FAIL")}");
        DebugHelper.Flush();
        return pass ? 0 : 1;
    }
}