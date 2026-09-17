# Capture the Unity editor window (with the Chart Editor open) to a PNG.
# Usage: double-click capture_editor_shot.cmd, or run this file with PowerShell.
#
# 1. Open the project in Unity, open Tools > FallenAngel > Chart Editor
# 2. Load a chart so the timeline is not empty (button: 载入 / Load)
# 3. Run this script — it brings Unity to the front and captures its window

param(
  [string]$Out = "C:\Users\LorXer\Desktop\作品展示手册\assets\fallenangel-editor.png"
)

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class ShotWin32 {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
"@

$unity = Get-Process -Name Unity -ErrorAction SilentlyContinue |
  Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } |
  Select-Object -First 1

if (-not $unity) {
  Write-Host "Unity 没有在运行，或者窗口还没出来。先打开 Unity 工程，再跑一次。" -ForegroundColor Yellow
  exit 1
}

$handle = $unity.MainWindowHandle
[void][ShotWin32]::SetForegroundWindow($handle)
Start-Sleep -Milliseconds 900

$rect = New-Object ShotWin32+RECT
[void][ShotWin32]::GetWindowRect($handle, [ref]$rect)
$w = $rect.Right - $rect.Left
$h = $rect.Bottom - $rect.Top
if ($w -lt 200 -or $h -lt 200) {
  Write-Host "窗口尺寸异常（${w}x${h}），没有截图。" -ForegroundColor Yellow
  exit 1
}

$dir = Split-Path -Parent $Out
if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bmp.Size)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()

Write-Host ("已截图 {0}x{1} -> {2}" -f $w, $h, $Out) -ForegroundColor Green
Write-Host ("文件大小: {0} KB" -f [math]::Round((Get-Item -LiteralPath $Out).Length / 1KB, 0))
