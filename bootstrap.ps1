# Optional one-time dependency setup. Never launches LiveSplit or prints.
$ErrorActionPreference = 'Stop'
$vendor = Join-Path $PSScriptRoot 'vendor'
New-Item -ItemType Directory -Force $vendor | Out-Null
$archive = Join-Path $vendor 'LiveSplit.zip'
$expected = '14BC8EF8DED9EF4033FB2F0CB6A152386D393127DA18A4DE14F096C5347AA991'
if (!(Test-Path $archive)) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest 'https://github.com/LiveSplit/LiveSplit/releases/download/1.8.37/LiveSplit_1.8.37.zip' -OutFile $archive
}
if ((Get-FileHash $archive -Algorithm SHA256).Hash -ne $expected) { throw 'LiveSplit archive checksum mismatch. Dependency not extracted.' }
Expand-Archive $archive (Join-Path $vendor 'runtime') -Force
Write-Host 'LiveSplit 1.8.37 references ready. Run .\build.ps1 next.'
