[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$toolVersions = Import-PowerShellDataFile (Join-Path $repoRoot "powershell-tools.psd1")
$requiredVersion = [string] $toolVersions.PSScriptAnalyzer
$settingsPath = Join-Path $repoRoot "PSScriptAnalyzerSettings.psd1"
$moduleManifest = Join-Path `
    $repoRoot `
    ".tools/powershell/PSScriptAnalyzer/$requiredVersion/PSScriptAnalyzer.psd1"

try {
    Import-Module $moduleManifest -Force -ErrorAction Stop
}
catch {
    throw "PSScriptAnalyzer $requiredVersion is required. Run 'mise run setup'."
}

$findings = @(
    Invoke-ScriptAnalyzer `
        -Path $PSScriptRoot `
        -Recurse `
        -Settings $settingsPath
)

if ($findings.Count -eq 0) {
    Write-Output "PSScriptAnalyzer: no warnings or errors."
    return
}

$findings = @($findings | Sort-Object ScriptPath, Line, Column, RuleName)
foreach ($finding in $findings) {
    $relativePath = [IO.Path]::GetRelativePath($repoRoot, $finding.ScriptPath).Replace("\", "/")
    $message = $finding.Message -replace "\s+", " "
    [Console]::Error.WriteLine(
        "{0}:{1}:{2}: {3} {4}: {5}",
        $relativePath,
        $finding.Line,
        $finding.Column,
        $finding.Severity,
        $finding.RuleName,
        $message
    )
}

throw "PSScriptAnalyzer reported $($findings.Count) warning(s) or error(s)."
