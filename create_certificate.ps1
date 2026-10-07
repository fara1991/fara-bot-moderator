# 自己署名証明書を作成し、GitHub Secrets登録用のbase64文字列を出力するスクリプト
# 使い方: PowerShellで一度だけ実行してください

param(
    [string]$SubjectName = "FaraBotModerator",
    [string]$OutputPath  = ".\cert.pfx",
    [string]$Password    = "changeme"
)

Write-Host "Creating self-signed certificate..." -ForegroundColor Cyan

$cert = New-SelfSignedCertificate `
    -Subject "CN=$SubjectName" `
    -Type CodeSigning `
    -KeyUsage DigitalSignature `
    -FriendlyName $SubjectName `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -NotAfter (Get-Date).AddYears(5)

$securePassword = ConvertTo-SecureString -String $Password -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $OutputPath -Password $securePassword | Out-Null

$base64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Resolve-Path $OutputPath)))

Write-Host ""
Write-Host "=== GitHub Secrets に以下を登録してください ===" -ForegroundColor Green
Write-Host ""
Write-Host "Secret名: CERTIFICATE_PFX" -ForegroundColor Yellow
Write-Host "値:"
Write-Host $base64
Write-Host ""
Write-Host "Secret名: CERTIFICATE_PASSWORD" -ForegroundColor Yellow
Write-Host "値: $Password"
Write-Host ""
Write-Host "登録先: https://github.com/<owner>/<repo>/settings/secrets/actions" -ForegroundColor Cyan
Write-Host ""
Write-Host "証明書ファイル: $OutputPath (このファイルは安全な場所に保管してください)" -ForegroundColor Gray
