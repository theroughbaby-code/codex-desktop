param(
  [Parameter(Mandatory = $true)]
  [ValidateSet('ChatGPT', 'Codex')]
  [string] $Mode,

  [string] $CodexPath = '',

  [int] $TimeoutSeconds = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class NativeWindow {
  [DllImport("user32.dll")]
  public static extern bool SetForegroundWindow(IntPtr hWnd);

  [DllImport("user32.dll")]
  public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

  [DllImport("user32.dll")]
  public static extern bool SetCursorPos(int X, int Y);

  [DllImport("user32.dll")]
  public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
}
'@

$SW_RESTORE = 9
$MOUSEEVENTF_LEFTDOWN = 0x0002
$MOUSEEVENTF_LEFTUP = 0x0004

function Test-Executable {
  param([string] $Path)
  return [bool]($Path -and (Test-Path -LiteralPath $Path -PathType Leaf))
}

function Find-CodexExecutable {
  if (Test-Executable $env:CODEX_CLI) {
    return $env:CODEX_CLI
  }

  if (Test-Executable $CodexPath) {
    return $CodexPath
  }

  $pathEntries = ($env:PATH -split ';') | Where-Object { $_ }
  foreach ($entry in $pathEntries) {
    $candidate = Join-Path $entry 'codex.exe'
    if (Test-Executable $candidate) {
      return $candidate
    }
  }

  $roots = @()
  if ($env:LOCALAPPDATA) {
    $roots += Join-Path $env:LOCALAPPDATA 'OpenAI\Codex\bin'
  }
  $roots += Join-Path $env:USERPROFILE 'AppData\Local\OpenAI\Codex\bin'

  foreach ($root in ($roots | Select-Object -Unique)) {
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
      continue
    }

    $candidate = Get-ChildItem -LiteralPath $root -Directory |
      ForEach-Object { Join-Path $_.FullName 'codex.exe' } |
      Where-Object { Test-Executable $_ } |
      Sort-Object { (Get-Item -LiteralPath $_).LastWriteTimeUtc } -Descending |
      Select-Object -First 1

    if ($candidate) {
      return $candidate
    }
  }

  return $null
}

function Get-ChatGptWindowProcess {
  $processes = Get-Process -ErrorAction SilentlyContinue |
    Where-Object {
      $_.MainWindowHandle -ne 0 -and
      (
        $_.ProcessName -match 'ChatGPT|Codex' -or
        $_.MainWindowTitle -match 'ChatGPT|Codex'
      )
    }

  return $processes | Sort-Object StartTime -Descending | Select-Object -First 1
}

function Ensure-ChatGptWindow {
  $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
  $process = Get-ChatGptWindowProcess

  if (-not $process) {
    $resolvedCodexPath = Find-CodexExecutable
    if (-not $resolvedCodexPath) {
      throw 'Could not find codex.exe. Set CODEX_CLI to the full codex.exe path or reinstall ChatGPT/Codex Desktop.'
    }

    Start-Process -FilePath $resolvedCodexPath -ArgumentList @('app') -WindowStyle Hidden
  }

  do {
    $process = Get-ChatGptWindowProcess
    if ($process) {
      [NativeWindow]::ShowWindowAsync($process.MainWindowHandle, $SW_RESTORE) | Out-Null
      [NativeWindow]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
      Start-Sleep -Milliseconds 400
      return [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    }
    Start-Sleep -Milliseconds 250
  } while ((Get-Date) -lt $deadline)

  throw 'Could not find a ChatGPT/Codex Desktop window.'
}

function Click-ElementCenter {
  param([System.Windows.Automation.AutomationElement] $Element)

  $rectangle = $Element.Current.BoundingRectangle
  if ($rectangle.IsEmpty) {
    return $false
  }

  $x = [int]($rectangle.Left + ($rectangle.Width / 2))
  $y = [int]($rectangle.Top + ($rectangle.Height / 2))
  [NativeWindow]::SetCursorPos($x, $y) | Out-Null
  [NativeWindow]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
  [NativeWindow]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
  return $true
}

function Invoke-Element {
  param([System.Windows.Automation.AutomationElement] $Element)

  $pattern = $null

  if ($Element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref] $pattern)) {
    $pattern.Invoke()
    return $true
  }

  if ($Element.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref] $pattern)) {
    $pattern.Select()
    return $true
  }

  if ($Element.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref] $pattern)) {
    $pattern.Expand()
    return $true
  }

  return Click-ElementCenter $Element
}

function Find-NamedElement {
  param(
    [System.Windows.Automation.AutomationElement] $Root,
    [string] $Name
  )

  $all = $Root.FindAll(
    [System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.Condition]::TrueCondition
  )

  foreach ($element in $all) {
    try {
      $elementName = $element.Current.Name
      if ($elementName -eq $Name -or $elementName -like "*$Name*") {
        return $element
      }
    } catch {
      continue
    }
  }

  return $null
}

function Switch-Mode {
  param([string] $TargetMode)

  $root = Ensure-ChatGptWindow
  $target = Find-NamedElement -Root $root -Name $TargetMode

  if ($target -and (Invoke-Element $target)) {
    return
  }

  $openerName = if ($TargetMode -eq 'Codex') { 'ChatGPT' } else { 'Codex' }
  $opener = Find-NamedElement -Root $root -Name $openerName

  if ($opener) {
    Invoke-Element $opener | Out-Null
    Start-Sleep -Milliseconds 500
  }

  $root = Ensure-ChatGptWindow
  $target = Find-NamedElement -Root $root -Name $TargetMode

  if ($target -and (Invoke-Element $target)) {
    return
  }

  throw "Could not find a visible '$TargetMode' control. Open the top-left ChatGPT/Codex switcher once, then retry."
}

Switch-Mode -TargetMode $Mode
