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
/// Renders every HDR→SDR tone-map variant for real HDR photographs (Radiance RGBE, CC0
/// Polyhaven captures with genuine extended range) into a Windows-visible folder for visual
/// assessment. Skips when the test data is not present.
/// </summary>
public class HdrPhotoVariantTests
{
    private static readonly string PhotoDir = Path.Combine(FindRepoRoot(), "tests", "XerahS.Tests", "TestData", "HdrPhotos");
    private static readonly string OutRoot = Path.Combine("/mnt/c/Users/x/Documents/XerahS/HdrPhotoVariants");

    [Test]
    public void RenderVariants_ForRealHdrPhotos()
    {
        if (!Directory.Exists(PhotoDir))
        {
            Assert.Ignore($"HDR photo test data not found: {PhotoDir}");
        }

        var photos = Directory.GetFiles(PhotoDir, "*.hdr").OrderBy(p => p).ToArray();
        if (photos.Length == 0)
        {
            Assert.Ignore($"No .hdr files in {PhotoDir}");
        }

        Directory.CreateDirectory(OutRoot);
        var summary = new StringBuilder();
        summary.AppendLine("HDR photo variant assessment. Compare each variants/*.png against the others;");
        summary.AppendLine("the reference file is what a standard viewer shows for the photo (clamp mapping).");
        summary.AppendLine();

        foreach (string photoPath in photos)
        {
            string name = Path.GetFileNameWithoutExtension(photoPath);
            string outDir = Path.Combine(OutRoot, name);
            Directory.CreateDirectory(outDir);
            Directory.CreateDirectory(Path.Combine(outDir, "variants"));

            using var hdr = HdrPhotoLoader.LoadRadianceHdr(photoPath);
            var (white, peakRatio) = HdrContentAnalyzer.Analyze(hdr);
            summary.AppendLine($"{name}: {hdr.Width}x{hdr.Height}, sdrWhite={white:F3}, peak/white={peakRatio:F1} -> {(peakRatio <= 1.05f ? "effectively SDR" : "REAL HDR")}");

            // Reference: clamp at SDR white, display-referred (what "None" looks like today).
            var referenceVariant = HdrTonemapVariants.All.First(v => v.Id == "clamp-norm");
            using (var reference = HdrTonemapVariants.Apply(hdr, referenceVariant, white))
            {
                if (reference != null)
                {
                    SavePng(reference, Path.Combine(outDir, "reference-clamp.png"));
                }
            }

            int index = 0;
            foreach (var variant in HdrTonemapVariants.All)
            {
                using var rendered = HdrTonemapVariants.Apply(hdr, variant, white);
                if (rendered == null)
                {
                    continue;
                }

                index++;
                string path = Path.Combine(outDir, "variants", $"{index:00}-{variant.Id}.png");
                SavePng(rendered, path);
                summary.AppendLine($"  {index:00} {variant.Id,-14} {Describe(rendered)}  ({variant.Label})");
            }

            summary.AppendLine();
            TestContext.Progress.WriteLine($"{name}: sdrWhite={white:F3} peak/white={peakRatio:F1}, {index} variants");
        }

        File.WriteAllText(Path.Combine(OutRoot, "summary.txt"), summary.ToString());
        TestContext.Progress.WriteLine(summary.ToString());
    }

    private static string Describe(SKBitmap bitmap)
    {
        var span = bitmap.GetPixelSpan();
        int stride = bitmap.RowBytes;
        int w = bitmap.Width, h = bitmap.Height;
        int stepX = Math.Max(1, w / 200), stepY = Math.Max(1, h / 100);
        double sr = 0, sg = 0, sb = 0;
        long n = 0, clipped = 0;
        for (int y = 0; y < h; y += stepY)
        {
            for (int x = 0; x < w; x += stepX)
            {
                int o = y * stride + x * 4;
                byte b = span[o], g = span[o + 1], r = span[o + 2];
                sr += r; sg += g; sb += b;
                if (r >= 250 && g >= 250 && b >= 250) clipped++;
                n++;
            }
        }
        return $"mean=({sr / n:F0},{sg / n:F0},{sb / n:F0}) clipped={100.0 * clipped / n:F2}%";
    }

    private static void SavePng(SKBitmap bitmap, string path)
    {
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data!.ToArray());
    }

    /// <summary>Loads a Radiance RGBE (.hdr) file into an fp16 linear (scRGB) bitmap.</summary>

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