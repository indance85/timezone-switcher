$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$wpf = Join-Path $framework 'WPF'
$refs = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','System.Xml.dll','System.Xaml.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$refs += @('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll') | ForEach-Object { '/reference:' + (Join-Path $wpf $_) }
$sources = @('Core.cs','App.cs','Tests.cs') | ForEach-Object { Join-Path $PSScriptRoot "src/$_" }
& (Join-Path $framework 'csc.exe') /nologo /target:winexe /platform:anycpu /optimize+ /utf8output ('/out:' + (Join-Path $PSScriptRoot 'TimezoneSwitcher.exe')) ('/win32manifest:' + (Join-Path $PSScriptRoot 'src/app.manifest')) ('/resource:' + (Join-Path $PSScriptRoot 'src/Main.xaml') + ',Main.xaml') ('/resource:' + (Join-Path $PSScriptRoot 'src/windowsZones.xml') + ',windowsZones.xml') ('/win32icon:' + (Join-Path $PSScriptRoot 'assets/timezone.ico')) ('/resource:' + (Join-Path $PSScriptRoot 'assets/timezone.ico') + ',timezone.ico') ('/resource:' + (Join-Path $PSScriptRoot 'assets/timezone-icon.png') + ',timezone-icon.png') $refs $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Output 'Built TimezoneSwitcher.exe'
