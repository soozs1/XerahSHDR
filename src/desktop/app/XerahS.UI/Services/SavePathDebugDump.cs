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

        string name = $"{DateTime.Now:HHmmss}_{stage}.png";
        string path = Path.Combine(DumpDir, name);
        try
        {
            // Encode from whatever pixel layout the stage produced (sRGB-encode fp16 as-is so
            // the dump is inspectable; channel order is preserved by Encode for the type).
            using var data = bitmap.ColorType == SKColorType.RgbaF16
                ? EncodeF16Preview(bitmap)
                : bitmap.Encode(SKEncodedImageFormat.Png, 100);
            if (data != null)
            {
                File.WriteAllBytes(path, data.ToArray());
                DebugHelper.WriteLine($"[SavePathDebug] dumped {stage} -> {path}");
            }
        }
        catch (Exception ex)
        {
            DebugHelper.WriteLine($"[SavePathDebug] dump {stage} failed: {ex.Message}");
        }
    }

    private static SKData? EncodeF16Preview(SKBitmap hdr)
    {
        // Simple linear->sRGB 8-bit conversion for inspection; channel order preserved.
        using var sdr = new SKBitmap(new SKImageInfo(hdr.Width, hdr.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var src = hdr.GetPixelSpan();
        var dst = sdr.GetPixelSpan();
        int sw = hdr.RowBytes, dw = sdr.RowBytes;
        for (int y = 0; y < hdr.Height; y++)
        {
            for (int x = 0; x < hdr.Width; x++)
            {
                float r = Math.Clamp(DecodeHalf(src, y * sw + x * 8), 0f, 1f);
                float g = Math.Clamp(DecodeHalf(src, y * sw + x * 8 + 2), 0f, 1f);
                float b = Math.Clamp(DecodeHalf(src, y * sw + x * 8 + 4), 0f, 1f);
                dst[y * dw + x * 4] = ToByte(SrgbOetf(b));
                dst[y * dw + x * 4 + 1] = ToByte(SrgbOetf(g));
                dst[y * dw + x * 4 + 2] = ToByte(SrgbOetf(r));
                dst[y * dw + x * 4 + 3] = 255;
            }
        }
        return sdr.Encode(SKEncodedImageFormat.Png, 100);
    }

    private static float DecodeHalf(ReadOnlySpan<byte> buffer, int offset)
    {
        ushort bits = (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
        float value = (float)BitConverter.UInt16BitsToHalf(bits);
        return float.IsFinite(value) ? MathF.Max(value, 0f) : 0f;
    }

    private static float SrgbOetf(float linear)
    {
        if (linear <= 0.0031308f) return 12.92f * linear;
        return 1.055f * MathF.Pow(linear, 1f / 2.4f) - 0.055f;
    }

    private static byte ToByte(float v) => (byte)MathF.Round(Math.Clamp(v, 0f, 1f) * 255f);
}