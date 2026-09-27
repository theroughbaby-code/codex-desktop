param(
    [switch] $Diagnostic
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient

$projectRoot = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path $PSScriptRoot "approval-card-fixture.html"
$pluginAssembly = Join-Path $projectRoot "src-csharp\bin\Release\net10.0-windows\CodexDesktopPlugin.dll"
$chrome = "C:\Program Files\Google\Chrome\Application\chrome.exe"

if (-not (Test-Path -LiteralPath $chrome)) {
    throw "Chrome is required for the UI Automation integration fixture."
}

$assembly = [Reflection.Assembly]::LoadFrom($pluginAssembly)
$monitorType = $assembly.GetType(
    "Loupedeck.CodexDesktopPlugin.CodexApprovalMonitor",
    $true)
$flags = [Reflection.BindingFlags] "NonPublic,Static"
$readApprovalUis = $monitorType.GetMethods($flags) |
    Where-Object {
        $_.Name -eq "ReadApprovalUis" -and
        $_.GetParameters().Count -eq 1 -and
        $_.GetParameters()[0].ParameterType -eq [IntPtr]
    } |
    Select-Object -First 1
$invokeMatchedControl = $monitorType.GetMethod("TryInvokeMatchedControl", $flags)
$approvalRoleType = $assembly.GetType(
    "Loupedeck.CodexDesktopPlugin.ApprovalRole",
    $true)

function Get-RecordProperty {
    param(
        [Parameter(Mandatory)] $Record,
        [Parameter(Mandatory)] [string] $Name
    )

    return $Record.GetType().GetProperty($Name).GetValue($Record)
}

function Invoke-ApprovalRole {
    param(
        [Parameter(Mandatory)] [IntPtr] $WindowHandle,
        [Parameter(Mandatory)] $ApprovalUi,
        [Parameter(Mandatory)] [string] $Role,
        [Parameter(Mandatory)] [string] $ExpectedStatus
    )

    $control = Get-RecordProperty $ApprovalUi $Role
    if ($null -eq $control) {
        return $false
    }

    $roleValue = [Enum]::Parse($approvalRoleType, $Role)
    $attempt = $invokeMatchedControl.Invoke(
        $null,
        [object[]] @($WindowHandle, $roleValue, $control))
    if ($Diagnostic) {
        Write-Host "INVOKE $Role attempt=$attempt"
    }
    if ($attempt.ToString() -ne "Invoked") {
        return $false
    }

    Start-Sleep -Milliseconds 100
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($WindowHandle)
    $status = $root.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $ExpectedStatus))
    $statusName = if ($null -ne $status) {
        $status.Current.Name
    }
    else {
        ""
    }
    if ($Diagnostic) {
        Write-Host "INVOKE $Role status=$statusName"
    }
    return $statusName -eq $ExpectedStatus
}

function Stop-FixtureProcesses {
    param([Parameter(Mandatory)] [string] $ProfilePath)

    Get-CimInstance Win32_Process -Filter "Name = 'chrome.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -like "*$ProfilePath*" } |
        ForEach-Object {
            Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
        }

    Start-Sleep -Milliseconds 500
    $resolvedProfile = [IO.Path]::GetFullPath($ProfilePath)
    $resolvedTemp = [IO.Path]::GetFullPath($env:TEMP).TrimEnd("\") + "\"
    if (
        $resolvedProfile.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedProfile)
    ) {
        Remove-Item -LiteralPath $resolvedProfile -Recurse -Force
    }
}

