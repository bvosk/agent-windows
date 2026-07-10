param(
    [Parameter(Mandatory = $true)][string]$DataRoot,
    [Parameter(Mandatory = $true)][string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
$resultsRoot = Join-Path $DataRoot 'results/v1'
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

function Get-PerformanceHistory([string]$Context) {
    $path = Join-Path $resultsRoot $Context
    if (-not (Test-Path -LiteralPath $path)) { return @() }
    return @(Get-ChildItem -LiteralPath $path -Filter '*.json' | ForEach-Object {
        try { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json -Depth 20 } catch { $null }
    } | Where-Object { $_ } | Sort-Object timestamp)
}

function Write-Context([string]$Context, [string]$Destination) {
    $history = @(Get-PerformanceHistory $Context)
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    $runsDirectory = Join-Path $Destination 'runs'
    New-Item -ItemType Directory -Force -Path $runsDirectory | Out-Null
    $sourceDirectory = Join-Path $resultsRoot $Context
    if (Test-Path -LiteralPath $sourceDirectory) {
        Copy-Item -Path (Join-Path $sourceDirectory '*') -Destination $runsDirectory -Force
    }
    $history | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $Destination 'history.json')
    $title = if ($Context -eq 'main') { 'agent-windows performance' } else { "agent-windows $Context performance" }
    $html = @"
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width">
<title>$title</title><script src="https://cdn.jsdelivr.net/npm/chart.js@4.4.7"></script>
<style>body{font:15px Segoe UI,Arial;background:#0d1117;color:#e6edf3;margin:24px}main{max-width:1200px;margin:auto}canvas{background:#161b22;border-radius:12px;padding:16px}a{color:#58a6ff}</style></head>
<body><main><h1>$title</h1><p>Same-runner score; baseline = 100 and lower is better.</p><canvas id="chart"></canvas>
<script>fetch('./history.json').then(r=>r.json()).then(runs=>{const cats=['Overall','Actions','Discovery','Snapshots','Client/Transport'];const colors=['#f2cc60','#3fb950','#58a6ff','#d2a8ff','#f0883e'];const labels=runs.map(r=>r.candidateSha.substring(0,8));const geometric=a=>a.length?Math.exp(a.reduce((s,v)=>s+Math.log(v),0)/a.length):null;const values=(r,c)=>{const b=r.benchmarks||[];return geometric(b.filter(x=>x.score&&(c==='Overall'||x.category===c)).map(x=>x.score))};const datasets=cats.map((c,i)=>({label:c,data:runs.map(r=>values(r,c)),borderColor:colors[i],tension:.2,spanGaps:false}));new Chart(document.getElementById('chart'),{type:'line',data:{labels,datasets},options:{plugins:{legend:{labels:{color:'#e6edf3'}}},scales:{x:{ticks:{color:'#8b949e'}},y:{ticks:{color:'#8b949e'},title:{display:true,text:'Score',color:'#8b949e'}}}}})});</script></main></body></html>
"@
    Set-Content -LiteralPath (Join-Path $Destination 'index.html') -Value $html
    return $history
}

$mainHistory = @(Write-Context 'main' $OutputRoot)
$contexts = if (Test-Path -LiteralPath $resultsRoot) {
    @(Get-ChildItem -LiteralPath $resultsRoot -Directory | Where-Object Name -ne 'main')
} else { @() }
foreach ($context in $contexts) {
    Write-Context $context.Name (Join-Path $OutputRoot $context.Name) | Out-Null
}

$latest = $mainHistory | Select-Object -Last 1
$sha = if ($latest) { $latest.candidateSha.Substring(0, [Math]::Min(8, $latest.candidateSha.Length)) } else { 'awaiting data' }
$scores = if ($latest) { @($latest.benchmarks | Where-Object score | ForEach-Object { [double]$_.score }) } else { @() }
$overall = if ($scores.Count) { [Math]::Exp(($scores | ForEach-Object { [Math]::Log($_) } | Measure-Object -Average).Average) } else { 100 }
$categories = if ($latest) {
    @($latest.benchmarks | Where-Object score | Group-Object category | ForEach-Object {
        $values = @($_.Group | ForEach-Object { [double]$_.score })
        [pscustomobject]@{ Name = $_.Name; Score = [Math]::Exp(($values | ForEach-Object { [Math]::Log($_) } | Measure-Object -Average).Average) }
    } | Sort-Object Name)
} else { @() }
$epoch = if ($latest -and $latest.testbed) { "$($latest.testbed.runnerImage) $($latest.testbed.runnerImageVersion)" } else { 'awaiting runner data' }
$categoryText = if ($categories.Count) { ($categories | ForEach-Object { "$($_.Name) $($_.Score.ToString('0.0'))" }) -join ' | ' } else { 'No category data yet' }
$improvement = 100 - $overall
$svg = @"
<svg xmlns="http://www.w3.org/2000/svg" width="900" height="170" viewBox="0 0 900 170"><style>text{font-family:Segoe UI,Arial,sans-serif;fill:#e6edf3}.t{font-size:24px;font-weight:700}.s{font-size:15px;fill:#8b949e}.c{font-size:14px}</style><rect width="900" height="170" rx="14" fill="#0d1117"/><text x="28" y="40" class="t">agent-windows performance | $($overall.ToString('0.0'))</text><text x="28" y="68" class="s">Baseline = 100 | lower is better | latest $sha | improvement $($improvement.ToString('+0.0;-0.0;0.0'))%</text><text x="28" y="96" class="c">$categoryText</text><text x="28" y="122" class="s">Runner epoch: $epoch</text><rect x="28" y="140" width="844" height="10" rx="5" fill="#21262d"/><rect x="28" y="140" width="$([Math]::Min(844, $overall * 8.44))" height="10" rx="5" fill="#3fb950"/></svg>
"@
Set-Content -LiteralPath (Join-Path $OutputRoot 'summary.svg') -Value $svg
