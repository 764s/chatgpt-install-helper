param([string]$OutputPath = (Join-Path $PSScriptRoot '..\ChatGPTInstaller-1.6.6.exe'))
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
$automationRoot = Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\System.Management.Automation'
$automation = Get-ChildItem -LiteralPath $automationRoot -Filter System.Management.Automation.dll -Recurse | Select-Object -First 1 -ExpandProperty FullName
if (-not $automation) { throw '找不到 Windows PowerShell 5.1 的系统组件。' }
$sourceFiles = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | Select-Object -ExpandProperty FullName
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Xml.dll','System.Xml.Linq.dll','System.Web.Extensions.dll','System.Runtime.Serialization.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll',$automation)
$arguments = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/utf8output',('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')),('/out:' + $OutputPath))
$arguments += $references | ForEach-Object { '/reference:' + $_ }
$arguments += Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Sidebar') -File | ForEach-Object { '/resource:' + $_.FullName + ',Sidebar.' + $_.Name }
& $compiler @arguments @sourceFiles
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
Get-Item -LiteralPath $OutputPath | Select-Object FullName,Length
