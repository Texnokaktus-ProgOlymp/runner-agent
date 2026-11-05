param([string]$Image = 'runner-agent-prototype:local')

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $repoRoot
try {
    dotnet build Texnokaktus.ProgOlymp.RunnerAgent.slnx -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    dotnet publish src/Texnokaktus.ProgOlymp.RunnerAgent -c Release --no-restore -o artifacts/host
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    docker build -f tests/prototype/Dockerfile -t $Image artifacts/host
    if ($LASTEXITCODE -ne 0) { throw 'Prototype image build failed' }
    $results = Join-Path $repoRoot 'artifacts/sandbox-results'
    New-Item -ItemType Directory -Force $results | Out-Null
    $command = @'
ulimit -c 0
sed 's/\r$//' /repo/tests/prototype/environment.sh > /tmp/runner-agent-environment.sh
. /tmp/runner-agent-environment.sh
cd /repo
dotnet /repo/tests/Texnokaktus.ProgOlymp.RunnerAgent.IntegrationTests/bin/Release/net10.0/Texnokaktus.ProgOlymp.RunnerAgent.IntegrationTests.dll --filter TestCategory=Sandbox --report-trx --results-directory /results
'@
    $command = $command.Replace("`r`n", "`n")
    docker run --rm --privileged --cgroupns=private --env DOTNET_CLI_TELEMETRY_OPTOUT=1 `
        --mount "type=bind,source=$repoRoot,target=/repo,readonly" `
        --mount "type=bind,source=$results,target=/results" `
        --entrypoint sh $Image -c $command
    if ($LASTEXITCODE -ne 0) { throw 'Sandbox tests failed' }
}
finally {
    Pop-Location
}
