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

using System.Runtime.InteropServices;
using NUnit.Framework;
using XerahS.Platform.Windows.Capture;

namespace XerahS.Tests.Platform.Windows;

[TestFixture]
public class DxgiFrameAcquisitionHelperTests
{
    private const int Width = 2560;
    private const int Height = 1440;
    private const int Fp16PixelSize = 8; // R16G16B16A16_FLOAT
    private const int BgraPixelSize = 4; // B8G8R8A8_UNORM
    private static readonly int Fp16Stride = Width * Fp16PixelSize;
    private static readonly int BgraStride = Width * BgraPixelSize;

    // Grid sample points used by the helper: x = gx * cellWidth + cellWidth / 2, same for y.
    private static int GridX(int gx) => gx * (Width / 16) + (Width / 16) / 2;
    private static int GridY(int gy) => gy * (Height / 16) + (Height / 16) / 2;

    private static IntPtr Alloc(int size)
    {
        // AllocHGlobal does not zero the memory; callers expect deterministic buffers.
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(new byte[size], 0, ptr, size);
            return ptr;
        }
        catch
        {
            Marshal.FreeHGlobal(ptr);
            throw;
        }
    }

    private static void SetByte(IntPtr ptr, long offset, byte value) => Marshal.WriteByte(ptr, (int)offset, value);

    [Test]
    public void IsEffectivelyBlackFrame_AllZeroFp16Buffer_ReturnsTrue()
    {
        IntPtr ptr = Alloc(Fp16Stride * Height);
        try
        {
            Assert.That(DxgiFrameAcquisitionHelper.IsEffectivelyBlackFrame(ptr, Width, Height, Fp16Stride, Fp16PixelSize), Is.True);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [Test]
    public void IsZeroFrame_AllZeroFp16Buffer_ReturnsTrue()
    {
        IntPtr ptr = Alloc(Fp16Stride * Height);
        try
        {
            Assert.That(DxgiFrameAcquisitionHelper.IsZeroFrame(ptr, Width, Height, Fp16Stride, Fp16PixelSize), Is.True);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [Test]
    public void IsEffectivelyBlackFrame_CursorPixelOnGridPoint_ReturnsTrue()
    {
        // A black fp16 buffer with one lit pixel exactly on a grid point simulates the
        // cursor-only first frame after DuplicateOutput that previously produced a
        // "black screen with cursor" capture: strict IsZeroFrame misses it, the
        // cursor-tolerant check must still treat it as black.
        IntPtr ptr = Alloc(Fp16Stride * Height);
        try
        {
            long offset = (long)GridY(4) * Fp16Stride + (long)GridX(3) * Fp16PixelSize;
            SetByte(ptr, offset, 0xFF); // R low byte non-zero

            Assert.That(DxgiFrameAcquisitionHelper.IsZeroFrame(ptr, Width, Height, Fp16Stride, Fp16PixelSize), Is.False);
            Assert.That(DxgiFrameAcquisitionHelper.IsEffectivelyBlackFrame(ptr, Width, Height, Fp16Stride, Fp16PixelSize), Is.True);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [Test]
    public void IsEffectivelyBlackFrame_CursorPixelBetweenGridPoints_ReturnsTrue()
    {
        // Cursor pixels that miss every grid point do not even register as non-zero.
        IntPtr ptr = Alloc(Fp16Stride * Height);
        try
        {
            // Off-grid pixel (grid points are at cell centers; this sits in a cell corner).
            long offset = (long)(Height / 2) * Fp16Stride + (long)(Width / 2) * Fp16PixelSize + 1;
            SetByte(ptr, offset, 0x3C);

            Assert.That(DxgiFrameAcquisitionHelper.IsEffectivelyBlackFrame(ptr, Width, Height, Fp16Stride, Fp16PixelSize), Is.True);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [Test]
    public void IsEffectivelyBlackFrame_ManyLitSamples_ReturnsFalse()
    {
        // A real desktop lights up far more than a handful of grid points.
        IntPtr ptr = Alloc(Fp16Stride * Height);
        try
        {
            for (int i = 0; i < 16; i++)
            {
                long offset = (long)GridY(i % 16) * Fp16Stride + (long)GridX(i) * Fp16PixelSize;
                SetByte(ptr, offset, 0xFF);
            }

            Assert.That(DxgiFrameAcquisitionHelper.IsEffectivelyBlackFrame(ptr, Width, Height, Fp16Stride, Fp16PixelSize), Is.False);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [Test]
    public void IsEffectivelyBlackFrame_BlackBufferWithAlphaOnly_ReturnsTrue()
    {
        // fp16 surfaces can carry alpha with zero color channels; alpha must be ignored.
        IntPtr ptr = Alloc(Fp16Stride * Height);
        try
        {
            long offset = (long)GridY(0) * Fp16Stride + (long)GridX(0) * Fp16PixelSize;
            SetByte(ptr, offset + 6, 0xFF); // alpha low byte

            Assert.That(DxgiFrameAcquisitionHelper.IsEffectivelyBlackFrame(ptr, Width, Height, Fp16Stride, Fp16PixelSize), Is.True);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [Test]
    public void IsEffectivelyBlackFrame_BgraBuffer_CursorOnGridPoint_ReturnsTrue()
    {
        IntPtr ptr = Alloc(BgraStride * Height);
        try
        {
            long offset = (long)GridY(1) * BgraStride + (long)GridX(1) * BgraPixelSize;
            SetByte(ptr, offset, 0x40); // B channel

            Assert.That(DxgiFrameAcquisitionHelper.IsZeroFrame(ptr, Width, Height, BgraStride, BgraPixelSize), Is.False);
            Assert.That(DxgiFrameAcquisitionHelper.IsEffectivelyBlackFrame(ptr, Width, Height, BgraStride, BgraPixelSize), Is.True);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [Test]
    public void IsEffectivelyBlackFrame_InvalidArguments_TreatsAsNotBlack()
    {
        Assert.That(DxgiFrameAcquisitionHelper.IsEffectivelyBlackFrame(IntPtr.Zero, Width, Height, Fp16Stride, Fp16PixelSize), Is.False);
        Assert.That(DxgiFrameAcquisitionHelper.IsZeroFrame(IntPtr.Zero, Width, Height, Fp16Stride, Fp16PixelSize), Is.False);
    }
}
