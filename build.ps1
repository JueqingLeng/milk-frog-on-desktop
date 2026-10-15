param([switch]$Test)
$ErrorActionPreference = 'Stop'
$projectDirectory = $PSScriptRoot
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (!(Test-Path -LiteralPath $compilerPath)) { throw '需要 Windows 的 .NET Framework 4.x。' }
$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $projectDirectory 'src') -Filter '*.cs' | Where-Object Name -ne 'Tests.cs' | ForEach-Object FullName)
$references = @('/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Web.Extensions.dll', '/r:System.Core.dll')
& $compilerPath /nologo /codepage:65001 /target:winexe /platform:x64 "/out:$projectDirectory/MilkyFrog.exe" "/win32icon:$projectDirectory/assets/frog.ico" "/win32manifest:$projectDirectory/app.manifest" @references @sourceFiles
if ($LASTEXITCODE -ne 0) { throw '编译桌宠失败。' }
Write-Host '已构建 MilkyFrog.exe'
if ($Test) {
    $testSource = Join-Path $projectDirectory 'src\Tests.cs'
    & $compilerPath /nologo /codepage:65001 /target:exe /platform:x64 /main:MilkyFrog.Tests "/out:$projectDirectory/MilkyFrog.Tests.exe" @references @sourceFiles $testSource
    if ($LASTEXITCODE -ne 0) { throw '测试编译失败。' }
    & "$projectDirectory/MilkyFrog.Tests.exe"
    if ($LASTEXITCODE -ne 0) { throw '测试未通过。' }
}
