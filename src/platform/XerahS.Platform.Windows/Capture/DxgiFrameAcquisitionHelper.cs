using System.Runtime.InteropServices;

namespace XerahS.Platform.Windows.Capture;

internal static class DxgiFrameAcquisitionHelper
{
    private const int ZeroFrameSampleGridSize = 16;

    /// <summary>
    /// Maximum number of retries when Desktop Duplication returns an all-black
    /// (uninitialized) frame, which has been observed on the first acquire right
    /// after creating an fp16 (HDR) duplication.
    /// </summary>
    public const int MaxZeroFrameRetries = 2;

    public static bool IsUsableFrame(bool acquireSucceeded, bool desktopResourceAvailable)
    {
        return acquireSucceeded && desktopResourceAvailable;
    }

    public static bool ShouldRetryFrameAcquisition(bool acquireSucceeded, bool desktopResourceAvailable)
    {
        return !IsUsableFrame(acquireSucceeded, desktopResourceAvailable);
    }

    public static bool ShouldFallbackToGdi(int expectedOutputCount, int capturedOutputCount)
    {
        return expectedOutputCount <= 0 || capturedOutputCount < expectedOutputCount;
    }

    /// <summary>
    /// Returns true when every sampled pixel has zero color channels, meaning the
    /// duplicated frame was never written (an uninitialized buffer). Alpha is ignored
    /// because an fp16 surface can legitimately carry alpha 0 with valid colors, and the
    /// black frames observed had alpha 1. Sampling a 16x16 grid keeps the check cheap
    /// even for large surfaces.
    /// </summary>
    public static bool IsZeroFrame(IntPtr scan0, int width, int height, int stride, int pixelSize)
    {
        if (scan0 == IntPtr.Zero || width <= 0 || height <= 0 || stride <= 0 || pixelSize <= 0)
        {
            return false;
        }

        long maxOffsetLong = (long)(height - 1) * stride + (long)(width - 1) * pixelSize;
        if (maxOffsetLong > int.MaxValue)
        {
            // Marshal.ReadByte only supports int offsets; never probe such surfaces.
            return false;
        }

        int cellWidth = width / ZeroFrameSampleGridSize;
        int cellHeight = height / ZeroFrameSampleGridSize;
        if (cellWidth <= 0 || cellHeight <= 0)
        {
            return false;
        }

        for (int gy = 0; gy < ZeroFrameSampleGridSize; gy++)
        {
            int y = gy * cellHeight + cellHeight / 2;
            for (int gx = 0; gx < ZeroFrameSampleGridSize; gx++)
            {
                int x = gx * cellWidth + cellWidth / 2;
                int offset = y * stride + x * pixelSize;

                // B, G, R color channels only.
                if (Marshal.ReadByte(scan0, offset) != 0 ||
                    Marshal.ReadByte(scan0, offset + 1) != 0 ||
                    Marshal.ReadByte(scan0, offset + 2) != 0)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