function Write-SurfaceDiagnostic {
    param(
        [Parameter(Mandatory)] [IntPtr] $WindowHandle,
        [Parameter(Mandatory)] [System.Windows.Automation.AutomationElement] $Root
    )

    $all = $Root.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    $surfaceElement = $null
    $documentOrder = 0
    for ($index = 0; $index -lt $all.Count; $index++) {
        $candidate = $all.Item($index)
        if (
            $candidate.Current.AutomationId -like "*approval-card*" -or
            $candidate.Current.ClassName -like "*approval-card*"
        ) {
            $surfaceElement = $candidate
            $documentOrder = $index
            break
        }
    }

    if ($null -eq $surfaceElement) {
        Write-Host "DIAGNOSTIC: no approval-card element in the raw UIA tree"
        return
    }

    $tryReadElement = $monitorType.GetMethod("TryReadElement", $flags)
    $arguments = [object[]] @($surfaceElement, [int] $documentOrder, $null)
    $read = $tryReadElement.Invoke($null, $arguments)
    $snapshot = $arguments[2]
    if ($null -eq $snapshot) {
        Write-Host "DIAGNOSTIC: TryReadElement=$read and returned no snapshot"
        return
    }

    $isSurface = $monitorType.GetMethod("IsApprovalSurface", $flags).Invoke(
        $null,
        [object[]] @($snapshot))
    $approvalUi = $monitorType.GetMethod("BuildStructuralApprovalUi", $flags).Invoke(
        $null,
        [object[]] @($WindowHandle, $snapshot))
    $details = [pscustomobject] @{
        Diagnostic = $true
        TryRead = $read
        Identity = Get-RecordProperty $snapshot "IdentityText"
        ClassName = Get-RecordProperty $snapshot "ClassName"
        RuntimeKey = Get-RecordProperty $snapshot "RuntimeKey"
        IsSurface = $isSurface
        BuildReturned = $null -ne $approvalUi
        LastDiagnostic = $monitorType.GetProperty(
            "LastDiagnostic",
            [Reflection.BindingFlags] "NonPublic,Static").GetValue($null)
    }
    Write-Host ($details | Format-List | Out-String)
}

$results = foreach ($language in @("en", "de", "fr", "structural")) {
    $profile = Join-Path $env:TEMP (
        "codex-approval-fixture-$language-" + [Guid]::NewGuid().ToString("N"))
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
                    $_.MainWindowTitle -like "Codex Approval Scanner Fixture - $language*" -and
                    $_.MainWindowHandle -ne 0
                } |
                Select-Object -First 1
        } while ($null -eq $window -and [DateTime]::UtcNow -lt $deadline)

        if ($null -eq $window) {
            throw "Fixture window did not appear for $language."
        }

        $windowHandle = [IntPtr] $window.MainWindowHandle
        $approvalUis = @($readApprovalUis.Invoke($null, [object[]] @($windowHandle)))
        $approvalUi = $approvalUis | Select-Object -First 1
        if ($null -eq $approvalUi) {
            if ($Diagnostic) {
                $root = [System.Windows.Automation.AutomationElement]::FromHandle($windowHandle)
                Write-SurfaceDiagnostic -WindowHandle $windowHandle -Root $root
            }

            [pscustomobject] @{
                Language = $language
                Count = 0
                Surface = $false
                Pending = $false
                Approve = $false
                Persistent = $false
                Deny = $false
                Options = $false
                ApproveInvoked = $false
                PersistentInvoked = $false
                DenyInvoked = $false
            }
            continue
        }

        $approveInvoked = Invoke-ApprovalRole `
            -WindowHandle $windowHandle `
            -ApprovalUi $approvalUi `
            -Role "Approve" `
            -ExpectedStatus "approve"
        $persistentInvoked = Invoke-ApprovalRole `
            -WindowHandle $windowHandle `
            -ApprovalUi $approvalUi `
            -Role "Persistent" `
            -ExpectedStatus "persistent"
        $denyInvoked = Invoke-ApprovalRole `
            -WindowHandle $windowHandle `
            -ApprovalUi $approvalUi `
            -Role "Deny" `
            -ExpectedStatus "deny"

        [pscustomobject] @{
            Language = $language
            Count = $approvalUis.Count
            Surface = [bool] (Get-RecordProperty $approvalUi "HasApprovalSurface")
            Pending = [bool] (Get-RecordProperty $approvalUi "HasPending")
            Approve = $null -ne (Get-RecordProperty $approvalUi "Approve")
            Persistent = $null -ne (Get-RecordProperty $approvalUi "Persistent")
            Deny = $null -ne (Get-RecordProperty $approvalUi "Deny")
            Options = $null -ne (Get-RecordProperty $approvalUi "Options")
            ApproveInvoked = $approveInvoked
            PersistentInvoked = $persistentInvoked
            DenyInvoked = $denyInvoked
        }
    }
    finally {
        Stop-FixtureProcesses -ProfilePath $profile
    }
}

$results | Format-Table -AutoSize

if ($results.Where({
    -not $_.Surface -or
    -not $_.Pending -or
    -not $_.Approve -or
    -not $_.Persistent -or
    -not $_.Deny -or
    -not $_.Options -or
    -not $_.ApproveInvoked -or
    -not $_.PersistentInvoked -or
    -not $_.DenyInvoked
}).Count -gt 0) {
    exit 1
}
