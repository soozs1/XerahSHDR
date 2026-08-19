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

using SkiaSharp;
using XerahS.Common;

namespace XerahS.UI.Services;

/// <summary>
/// TEMPORARY diagnostic dump for the Save-As HDR color investigation (2026-08-19).
/// Writes each intermediate bitmap of the Save-As chain to
/// %USERPROFILE%\Documents\XerahS\DebugDumps so the corrupting stage can be pinned
/// pixel-exactly against a fresh capture. Remove this file once the issue is fixed.
/// </summary>
public static class SavePathDebugDump
{
    public static bool Enabled { get; } = true;

    /// <summary>Last content signature logged by the preview pipeline, to detect a stale
    /// (non-current-capture) source at Save-As time.</summary>
    public static string? LastPreviewSignature { get; set; }

    /// <summary>
    /// Small deterministic content signature: mean of a few sampled pixels. Cheap and stable
    /// within one frame, distinct across different frames.
    /// </summary>
    public static string Signature(SKBitmap? bitmap)
    {
        if (bitmap == null) return "<null>";
        double sr = 0, sg = 0, sb = 0;
        int n = 0;
        int w = bitmap.Width, h = bitmap.Height;
        Span<byte> span = bitmap.GetPixelSpan();
        int stride = bitmap.RowBytes;
        int step = Math.Max(1, Math.Max(w, h) / 40);
        for (int y = 0; y < h; y += step)
        {
            for (int x = 0; x < w; x += step)
            {
                int o = y * stride + x;
                if (bitmap.ColorType == SKColorType.RgbaF16)
                {
                    o *= 8;
                    sr += DecodeClampedHalf(span, o);
                    sg += DecodeClampedHalf(span, o + 2);
                    sb += DecodeClampedHalf(span, o + 4);
                }
                else
                {
                    o *= 4;
                    sr += span[o + 2] / 255.0; // BGRA -> R
                    sg += span[o + 1] / 255.0;
                    sb += span[o] / 255.0;
                }
                n++;
            }
        }
        return n == 0 ? "<empty>" : $"{bitmap.Width}x{bitmap.Height} m={sr / n:F3},{sg / n:F3},{sb / n:F3} ct={bitmap.ColorType}";
    }

    private static double DecodeClampedHalf(ReadOnlySpan<byte> span, int offset)
    {
        ushort bits = (ushort)(span[offset] | (span[offset + 1] << 8));
        float v = (float)BitConverter.UInt16BitsToHalf(bits);
        return float.IsFinite(v) ? Math.Clamp(v, 0f, 1f) : 0f;
    }

    private static string DumpDir
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "XerahS", "DebugDumps");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static void Info(string stage, SKBitmap? bitmap)
    {
        if (!Enabled) return;
        if (bitmap == null)
        {
            DebugHelper.WriteLine($"[SavePathDebug] {stage}: <null>");
            return;
        }
        DebugHelper.WriteLine($"[SavePathDebug] {stage}: {bitmap.Width}x{bitmap.Height} ColorType={bitmap.ColorType} AlphaType={bitmap.AlphaType} ColorSpace={bitmap.ColorSpace?.ToString() ?? "null"}");
    }

    public static void Dump(string stage, SKBitmap? bitmap)
    {
        if (!Enabled) return;
        if (bitmap == null)
        {
            DebugHelper.WriteLine($"[SavePathDebug] dump {stage}: <null>");
            return;
        }

        try
        {
            if (bitmap.ColorType == SKColorType.RgbaF16)
            {
                // Preserve the exact raw samples so the dump can be replayed byte-for-byte.
                string rawPath = Path.Combine(DumpDir, $"{DateTime.Now:HHmmss}_{stage}_raw16.png");
                XerahS.Common.ImageHelpers.SaveHdrF16Png(bitmap, rawPath);
                DebugHelper.WriteLine($"[SavePathDebug] dumped {stage} -> {rawPath}");
                return;
            }

            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            if (data != null)
            {
                string path = Path.Combine(DumpDir, $"{DateTime.Now:HHmmss}_{stage}.png");
                File.WriteAllBytes(path, data.ToArray());
                DebugHelper.WriteLine($"[SavePathDebug] dumped {stage} -> {path}");
            }
        }
        catch (Exception ex)
        {
            DebugHelper.WriteLine($"[SavePathDebug] dump {stage} failed: {ex.Message}");
        }
    }

    private static float DecodeHalf(ReadOnlySpan<byte> buffer, int offset)
    {
        ushort bits = (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
        float value = (float)BitConverter.UInt16BitsToHalf(bits);
        return float.IsFinite(value) ? MathF.Max(value, 0f) : 0f;
    }
}