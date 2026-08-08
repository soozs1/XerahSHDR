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
using Vortice.DXGI;
using XerahS.Platform.Windows.Capture;

namespace XerahS.Tests.Platform.Windows;

/// <summary>
/// Tests for DXGI HDR capability detection (sub-phase 2.2a).
/// Covers the pure color-space classification and the SupportsHDR wiring
/// in DxgiCapabilitiesHelper, without requiring COM/DXGI.
/// </summary>
[TestFixture]
public class DxgiCapabilitiesHelperTests
{
    [Test]
    public void IsHdrColorSpace_Hdr10FullRange_ReturnsTrue()
    {
        Assert.That(DxgiCapabilitiesHelper.IsHdrColorSpace(ColorSpaceType.RgbFullG2084NoneP2020), Is.True);
    }

    [TestCase(ColorSpaceType.RgbFullG22NoneP709)]
    [TestCase(ColorSpaceType.RgbStudioG22NoneP2020)]
    [TestCase(ColorSpaceType.RgbStudioG2084NoneP2020)]
    [TestCase(ColorSpaceType.Custom)]
    public void IsHdrColorSpace_NonHdr10FullRange_ReturnsFalse(ColorSpaceType colorSpace)
    {
        Assert.That(DxgiCapabilitiesHelper.IsHdrColorSpace(colorSpace), Is.False);
    }

    [Test]
    public void Create_AnyHdrMonitor_SupportsHdrTrue()
    {
        var capabilities = DxgiCapabilitiesHelper.Create(anyHdrMonitor: true);

        Assert.That(capabilities.SupportsHDR, Is.True);
    }

    [Test]
    public void Create_NoHdrMonitor_SupportsHdrFalse()
    {
        var capabilities = DxgiCapabilitiesHelper.Create(anyHdrMonitor: false);

        Assert.That(capabilities.SupportsHDR, Is.False);
    }

    [Test]
    public void Create_Parameterless_SupportsHdrFalse()
    {
        var capabilities = DxgiCapabilitiesHelper.Create();

        Assert.That(capabilities.SupportsHDR, Is.False);
    }
}