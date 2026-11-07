param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$network = 'runner-e2e-' + [Guid]::NewGuid().ToString('N')
$minio = $null
$agent = $null
$oldAgent = $env:RUNNER_TEST_AGENT_ENDPOINT
$oldMinio = $env:RUNNER_TEST_MINIO_ENDPOINT
$oldContainer = $env:RUNNER_TEST_AGENT_CONTAINER
$oldMinioContainer = $env:RUNNER_TEST_MINIO_CONTAINER
try {
    if (-not $SkipBuild) {
        dotnet build Texnokaktus.ProgOlymp.RunnerAgent.slnx -c Release --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
        dotnet publish src/Texnokaktus.ProgOlymp.RunnerAgent -c Release --no-restore -o artifacts/host
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
        docker build -f docker/base/Dockerfile -t runner-agent-base:local .
        if ($LASTEXITCODE -ne 0) { throw 'Base build failed' }
        docker build -f docker/cpp23/Dockerfile -t runner-agent-cpp-23:local .
        if ($LASTEXITCODE -ne 0) { throw 'Compiler build failed' }
    }
    docker network create $network | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Test network failed' }
    $minio = (docker run -d --network $network --network-alias minio -p '127.0.0.1::9000' `
        -e MINIO_ROOT_USER=runner-test-access -e MINIO_ROOT_PASSWORD=runner-test-secret `
        minio/minio:RELEASE.2025-04-22T22-12-26Z server /data --address ':9000').Trim()
    if ($LASTEXITCODE -ne 0) { throw 'MinIO start failed' }
    $env:RUNNER_TEST_MINIO_ENDPOINT = 'http://' + (docker port $minio 9000/tcp).Trim()
    $ready = $false
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    while ([DateTime]::UtcNow -lt $deadline) {
        try { $null = Invoke-WebRequest "$env:RUNNER_TEST_MINIO_ENDPOINT/minio/health/ready" -UseBasicParsing -TimeoutSec 2; $ready = $true; break }
        catch { Start-Sleep -Milliseconds 200 }
    }
    if (-not $ready) { throw 'MinIO not ready' }
    $agent = (docker run -d --privileged --cgroupns=private --network $network -p '127.0.0.1::8080' `
        -e SERVICE_NAME=runner-agent-cpp-23 -e AWS_ACCESS_KEY_ID=runner-test-access -e AWS_SECRET_ACCESS_KEY=runner-test-secret `
        runner-agent-cpp-23:local).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Agent start failed' }
    $env:RUNNER_TEST_AGENT_ENDPOINT = 'http://' + (docker port $agent 8080/tcp).Trim()
    $env:RUNNER_TEST_AGENT_CONTAINER = $agent
    $env:RUNNER_TEST_MINIO_CONTAINER = $minio
    dotnet test --project tests/Texnokaktus.ProgOlymp.RunnerAgent.IntegrationTests -c Release --no-build `
        --filter 'TestCategory=EndToEnd' --report-trx --results-directory artifacts/e2e-tests
    if ($LASTEXITCODE -ne 0) { throw 'End-to-end checks failed' }
} finally {
    if ($agent) { docker logs $agent 2>&1 | Out-File -Encoding utf8 artifacts/e2e-agent.log; docker rm -f $agent | Out-Null }
    if ($minio) { docker rm -f $minio | Out-Null }
    docker network rm $network | Out-Null
    $env:RUNNER_TEST_AGENT_ENDPOINT = $oldAgent
    $env:RUNNER_TEST_MINIO_ENDPOINT = $oldMinio
    $env:RUNNER_TEST_AGENT_CONTAINER = $oldContainer
    $env:RUNNER_TEST_MINIO_CONTAINER = $oldMinioContainer
}
