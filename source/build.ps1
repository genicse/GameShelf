param([string]$TestOutput = '')
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$target = Join-Path (Split-Path $PSScriptRoot -Parent) 'GameShelf.exe'
$manifestPath = Join-Path $PSScriptRoot 'app.manifest'
if ($TestOutput) {
    $target = [System.IO.Path]::GetFullPath($TestOutput)
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    $manifestPath = Join-Path (Split-Path $target -Parent) 'test.manifest'
    (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'app.manifest') -Raw).Replace('requireAdministrator', 'asInvoker') | Set-Content -LiteralPath $manifestPath -Encoding UTF8
}
$references = @('System.dll', 'System.Core.dll', 'System.Runtime.Serialization.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Xaml.dll')
$arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/codepage:65001', ('/out:' + $target), ('/win32manifest:' + $manifestPath))
foreach ($reference in $references) { $arguments += '/reference:' + (Join-Path $framework $reference) }
foreach ($reference in @('PresentationCore.dll', 'PresentationFramework.dll', 'WindowsBase.dll')) { $arguments += '/reference:' + (Join-Path $framework ('WPF\' + $reference)) }
if (Test-Path (Join-Path $PSScriptRoot 'app.ico')) { $arguments += '/win32icon:' + (Join-Path $PSScriptRoot 'app.ico') }
$arguments += Join-Path $PSScriptRoot 'GameShelf.cs'
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output "Built: $target"
