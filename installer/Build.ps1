param([switch]$TestsOnly)
$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$output=Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$core=Join-Path $PSScriptRoot 'Installer.Core.cs'
$baseRefs=@('/r:System.dll','/r:System.Core.dll','/r:System.Security.dll','/r:System.Net.Http.dll','/r:System.Web.Extensions.dll','/r:System.IO.Compression.dll')
& $compiler /nologo /target:exe /platform:x64 /optimize+ /debug- ('/out:'+(Join-Path $output 'Publisher.exe')) @baseRefs $core (Join-Path $PSScriptRoot 'Publisher.cs')
if($LASTEXITCODE -ne 0){throw 'Publisher compilation failed.'}
if($TestsOnly){return}
$assets=Join-Path $PSScriptRoot 'assets'
if(-not(Test-Path -LiteralPath (Join-Path $assets 'update-public.xml'))){throw 'Initialize the publisher public key before building the installer.'}
$refs=$baseRefs+@('/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Xaml.dll')
$refs+=@('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll') | ForEach-Object {'/r:'+(Join-Path (Split-Path $compiler) ('WPF/'+$_))}
$refs+=@('System.Runtime.WindowsRuntime.dll','System.Runtime.InteropServices.WindowsRuntime.dll','System.Numerics.dll','System.Numerics.Vectors.dll') | ForEach-Object {'/r:'+(Join-Path (Split-Path $compiler) $_)}
$runtime=Get-ChildItem -LiteralPath (Join-Path $env:WINDIR 'Microsoft.NET/assembly/GAC_MSIL/System.Runtime') -Filter 'System.Runtime.dll' -Recurse | Select-Object -First 1
$refs+='/r:'+$runtime.FullName
$refs+=@('Windows.Foundation.winmd','Windows.UI.winmd','Windows.System.winmd','Windows.Graphics.winmd') | ForEach-Object {'/r:'+(Join-Path $env:WINDIR ('System32/WinMetadata/'+$_))}
$ui=Join-Path $PSScriptRoot 'ui'
$sources=@($core)+( @('Installer.Shell.cs','Installer.Components.cs','Installer.ClientCheck.cs','ClientUpdateHook.cs','Installer.Program.cs','AssemblyInfo.cs') | ForEach-Object {Join-Path $PSScriptRoot $_})
$sources+=@('AcPlayer.StartupView.cs','AcPlayer.StartupBackdrop.cs','AcPlayer.StartupMaterialPolicy.cs','AcPlayer.StartupWindowPolicy.cs','AcPlayer.StartupWindowBehavior.cs','AcPlayer.StartupProgress.cs') | ForEach-Object {Join-Path $ui $_}
$options=@('/nologo','/target:winexe','/platform:x64','/optimize+','/debug-',('/out:'+(Join-Path $output 'AnotherClientSetup.exe')),('/win32manifest:'+(Join-Path $PSScriptRoot 'Installer.manifest')),('/win32icon:'+(Join-Path $assets 'ac.ico')))
$options+=('/resource:'+(Join-Path $assets 'ac.ico')+',AnotherClient.Icon'),('/resource:'+(Join-Path $assets 'branding.png')+',AnotherClient.Branding'),('/resource:'+(Join-Path $assets 'update-public.xml')+',AnotherClient.UpdatePublicKey')
& $compiler @options @refs @sources
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed.'}
$updateSources=@('Installer.Core.cs','Installer.ClientCheck.cs','ClientStartupUpdates.cs') | ForEach-Object {Join-Path $PSScriptRoot $_}
$updateOptions=@('/nologo','/target:library','/platform:x64','/optimize+','/debug-',('/out:'+(Join-Path $output 'AnotherClient.Updates.dll')),('/resource:'+(Join-Path $output 'AnotherClientSetup.exe')+',AnotherClient.UpdateBootstrapper'),('/resource:'+(Join-Path $assets 'update-public.xml')+',AnotherClient.UpdatePublicKey'))
& $compiler @updateOptions @baseRefs @updateSources
if($LASTEXITCODE -ne 0){throw 'Client update bridge compilation failed.'}
Get-FileHash -LiteralPath (Join-Path $output 'AnotherClientSetup.exe') -Algorithm SHA256
