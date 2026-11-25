param([string]$Archive='',[string]$Channel='')
$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$output=Join-Path $PSScriptRoot 'build/Installer.Tests.exe'
$sources=@('Installer.Core.cs','Installer.Tests.cs') | ForEach-Object {Join-Path $PSScriptRoot $_}
& $compiler /nologo /target:exe /platform:x64 /optimize+ /debug- ('/out:'+$output) ('/win32manifest:'+(Join-Path $PSScriptRoot 'Installer.manifest')) /r:System.dll /r:System.Core.dll /r:System.Security.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll @sources
if($LASTEXITCODE -ne 0){throw 'Tests did not compile.'}
$fixture=Join-Path $PSScriptRoot ('tests/'+[Guid]::NewGuid().ToString('N'))
if($Archive){& $output $fixture $Archive (Join-Path $PSScriptRoot 'assets/update-public.xml') $Channel}
else{& $output $fixture}
if($LASTEXITCODE -ne 0){throw 'Installer tests failed.'}
$shellOutput=Join-Path $PSScriptRoot 'build/Shell.Tests.exe'
$shellSources=@('Installer.Core.cs','Installer.Shell.cs','Shell.Tests.cs') | ForEach-Object {Join-Path $PSScriptRoot $_}
& $compiler /nologo /target:exe /platform:x64 /optimize+ /debug- ('/out:'+$shellOutput) ('/resource:'+(Join-Path $PSScriptRoot 'assets/ac.ico')+',AnotherClient.Icon') /r:System.dll /r:System.Core.dll /r:System.Security.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll @shellSources
if($LASTEXITCODE -ne 0){throw 'Shell tests did not compile.'}
& $shellOutput (Join-Path $fixture 'shell')
if($LASTEXITCODE -ne 0){throw 'Shell tests failed.'}
$componentOutput=Join-Path $PSScriptRoot 'build/Components.Tests.exe'
$componentSources=@('Installer.Core.cs','Installer.Components.cs','Components.Tests.cs') | ForEach-Object {Join-Path $PSScriptRoot $_}
& $compiler /nologo /target:exe /platform:x64 /optimize+ /debug- ('/out:'+$componentOutput) /r:System.dll /r:System.Core.dll /r:System.Security.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll @componentSources
if($LASTEXITCODE -ne 0){throw 'Component tests did not compile.'}
& $componentOutput (Join-Path $fixture 'components')
if($LASTEXITCODE -ne 0){throw 'Component tests failed.'}
