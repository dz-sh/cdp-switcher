$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class CdpSwitcherSmokeInput
{
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(
        uint flags,
        uint dx,
        uint dy,
        uint data,
        UIntPtr extraInfo);
}
'@

function Write-StartupEvidence {
  param(
    [Parameter(Mandatory)]
    [string] $Label,

    [Parameter(Mandatory)]
    [string] $Path,

    [Parameter(Mandatory)]
    [System.Diagnostics.Process] $Process,

    [Parameter(Mandatory)]
    [datetime] $StartedAt
  )

  Start-Sleep -Seconds 2
  $Process.Refresh()
  $exitCode = if ($Process.HasExited) { $Process.ExitCode } else { '<running>' }

  Write-Host "::group::Startup evidence: $Label"
  Write-Host "Path: $Path"
  Write-Host "Process ID: $($Process.Id)"
  Write-Host "Has exited: $($Process.HasExited)"
  Write-Host "Exit code: $exitCode"
  Write-Host "Main window handle: $($Process.MainWindowHandle)"
  Write-Host "Main window title: $($Process.MainWindowTitle)"

  $relatedProcesses = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object {
      $_.ProcessId -eq $Process.Id -or
      $_.ParentProcessId -eq $Process.Id
    } |
    Select-Object Name, ProcessId, ParentProcessId
  if ($relatedProcesses) {
    Write-Host 'Related processes:'
    $relatedProcesses | Format-Table -AutoSize | Out-String | Write-Host
  }
  else {
    Write-Host 'Related processes: none running'
  }

  $extractionRoot = Join-Path $env:TEMP '.net\CdpSwitcher'
  if (Test-Path $extractionRoot) {
    $extractedFiles = Get-ChildItem $extractionRoot -Recurse -File -ErrorAction SilentlyContinue
    Write-Host "Single-file extraction: present ($($extractedFiles.Count) files)"
    $extractedFiles |
      Where-Object {
        $_.Name -in @(
          'CdpSwitcher.dll',
          'Microsoft.UI.Xaml.dll',
          'Microsoft.WindowsAppRuntime.dll',
          'resources.pri'
        )
      } |
      Select-Object Name, Length, LastWriteTimeUtc |
      Format-Table -AutoSize |
      Out-String |
      Write-Host
  }
  else {
    Write-Host 'Single-file extraction: absent'
  }

  $events = Get-WinEvent -FilterHashtable @{
    LogName = 'Application'
    StartTime = $StartedAt.AddSeconds(-2)
  } -ErrorAction SilentlyContinue |
    Where-Object {
      $_.ProviderName -in @(
        '.NET Runtime',
        'Application Error',
        'Windows Error Reporting'
      ) -and
      $_.Message -match 'CdpSwitcher'
    } |
    Sort-Object TimeCreated

  if ($events) {
    Write-Host 'Relevant Windows application events:'
    foreach ($event in $events) {
      $message = $event.Message
      foreach ($root in @(
        $env:GITHUB_WORKSPACE,
        $env:RUNNER_TEMP,
        $env:TEMP
      )) {
        if ($root) {
          $message = $message.Replace($root, '<path>')
        }
      }
      Write-Host "[$($event.TimeCreated.ToUniversalTime().ToString('o'))] $($event.ProviderName) $($event.Id)"
      Write-Host $message
    }
  }
  else {
    Write-Host 'Relevant Windows application events: none'
  }
  Write-Host '::endgroup::'
}

function Assert-AppStarts {
  param(
    [Parameter(Mandatory)]
    [string] $Label,

    [Parameter(Mandatory)]
    [string] $Path
  )

  $startedAt = Get-Date
  $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
  $process = Start-Process -FilePath $Path -PassThru
  try {
    do {
      Start-Sleep -Milliseconds 500
      $process.Refresh()
      if ($process.HasExited) {
        throw "$Label exited after $([math]::Round($stopwatch.Elapsed.TotalSeconds, 1)) seconds with code $($process.ExitCode)."
      }
      if ($process.MainWindowHandle -ne 0 -and
          $process.MainWindowTitle -eq 'CDP Switcher') {
        Assert-ProfileCanBeCreated $Label $process
        Write-Host "$Label opened in $([math]::Round($stopwatch.Elapsed.TotalSeconds, 1)) seconds."
        return
      }
    } while ($stopwatch.Elapsed.TotalSeconds -lt 30)

    throw "$Label did not create the expected main window."
  }
  catch {
    Write-StartupEvidence $Label $Path $process $startedAt
    throw
  }
  finally {
    if (-not $process.HasExited) {
      Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
    }
  }
}

