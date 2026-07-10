[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet("check", "update", "fix-vulnerable")]
    [string] $Command = "check",

    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot "AgentWindows.slnx"

function Write-Section {
    param([Parameter(Mandatory)][string] $Title)

    Write-Host ""
    Write-Host "== $Title =="
}

function Invoke-External {
    param(
        [Parameter(Mandatory)][string] $FilePath,
        [Parameter()][string[]] $ArgumentList = @()
    )

    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($ArgumentList -join ' ')"
    }
}

function Assert-CleanWorktree {
    if ($Force) {
        Write-Warning "Bypassing the clean-worktree check because -Force was supplied."
        return
    }

    $status = @(& git -C $repoRoot status --porcelain=v1 --untracked-files=normal)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect the Git worktree."
    }

    if ($status.Count -ne 0) {
        $details = $status -join [Environment]::NewLine
        throw @"
Dependency updates require a clean Git worktree so their changes are easy to review.

$details

Commit or stash these changes, or rerun with -Force to bypass this safeguard.
"@
    }
}

function Test-NuGetSecurity {
    Write-Section "NuGet vulnerability audit"
    Invoke-External dotnet @("restore", $solution, "--force-evaluate")
    Invoke-External dotnet @(
        "package", "list",
        "--project", $solution,
        "--vulnerable",
        "--include-transitive",
        "--no-restore"
    )
}

function Show-NuGetFreshness {
    Write-Section "Outdated direct NuGet packages"
    Invoke-External dotnet @(
        "package", "list",
        "--project", $solution,
        "--outdated",
        "--no-restore"
    )
}

function Show-DotNetToolFreshness {
    Write-Section "Local .NET tools"

    $toolJson = & dotnet tool list --local --format json
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to list local .NET tools."
    }

    $tools = ($toolJson | ConvertFrom-Json).data
    $rows = foreach ($tool in $tools) {
        $packageId = [string] $tool.packageId
        $currentVersion = [string] $tool.version
        $normalizedId = $packageId.ToLowerInvariant()
        $versionIndex = Invoke-RestMethod "https://api.nuget.org/v3-flatcontainer/$normalizedId/index.json"
        $latestVersion = @($versionIndex.versions | Where-Object { $_ -notmatch "-" })[-1]

        [pscustomobject]@{
            Package = $packageId
            Current = $currentVersion
            Latest = $latestVersion
            Status = if ($currentVersion -eq $latestVersion) { "current" } else { "update available" }
        }
    }

    $rows | Format-Table Package, Current, Latest, Status -AutoSize | Out-Host
}

function Show-EnvironmentFreshness {
    Write-Section ".NET SDK and runtimes"
    Invoke-External dotnet @("sdk", "check")

    Write-Section "Project-local mise tools"
    $miseOutput = @(& mise outdated --local --bump)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to check project-local mise tools."
    }

    if ($miseOutput.Count -eq 0) {
        Write-Host "All project-local mise tools are up to date."
    }
    else {
        $miseOutput | ForEach-Object { Write-Host $_ }
    }
}

function Test-UpdatedDependencies {
    Write-Section "Restore local .NET tools"
    Invoke-External dotnet @("tool", "restore")

    Write-Section "NuGet vulnerability audit"
    Invoke-External dotnet @("restore", $solution, "--force-evaluate")

    Write-Section "Formatting"
    Invoke-External dotnet @("csharpier", "check", ".")

    Write-Section "Build"
    Invoke-External dotnet @("build", $solution, "--no-restore")

    Write-Section "Unit and architecture tests"
    Invoke-External dotnet @("test", "tests/AgentWindows.Core.Tests", "--no-build")
    Invoke-External dotnet @("test", "tests/AgentWindows.Cli.Tests", "--no-build")
    Invoke-External dotnet @("test", "tests/AgentWindows.Architecture.Tests", "--no-build")
}

function Invoke-Check {
    Test-NuGetSecurity
    Show-NuGetFreshness
    Show-DotNetToolFreshness
    Show-EnvironmentFreshness
}

function Invoke-Update {
    Assert-CleanWorktree

    Write-Section "Update direct NuGet packages"
    Invoke-External dotnet @("package", "update", "--project", $solution)

    Write-Section "Update local .NET tools"
    Invoke-External dotnet @("tool", "update", "--local", "--all")

    Test-UpdatedDependencies
    Show-EnvironmentFreshness
}

function Invoke-VulnerabilityFix {
    Assert-CleanWorktree

    Write-Section "Update vulnerable NuGet packages"
    Invoke-External dotnet @("package", "update", "--project", $solution, "--vulnerable")

    Test-UpdatedDependencies
    Show-EnvironmentFreshness
}

Push-Location $repoRoot
try {
    switch ($Command) {
        "check" { Invoke-Check }
        "update" { Invoke-Update }
        "fix-vulnerable" { Invoke-VulnerabilityFix }
    }
}
finally {
    Pop-Location
}
