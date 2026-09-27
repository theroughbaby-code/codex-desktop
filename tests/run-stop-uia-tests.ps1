param([switch] $Diagnostic)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient

$projectRoot = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path $PSScriptRoot "stop-control-fixture.html"
$pluginAssembly = Join-Path $projectRoot "src-csharp\bin\Release\net10.0-windows\CodexDesktopPlugin.dll"
$chrome = "C:\Program Files\Google\Chrome\Application\chrome.exe"

if (-not (Test-Path -LiteralPath $chrome)) {
    throw "Chrome is required for the UI Automation integration fixture."
}

$assembly = [Reflection.Assembly]::LoadFrom($pluginAssembly)
$monitorType = $assembly.GetType(
    "Loupedeck.CodexDesktopPlugin.CodexStopMonitor",
    $true)
$flags = [Reflection.BindingFlags] "NonPublic,Static"
$readStopControls = $monitorType.GetMethods($flags) |
    Where-Object {
        $_.Name -eq "ReadStopControls" -and
        $_.GetParameters().Count -eq 1 -and
        $_.GetParameters()[0].ParameterType -eq [IntPtr]
    } |
    Select-Object -First 1
$tryInvokeControl = $monitorType.GetMethod("TryInvokeControl", $flags)

function Stop-FixtureProcesses {
    param([Parameter(Mandatory)] [string] $ProfilePath)

    Get-CimInstance Win32_Process -Filter "Name = 'chrome.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -like "*$ProfilePath*" } |
        ForEach-Object {
            Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
        }

    Start-Sleep -Milliseconds 300
    $resolvedProfile = [IO.Path]::GetFullPath($ProfilePath)
    $resolvedTemp = [IO.Path]::GetFullPath($env:TEMP).TrimEnd("\") + "\"
    if (
        $resolvedProfile.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedProfile)
    ) {
        Remove-Item -LiteralPath $resolvedProfile -Recurse -Force
    }
}

$results = foreach ($language in @("en", "cs", "de", "fr", "zh", "structural")) {
    $profile = Join-Path $env:TEMP (
        "codex-stop-fixture-$language-" + [Guid]::NewGuid().ToString("N"))
    $url = ([Uri]::new($fixture)).AbsoluteUri + "?lang=$language"
    Start-Process $chrome -ArgumentList @(
        "--user-data-dir=$profile",
        "--no-first-run",
        "--disable-default-apps",
        "--force-renderer-accessibility",
        "--app=$url"
    ) -WindowStyle Normal | Out-Null

    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        $window = $null
        do {
            Start-Sleep -Milliseconds 250
            $window = Get-Process chrome -ErrorAction SilentlyContinue |
                Where-Object {
                    $_.MainWindowTitle -like "Codex Stop Scanner Fixture - $language*" -and
                    $_.MainWindowHandle -ne 0
                } |
                Select-Object -First 1
        } while ($null -eq $window -and [DateTime]::UtcNow -lt $deadline)

        if ($null -eq $window) {
            throw "Fixture window did not appear for $language."
        }

        $windowHandle = [IntPtr] $window.MainWindowHandle
        $controls = @($readStopControls.Invoke($null, [object[]] @($windowHandle)))
        if ($Diagnostic -and $controls.Count -ne 1) {
            $diagnosticRoot = [System.Windows.Automation.AutomationElement]::FromHandle($windowHandle)
            $buttonCondition = [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Button)
            $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
            foreach ($button in $diagnosticRoot.FindAll(
                [System.Windows.Automation.TreeScope]::Descendants,
                $buttonCondition)) {
                $parents = @()
                $parent = $walker.GetParent($button)
                for ($depth = 0; $depth -lt 8 -and $null -ne $parent; $depth++) {
                    $parents += "$($parent.Current.ControlType.ProgrammaticName):$($parent.Current.AutomationId):$($parent.Current.ClassName):$($parent.Current.Name)"
                    $parent = $walker.GetParent($parent)
                }

                Write-Host "BUTTON language=$language name='$($button.Current.Name)' id='$($button.Current.AutomationId)' class='$($button.Current.ClassName)' parents='$($parents -join ' > ')'"
            }
        }
        $invoked = $false
        if ($controls.Count -eq 1) {
            $element = $controls[0].GetType().GetProperty("Element").GetValue($controls[0])
            $invoked = [bool] $tryInvokeControl.Invoke($null, [object[]] @($element))
        }

        Start-Sleep -Milliseconds 100
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($windowHandle)
        $status = $root.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::NameProperty,
                "stopped"))
        $statusText = if ($null -ne $status) {
            $status.Current.Name
        } else {
            ""
        }

        [pscustomobject] @{
            Language = $language
            Count = $controls.Count
            DecoyIgnored = $controls.Count -eq 1
            Invoked = $invoked
            Status = $statusText
        }
    }
    finally {
        Stop-FixtureProcesses -ProfilePath $profile
    }
}

$results | Format-Table -AutoSize
$failed = @($results | Where-Object {
    $_.Count -ne 1 -or
    -not $_.DecoyIgnored -or
    -not $_.Invoked -or
    $_.Status -ne "stopped"
})
if ($failed.Count -gt 0) {
    throw "Stop UI Automation test failed for: $($failed.Language -join ', ')."
}