function Wait-ForVisibleElement {
  param(
    [Parameter(Mandatory)]
    $Root,

    [Parameter(Mandatory)]
    $Condition,

    [switch] $RequireEnabled
  )

  $stopwatch =
    [System.Diagnostics.Stopwatch]::StartNew()
  do {
    $element = $Root.FindAll(
      [System.Windows.Automation.TreeScope]::Descendants,
      $Condition
    ) |
      Where-Object {
        -not $_.Current.IsOffscreen -and
        (-not $RequireEnabled -or $_.Current.IsEnabled)
      } |
      Select-Object -First 1
    if ($element) {
      return $element
    }
    Start-Sleep -Milliseconds 250
  } while ($stopwatch.Elapsed.TotalSeconds -lt 30)

  return $null
}

function Wait-ForElementAbsent {
  param(
    [Parameter(Mandatory)]
    $Root,

    [Parameter(Mandatory)]
    $Condition
  )

  $stopwatch =
    [System.Diagnostics.Stopwatch]::StartNew()
  do {
    $element = $Root.FindAll(
      [System.Windows.Automation.TreeScope]::Descendants,
      $Condition
    ) |
      Where-Object { -not $_.Current.IsOffscreen } |
      Select-Object -First 1
    if (-not $element) {
      return $true
    }
    Start-Sleep -Milliseconds 250
  } while ($stopwatch.Elapsed.TotalSeconds -lt 30)

  return $false
}

function Invoke-MouseClick {
  param(
    [Parameter(Mandatory)]
    $Element,

    [Parameter(Mandatory)]
    [System.Diagnostics.Process] $Process
  )

  $bounds = $Element.Current.BoundingRectangle
  $clickX = [int][math]::Round(
    $bounds.Left + ($bounds.Width / 2)
  )
  $clickY = [int][math]::Round(
    $bounds.Top + ($bounds.Height / 2)
  )
  [CdpSwitcherSmokeInput]::SetForegroundWindow(
    $Process.MainWindowHandle
  ) | Out-Null
  [CdpSwitcherSmokeInput]::SetCursorPos(
    $clickX,
    $clickY
  ) | Out-Null
  Start-Sleep -Milliseconds 100
  [CdpSwitcherSmokeInput]::mouse_event(
    0x0002,
    0,
    0,
    0,
    [UIntPtr]::Zero
  )
  [CdpSwitcherSmokeInput]::mouse_event(
    0x0004,
    0,
    0,
    0,
    [UIntPtr]::Zero
  )
}

