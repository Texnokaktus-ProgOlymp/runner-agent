param([string]$Image = 'runner-agent-cpp-23:local', [string]$BaseImage = 'runner-agent-base:local')

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $repoRoot
try {
    dotnet build Texnokaktus.ProgOlymp.RunnerAgent.slnx -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    dotnet publish src/Texnokaktus.ProgOlymp.RunnerAgent -c Release --no-restore -o artifacts/host
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    docker build -f docker/base/Dockerfile -t $BaseImage .
    if ($LASTEXITCODE -ne 0) { throw 'Base image build failed' }
    docker build -f docker/cpp23/Dockerfile --build-arg "BASE_IMAGE=$BaseImage" -t $Image .
    if ($LASTEXITCODE -ne 0) { throw 'Compiler image build failed' }
    docker run --rm --entrypoint sh $BaseImage -c 'test ! -e /usr/bin/g++ && test ! -e /app/appsettings.Production.json && test -x /usr/bin/isolate'
    if ($LASTEXITCODE -ne 0) { throw 'Base image must contain isolate and agent without a compiler profile' }
    $results = Join-Path $repoRoot 'artifacts/sandbox-results'
    New-Item -ItemType Directory -Force $results | Out-Null
    $command = @'
ulimit -c 0
sed 's/\r$//' /repo/tests/prototype/environment.sh > /tmp/runner-agent-environment.sh
. /tmp/runner-agent-environment.sh
sed 's/\r$//' /repo/scripts/smoke.sh > /tmp/runner-agent-smoke.sh
sh /tmp/runner-agent-smoke.sh /app/Texnokaktus.ProgOlymp.RunnerAgent.dll
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
