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
using System.Buffers.Binary;
using System.Text;

namespace XerahS.Tests.Helpers;

/// <summary>
/// Decodes Radiance RGBE (.hdr) photographs into fp16 linear (scRGB) bitmaps for the HDR
/// test tooling. Supports both adaptive-RLE and flat scanline encodings.
/// </summary>
internal static class HdrPhotoLoader
{
    public static SKBitmap LoadRadianceHdr(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);

        // Header ends with an empty line.
        string? line;
        while (!string.IsNullOrEmpty(line = ReadLine(br)))
        {
        }

        string resolution = ReadLine(br) ?? throw new FormatException("missing resolution line");
        var parts = resolution.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
        {
            throw new FormatException($"unexpected resolution line: {resolution}");
        }

        bool yFirst = parts[0].StartsWith("-Y", StringComparison.Ordinal);
        int h = int.Parse(parts[1]);
        int w = int.Parse(parts[3]);
        if (!yFirst)
        {
            (h, w) = (w, h);
        }

        var rgbe = new byte[w * h * 4];
        if (w >= 8 && w < 32768)
        {
            for (int y = 0; y < h; y++)
            {
                byte b0 = br.ReadByte(), b1 = br.ReadByte(), b2 = br.ReadByte(), b3 = br.ReadByte();
                if (b0 == 2 && b1 == 2 && (b2 & 0x80) == 0)
                {
                    int shift = (b2 << 8) | b3;
                    if (shift != w)
                    {
                        throw new FormatException($"RLE scanline length {shift} != width {w}");
                    }

                    for (int c = 0; c < 4; c++)
                    {
                        int x = 0;
                        while (x < w)
                        {
                            int count = br.ReadByte();
                            if (count > 128)
                            {
                                int run = count - 128;
                                byte v = br.ReadByte();
                                for (int i = 0; i < run && x < w; i++)
                                {
                                    rgbe[(y * w + x) * 4 + c] = v;
                                    x++;
                                }
                            }
                            else
                            {
                                for (int i = 0; i < count && x < w; i++)
                                {
                                    rgbe[(y * w + x) * 4 + c] = br.ReadByte();
                                    x++;
                                }
                            }
                        }
                    }
                }
                else
                {
                    rgbe[(y * w) * 4] = b0;
                    rgbe[(y * w) * 4 + 1] = b1;
                    rgbe[(y * w) * 4 + 2] = b2;
                    rgbe[(y * w) * 4 + 3] = b3;
                    for (int x = 1; x < w; x++)
                    {
                        int o = (y * w + x) * 4;
                        rgbe[o] = br.ReadByte();
                        rgbe[o + 1] = br.ReadByte();
                        rgbe[o + 2] = br.ReadByte();
                        rgbe[o + 3] = br.ReadByte();
                    }
                }
            }
        }
        else
        {
            // Flat format for very small widths.
            for (int i = 0; i < w * h; i++)
            {
                int o = i * 4;
                rgbe[o] = br.ReadByte();
                rgbe[o + 1] = br.ReadByte();
                rgbe[o + 2] = br.ReadByte();
                rgbe[o + 3] = br.ReadByte();
            }
        }

        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.RgbaF16, SKAlphaType.Premul, SKColorSpace.CreateSrgbLinear()));
        Span<byte> dst = bmp.GetPixelSpan();
        int stride = bmp.RowBytes;
        for (int i = 0; i < w * h; i++)
        {
            int o = i * 4;
            byte e = rgbe[o + 3];
            float scale = e == 0 ? 0f : MathF.Pow(2f, e - 136f); // RGBE: mantissa * 2^(E-128-8)
            float r = rgbe[o] * scale;
            float g = rgbe[o + 1] * scale;
            float b = rgbe[o + 2] * scale;

            int d = (i / w) * stride + (i % w) * 8;
            WriteHalf(dst, d, r);
            WriteHalf(dst, d + 2, g);
            WriteHalf(dst, d + 4, b);
            WriteHalf(dst, d + 6, 1f);
        }

        return bmp;
    }

    private static void WriteHalf(Span<byte> span, int offset, float value)
    {
        float clamped = float.IsFinite(value) ? MathF.Min(MathF.Max(value, 0f), 60000f) : 0f;
        ushort bits = BitConverter.HalfToUInt16Bits((Half)clamped);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(offset, 2), bits);
    }

    private static string? ReadLine(BinaryReader reader)
    {
        var sb = new StringBuilder();
        int ch;
        while ((ch = reader.Read()) != -1)
        {
            if (ch == '\n')
            {
                return sb.ToString();
            }
            if (ch != '\r')
            {
                sb.Append((char)ch);
            }
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }
}