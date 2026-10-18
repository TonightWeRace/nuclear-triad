<#
.SYNOPSIS
  Prints the Windows display arrangement Nuclear Triad depends on.
.DESCRIPTION
  Read-only. Lists each monitor's device name, rectangle, refresh rate, orientation and
  DPI scale, plus the panel size each monitor reports through EDID and WMI. Runs
  per-monitor DPI aware, so rectangles are physical pixels.

  powershell -NoProfile -ExecutionPolicy Bypass -File tools\display-probe.ps1
#>
$ErrorActionPreference = 'Stop'

Add-Type -Language CSharp -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class DisplayProbe
{
    [StructLayout(LayoutKind.Sequential)] public struct LUID { public uint LowPart; public int HighPart; }
    [StructLayout(LayoutKind.Sequential)] public struct RATIONAL { public uint Numerator; public uint Denominator; }
    [StructLayout(LayoutKind.Sequential)] public struct REGION2D { public uint cx; public uint cy; }
    [StructLayout(LayoutKind.Sequential)] public struct POINTL { public int x; public int y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int left; public int top; public int right; public int bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct PATH_SOURCE_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }
    [StructLayout(LayoutKind.Sequential)] public struct PATH_TARGET_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint outputTechnology; public uint rotation; public uint scaling; public RATIONAL refreshRate; public uint scanLineOrdering; public int targetAvailable; public uint statusFlags; }
    [StructLayout(LayoutKind.Sequential)] public struct PATH_INFO { public PATH_SOURCE_INFO sourceInfo; public PATH_TARGET_INFO targetInfo; public uint flags; }
    [StructLayout(LayoutKind.Sequential)] public struct VIDEO_SIGNAL_INFO { public ulong pixelRate; public RATIONAL hSyncFreq; public RATIONAL vSyncFreq; public REGION2D activeSize; public REGION2D totalSize; public uint videoStandard; public uint scanLineOrdering; }
    [StructLayout(LayoutKind.Sequential)] public struct SOURCE_MODE { public uint width; public uint height; public uint pixelFormat; public POINTL position; }
    [StructLayout(LayoutKind.Explicit)] public struct MODE_UNION { [FieldOffset(0)] public VIDEO_SIGNAL_INFO target; [FieldOffset(0)] public SOURCE_MODE source; }
    [StructLayout(LayoutKind.Sequential)] public struct MODE_INFO { public uint infoType; public uint id; public LUID adapterId; public MODE_UNION mode; }
    [StructLayout(LayoutKind.Sequential)] public struct DEVICE_INFO_HEADER { public uint type; public uint size; public LUID adapterId; public uint id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct SOURCE_DEVICE_NAME { public DEVICE_INFO_HEADER header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct TARGET_DEVICE_NAME { public DEVICE_INFO_HEADER header; public uint flags; public uint outputTechnology; public ushort edidManufactureId; public ushort edidProductCodeId; public uint connectorInstance; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct MONITORINFOEX { public uint cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct DEVMODE {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion; public ushort dmDriverVersion; public ushort dmSize; public ushort dmDriverExtra; public uint dmFields;
        public int dmPositionX; public int dmPositionY; public uint dmDisplayOrientation; public uint dmDisplayFixedOutput;
        public short dmColor; public short dmDuplex; public short dmYResolution; public short dmTTOption; public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels; public uint dmBitsPerPel; public uint dmPelsWidth; public uint dmPelsHeight; public uint dmDisplayFlags; public uint dmDisplayFrequency;
        public uint dmICMMethod; public uint dmICMIntent; public uint dmMediaType; public uint dmDitherType; public uint dmReserved1; public uint dmReserved2; public uint dmPanningWidth; public uint dmPanningHeight;
    }
    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprcMonitor, IntPtr dwData);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);
    [DllImport("user32.dll")] public static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] PATH_INFO[] paths, ref uint numModes, [Out] MODE_INFO[] modes, IntPtr topology);
    [DllImport("user32.dll")] public static extern int DisplayConfigGetDeviceInfo(ref SOURCE_DEVICE_NAME packet);
    [DllImport("user32.dll")] public static extern int DisplayConfigGetDeviceInfo(ref TARGET_DEVICE_NAME packet);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplaySettingsEx(string device, int mode, ref DEVMODE dm, uint flags);

    public class Monitor { public string Device; public int X; public int Y; public int W; public int H; public RECT Work; public bool Primary; public uint Dpi; public uint Hz; public uint Orientation; public uint Bpp; }
    public class Panel { public string Device; public string Name; public string Path; public uint Tech; public double Hz; public uint SignalW; public uint SignalH; }

    const uint QDC_ONLY_ACTIVE_PATHS = 2;
    static readonly IntPtr PER_MONITOR_AWARE_V2 = new IntPtr(-4);

    public static List<Monitor> Monitors()
    {
        IntPtr previous = SetThreadDpiAwarenessContext(PER_MONITOR_AWARE_V2);
        var list = new List<Monitor>();
        MonitorEnumProc callback = delegate(IntPtr h, IntPtr dc, IntPtr r, IntPtr d)
        {
            var info = new MONITORINFOEX();
            info.cbSize = (uint)Marshal.SizeOf(typeof(MONITORINFOEX));
            GetMonitorInfo(h, ref info);
            uint dpiX, dpiY;
            GetDpiForMonitor(h, 0, out dpiX, out dpiY);
            var mode = new DEVMODE();
            mode.dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE));
            EnumDisplaySettingsEx(info.szDevice, -1, ref mode, 0);
            list.Add(new Monitor {
                Device = info.szDevice,
                X = info.rcMonitor.left, Y = info.rcMonitor.top,
                W = info.rcMonitor.right - info.rcMonitor.left, H = info.rcMonitor.bottom - info.rcMonitor.top,
                Work = info.rcWork, Primary = (info.dwFlags & 1) != 0, Dpi = dpiX,
                Hz = mode.dmDisplayFrequency, Orientation = mode.dmDisplayOrientation, Bpp = mode.dmBitsPerPel
            });
            return true;
        };
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        SetThreadDpiAwarenessContext(previous);
        return list;
    }

    public static List<Panel> Panels()
    {
        uint pathCount, modeCount;
        int rc = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount);
        if (rc != 0) throw new Exception("GetDisplayConfigBufferSizes failed: " + rc);
        var paths = new PATH_INFO[pathCount];
        var modes = new MODE_INFO[modeCount];
        rc = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
        if (rc != 0) throw new Exception("QueryDisplayConfig failed: " + rc);

        var list = new List<Panel>();
        for (int i = 0; i < pathCount; i++)
        {
            var path = paths[i];
            var source = new SOURCE_DEVICE_NAME();
            source.header.type = 1;
            source.header.size = (uint)Marshal.SizeOf(typeof(SOURCE_DEVICE_NAME));
            source.header.adapterId = path.sourceInfo.adapterId;
            source.header.id = path.sourceInfo.id;
            DisplayConfigGetDeviceInfo(ref source);

            var target = new TARGET_DEVICE_NAME();
            target.header.type = 2;
            target.header.size = (uint)Marshal.SizeOf(typeof(TARGET_DEVICE_NAME));
            target.header.adapterId = path.targetInfo.adapterId;
            target.header.id = path.targetInfo.id;
            DisplayConfigGetDeviceInfo(ref target);

            var panel = new Panel { Device = source.viewGdiDeviceName, Name = target.monitorFriendlyDeviceName, Path = target.monitorDevicePath, Tech = path.targetInfo.outputTechnology };
            if (path.targetInfo.refreshRate.Denominator != 0)
                panel.Hz = (double)path.targetInfo.refreshRate.Numerator / path.targetInfo.refreshRate.Denominator;
            uint t = path.targetInfo.modeInfoIdx;
            if (t < modeCount && modes[t].infoType == 2)
            {
                panel.SignalW = modes[t].mode.target.activeSize.cx;
                panel.SignalH = modes[t].mode.target.activeSize.cy;
            }
            list.Add(panel);
        }
        return list;
    }
}
'@

