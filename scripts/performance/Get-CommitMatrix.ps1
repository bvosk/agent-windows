param(
    [Parameter(Mandatory = $true)][string]$EventName,
    [string]$Before,
    [string]$After,
    [string]$BaseSha,
    [string]$HeadSha,
    [string]$ExplicitShas,
    [Parameter(Mandatory = $true)][string]$Context,
    [string]$DataRoot,
    [int]$Maximum = 256
)

$ErrorActionPreference = 'Stop'

if ($ExplicitShas) {
    $shas = $ExplicitShas -split '[,\s]+' | Where-Object { $_ }
} elseif ($EventName -eq 'pull_request') {
    $shas = @(git rev-list --reverse --topo-order "$BaseSha..$HeadSha")
} elseif ($Before -and $Before -notmatch '^0+$') {
    $shas = @(git rev-list --reverse --topo-order "$Before..$After")
} else {
    $shas = @($After)
}

$seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$eligible = foreach ($sha in $shas) {
    $full = if ($sha -match '^[0-9a-fA-F]{40}$') { $sha.ToLowerInvariant() } else { (git rev-parse $sha).Trim() }
    if (-not $seen.Add($full)) { continue }
    $existing = if ($DataRoot) { Join-Path $DataRoot "results/v1/$Context/$full.json" } else { $null }
    if (-not $existing -or -not (Test-Path -LiteralPath $existing)) {
        [ordered]@{ sha = $full; context = $Context }
    }
}

$selected = @($eligible | Select-Object -First $Maximum)
$overflow = @($eligible | Select-Object -Skip $Maximum)
[ordered]@{
    include = $selected
    overflow = $overflow
    total = @($eligible).Count
} | ConvertTo-Json -Depth 5 -Compress
