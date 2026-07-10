[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [switch] $SkipNormalRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $repoRoot "artifacts"
$publish = Join-Path $artifacts "publish"
$toolPayload = Join-Path $artifacts "tool-payload"
$package = Join-Path $artifacts "package"
$toolInstall = Join-Path $artifacts "tool-smoke"

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]] $ArgumentList)

    & dotnet @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($ArgumentList -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Invoke-AgentCommand {
    param(
        [Parameter(Mandatory)][string] $FilePath,
        [Parameter(Mandatory)][string[]] $ArgumentList,
        [int] $TimeoutSeconds = 30,
        [switch] $CaptureFirstLine
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new($FilePath)
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $CaptureFirstLine
    foreach ($argument in $ArgumentList) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    try {
        $process.StartInfo = $startInfo
        if (-not $process.Start()) {
            throw "Unable to start $FilePath."
        }

        $outputTask = if ($CaptureFirstLine) {
            $process.StandardOutput.ReadLineAsync()
        }
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "$FilePath $($ArgumentList -join ' ') timed out after $TimeoutSeconds seconds."
        }

        if ($process.ExitCode -ne 0) {
            throw "$FilePath $($ArgumentList -join ' ') failed with exit code $($process.ExitCode)."
        }
        if ($CaptureFirstLine) {
            return $outputTask.WaitAsync([TimeSpan]::FromSeconds(5)).GetAwaiter().GetResult()
        }
    }
    finally {
        $process.Dispose()
    }
}

function Invoke-AgentWindows {
    param([Parameter(Mandatory)][string] $FilePath)

    $session = "artifact-smoke-$PID-$([Guid]::NewGuid().ToString('N'))"
    try {
        Invoke-AgentCommand $FilePath @("--version")
        Invoke-AgentCommand $FilePath @("skills", "list")
        $windowList = Invoke-AgentCommand `
            $FilePath `
            @("list", "--session", $session, "--json") `
            -CaptureFirstLine
        $response = $windowList | ConvertFrom-Json
        if ($response.ok -ne $true) {
            throw "$FilePath list returned an unsuccessful response."
        }
    }
    finally {
        try {
            $null = Invoke-AgentCommand $FilePath @("daemon", "stop", "--session", $session) 10
        }
        catch {
            Write-Warning "Unable to stop artifact-smoke session ${session}: $_"
        }
    }
}

Push-Location $repoRoot
try {
    foreach ($path in @($publish, $toolPayload, $package, $toolInstall)) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force
        }
    }

    if (-not $SkipNormalRestore) {
        Invoke-DotNet @(
            "restore", "AgentWindows.slnx",
            "--locked-mode",
            "--verbosity", "quiet"
        )
    }

    Invoke-DotNet @(
        "publish",
        "src/AgentWindows.Cli",
        "--configuration", $Configuration,
        "--verbosity", "quiet",
        "--self-contained", "false",
        "-p:PublishReadyToRun=false",
        "--no-restore",
        "--output", $toolPayload
    )
    Invoke-DotNet @(
        "pack",
        "src/AgentWindows.Tool",
        "--configuration", $Configuration,
        "--verbosity", "quiet",
        "--no-restore",
        "--output", $package
    )

    $packageFiles = @(Get-ChildItem -LiteralPath $package -Filter "AgentWindows.*.nupkg")
    if ($packageFiles.Count -ne 1 -or $packageFiles[0].BaseName -notmatch "^AgentWindows\.(.+)$") {
        throw "Expected one versioned AgentWindows nupkg under $package."
    }
    $packageVersion = $Matches[1]

    Invoke-DotNet @(
        "tool", "install", "AgentWindows",
        "--tool-path", $toolInstall,
        "--source", $package,
        "--version", $packageVersion,
        "--no-cache"
    )

    Invoke-AgentWindows (Join-Path $toolInstall "agent-windows.exe")
    Invoke-DotNet @("tool", "uninstall", "AgentWindows", "--tool-path", $toolInstall)

    Invoke-DotNet @(
        "publish",
        "src/AgentWindows.Cli",
        "--configuration", $Configuration,
        "--runtime", "win-x64",
        "--verbosity", "quiet",
        "--self-contained",
        "-p:PublishSingleFile=true",
        "-p:NuGetLockFilePath=packages.win-x64.lock.json",
        "-p:RestoreLockedMode=true",
        "--output", $publish
    )
    Invoke-AgentWindows (Join-Path $publish "agent-windows.exe")

    Write-Output "Published executable and packed tool passed isolated smoke tests."
}
finally {
    Pop-Location
}
