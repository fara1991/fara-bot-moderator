# 自己署名証明書を作成し、GitHub Secrets登録用のbase64文字列を出力するスクリプト
# 使い方: PowerShellで一度だけ実行してください。パスワードは自分で決めて、実行時に入力します（SecureString。画面には表示されません）
#   例: ./create_certificate.ps1            （-Password を省くと入力を求められます）
#   例: $pw = Read-Host -AsSecureString "PFX のパスワード"; ./create_certificate.ps1 -Password $pw
# 出力される .pfx には秘密鍵が含まれます。リポジトリにコミットしないでください（.gitignore で *.pfx を除外済み）。

param(
    [string]$SubjectName = "FaraBotModerator",
    [string]$OutputPath  = ".\cert.pfx",
    [Parameter(Mandatory = $true)]
    [securestring]$Password
)

if ($Password.Length -eq 0) {
    throw "パスワードが空です。1 文字以上のパスワードを指定してください。"
}

Write-Host "Creating self-signed certificate..." -ForegroundColor Cyan

$cert = New-SelfSignedCertificate `
    -Subject "CN=$SubjectName" `
    -Type CodeSigning `
    -KeyUsage DigitalSignature `
    -FriendlyName $SubjectName `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -NotAfter (Get-Date).AddYears(5)

Export-PfxCertificate -Cert $cert -FilePath $OutputPath -Password $Password | Out-Null

$base64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Resolve-Path $OutputPath)))

Write-Host ""
Write-Host "=== GitHub Secrets に以下を登録してください ===" -ForegroundColor Green
Write-Host ""
Write-Host "Secret名: CERTIFICATE_PFX" -ForegroundColor Yellow
Write-Host "値:"
Write-Host $base64
Write-Host ""
Write-Host "Secret名: CERTIFICATE_PASSWORD" -ForegroundColor Yellow
Write-Host "値: 手順で入力した PFX のパスワード（画面には表示しません。控えは自分で保管してください）"
Write-Host ""
Write-Host "登録先: https://github.com/<owner>/<repo>/settings/secrets/actions" -ForegroundColor Cyan
Write-Host ""
Write-Host "証明書ファイル: $OutputPath (このファイルは安全な場所に保管してください)" -ForegroundColor Gray
