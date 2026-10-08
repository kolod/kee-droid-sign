<#
.SYNOPSIS
    Installs KeeDroidSign.plgx into KeePass: closes KeePass, copies the plugin, starts KeePass again.

.DESCRIPTION
    1. Closes all running KeePass instances with "KeePass.exe --exit-all" (a normal exit: KeePass
       asks about unsaved changes). If an instance is still running after -ExitTimeoutSeconds, the
       script stops, unless -Force is given, which kills the remaining processes (unsaved changes
       are lost).
    2. Removes leftovers of earlier DLL-based installs and copies artifacts/plgx/KeeDroidSign.plgx
       (built by build/Build-Plgx.ps1) to <KeePassDir>/Plugins/.
    3. Starts KeePass again through explorer.exe, so it runs with normal user rights even when this
       script runs elevated (writing to Program Files usually needs an elevated PowerShell).
       Use -NoStart to skip this step.
    4. Waits up to -FocusTimeoutSeconds for KeePass's active window (the master password prompt
       while it is open, since it disables the main window) and brings it to the foreground without
       injecting keys, so the password can be typed right away: Caps Lock is turned off first and
       the window is switched to an installed English keyboard layout (none is added). Use -NoFocus
       to skip this step.
#>
[CmdletBinding()]
param(
    [string]$KeePassDir = (Join-Path $env:ProgramFiles 'KeePass Password Safe 2'),
    [int]$ExitTimeoutSeconds = 60,
    [int]$FocusTimeoutSeconds = 60,
    [switch]$Force,
    [switch]$NoStart,
    [switch]$NoFocus
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$plgx = Join-Path $repoRoot 'artifacts/plgx/KeeDroidSign.plgx'
$keePassExe = Join-Path $KeePassDir 'KeePass.exe'

if (-not (Test-Path $keePassExe)) {
    throw "KeePass.exe not found in '$KeePassDir'. Pass -KeePassDir."
}
if (-not (Test-Path $plgx)) {
    throw "'$plgx' not found. Run build/Build-Plgx.ps1 first."
}

# 1. Close running KeePass instances.
$running = @(Get-Process -Name 'KeePass' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Host "Closing $($running.Count) KeePass instance(s)..."
    & $keePassExe --exit-all

    $deadline = (Get-Date).AddSeconds($ExitTimeoutSeconds)
    while ((Get-Process -Name 'KeePass' -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
    }

    $remaining = @(Get-Process -Name 'KeePass' -ErrorAction SilentlyContinue)
    if ($remaining.Count -gt 0) {
        if (-not $Force) {
            throw "KeePass is still running after $ExitTimeoutSeconds s (maybe waiting for an answer about unsaved changes). Close it and run again, or use -Force to kill it (unsaved changes are lost)."
        }
        Write-Warning 'Killing remaining KeePass processes (-Force); unsaved changes are lost.'
        $remaining | Stop-Process -Force
        $remaining | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
    }
}

# 2. Install.
$target = Join-Path $KeePassDir 'Plugins'
New-Item -ItemType Directory -Force -Path $target | Out-Null

# Leftovers of DLL-based installs would load the plugin twice or fail on missing dependencies:
# Plugins/KeeDroidSign/ and loose DLLs directly in Plugins/.
$oldDir = Join-Path $target 'KeeDroidSign'
if (Test-Path $oldDir) {
    Remove-Item -Recurse -Force $oldDir
    Write-Host "Removed old DLL installation: $oldDir"
}
# Only our own DLLs: a loose BouncyCastle.Cryptography.dll may belong to another plugin.
foreach ($name in 'KeeDroidSign.dll', 'KeeDroidSign.Core.dll') {
    $loose = Join-Path $target $name
    if (Test-Path $loose) {
        Remove-Item -Force $loose
        Write-Host "Removed old file: $loose"
    }
}

Copy-Item -Path $plgx -Destination $target -Force
Write-Host "Installed $(Join-Path $target 'KeeDroidSign.plgx')"

# 3. Start KeePass with normal user rights (explorer.exe launches it unelevated).
if (-not $NoStart) {
    Start-Process -FilePath (Join-Path $env:WINDIR 'explorer.exe') -ArgumentList "`"$keePassExe`""
    Write-Host 'Started KeePass. The first start compiles the plugin and takes a few seconds longer.'

    # 4. Bring KeePass's active window (the master password prompt) to the foreground.
    if (-not $NoFocus) {
        # Add-Type types live as long as the PowerShell session: compile only once per session, and
        # bump the version in the name whenever the code below changes.
        if (-not ('KdsWindowFocus3' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class KdsWindowFocus3
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool doAttach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    [DllImport("user32.dll")] private static extern int GetKeyboardLayoutList(int count, IntPtr[] layouts);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Turns Caps Lock off; returns true if it was on. Call it before focusing KeePass, so the
    /// synthetic Caps Lock press goes to the console, not to the password box.
    /// </summary>
    public static bool TurnOffCapsLock()
    {
        const int VK_CAPITAL = 0x14;
        const uint KEYEVENTF_KEYUP = 0x2;
        if ((GetKeyState(VK_CAPITAL) & 1) == 0) return false;
        keybd_event(VK_CAPITAL, 0x3A, 0, UIntPtr.Zero);
        keybd_event(VK_CAPITAL, 0x3A, KEYEVENTF_KEYUP, UIntPtr.Zero);
        return true;
    }

    /// <summary>
    /// Asks the window to switch to an installed English keyboard layout (en-US preferred); never
    /// adds a layout. Returns the language ID used, or 0 when no English layout is installed.
    /// </summary>
    public static int SwitchToEnglishLayout(IntPtr hWnd)
    {
        const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
        const int LANG_ENGLISH = 0x09;
        const int EN_US = 0x0409;
        int count = GetKeyboardLayoutList(0, null);
        var layouts = new IntPtr[count];
        GetKeyboardLayoutList(count, layouts);

        IntPtr chosen = IntPtr.Zero;
        foreach (IntPtr layout in layouts)
        {
            int language = (int)((long)layout & 0xFFFF);
            if ((language & 0x3FF) != LANG_ENGLISH) continue;
            if (chosen == IntPtr.Zero || language == EN_US) chosen = layout;
            if (language == EN_US) break;
        }
        if (chosen == IntPtr.Zero) return 0;
        PostMessage(hWnd, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, chosen);
        return (int)((long)chosen & 0xFFFF);
    }

    /// <summary>First visible, enabled, titled top-level window of the processes (and its title), or zero.</summary>
    public static IntPtr FindActiveWindow(uint[] processIds, out string title)
    {
        var ids = new HashSet<uint>(processIds);
        IntPtr found = IntPtr.Zero;
        string text = null;
        EnumWindows((hWnd, l) =>
        {
            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);
            if (!ids.Contains(pid) || !IsWindowVisible(hWnd) || !IsWindowEnabled(hWnd)) return true;
            int length = GetWindowTextLength(hWnd);
            if (length == 0) return true;
            var buffer = new StringBuilder(length + 1);
            GetWindowText(hWnd, buffer, buffer.Capacity);
            found = hWnd;
            text = buffer.ToString();
            return false;
        }, IntPtr.Zero);
        title = text;
        return found;
    }

    /// <summary>
    /// Brings the window to the foreground. Windows only lets the process that owns the foreground
    /// (or got the last input) do that, so this thread briefly shares the input state of the
    /// current foreground window's thread. No key is injected: a synthetic key (such as Alt) would
    /// reach the password prompt and swallow the first typed character.
    /// </summary>
    public static bool Focus(IntPtr hWnd)
    {
        const int SW_RESTORE = 9;
        const uint MOUSEEVENTF_MOVE = 0x1;
        if (IsIconic(hWnd)) ShowWindow(hWnd, SW_RESTORE);

        uint ignored;
        uint self = GetCurrentThreadId();
        uint foreground = GetWindowThreadProcessId(GetForegroundWindow(), out ignored);
        bool attached = foreground != 0 && foreground != self && AttachThreadInput(self, foreground, true);
        try
        {
            BringWindowToTop(hWnd);
            SetForegroundWindow(hWnd);
        }
        finally
        {
            if (attached) AttachThreadInput(self, foreground, false);
        }
        if (GetForegroundWindow() == hWnd) return true;

        // Fallback: a zero-length mouse move counts as this process's last input event.
        mouse_event(MOUSEEVENTF_MOVE, 0, 0, 0, UIntPtr.Zero);
        SetForegroundWindow(hWnd);
        return GetForegroundWindow() == hWnd;
    }
}
'@
        }

        $deadline = (Get-Date).AddSeconds($FocusTimeoutSeconds)
        $window = [IntPtr]::Zero
        $title = $null
        while ((Get-Date) -lt $deadline) {
            $ids = @(Get-Process -Name 'KeePass' -ErrorAction SilentlyContinue | ForEach-Object { [uint32]$_.Id })
            if ($ids.Count -gt 0) {
                $window = [KdsWindowFocus3]::FindActiveWindow([uint32[]]$ids, [ref]$title)
                if ($window -ne [IntPtr]::Zero) { break }
            }
            Start-Sleep -Milliseconds 300
        }

        if ($window -eq [IntPtr]::Zero) {
            Write-Warning "No KeePass window appeared within $FocusTimeoutSeconds s; focus not changed."
        }
        else {
            # Ready for typing the master password: Caps Lock off, English layout.
            if ([KdsWindowFocus3]::TurnOffCapsLock()) { Write-Host 'Caps Lock turned off.' }
            if ([KdsWindowFocus3]::Focus($window)) {
                Write-Host "Focused KeePass window: $title"
            }
            else {
                Write-Warning "Windows did not allow focusing '$title'; click it to type the master password."
            }
            $language = [KdsWindowFocus3]::SwitchToEnglishLayout($window)
            if ($language -ne 0) {
                Write-Host ("Keyboard layout: {0}" -f [System.Globalization.CultureInfo]::GetCultureInfo($language).DisplayName)
            }
            else {
                Write-Warning 'No English keyboard layout is installed; the layout was not changed.'
            }
        }
    }
}
