#Requires -Version 5.1
<#
.SYNOPSIS
	NolumiaScheduler のスパースパッケージを自己署名証明書でビルド・署名・登録します。
	登録後はタスクマネージャー「詳細」タブの「パッケージ名」列に NolumiaScheduler が表示されます。

.DESCRIPTION
	アプリは WindowsPackageType=None（アンパッケージ）で動いているため、
	タスクマネージャーの「パッケージ名」列が空白になります。
	スパースパッケージ（manifest のみの軽量 .appx）を使うことで、MSIX フル移行なしに
	パッケージ ID を付与できます。

	前提条件:
	- Windows SDK の MakeAppx.exe と SignTool.exe が PATH にある、
	  またはスクリプトが自動検索します（HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots）。
	- 管理者権限は不要ですが、証明書を「信頼されたルート証明機関」へ追加する
	  ステップだけは昇格が必要です（初回のみ）。

.PARAMETER ExeDir
	NolumiaScheduler.WinUI.exe が置かれたフォルダのパス。
	省略すると最終更新の NolumiaScheduler.WinUI.exe を自動検出します。

.EXAMPLE
	# デバッグビルドを対象に登録
	.\Register-SparsePackage.ps1

.EXAMPLE
	# リリースビルドを対象に登録
	.\Register-SparsePackage.ps1 -ExeDir "bin\x64\Release\net10.0-windows10.0.22621.0"
#>
[CmdletBinding()]
param(
	[string] $ExeDir = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# 0. パス解決
# ---------------------------------------------------------------------------
$scriptDir = $PSScriptRoot

if (-not $ExeDir) {
	$candidates = Get-ChildItem -Path $scriptDir -Recurse -Filter 'NolumiaScheduler.WinUI.exe' -ErrorAction SilentlyContinue |
		Sort-Object LastWriteTime -Descending
	if ($candidates) {
		$ExeDir = $candidates[0].DirectoryName
		Write-Host "EXE を自動検出しました: $ExeDir"
	} else {
		Write-Error 'NolumiaScheduler.WinUI.exe が見つかりません。-ExeDir で明示してください。'
	}
}

$ExeDir = (Resolve-Path $ExeDir).Path

# ---------------------------------------------------------------------------
# 1. Windows SDK ツールを検索
# ---------------------------------------------------------------------------
function Find-SdkTool {
	param([string] $ToolName)

	$found = Get-Command $ToolName -ErrorAction SilentlyContinue
	if ($found) { return $found.Source }

	$kitsRoot = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots' `
		-ErrorAction SilentlyContinue).'KitsRoot10'

	if (-not $kitsRoot) {
		Write-Error "$ToolName が見つかりません。Windows SDK をインストールしてください。"
		return
	}

	$tool = Get-ChildItem -Path $kitsRoot -Filter $ToolName -Recurse -ErrorAction SilentlyContinue |
		Where-Object { $_.FullName -match 'x64' } |
		Sort-Object FullName -Descending |
		Select-Object -First 1

	if (-not $tool) {
		Write-Error "$ToolName が Windows SDK 内に見つかりません。"
		return
	}

	return $tool.FullName
}

$makeAppx = Find-SdkTool 'MakeAppx.exe'
$signTool = Find-SdkTool 'SignTool.exe'
Write-Host "MakeAppx : $makeAppx"
Write-Host "SignTool  : $signTool"

# ---------------------------------------------------------------------------
# 2. 作業ディレクトリ
# ---------------------------------------------------------------------------
$workDir = Join-Path $env:TEMP 'NolumiaScheduler_SparsePackage'
Remove-Item $workDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item $workDir -ItemType Directory | Out-Null

# ---------------------------------------------------------------------------
# 3. スパースパッケージ用 manifest を生成
# ---------------------------------------------------------------------------
$srcManifest = Join-Path $scriptDir 'Package.appxmanifest'
$dstManifest = Join-Path $workDir 'AppxManifest.xml'

[xml] $xml = Get-Content $srcManifest -Encoding UTF8
$xml.Package.Identity.Version = '1.0.0.0'
$xml.Save($dstManifest)
Write-Host 'Manifest を生成しました'

# ---------------------------------------------------------------------------
# 4. スパース .appx をビルド
# ---------------------------------------------------------------------------
$appxPath = Join-Path $workDir 'NolumiaScheduler.appx'
& $makeAppx pack /d $workDir /p $appxPath /nv /o
if ($LASTEXITCODE -ne 0) { Write-Error "MakeAppx が失敗しました (exit $LASTEXITCODE)" }
Write-Host "appx を作成しました: $appxPath"

# ---------------------------------------------------------------------------
# 5. 自己署名証明書を生成（初回のみ、既存があればスキップ）
# ---------------------------------------------------------------------------
$certSubject = 'CN=kiyoteru.hosoda'
$pfxPath     = Join-Path $workDir 'NolumiaScheduler.pfx'
$pfxPassword = ConvertTo-SecureString 'NolumiaSchedulerDev' -AsPlainText -Force

$existingCert = Get-ChildItem 'Cert:\CurrentUser\My' |
	Where-Object { $_.Subject -eq $certSubject -and $_.NotAfter -gt (Get-Date) } |
	Select-Object -First 1

if ($existingCert) {
	Write-Host "既存の証明書を再利用します: $($existingCert.Thumbprint)"
	Export-PfxCertificate -Cert $existingCert -FilePath $pfxPath -Password $pfxPassword | Out-Null
} else {
	$cert = New-SelfSignedCertificate `
		-Subject $certSubject `
		-CertStoreLocation 'Cert:\CurrentUser\My' `
		-Type CodeSigningCert `
		-NotAfter (Get-Date).AddYears(10)

	Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $pfxPassword | Out-Null
	Write-Host "自己署名証明書を生成しました: $($cert.Thumbprint)"

	# 「信頼されたルート証明機関」への追加（管理者権限が必要）
	Write-Host ''
	Write-Host '--- 証明書を信頼済みルートへ追加します（UAC が表示される場合があります）---'

	$certBytes = $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert)
	$tempCer   = Join-Path $workDir 'NolumiaScheduler.cer'
	[IO.File]::WriteAllBytes($tempCer, $certBytes)

	$importCmd = "Import-Certificate -FilePath '$tempCer' -CertStoreLocation Cert:\LocalMachine\Root"
	Start-Process powershell.exe -ArgumentList @('-NoProfile', '-Command', $importCmd) -Verb RunAs -Wait
}

# ---------------------------------------------------------------------------
# 6. appx に署名
# ---------------------------------------------------------------------------
& $signTool sign /fd SHA256 /a /f $pfxPath /p 'NolumiaSchedulerDev' $appxPath
if ($LASTEXITCODE -ne 0) { Write-Error "SignTool が失敗しました (exit $LASTEXITCODE)" }
Write-Host '署名完了'

# ---------------------------------------------------------------------------
# 7. スパースパッケージとして登録
#    -ExternalLocation で EXE フォルダを指定することで
#    OS がプロセス起動時にパッケージ ID を自動で紐付けます。
# ---------------------------------------------------------------------------
Add-AppxPackage -Path $appxPath -ExternalLocation $ExeDir
Write-Host ''
Write-Host 'スパースパッケージの登録が完了しました。'
Write-Host "   パッケージ名       : NolumiaScheduler"
Write-Host "   外部ロケーション   : $ExeDir"
Write-Host ''
Write-Host 'アプリを起動後、タスクマネージャー「詳細」タブの「パッケージ名」列に NolumiaScheduler と表示されます。'