function Get-ConnectionName([uint32]$tech) {
    switch ([int64]$tech) {
        0 { 'VGA' } 4 { 'DVI' } 5 { 'HDMI' } 6 { 'LVDS' } 10 { 'DisplayPort' } 11 { 'eDP' }
        15 { 'Miracast' } 16 { 'Indirect wired' } 17 { 'Indirect virtual' } 18 { 'DP over USB-C' }
        2147483648 { 'Internal' } default { "$tech" }
    }
}

# Reads the EDID Windows cached for a monitor device path like
# \\?\DISPLAY#GSM778B#5&...&UID4352#{...}.
function Get-Edid([string]$devicePath) {
    $parts = $devicePath -split '#'
    if ($parts.Count -lt 3) { return $null }
    $key = "HKLM:\SYSTEM\CurrentControlSet\Enum\DISPLAY\$($parts[1])\$($parts[2])\Device Parameters"
    $e = (Get-ItemProperty -Path $key -Name EDID -ErrorAction SilentlyContinue).EDID
    if (-not $e) { return $null }
    $mfg = ([int]$e[8] -shl 8) -bor [int]$e[9]
    $mfgId = -join @([char](64 + (($mfg -shr 10) -band 31)), [char](64 + (($mfg -shr 5) -band 31)), [char](64 + ($mfg -band 31)))
    $product = ([int]$e[11] -shl 8) -bor [int]$e[10]
    $d = 54  # first detailed timing descriptor
    $wMm = [int]$e[$d + 12] -bor (([int]$e[$d + 14] -band 0xF0) -shl 4)
    $hMm = [int]$e[$d + 13] -bor (([int]$e[$d + 14] -band 0x0F) -shl 8)
    $wPx = [int]$e[$d + 2] -bor (([int]$e[$d + 4] -band 0xF0) -shl 4)
    $hPx = [int]$e[$d + 5] -bor (([int]$e[$d + 7] -band 0xF0) -shl 4)
    [pscustomobject]@{
        Id = ('{0}{1:X4}' -f $mfgId, $product)
        Year = 1990 + [int]$e[17]
        Native = "$wPx x $hPx"
        Mm = "$wMm x $hMm"
        Cm = "$($e[21]) x $($e[22])"
        Instance = "DISPLAY\$($parts[1])\$($parts[2])"
    }
}

