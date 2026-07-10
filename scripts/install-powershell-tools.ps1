[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$toolVersions = Import-PowerShellDataFile (Join-Path $repoRoot "powershell-tools.psd1")
$requiredVersion = [string] $toolVersions.PSScriptAnalyzer
$moduleRoot = Join-Path $repoRoot ".tools/powershell"
$moduleManifest = Join-Path `
    $moduleRoot `
    "PSScriptAnalyzer/$requiredVersion/PSScriptAnalyzer.psd1"

if (Test-Path -LiteralPath $moduleManifest) {
    Write-Output "PSScriptAnalyzer $requiredVersion is already available under .tools/powershell."
    return
}

New-Item -ItemType Directory -Path $moduleRoot -Force | Out-Null
Save-PSResource `
    -Name PSScriptAnalyzer `
    -Version $requiredVersion `
    -Repository PSGallery `
    -Path $moduleRoot `
    -TrustRepository `
    -AcceptLicense `
    -Quiet

if (-not (Test-Path -LiteralPath $moduleManifest)) {
    throw "PSScriptAnalyzer $requiredVersion was not saved to $moduleManifest."
}

Write-Output "Saved PSScriptAnalyzer $requiredVersion under .tools/powershell."
