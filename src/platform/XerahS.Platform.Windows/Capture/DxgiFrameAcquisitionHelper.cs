using System.Runtime.InteropServices;

namespace XerahS.Platform.Windows.Capture;

internal static class DxgiFrameAcquisitionHelper
{
    private const int ZeroFrameSampleGridSize = 16;

    /// <summary>
    /// maximum number of non-zero grid samples a frame may contain and still be treated as
    /// "effectively black". DWM composites the mouse cursor into duplication frames, so the
    /// first frame after DuplicateOutput can be an uninitialized black buffer with only the
    /// cursor lit; the cursor covers at most a handful of the 16x16 sample points, while a
    /// real desktop always lights up far more. Treating such frames as black makes the capture
    /// retry them instead of saving a "black screen with cursor" image.
    /// </summary>
    private const int MaxEffectivelyBlackSamples = 4;

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
        return CountNonZeroSamples(scan0, width, height, stride, pixelSize) == 0;
    }

    /// <summary>
    /// Returns true when the duplicated frame carries no meaningful content: zero or only a
    /// handful of non-zero samples. This is the cursor-tolerant variant of
    /// <see cref="IsZeroFrame"/>: the first frame after DuplicateOutput can be an
    /// uninitialized black buffer with only the DWM-composited mouse cursor lit, which
    /// previously produced a "black screen with cursor" capture. Accepting such a frame
    /// makes an HDR capture save a black image, so it must be retried instead.
    /// </summary>
    public static bool IsEffectivelyBlackFrame(IntPtr scan0, int width, int height, int stride, int pixelSize)
    {
        return CountNonZeroSamples(scan0, width, height, stride, pixelSize) <= MaxEffectivelyBlackSamples;
    }

    /// <summary>
    /// Counts non-zero color samples on a 16x16 grid over the buffer (256 probes). Returns
    /// <see cref="int.MaxValue"/> when the buffer cannot be probed safely (invalid geometry
    /// or offsets that overflow the Marshal int API) so callers treat the frame as "not
    /// black" instead of silently dropping a real capture.
    /// </summary>
    private static int CountNonZeroSamples(IntPtr scan0, int width, int height, int stride, int pixelSize)
    {
        if (scan0 == IntPtr.Zero || width <= 0 || height <= 0 || stride <= 0 || pixelSize <= 0)
        {
            return int.MaxValue;
        }

        long maxOffsetLong = (long)(height - 1) * stride + (long)(width - 1) * pixelSize;
        if (maxOffsetLong > int.MaxValue)
        {
            // Marshal.ReadByte only supports int offsets; never probe such surfaces.
            return int.MaxValue;
        }

        int cellWidth = width / ZeroFrameSampleGridSize;
        int cellHeight = height / ZeroFrameSampleGridSize;
        if (cellWidth <= 0 || cellHeight <= 0)
        {
            return int.MaxValue;
        }

        int nonZero = 0;
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
                    nonZero++;
                }
            }
        }

        return nonZero;
    }
}
