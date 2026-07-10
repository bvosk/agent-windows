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
$releaseProject = Join-Path $repoRoot "src/AgentWindows.Cli/AgentWindows.Cli.csproj"
$powerShellToolManifest = Join-Path $repoRoot "powershell-tools.psd1"

function Write-Section {
    param([Parameter(Mandatory)][string] $Title)

    Write-Output ""
    Write-Output "== $Title =="
}

function Invoke-External {
    param(
        [Parameter(Mandatory)][string] $FilePath,
        [Parameter()][string[]] $ArgumentList = @(),
        [Parameter()][int[]] $SuccessExitCodes = @(0)
    )

    & $FilePath @ArgumentList
    if ($LASTEXITCODE -notin $SuccessExitCodes) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($ArgumentList -join ' ')"
    }
}

function Get-PackageReportEntry {
    param([Parameter(Mandatory)] $Report)

    foreach ($project in @($Report.projects)) {
        $frameworks = $project.PSObject.Properties["frameworks"]
        if ($null -eq $frameworks) {
            continue
        }

        foreach ($framework in @($frameworks.Value)) {
            foreach ($propertyName in @("topLevelPackages", "transitivePackages")) {
                $packages = $framework.PSObject.Properties[$propertyName]
                if ($null -eq $packages) {
                    continue
                }

                foreach ($package in @($packages.Value)) {
                    $latestVersion = $package.PSObject.Properties["latestVersion"]
                    [pscustomobject]@{
                        Package = [string] $package.id
                        Current = [string] $package.resolvedVersion
                        Latest = if ($null -eq $latestVersion) {
                            $null
                        }
                        else {
                            [string] $latestVersion.Value
                        }
                    }
                }
            }
        }
    }
}

function Invoke-DotNetPackageReport {
    param([Parameter(Mandatory)][string[]] $ArgumentList)

    $output = @(& dotnet @ArgumentList --format json)
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($ArgumentList -join ' ') failed with exit code $LASTEXITCODE.`n$($output -join [Environment]::NewLine)"
    }

    ($output -join [Environment]::NewLine) | ConvertFrom-Json
}

function Assert-CleanWorktree {
    param([switch] $Force)

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
    $report = Invoke-DotNetPackageReport @(
        "package", "list",
        "--project", $solution,
        "--vulnerable",
        "--include-transitive",
        "--no-restore"
    )
    $vulnerablePackages = @(
        Get-PackageReportEntry $report |
            Sort-Object -Property Package, Current -Unique
    )
    if ($vulnerablePackages.Count -ne 0) {
        $vulnerablePackages | Format-Table Package, Current -AutoSize | Out-Host
        throw "$($vulnerablePackages.Count) vulnerable NuGet package(s) found."
    }
    Write-Output "NuGet: no known vulnerabilities."

    Write-Section "Locked win-x64 release restore"
    Restore-ReleasePackageGraph -Locked

    Write-Section "Restore default package graph"
    Invoke-External dotnet @("restore", $solution, "--locked-mode", "--verbosity", "quiet")
}

function Restore-ReleasePackageGraph {
    param(
        [switch] $ForceEvaluate,
        [switch] $Locked
    )

    $arguments = @(
        "restore",
        $releaseProject,
        "--runtime", "win-x64",
        "-p:PublishSingleFile=true",
        "-p:NuGetLockFilePath=packages.win-x64.lock.json",
        "--verbosity", "quiet"
    )
    if ($ForceEvaluate) {
        $arguments += "--force-evaluate"
    }
    if ($Locked) {
        $arguments += "--locked-mode"
    }

    Invoke-External dotnet $arguments
}

function Show-NuGetFreshness {
    Write-Section "Outdated direct NuGet packages"
    $report = Invoke-DotNetPackageReport @(
        "package", "list",
        "--project", $solution,
        "--outdated",
        "--no-restore"
    )
    $updates = @(
        Get-PackageReportEntry $report |
            Sort-Object -Property Package, Current, Latest -Unique
    )
    if ($updates.Count -eq 0) {
        Write-Output "NuGet packages: current."
        return
    }

    $updates | Format-Table Package, Current, Latest -AutoSize | Out-Host
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

    $updates = @($rows | Where-Object Status -ne "current")
    if ($updates.Count -eq 0) {
        Write-Output "Local .NET tools: $($rows.Count) current."
        return
    }

    $updates | Format-Table Package, Current, Latest, Status -AutoSize | Out-Host
}

function Get-PowerShellToolState {
    $toolVersions = Import-PowerShellDataFile $powerShellToolManifest
    $currentVersion = [string] $toolVersions.PSScriptAnalyzer
    $latestVersion = [string] (
        Find-PSResource -Name PSScriptAnalyzer -Repository PSGallery -ErrorAction Stop
    ).Version

    [pscustomobject]@{
        Package = "PSScriptAnalyzer"
        Current = $currentVersion
        Latest = $latestVersion
        Status = if ($currentVersion -eq $latestVersion) { "current" } else { "update available" }
    }
}

function Show-PowerShellToolFreshness {
    Write-Section "Project-local PowerShell tools"
    $state = Get-PowerShellToolState
    if ($state.Status -eq "current") {
        Write-Output "PSScriptAnalyzer: $($state.Current) current."
        return
    }

    $state | Format-Table Package, Current, Latest, Status -AutoSize | Out-Host
}

function Update-PowerShellTool {
    [CmdletBinding(SupportsShouldProcess)]
    param()

    $state = Get-PowerShellToolState
    if ($state.Current -eq $state.Latest) {
        Write-Output "PSScriptAnalyzer $($state.Current) is current."
        return
    }

    $content = Get-Content -Raw $powerShellToolManifest
    $pattern = '(?m)^(\s*PSScriptAnalyzer\s*=\s*")[^"]+("\s*)$'
    $updated = [regex]::Replace(
        $content,
        $pattern,
        { param($match) $match.Groups[1].Value + $state.Latest + $match.Groups[2].Value }
    )
    if ($updated -eq $content) {
        throw "Unable to update PSScriptAnalyzer in $powerShellToolManifest."
    }
    if (-not $PSCmdlet.ShouldProcess($powerShellToolManifest, "Update PSScriptAnalyzer")) {
        return
    }

    Set-Content -LiteralPath $powerShellToolManifest -Value $updated -NoNewline -Encoding utf8
    & (Join-Path $PSScriptRoot "install-powershell-tools.ps1")
}

function Show-EnvironmentFreshness {
    Write-Section ".NET SDK and runtimes"
    $sdkOutput = @(& dotnet sdk check)
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet sdk check failed with exit code $LASTEXITCODE.`n$($sdkOutput -join [Environment]::NewLine)"
    }
    $sdkAttention = @(
        $sdkOutput |
            Where-Object { $_ -match "(?i)(update available|out of support|unsupported)" }
    )
    if ($sdkAttention.Count -eq 0) {
        Write-Output ".NET SDK and runtimes: current."
    }
    else {
        $sdkAttention | ForEach-Object { Write-Output $_ }
    }

    Write-Section "Project-local mise tools"
    $miseOutput = @(& mise outdated --local --bump)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to check project-local mise tools."
    }

    if ($miseOutput.Count -eq 0) {
        Write-Output "All project-local mise tools are up to date."
    }
    else {
        $miseOutput | ForEach-Object { Write-Output $_ }
    }
}

