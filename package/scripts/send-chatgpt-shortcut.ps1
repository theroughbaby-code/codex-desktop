param(
  [Parameter(Mandatory = $true)]
  [ValidateSet('Companion', 'Search', 'Browser')]
  [string]$Shortcut,

  [string]$CodexPath
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class WindowTools {
  [DllImport("user32.dll")]
  public static extern bool SetForegroundWindow(IntPtr hWnd);
}
'@

function Get-ChatGptDesktopWindow {
  Get-Process |
    Where-Object {
      $_.MainWindowHandle -ne 0 -and
      ($_.MainWindowTitle -match 'ChatGPT|Codex' -or $_.ProcessName -match 'ChatGPT|Codex|OpenAI')
    } |
    Select-Object -First 1
}

function Ensure-AppOpen {
  $process = Get-ChatGptDesktopWindow
  if ($process) {
    return $process
  }

  if ($CodexPath -and (Test-Path -LiteralPath $CodexPath)) {
    Start-Process -FilePath $CodexPath -ArgumentList 'app' -WindowStyle Hidden
  }

  $deadline = (Get-Date).AddSeconds(8)
  while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 250
    $process = Get-ChatGptDesktopWindow
    if ($process) {
      return $process
    }
  }

  return $null
}

$process = Ensure-AppOpen

if ($Shortcut -ne 'Companion' -and $process) {
  [WindowTools]::SetForegroundWindow([IntPtr]$process.MainWindowHandle) | Out-Null
  Start-Sleep -Milliseconds 150
}

switch ($Shortcut) {
  'Companion' { [System.Windows.Forms.SendKeys]::SendWait('% ') }
  'Search' { [System.Windows.Forms.SendKeys]::SendWait('^k') }
  'Browser' { [System.Windows.Forms.SendKeys]::SendWait('^+b') }
}
