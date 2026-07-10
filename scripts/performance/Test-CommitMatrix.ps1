$ErrorActionPreference = 'Stop'

$matrixScript = Join-Path $PSScriptRoot 'Get-CommitMatrix.ps1'
$temporary = Join-Path ([IO.Path]::GetTempPath()) "agent-windows-perf-$([Guid]::NewGuid().ToString('N'))"

function Assert-Equal($Expected, $Actual, [string]$Message) {
    if ($Expected -ne $Actual) {
        throw "$Message Expected '$Expected', got '$Actual'."
    }
}

function Invoke-Matrix([hashtable]$Arguments) {
    return (& $matrixScript @Arguments | ConvertFrom-Json)
}

try {
    New-Item -ItemType Directory -Force $temporary | Out-Null
    git -C $temporary init --initial-branch main | Out-Null
    git -C $temporary config user.name benchmark-tests
    git -C $temporary config user.email benchmark-tests@example.invalid

    $stream = [Text.StringBuilder]::new()
    0..260 | ForEach-Object {
        $mark = $_ + 1
        $message = if ($_ -eq 0) { 'initial' } else { "commit-$_" }
        [void]$stream.AppendLine('commit refs/heads/main')
        [void]$stream.AppendLine("mark :$mark")
        [void]$stream.AppendLine("committer Benchmark Tests <benchmark-tests@example.invalid> $(1704067200 + $_) +0000")
        [void]$stream.AppendLine("data $($message.Length)")
        [void]$stream.AppendLine($message)
        if ($_ -gt 0) { [void]$stream.AppendLine("from :$($_)") }
        [void]$stream.AppendLine()
    }
    $importPath = Join-Path $temporary 'commits.fast-import'
    [IO.File]::WriteAllText(
        $importPath,
        $stream.ToString().Replace("`r`n", "`n"),
        [Text.Encoding]::ASCII
    )
    $import = Start-Process git -ArgumentList '-C', $temporary, 'fast-import', '--quiet' `
        -RedirectStandardInput $importPath -NoNewWindow -Wait -PassThru
    if ($import.ExitCode -ne 0) { throw 'git fast-import failed.' }

    $initial = (git -C $temporary rev-parse 'HEAD~260').Trim()
    $head = (git -C $temporary rev-parse HEAD).Trim()

    Push-Location $temporary
    $single = Invoke-Matrix @{
        EventName = 'push'; Before = 'HEAD~1'; After = 'HEAD'; Context = 'main'
    }
    Assert-Equal 1 $single.total 'Single-commit push discovery failed.'

    $multi = Invoke-Matrix @{
        EventName = 'push'; Before = 'HEAD~3'; After = 'HEAD'; Context = 'main'
    }
    Assert-Equal 3 $multi.total 'Multi-commit push discovery failed.'

    $large = Invoke-Matrix @{
        EventName = 'push'; Before = $initial; After = $head; Context = 'main'
    }
    Assert-Equal 260 $large.total 'Large push discovery failed.'
    Assert-Equal 256 @($large.include).Count 'The first matrix must stop at 256 jobs.'
    Assert-Equal 4 @($large.overflow).Count 'Overflow commits were not preserved.'

    $deduplicated = Invoke-Matrix @{
        EventName = 'workflow_dispatch'; ExplicitShas = "$head,$head"; Context = 'main'
    }
    Assert-Equal 1 $deduplicated.total 'Overlapping event SHAs were not deduplicated.'

    $dataRoot = Join-Path $temporary 'perf-data'
    $resultPath = Join-Path $dataRoot "results/v1/main/$head.json"
    New-Item -ItemType Directory -Force (Split-Path $resultPath) | Out-Null
    '{}' | Set-Content $resultPath
    $existing = Invoke-Matrix @{
        EventName = 'workflow_dispatch'; ExplicitShas = $head; Context = 'main'; DataRoot = $dataRoot
    }
    Assert-Equal 0 $existing.total 'An existing immutable result was scheduled again.'

    $newBranch = Invoke-Matrix @{
        EventName = 'push'; Before = ('0' * 40); After = $head; Context = 'pr-1'
    }
    Assert-Equal 1 $newBranch.total 'New-branch discovery failed.'

    git checkout -b force-fixture HEAD~1 | Out-Null
    git commit --allow-empty -m abandoned | Out-Null
    $abandoned = (git rev-parse HEAD).Trim()
    git reset --hard HEAD~1 | Out-Null
    git commit --allow-empty -m replacement | Out-Null
    $replacement = (git rev-parse HEAD).Trim()
    $forcePush = Invoke-Matrix @{
        EventName = 'push'; Before = $abandoned; After = $replacement; Context = 'pr-2'
    }
    Assert-Equal 1 $forcePush.total 'Force-push range discovery failed.'

    git checkout main | Out-Null
    git checkout -b merge-fixture HEAD~2 | Out-Null
    git commit --allow-empty -m branch-change | Out-Null
    git checkout main | Out-Null
    $mergeBase = (git rev-parse HEAD).Trim()
    git commit --allow-empty -m main-change | Out-Null
    git merge --no-ff merge-fixture -m merge-fixture | Out-Null
    $merge = Invoke-Matrix @{
        EventName = 'push'; Before = $mergeBase; After = 'HEAD'; Context = 'main'
    }
    Assert-Equal 3 $merge.total 'Merge push did not include both parents and the merge commit.'
    Pop-Location

    Write-Output 'Commit matrix operational tests passed.'
}
finally {
    if ((Get-Location).Path -eq $temporary) { Pop-Location }
    Remove-Item -LiteralPath $temporary -Recurse -Force -ErrorAction SilentlyContinue
}
