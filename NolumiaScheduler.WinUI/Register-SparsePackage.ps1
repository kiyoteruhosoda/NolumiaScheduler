#Requires -Version 5.1
<#
.SYNOPSIS
	Builds, signs (with a self-signed certificate) and registers the NolumiaScheduler sparse package.
	After registration, NolumiaScheduler appears in the "Package name" column of the Task Manager "Details" tab.

.DESCRIPTION
	The app runs as WindowsPackageType=None (unpackaged), so the "Package name" column
	in Task Manager is empty. A sparse package (a lightweight manifest-only .appx) gives it
	a package identity without a full move to MSIX.

	Prerequisites:
	- MakeAppx.exe and SignTool.exe from the Windows SDK are on PATH,
	  or the script finds them (HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots).
	- Admin rights are not needed, except for the step that adds the certificate to
	  "Trusted Root Certification Authorities" (first run only).

.PARAMETER ExeDir
	Path of the folder containing NolumiaScheduler.WinUI.exe.
	If omitted, the most recently written NolumiaScheduler.WinUI.exe is detected automatically.

.EXAMPLE
	# Register the Debug build
	.\Register-SparsePackage.ps1

.EXAMPLE
	# Register the Release build
	.\Register-SparsePackage.ps1 -ExeDir "bin\x64\Release\net10.0-windows10.0.22621.0"
#>
[CmdletBinding()]
param(
	[string] $ExeDir = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# 0. Resolve paths
# ---------------------------------------------------------------------------
$scriptDir = $PSScriptRoot

if (-not $ExeDir) {
	$candidates = Get-ChildItem -Path $scriptDir -Recurse -Filter 'NolumiaScheduler.WinUI.exe' -ErrorAction SilentlyContinue |
		Sort-Object LastWriteTime -Descending
	if ($candidates) {
		$ExeDir = $candidates[0].DirectoryName
		Write-Host "Detected the EXE automatically: $ExeDir"
	} else {
		Write-Error 'NolumiaScheduler.WinUI.exe was not found. Specify it with -ExeDir.'
	}
}

$ExeDir = (Resolve-Path $ExeDir).Path

# ---------------------------------------------------------------------------
# 1. Find the Windows SDK tools
# ---------------------------------------------------------------------------
function Find-SdkTool {
	param([string] $ToolName)

	$found = Get-Command $ToolName -ErrorAction SilentlyContinue
	if ($found) { return $found.Source }

	$kitsRoot = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots' `
		-ErrorAction SilentlyContinue).'KitsRoot10'

	if (-not $kitsRoot) {
		Write-Error "$ToolName was not found. Install the Windows SDK."
		return
	}

	$tool = Get-ChildItem -Path $kitsRoot -Filter $ToolName -Recurse -ErrorAction SilentlyContinue |
		Where-Object { $_.FullName -match 'x64' } |
		Sort-Object FullName -Descending |
		Select-Object -First 1

	if (-not $tool) {
		Write-Error "$ToolName was not found in the Windows SDK."
		return
	}

	return $tool.FullName
}

$makeAppx = Find-SdkTool 'MakeAppx.exe'
$signTool = Find-SdkTool 'SignTool.exe'
Write-Host "MakeAppx : $makeAppx"
Write-Host "SignTool  : $signTool"

# ---------------------------------------------------------------------------
# 2. Working directory
# ---------------------------------------------------------------------------
$workDir = Join-Path $env:TEMP 'NolumiaScheduler_SparsePackage'
Remove-Item $workDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item $workDir -ItemType Directory | Out-Null

# ---------------------------------------------------------------------------
# 3. Generate the manifest for the sparse package
# ---------------------------------------------------------------------------
$srcManifest = Join-Path $scriptDir 'Package.appxmanifest'
$dstManifest = Join-Path $workDir 'AppxManifest.xml'

[xml] $xml = Get-Content $srcManifest -Encoding UTF8
$xml.Package.Identity.Version = '1.0.0.0'
$xml.Save($dstManifest)
Write-Host 'Generated the manifest'

# ---------------------------------------------------------------------------
# 4. Build the sparse .appx
# ---------------------------------------------------------------------------
$appxPath = Join-Path $workDir 'NolumiaScheduler.appx'
& $makeAppx pack /d $workDir /p $appxPath /nv /o
if ($LASTEXITCODE -ne 0) { Write-Error "MakeAppx failed (exit $LASTEXITCODE)" }
Write-Host "Created the appx: $appxPath"

# ---------------------------------------------------------------------------
# 5. Generate a self-signed certificate (first run only; skipped if one exists)
# ---------------------------------------------------------------------------
$certSubject = 'CN=kiyoteru.hosoda'
$pfxPath     = Join-Path $workDir 'NolumiaScheduler.pfx'
$pfxPassword = ConvertTo-SecureString 'NolumiaSchedulerDev' -AsPlainText -Force

$existingCert = Get-ChildItem 'Cert:\CurrentUser\My' |
	Where-Object { $_.Subject -eq $certSubject -and $_.NotAfter -gt (Get-Date) } |
	Select-Object -First 1

if ($existingCert) {
	Write-Host "Reusing the existing certificate: $($existingCert.Thumbprint)"
	Export-PfxCertificate -Cert $existingCert -FilePath $pfxPath -Password $pfxPassword | Out-Null
} else {
	$cert = New-SelfSignedCertificate `
		-Subject $certSubject `
		-CertStoreLocation 'Cert:\CurrentUser\My' `
		-Type CodeSigningCert `
		-NotAfter (Get-Date).AddYears(10)

	Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $pfxPassword | Out-Null
	Write-Host "Generated a self-signed certificate: $($cert.Thumbprint)"

	# Add to "Trusted Root Certification Authorities" (needs admin rights)
	Write-Host ''
	Write-Host '--- Adding the certificate to the trusted root (a UAC prompt may appear) ---'

	$certBytes = $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert)
	$tempCer   = Join-Path $workDir 'NolumiaScheduler.cer'
	[IO.File]::WriteAllBytes($tempCer, $certBytes)

	$importCmd = "Import-Certificate -FilePath '$tempCer' -CertStoreLocation Cert:\LocalMachine\Root"
	Start-Process powershell.exe -ArgumentList @('-NoProfile', '-Command', $importCmd) -Verb RunAs -Wait
}

# ---------------------------------------------------------------------------
# 6. Sign the appx
# ---------------------------------------------------------------------------
& $signTool sign /fd SHA256 /a /f $pfxPath /p 'NolumiaSchedulerDev' $appxPath
if ($LASTEXITCODE -ne 0) { Write-Error "SignTool failed (exit $LASTEXITCODE)" }
Write-Host 'Signing complete'

# ---------------------------------------------------------------------------
# 7. Register as a sparse package
#    Passing the EXE folder as -ExternalLocation makes the OS
#    attach the package identity automatically when the process starts.
# ---------------------------------------------------------------------------
Add-AppxPackage -Path $appxPath -ExternalLocation $ExeDir
Write-Host ''
Write-Host 'Sparse package registration complete.'
Write-Host "   Package name       : NolumiaScheduler"
Write-Host "   External location  : $ExeDir"
Write-Host ''
Write-Host 'After the app starts, NolumiaScheduler appears in the "Package name" column of the Task Manager "Details" tab.'