function Assert-ProfileCanBeCreated {
  param(
    [Parameter(Mandatory)]
    [string] $Label,

    [Parameter(Mandatory)]
    [System.Diagnostics.Process] $Process
  )

  Add-Type -AssemblyName UIAutomationClient
  Add-Type -AssemblyName UIAutomationTypes

  $root = [System.Windows.Automation.AutomationElement]::FromHandle(
    $Process.MainWindowHandle
  )
  $addProfileCondition =
    [System.Windows.Automation.AndCondition]::new(
      [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button
      ),
      [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        'Add profile'
      )
    )
  $addButtonCondition =
    [System.Windows.Automation.AndCondition]::new(
      [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button
      ),
      [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        'Add'
      )
    )
  $addTagCondition =
    [System.Windows.Automation.AndCondition]::new(
      [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button
      ),
      [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        'Add tag'
      )
    )
  $removeTagCondition =
    [System.Windows.Automation.AndCondition]::new(
      [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button
      ),
      [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        'Remove'
      )
    )
  $editorCondition =
    [System.Windows.Automation.PropertyCondition]::new(
      [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
      [System.Windows.Automation.ControlType]::Edit
    )
  $profileName = "Smoke $Label profile"
  $profileRowCondition =
    [System.Windows.Automation.PropertyCondition]::new(
      [System.Windows.Automation.AutomationElement]::NameProperty,
      $profileName
    )
  $removeProfileCondition =
    [System.Windows.Automation.AndCondition]::new(
      [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button
      ),
      [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        'Remove...'
      )
    )
  $confirmRemoveCondition =
    [System.Windows.Automation.AndCondition]::new(
      [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button
      ),
      [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        'Remove'
      )
    )
  $removedProfilesCondition =
    [System.Windows.Automation.AndCondition]::new(
      [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button
      ),
      [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        'Removed profiles (1)'
      )
    )
  $restoreCondition =
    [System.Windows.Automation.AndCondition]::new(
      [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button
      ),
      [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        'Restore'
      )
    )
  $stoppedCondition =
    [System.Windows.Automation.PropertyCondition]::new(
      [System.Windows.Automation.AutomationElement]::NameProperty,
      'Stopped'
    )
  $addProfileButton = Wait-ForVisibleElement `
    $root `
    $addProfileCondition `
    -RequireEnabled

  if (-not $addProfileButton) {
    throw "$Label did not expose an enabled Add profile button."
  }

  Invoke-MouseClick $addProfileButton $Process
  $editor = Wait-ForVisibleElement $root $editorCondition

  if (-not $editor) {
    throw "$Label did not open the Add profile dialog."
  }

  $addTagButton = Wait-ForVisibleElement `
    $root `
    $addTagCondition `
    -RequireEnabled

  if (-not $addTagButton) {
    throw "$Label did not expose the Add tag button."
  }

  Invoke-MouseClick $addTagButton $Process
  $removeTagButton = Wait-ForVisibleElement `
    $root `
    $removeTagCondition `
    -RequireEnabled

  if (-not $removeTagButton) {
    throw "$Label did not expose a tag Remove button."
  }

  Invoke-MouseClick $removeTagButton $Process
  $valuePattern = $editor.GetCurrentPattern(
    [System.Windows.Automation.ValuePattern]::Pattern
  )
  $valuePattern.SetValue($profileName)
  $addButton = Wait-ForVisibleElement `
    $root `
    $addButtonCondition `
    -RequireEnabled

  if (-not $addButton) {
    throw "$Label did not expose the dialog Add button."
  }

  Invoke-MouseClick $addButton $Process
  $profileRow = Wait-ForVisibleElement `
    $root `
    $profileRowCondition

  if (-not $profileRow) {
    throw "$Label did not add the new profile to the list."
  }

  $stopped = Wait-ForVisibleElement `
    $root `
    $stoppedCondition
  if (-not $stopped) {
    throw "$Label changed lifecycle state while adding metadata."
  }

  $removeProfileButton = Wait-ForVisibleElement `
    $root `
    $removeProfileCondition `
    -RequireEnabled
  if (-not $removeProfileButton) {
    throw "$Label did not expose a direct Remove action."
  }

  Invoke-MouseClick $removeProfileButton $Process
  $confirmRemoveButton = Wait-ForVisibleElement `
    $root `
    $confirmRemoveCondition `
    -RequireEnabled
  if (-not $confirmRemoveButton) {
    throw "$Label did not open the Remove confirmation."
  }

  Invoke-MouseClick $confirmRemoveButton $Process
  if (-not (Wait-ForElementAbsent $root $profileRowCondition)) {
    throw "$Label did not remove the profile from the main list."
  }

  $removedProfilesButton = Wait-ForVisibleElement `
    $root `
    $removedProfilesCondition `
    -RequireEnabled
  if (-not $removedProfilesButton) {
    throw "$Label did not expose the removed profile."
  }

  Invoke-MouseClick $removedProfilesButton $Process
  $removedProfileRow = Wait-ForVisibleElement `
    $root `
    $profileRowCondition
  if (-not $removedProfileRow) {
    throw "$Label did not list the removed profile."
  }

  Invoke-MouseClick $removedProfileRow $Process
  $restoreButton = Wait-ForVisibleElement `
    $root `
    $restoreCondition `
    -RequireEnabled
  if (-not $restoreButton) {
    throw "$Label did not enable Restore for the selected profile."
  }

  Invoke-MouseClick $restoreButton $Process
  $restoredProfileRow = Wait-ForVisibleElement `
    $root `
    $profileRowCondition
  if (-not $restoredProfileRow) {
    throw "$Label did not restore the profile to the main list."
  }

  $stopped = Wait-ForVisibleElement `
    $root `
    $stoppedCondition
  if (-not $stopped) {
    throw "$Label changed lifecycle state during remove or restore."
  }

  Write-Host (
    "$Label created, removed, and restored a profile without " +
    "changing lifecycle state."
  )
}

Assert-AppStarts 'directory app' 'artifacts/CdpSwitcher-win-x64/CdpSwitcher.exe'
Assert-AppStarts 'single-file app' 'artifacts/CdpSwitcher-single-file/CdpSwitcher.exe'
