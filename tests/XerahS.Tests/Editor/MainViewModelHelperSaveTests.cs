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
using ShareX.ImageEditor.Core.Annotations;
using ShareX.ImageEditor.Core.Editor;
using ShareX.ImageEditor.Core.Persistence;
using ShareX.ImageEditor.Presentation.ViewModels;
using SkiaSharp;
using System.Buffers.Binary;
using System.Reflection;
using XerahS.Common;
using XerahS.Core;
using XerahS.UI.Services;

namespace XerahS.Tests.Editor;

[TestFixture]
public class MainViewModelHelperSaveTests
{
    [Test]
    public async Task SaveToPathAsync_TruncatesExistingDestinationFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(path, Enumerable.Repeat((byte)'x', 8192).ToArray());

        try
        {
            var viewModel = new MainViewModel
            {
                ImageFilePath = path,
                IsDirty = true
            };

            await InvokeSaveToPathAsync(viewModel, () =>
            {
                var bitmap = new SKBitmap(1, 1);
                bitmap.Erase(SKColors.CadetBlue);
                return bitmap;
            }, null, path);

            var savedBytes = await File.ReadAllBytesAsync(path);
            Assert.That(savedBytes.Length, Is.LessThan(8192));
            Assert.That(viewModel.ImageFilePath, Is.EqualTo(path));
            Assert.That(viewModel.IsDirty, Is.False);

            using var decoded = SKBitmap.Decode(savedBytes);
            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded.Width, Is.EqualTo(1));
            Assert.That(decoded.Height, Is.EqualTo(1));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Test]
    public void SaveToPathAsync_KeepsEditorDirty_WhenAnnotationSidecarSaveFails()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        string sidecarTempPath = XannProjectFileService.GetDefaultSidecarPath(path) + ".tmp";
        Directory.CreateDirectory(sidecarTempPath);

        try
        {
            using var source = new SKBitmap(4, 4);
            source.Erase(SKColors.White);

            var editorCore = new EditorCore();
            editorCore.LoadImage(source.Copy());
            editorCore.AddAnnotation(new RectangleAnnotation
            {
                StartPoint = new SKPoint(0, 0),
                EndPoint = new SKPoint(2, 2)
            });

            var viewModel = new MainViewModel
            {
                IsDirty = true
            };
            viewModel.AttachEditorCore(editorCore);

            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            {
                await InvokeSaveToPathAsync(viewModel, () =>
                {
                    var bitmap = new SKBitmap(1, 1);
                    bitmap.Erase(SKColors.CadetBlue);
                    return bitmap;
                }, null, path);
            });

            Assert.That(File.Exists(path), Is.True, "The image write should have completed before sidecar persistence failed.");
            Assert.That(viewModel.ImageFilePath, Is.EqualTo(path));
            Assert.That(viewModel.IsDirty, Is.True);
        }
        finally
        {
            if (Directory.Exists(sidecarTempPath))
            {
                Directory.Delete(sidecarTempPath);
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Test]
    public async Task SaveToPathAsync_HdrSource_SdrDepth_WritesTonemapped8BitPng()
    {
        // Design: "SDR" depth forces an HDR→SDR conversion on save (method = selected filter,
        // None = as-is/clamp). The saved PNG must be 8-bit and contain sRGB-encoded values:
        // linear 0.5 -> 188, linear 0.25 -> 137 when read by a standard viewer.
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        TaskSettings? previous = null;

        try
        {
            var settings = new TaskSettings
            {
                ImageSettings = new TaskSettingsImage
                {
                    ImagePNGBitDepth = PNGBitDepth.Default,
                    ImageHdrSaveToneMapping = HdrToneMappingMode.TonemapReinhard
                }
            };
            previous = SwapDefaultTaskSettings(settings);

            var viewModel = new MainViewModel { IsDirty = true };
            using var hdr = MakeF16Bitmap((0.5f, 0.5f, 0.5f, 1f), (0.25f, 0.25f, 0.25f, 1f));

            await InvokeSaveToPathAsync(viewModel, () => null, () => hdr.Copy(), path);

            var savedBytes = await File.ReadAllBytesAsync(path);
            Assert.That(savedBytes[24], Is.EqualTo(8), "SDR depth must write an 8-bit PNG");

            using var decoded = SKBitmap.Decode(SKData.CreateCopy(savedBytes));
            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded!.GetPixel(0, 0).Red, Is.GreaterThan(0), "tonemapped pixel must not be black");
        }
        finally
        {
            RestoreDefaultTaskSettings(previous);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public async Task SaveToPathAsync_HdrSource_Bit16Depth_Writes16BitHdrPng()
    {
        // Design: "SDR+HDR" depth keeps HDR content and writes a true 16-bit HDR PNG: raw
        // linear samples (including > 1.0) with a linear iCCP profile — not sRGB-encoded.
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        TaskSettings? previous = null;

        try
        {
            var settings = new TaskSettings
            {
                ImageSettings = new TaskSettingsImage
                {
                    ImagePNGBitDepth = PNGBitDepth.Bit16,
                    ImageHdrSaveToneMapping = HdrToneMappingMode.TonemapReinhard
                }
            };
            previous = SwapDefaultTaskSettings(settings);

            var viewModel = new MainViewModel { IsDirty = true };
            using var hdr = MakeF16Bitmap((0.5f, 0.5f, 0.5f, 1f), (4f, 4f, 4f, 1f));

            await InvokeSaveToPathAsync(viewModel, () => null, () => hdr.Copy(), path);

            var savedBytes = await File.ReadAllBytesAsync(path);
            Assert.That(savedBytes[24], Is.EqualTo(16), "SDR+HDR depth must write a 16-bit PNG");
            Assert.That(HasChunk(savedBytes, "iCCP"), Is.True, "HDR PNG must carry a linear iCCP profile");
            Assert.That(HasChunk(savedBytes, "sRGB"), Is.False, "HDR PNG must not carry an sRGB chunk");

            using var decoded = SKBitmap.Decode(SKData.CreateCopy(savedBytes));
            Assert.That(decoded, Is.Not.Null);
            // Raw linear values preserved: 4.0 sample must be brighter than 0.5 (not sRGB 188/137).
            Assert.That(decoded!.GetPixel(1, 0).Red, Is.GreaterThan(decoded.GetPixel(0, 0).Red),
                "HDR highlight must stay brighter than midtone (raw linear, not sRGB-encoded)");
        }
        finally
        {
            RestoreDefaultTaskSettings(previous);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static TaskSettings? SwapDefaultTaskSettings(TaskSettings settings)
    {
        var property = typeof(SettingsManager).GetProperty("DefaultTaskSettings", BindingFlags.Static | BindingFlags.Public)
            ?? throw new MissingMemberException(nameof(SettingsManager), "DefaultTaskSettings");
        var previous = (TaskSettings?)property.GetValue(null);
        property.SetValue(null, settings);
        // Save path consults the last capture settings first (see MainViewModelHelper).
        var previousCapture = MainViewModelHelper.LastCaptureTaskSettings;
        MainViewModelHelper.LastCaptureTaskSettings = settings;
        return previous; // restore both in RestoreDefaultTaskSettings via the captured values below
    }

    private static void RestoreDefaultTaskSettings(TaskSettings? previous)
    {
        var property = typeof(SettingsManager).GetProperty("DefaultTaskSettings", BindingFlags.Static | BindingFlags.Public)
            ?? throw new MissingMemberException(nameof(SettingsManager), "DefaultTaskSettings");
        property.SetValue(null, previous ?? new TaskSettings { Job = WorkflowType.None });
        MainViewModelHelper.LastCaptureTaskSettings = previous;
    }

    private static SKBitmap MakeF16Bitmap(params (float R, float G, float B, float A)[] pixels)
    {
        var bitmap = new SKBitmap(new SKImageInfo(
            pixels.Length, 1, SKColorType.RgbaF16, SKAlphaType.Premul, SKColorSpace.CreateSrgbLinear()));
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

    private static bool HasChunk(byte[] png, string chunkName)
    {
        int pos = 8;
        while (pos + 8 <= png.Length)
        {
            uint len = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos, 4));
            string name = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
            if (name == chunkName)
            {
                return true;
            }
            pos += 12 + (int)len;
        }
        return false;
    }

    private static Task InvokeSaveToPathAsync(MainViewModel viewModel, Func<SKBitmap?> snapshotFactory, Func<SKBitmap?>? sourceFactory, string path)
    {
        var method = typeof(MainViewModelHelper).GetMethod("SaveToPathAsync", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(MainViewModelHelper), "SaveToPathAsync");

        return (Task)(method.Invoke(null, new object?[] { viewModel, snapshotFactory, sourceFactory, path })
            ?? throw new InvalidOperationException("SaveToPathAsync returned null."));
    }
}
