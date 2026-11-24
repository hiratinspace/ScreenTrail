<#
.SYNOPSIS
    Says why ScreenTrail may refuse to photograph the window in front: another application's window over
    it, a see-through window, or a picture Windows hands back blank.

.DESCRIPTION
    A click's frame is skipped when any visible window of another process lies above the window in front
    and overlaps it (WindowCover), or when the window in front is drawn see-through. The service log then
    says only "No frame for the click ... the window was gone". This probe applies the same rules and
    names what it finds.

    Run it, then within the countdown click into the window you want checked (an AnyDesk or Remote
    Desktop session, say) and leave it in front:

        powershell -ExecutionPolicy Bypass -File scripts\windows\probe-capture-cover.ps1

    Read-only. Prints process names, window classes, sizes and flags. Window titles are never printed:
    they can name customers and machines, and this output may be pasted into an issue. Control characters
    in a class name are shown escaped, such as \n.
#>

[CmdletBinding()]
param(
    # Seconds to wait before looking, so there is time to click into the window to check.
    [int] $Delay = 10
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class CoverProbe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll")] public static extern bool GetLayeredWindowAttributes(IntPtr hWnd, out uint key, out byte alpha, out uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowDisplayAffinity(IntPtr hWnd, out uint affinity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder name, int count);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out Rect value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out int value, int size);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] public static extern bool StretchBlt(IntPtr dest, int dx, int dy, int dw, int dh, IntPtr src, int sx, int sy, int sw, int sh, uint rop);
    [DllImport("gdi32.dll")] public static extern uint GetPixel(IntPtr dc, int x, int y);

    public static Rect Visible(IntPtr hWnd)
    {
        Rect frame;
        if (DwmGetWindowAttribute(hWnd, 9, out frame, Marshal.SizeOf(typeof(Rect))) == 0) { return frame; }
        GetWindowRect(hWnd, out frame);
        return frame;
    }

    public static bool Cloaked(IntPtr hWnd)
    {
        int cloaked;
        return DwmGetWindowAttribute(hWnd, 14, out cloaked, 4) == 0 && cloaked != 0;
    }

    public static string ClassOf(IntPtr hWnd)
    {
        var name = new StringBuilder(256);
        GetClassName(hWnd, name, name.Capacity);
        var shown = new StringBuilder();
        foreach (var c in name.ToString())
        {
            if (c == '\n') { shown.Append("\\n"); }
            else if (c == '\r') { shown.Append("\\r"); }
            else if (char.IsControl(c)) { shown.Append("\\x").Append(((int)c).ToString("x2")); }
            else { shown.Append(c); }
        }
        return shown.ToString();
    }

    // The window reduced to 16x16 the way the scene sampler reads it (StretchBlt, no CAPTUREBLT), as
    // brightness 0-255: the lowest, the highest, and the average. All zero is a picture Windows gave back black.
    public static int[] Brightness(Rect r)
    {
        var screen = GetDC(IntPtr.Zero);
        var mem = CreateCompatibleDC(screen);
        var bmp = CreateCompatibleBitmap(screen, 16, 16);
        var old = SelectObject(mem, bmp);
        SetStretchBltMode(mem, 4);
        var ok = StretchBlt(mem, 0, 0, 16, 16, screen, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, 0x00CC0020);
        int min = 255, max = 0, sum = 0;
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                var p = GetPixel(mem, x, y);
                var luma = (int)((((p & 0xFF) * 299) + (((p >> 8) & 0xFF) * 587) + (((p >> 16) & 0xFF) * 114)) / 1000);
                if (luma < min) { min = luma; }
                if (luma > max) { max = luma; }
                sum += luma;
            }
        }
        SelectObject(mem, old);
        DeleteObject(bmp);
        DeleteDC(mem);
        ReleaseDC(IntPtr.Zero, screen);
        return ok ? new[] { min, max, sum / 256 } : null;
    }
}
'@

function Get-ProcessNameOf([IntPtr] $window) {
    $processId = [uint32]0
    [void][CoverProbe]::GetWindowThreadProcessId($window, [ref]$processId)
    $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
    $name = if ($null -eq $process) { 'unknown' } else { $process.ProcessName }
    return @{ Name = $name; Id = $processId }
}

function Format-Size($r) { '{0}x{1}' -f ($r.Right - $r.Left), ($r.Bottom - $r.Top) }

for ($left = $Delay; $left -gt 0; $left--) {
    Write-Host -NoNewline ("`rClick into the window to check and leave it in front: looking in {0,2} s " -f $left)
    Start-Sleep -Seconds 1
}
Write-Host ''

$target = [CoverProbe]::GetForegroundWindow()
if ($target -eq [IntPtr]::Zero) {
    Write-Output 'No window is in front. Run it again and click into the window during the countdown.'
    exit 1
}