$wmiCm = @{}
Get-CimInstance -Namespace root\wmi -ClassName WmiMonitorBasicDisplayParams -ErrorAction SilentlyContinue | ForEach-Object {
    $wmiCm[($_.InstanceName -replace '_\d+$', '')] = "$($_.MaxHorizontalImageSize) x $($_.MaxVerticalImageSize)"
}

$monitors = [DisplayProbe]::Monitors() | Sort-Object X
$orientations = @('Landscape', 'Portrait (90)', 'Landscape (180)', 'Portrait (270)')

"Monitors, left to right in desktop coordinates (physical px):"
$monitors | Format-Table -AutoSize @(
    'Device', 'Primary', 'X', 'Y', 'W', 'H', 'Hz',
    @{ n = 'Scale%'; e = { [math]::Round($_.Dpi / 96 * 100) } },
    @{ n = 'Orientation'; e = { $orientations[$_.Orientation] } },
    'Bpp',
    @{ n = 'WorkArea'; e = { "($($_.Work.left),$($_.Work.top))-($($_.Work.right),$($_.Work.bottom))" } }
) | Out-String -Width 300

$minX = ($monitors | Measure-Object X -Minimum).Minimum
$minY = ($monitors | Measure-Object Y -Minimum).Minimum
$maxX = ($monitors | ForEach-Object { $_.X + $_.W } | Measure-Object -Maximum).Maximum
$maxY = ($monitors | ForEach-Object { $_.Y + $_.H } | Measure-Object -Maximum).Maximum
"Virtual desktop: ($minX,$minY) to ($maxX,$maxY), $($maxX - $minX) x $($maxY - $minY)"
""
"Panels, as the monitors report them:"
[DisplayProbe]::Panels() | Sort-Object Device | ForEach-Object {
    $edid = Get-Edid $_.Path
    [pscustomobject]@{
        Device = $_.Device
        Model = $_.Name
        Link = Get-ConnectionName $_.Tech
        Hz = [math]::Round($_.Hz, 3)
        Signal = "$($_.SignalW) x $($_.SignalH)"
        EdidId = $edid.Id
        Year = $edid.Year
        Native = $edid.Native
        EdidMm = $edid.Mm
        EdidCm = $edid.Cm
        WmiCm = $wmiCm[$edid.Instance]
    }
} | Format-Table -AutoSize | Out-String -Width 300
