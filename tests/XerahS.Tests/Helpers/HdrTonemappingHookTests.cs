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
using XerahS.Common;
using XerahS.Core;
using XerahS.Core.Helpers;

namespace XerahS.Tests.Helpers;

public class HdrTonemappingHookTests
{
    [TestCase(HdrToneMappingMode.Auto, AfterCaptureTasks.None, false)]
    [TestCase(HdrToneMappingMode.Auto, AfterCaptureTasks.SaveImageToFile, false)]
    [TestCase(HdrToneMappingMode.Auto, AfterCaptureTasks.SaveImageToFile | AfterCaptureTasks.CopyImageToClipboard, true)]
    [TestCase(HdrToneMappingMode.Auto, AfterCaptureTasks.CopyImageToClipboard, true)]
    [TestCase(HdrToneMappingMode.Auto, AfterCaptureTasks.UploadImageToHost, true)]
    [TestCase(HdrToneMappingMode.Auto, AfterCaptureTasks.ShowAfterCaptureWindow, true)]
    [TestCase(HdrToneMappingMode.Auto, AfterCaptureTasks.AddImageEffects, true)]
    [TestCase(HdrToneMappingMode.Auto, AfterCaptureTasks.AnnotateMedia, true)]
    [TestCase(HdrToneMappingMode.Auto, AfterCaptureTasks.DoOCR, false)]
    [TestCase(HdrToneMappingMode.Auto, AfterCaptureTasks.SaveImageToFile | AfterCaptureTasks.DoOCR, false)]
    [TestCase(HdrToneMappingMode.TonemapReinhard, AfterCaptureTasks.None, false)]
    [TestCase(HdrToneMappingMode.TonemapReinhard, AfterCaptureTasks.SaveImageToFile, false)]
    [TestCase(HdrToneMappingMode.TonemapReinhard, AfterCaptureTasks.CopyImageToClipboard, true)]
    [TestCase(HdrToneMappingMode.TonemapAces, AfterCaptureTasks.SaveImageToFile, false)]
    [TestCase(HdrToneMappingMode.TonemapAces, AfterCaptureTasks.CopyImageToClipboard, true)]
    [TestCase(HdrToneMappingMode.TonemapBt2446C, AfterCaptureTasks.SaveImageToFile, false)]
    [TestCase(HdrToneMappingMode.TonemapBt2446C, AfterCaptureTasks.CopyImageToClipboard, true)]
    [TestCase(HdrToneMappingMode.TonemapBt2446C, AfterCaptureTasks.None, true)]
    [TestCase(HdrToneMappingMode.TonemapBt2446C, AfterCaptureTasks.SaveImageToFile, true)]
    [TestCase(HdrToneMappingMode.TonemapBt2446C, AfterCaptureTasks.CopyImageToClipboard, true)]
    [TestCase(HdrToneMappingMode.None, AfterCaptureTasks.CopyImageToClipboard, false)]
    [TestCase(HdrToneMappingMode.None, AfterCaptureTasks.UploadImageToHost, false)]
    public void ShouldTonemapToSdr_ReturnsExpected(HdrToneMappingMode mode, AfterCaptureTasks afterCaptureJob, bool expected)
    {
        Assert.That(HdrTonemapper.ShouldTonemapToSdr(mode, afterCaptureJob), Is.EqualTo(expected));
    }
}