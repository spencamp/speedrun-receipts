param([string]$LiveSplitPath = "$PSScriptRoot\vendor\runtime", [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$compiler = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if (!(Test-Path "$LiveSplitPath\LiveSplit.Core.dll")) { throw 'Pass -LiveSplitPath pointing to an extracted LiveSplit 1.8.37 folder. See README.md.' }
$build = Join-Path $PSScriptRoot 'build'
$dist = Join-Path $PSScriptRoot 'dist\Components'
New-Item -ItemType Directory -Force $build,$dist | Out-Null
$refs = @('/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Xml.dll',"/r:$LiveSplitPath\LiveSplit.Core.dll","/r:$LiveSplitPath\UpdateManager.dll")
$sources = @(Get-ChildItem "$PSScriptRoot\src\*.cs" | ForEach-Object FullName)
& $compiler /nologo /warn:4 /warnaserror /optimize+ /target:library "/out:$dist\LiveSplit.ThermalReceipt.dll" @refs @sources
if ($LASTEXITCODE -ne 0) { throw 'Component compilation failed.' }
if (!$SkipTests) {
    & $compiler /nologo /warn:4 /warnaserror /target:exe "/out:$build\ThermalReceipt.Tests.exe" @refs "/r:$dist\LiveSplit.ThermalReceipt.dll" "$PSScriptRoot\tests\Tests.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    Copy-Item "$LiveSplitPath\*.dll" $build
    Copy-Item "$dist\LiveSplit.ThermalReceipt.dll" $build
    Push-Location $PSScriptRoot
    try { & "$build\ThermalReceipt.Tests.exe" | Tee-Object -FilePath "$build\test-results.txt" }
    finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
Copy-Item "$PSScriptRoot\README.md","$PSScriptRoot\IMPLEMENTATION_NOTES.md" (Split-Path $dist)
Compress-Archive -Path $dist,"$PSScriptRoot\dist\README.md","$PSScriptRoot\dist\IMPLEMENTATION_NOTES.md" -DestinationPath "$PSScriptRoot\dist\LiveSplit.ThermalReceipt-1.1.0.zip" -Force
Write-Host "Component: $dist\LiveSplit.ThermalReceipt.dll"