$owner = Get-ProcessNameOf $target
$bounds = [CoverProbe]::Visible($target)
Write-Output ''
Write-Output ('Window in front: {0} (pid {1}) class={2} size {3} at {4},{5}' -f $owner.Name, $owner.Id, [CoverProbe]::ClassOf($target), (Format-Size $bounds), $bounds.Left, $bounds.Top)

$exStyle = [CoverProbe]::GetWindowLongPtr($target, -20).ToInt64()
if (($exStyle -band 0x00080000) -eq 0) {
    Write-Output '  Drawn: not layered (fine)'
} else {
    $key = [uint32]0; $alpha = [byte]0; $flags = [uint32]0
    if ([CoverProbe]::GetLayeredWindowAttributes($target, [ref]$key, [ref]$alpha, [ref]$flags)) {
        $drawn = @()
        if ($flags -band 2) { $drawn += "opacity $alpha of 255" }
        if ($flags -band 1) { $drawn += 'colour key' }
        if ($drawn.Count -eq 0) { $drawn += 'no attributes set' }
        $verdict = if (($flags -band 2) -and $alpha -lt 255) { '  <-- SKIPPED: see-through' } else { '' }
        Write-Output ('  Drawn: layered, {0}{1}' -f ($drawn -join ', '), $verdict)
    } else {
        Write-Output '  Drawn: layered, per-pixel alpha (fine)'
    }
}

Write-Output ''
Write-Output 'Every shown window above it in z-order (what ScreenTrail checks for cover):'
$covers = 0
$walked = 0
$above = [CoverProbe]::GetWindow($target, 3)   # GW_HWNDPREV
while ($above -ne [IntPtr]::Zero -and $walked -lt 512) {
    $walked++
    $rect = New-Object CoverProbe+Rect
    if ([CoverProbe]::IsWindowVisible($above) -and -not [CoverProbe]::IsIconic($above) -and [CoverProbe]::GetWindowRect($above, [ref]$rect)) {
        $who = Get-ProcessNameOf $above
        $class = [CoverProbe]::ClassOf($above)
        $r = [CoverProbe]::Visible($above)
        $topmost = (([CoverProbe]::GetWindowLongPtr($above, -20).ToInt64()) -band 0x8) -ne 0
        $cloaked = [CoverProbe]::Cloaked($above)
        $affinity = [uint32]0
        $excluded = [CoverProbe]::GetWindowDisplayAffinity($above, [ref]$affinity) -and $affinity -ne 0
        $taskbar = $class -eq 'Shell_TrayWnd' -or $class -eq 'Shell_SecondaryTrayWnd'
        $overlaps = $r.Left -lt $bounds.Right -and $bounds.Left -lt $r.Right -and $r.Top -lt $bounds.Bottom -and $bounds.Top -lt $r.Bottom

        $why = if ($cloaked) { 'ignored: hidden by Windows' }
            elseif ($who.Id -eq $owner.Id) { 'ignored: same process' }
            elseif ($excluded) { 'ignored: kept out of screenshots' }
            elseif ($taskbar) { 'ignored: taskbar' }
            elseif (-not $overlaps) { 'ignored: does not overlap' }
            else { $covers++; '<-- COVERS IT: frames skipped' }

        $sameName = if ($who.Id -ne $owner.Id -and $who.Name -eq $owner.Name) { ' (same app, another process)' } else { '' }
        Write-Output ('  {0} (pid {1}){2} class={3} size {4} at {5},{6} topmost={7}  {8}' -f $who.Name, $who.Id, $sameName, $class, (Format-Size $r), $r.Left, $r.Top, $topmost, $why)
    }
    $above = [CoverProbe]::GetWindow($above, 3)
}
if ($walked -ge 512) { Write-Output '  (stopped after 512 windows: ScreenTrail counts that as covered)'; $covers++ }
if ($walked -eq 0) { Write-Output '  none' }

Write-Output ''
Write-Output 'Brightness of the window as the scene sampler reads it (lowest / highest / average, 0-255), 3 reads 2 s apart:'
for ($i = 1; $i -le 3; $i++) {
    $b = [CoverProbe]::Brightness($bounds)
    if ($null -eq $b) { Write-Output "  read $i`: the copy failed" } else { Write-Output ("  read {0}: {1} / {2} / {3}" -f $i, $b[0], $b[1], $b[2]) }
    if ($i -lt 3) { Start-Sleep -Seconds 2 }
}

Write-Output ''
if ($covers -gt 0) {
    Write-Output "Result: $covers window(s) cover it, so ScreenTrail skips its frames."
} else {
    Write-Output 'Result: nothing covers it.'
}
