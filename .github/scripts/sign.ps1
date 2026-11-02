# 指定したファイルに Authenticode 署名する (release ワークフロー用)
# 必要な環境変数: CERTIFICATE_PFX (Base64), CERTIFICATE_PASSWORD, SIGNTOOL (signtool.exe のパス)
param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $Path)) { throw "Sign target not found: $Path" }
if (-not $env:SIGNTOOL) { throw "SIGNTOOL is not set." }

$certPath = Join-Path $env:RUNNER_TEMP "$([System.Guid]::NewGuid()).pfx"
try {
    [System.IO.File]::WriteAllBytes($certPath, [System.Convert]::FromBase64String($env:CERTIFICATE_PFX))

    & $env:SIGNTOOL sign /f $certPath /p $env:CERTIFICATE_PASSWORD `
        /tr http://timestamp.digicert.com /td sha256 /fd sha256 $Path
    if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit code $LASTEXITCODE" }

    Write-Host "Signed: $Path"
} finally {
    Remove-Item $certPath -Force -ErrorAction SilentlyContinue
}
