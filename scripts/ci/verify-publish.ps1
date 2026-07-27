$manifestTool = Get-ChildItem `
  "$env:USERPROFILE/.nuget/packages/microsoft.windows.sdk.buildtools" `
  -Recurse `
  -Filter mt.exe |
  Where-Object { $_.FullName -match '\\x64\\mt\.exe$' } |
  Select-Object -First 1
if (-not $manifestTool) {
  throw 'The x64 manifest tool was not found.'
}

function Read-EmbeddedManifest {
  param(
    [Parameter(Mandatory)]
    [string] $Executable,

    [Parameter(Mandatory)]
    [string] $Output
  )

  & $manifestTool.FullName `
    -nologo `
    "-inputresource:$Executable;#1" `
    "-out:$Output"
  if ($LASTEXITCODE -ne 0) {
    throw "Could not read the embedded manifest from $Executable."
  }
  Get-Content -Raw $Output
}

$folderManifest = Read-EmbeddedManifest `
  'artifacts/CdpSwitcher-win-x64/CdpSwitcher.exe' `
  "$env:RUNNER_TEMP/CdpSwitcher-folder.manifest"
$singleFileManifest = Read-EmbeddedManifest `
  'artifacts/CdpSwitcher-single-file/CdpSwitcher.exe' `
  "$env:RUNNER_TEMP/CdpSwitcher-single-file.manifest"
$baseDirectoryMarker = '%MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY%'

if ($folderManifest.Contains($baseDirectoryMarker)) {
  throw 'The directory app contains stale single-file DLL redirection.'
}
if (-not $singleFileManifest.Contains($baseDirectoryMarker)) {
  throw 'The single-file app is missing extracted DLL redirection.'
}
foreach ($deploymentManifest in @(
  $folderManifest,
  $singleFileManifest
)) {
  $dpiPattern =
    '<dpiAwareness(?:\s[^>]*)?>PerMonitorV2</dpiAwareness>'
  if ($deploymentManifest -notmatch $dpiPattern) {
    throw 'The app is missing Per-Monitor V2 DPI awareness.'
  }
}
