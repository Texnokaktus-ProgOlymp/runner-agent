param([string]$Image = 'minio/minio:RELEASE.2025-04-22T22-12-26Z')
$ErrorActionPreference = 'Stop'
$previous = $env:RUNNER_TEST_MINIO_ENDPOINT
$container = $null
try {
    dotnet build Texnokaktus.ProgOlymp.RunnerAgent.slnx -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    $container = (docker run -d --rm -p '127.0.0.1::9000' `
        -e MINIO_ROOT_USER=runner-test-access -e MINIO_ROOT_PASSWORD=runner-test-secret `
        $Image server /data --address ':9000').Trim()
    if ($LASTEXITCODE -ne 0) { throw 'MinIO start failed' }
    $address = (docker port $container 9000/tcp).Trim()
    $env:RUNNER_TEST_MINIO_ENDPOINT = "http://$address"
    $ready = $false
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    while ([DateTime]::UtcNow -lt $deadline) {
        try {
            $null = Invoke-WebRequest "$env:RUNNER_TEST_MINIO_ENDPOINT/minio/health/ready" -UseBasicParsing -TimeoutSec 2
            $ready = $true
            break
        } catch { Start-Sleep -Milliseconds 200 }
    }
    if (-not $ready) { throw 'MinIO did not become ready' }
    dotnet test --project tests/Texnokaktus.ProgOlymp.RunnerAgent.IntegrationTests -c Release --no-build `
        --filter 'TestCategory=MinIO' --report-trx --results-directory artifacts/minio-tests
    if ($LASTEXITCODE -ne 0) { throw 'MinIO checks failed' }
} finally {
    $env:RUNNER_TEST_MINIO_ENDPOINT = $previous
    if ($container) { docker rm -f $container | Out-Null }
}
