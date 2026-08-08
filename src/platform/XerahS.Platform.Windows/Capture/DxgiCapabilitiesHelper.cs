#region License Information (GPL v3)

/*
    XerahS - The Avalonia UI implementation of ShareX
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.
*/

#endregion License Information (GPL v3)

using ShareX.Avalonia.Platform.Abstractions.Capture;
using Vortice.DXGI;

namespace XerahS.Platform.Windows.Capture;

internal static class DxgiCapabilitiesHelper
{
    public static BackendCapabilities Create() => Create(false);

    public static BackendCapabilities Create(bool anyHdrMonitor)
    {
        return new BackendCapabilities
        {
            BackendName = "DXGI Desktop Duplication",
            Version = "1.2+",
            SupportsHardwareAcceleration = true,
            SupportsCursorCapture = true,
            SupportsHDR = anyHdrMonitor,
            SupportsPerMonitorDpi = true,
            SupportsMonitorHotplug = true,
            MaxCaptureResolution = 16384,
            RequiresPermission = false
        };
    }

    /// <summary>
    /// Whether the given DXGI color space denotes HDR10 output (full-range PQ/P2020).
    /// </summary>
    public static bool IsHdrColorSpace(ColorSpaceType colorSpace)
    {
        return colorSpace == ColorSpaceType.RgbFullG2084NoneP2020;
    }
}
