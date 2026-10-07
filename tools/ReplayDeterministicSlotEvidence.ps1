param(
    [string]$AuditRoot = 'artifacts/untapped-audit',
    [string]$OutputDirectory = 'artifacts/phase9e2a1-replay'
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Push-Location $taskRoot
try {
    $taskApp = 'src/DraftTG.App/bin/Debug/net10.0-windows10.0.19041.0/DraftTG.App.dll'
    if (!(Test-Path -LiteralPath $taskApp)) { throw 'Build DraftTG.sln first.' }
    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    $taskMetadata = Get-Content -LiteralPath 'tests/Fixtures/phase9e2a1-replay-metadata.json' -Raw | ConvertFrom-Json
    $taskPairs = Join-Path $OutputDirectory 'fixture-pairs.json'
    ConvertTo-Json -InputObject @($taskMetadata.PriorEvidenceForAuditedCaptures) -Depth 6 | Set-Content -LiteralPath $taskPairs -Encoding utf8
    function Invoke-Replay([string[]]$TaskArguments) {
        & dotnet $taskApp --deterministic-shadow-live @TaskArguments
        if ($LASTEXITCODE -ne 0) { throw "Replay failed with exit code $LASTEXITCODE" }
    }
    Invoke-Replay @('--fixture', 'tests/Fixtures/arena-artwork-live', '--output', (Join-Path $OutputDirectory 'p1p6'))
    Invoke-Replay @('--fixture', 'tests/Fixtures/arena-wgc-p1p7', '--evidence-pairs', $taskPairs, '--output', (Join-Path $OutputDirectory 'p1p7'))
    $taskP1 = '86686,86733,86900,86986,86785,86849,86760,86702,86751,86744,86878,86857,86993,87040'
    $taskP2 = '86725,86786,86710,86889,86978,86845,86721,86702,86700,86985,86897,86984,87058'
    $taskJobs = @(
        @{Name='p1p1-1723';Image='A-baseline/arena-window-1.png';Origin='3922,183';Client='3930,214,1723,1009';Pack='1,1';Ids=$taskP1},
        @{Name='p1p2-1723';Image='G-geometry/e01-arenaonly-b-1723x1009.png';Origin='3820,213';Client='3828,244,1723,1009';Pack='1,2';Ids=$taskP2},
        @{Name='p1p2-1280';Image='G-geometry/e02-arenaonly-b-1280x720.png';Origin='4116,346';Client='4124,377,1280,720';Pack='1,2';Ids=$taskP2},
        @{Name='p1p2-1920';Image='G-geometry/validation/e03-arenaonly-a-1920x1080.png';Origin='3764,184';Client='3772,215,1920,1080';Pack='1,2';Ids=$taskP2}
    )
    foreach ($taskJob in $taskJobs) {
        $taskImage = Join-Path $AuditRoot $taskJob.Image
        if (!(Test-Path -LiteralPath $taskImage)) { throw "Ignored Arena-only fixture missing: $taskImage" }
        Invoke-Replay @('--image', $taskImage, '--capture-origin', $taskJob.Origin, '--client', $taskJob.Client,
            '--pack', $taskJob.Pack, '--grpids', $taskJob.Ids, '--evidence-pairs', $taskPairs, '--output', (Join-Path $OutputDirectory $taskJob.Name))
    }
} finally { Pop-Location }