function Invoke-NuGetPackageUpdate {
    param([switch] $Vulnerable)

    # `dotnet package update` currently previews solution-wide updates one project
    # at a time. With central package management, that can compare an updated
    # project against the still-old central version and fail with NU1109. Running
    # the same SDK command per project lets it update Directory.Packages.props as
    # each centrally managed package is encountered.
    [xml] $solutionXml = Get-Content -Raw $solution
    $projects = @($solutionXml.SelectNodes("//Project") | ForEach-Object {
        Join-Path $repoRoot $_.Path
    })

    foreach ($project in $projects) {
        $arguments = @("package", "update", "--project", $project)
        if ($Vulnerable) {
            $arguments += "--vulnerable"
        }

        # The .NET 10 command returns 2 when a project has nothing to update.
        Invoke-External dotnet $arguments -SuccessExitCodes @(0, 2)
    }
}

function Test-DependencyUpdate {
    Write-Section "Restore local .NET tools"
    Invoke-External dotnet @("tool", "restore")

    Write-Section "NuGet vulnerability audit"
    Invoke-External dotnet @("restore", $solution, "--force-evaluate")

    Write-Section "Regenerate win-x64 release locks"
    Restore-ReleasePackageGraph -ForceEvaluate
    Invoke-External dotnet @("restore", $solution, "--force-evaluate")

    Write-Section "Normalize dependency manifests"
    Invoke-External dotnet @("csharpier", "format", "Directory.Packages.props")

    Write-Section "PowerShell analysis"
    Invoke-External pwsh @("-NoProfile", "-File", "scripts/analyze-powershell.ps1")

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
    Show-PowerShellToolFreshness
    Show-EnvironmentFreshness
}

function Invoke-Update {
    param([switch] $Force)

    Assert-CleanWorktree -Force:$Force

    Write-Section "Update direct NuGet packages"
    Invoke-NuGetPackageUpdate

    Write-Section "Update local .NET tools"
    Invoke-External dotnet @("tool", "update", "--local", "--all")

    Write-Section "Update project-local PowerShell tools"
    Update-PowerShellTool

    Test-DependencyUpdate
    Show-EnvironmentFreshness
}

function Invoke-VulnerabilityFix {
    param([switch] $Force)

    Assert-CleanWorktree -Force:$Force

    Write-Section "Update vulnerable NuGet packages"
    Invoke-NuGetPackageUpdate -Vulnerable

    Test-DependencyUpdate
    Show-EnvironmentFreshness
}

Push-Location $repoRoot
try {
    switch ($Command) {
        "check" { Invoke-Check }
        "update" { Invoke-Update -Force:$Force }
        "fix-vulnerable" { Invoke-VulnerabilityFix -Force:$Force }
    }
}
finally {
    Pop-Location
}
