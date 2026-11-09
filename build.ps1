param(
    [switch]$Test,
    [string]$IconPath
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) {
    throw 'The Windows .NET Framework C# compiler was not found.'
}
if (!$IconPath) {
    $IconPath = Join-Path $root 'assets\Bridge.ico'
}
$icon = (Resolve-Path -LiteralPath $IconPath).Path

$artifacts = Join-Path $root 'artifacts'
$app = Join-Path $artifacts 'app'
$null = New-Item -ItemType Directory -Path $app -Force
$exe = Join-Path $app 'Orca-Bambu-Connect.exe'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' -File |
    ForEach-Object FullName)
$options = @(
    '/nologo', '/platform:anycpu32bitpreferred', '/optimize+', '/codepage:65001', '/warnaserror+',
    "/win32icon:$icon", "/reference:$framework\System.IO.Compression.dll",
    '/reference:System.Web.Extensions.dll'
)

& $compiler @options /target:winexe "/out:$exe" @sources
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }

if ($Test) {
    $run = Join-Path $artifacts ('test-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    $inbox = Join-Path $run 'inbox (multi) & [slice]'
    $portable = Join-Path $run 'portable (test) #+% &'
    $null = New-Item -ItemType Directory -Path $inbox, $portable -Force
    $probe = Join-Path $portable 'Custom (Connect) [test] # + 100%.exe'
    $verify = Join-Path $run 'Verify.exe'
    [IO.File]::WriteAllText((Join-Path $run 'WatchFolder.txt'), $inbox)
    [IO.File]::WriteAllText((Join-Path $run 'ConnectPath.txt'), $probe)

    & $compiler /nologo /codepage:65001 /target:exe "/out:$probe" (Join-Path $root 'tests\ConnectProbe.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test receiver build failed.' }
    & $compiler @options /target:exe /main:Verify "/out:$verify" /reference:System.Web.dll @sources (Join-Path $root 'tests\Verify.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test suite build failed.' }
    & $verify $inbox $probe
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    Write-Output "Test evidence: $run"
}

$guide = Join-Path $app 'Quick-Start.zh-CN.txt'
Copy-Item -LiteralPath (Join-Path $root 'docs\quick-start.zh-CN.txt') -Destination $guide -Force
$zip = Join-Path $artifacts 'Orca-Bambu-Connect-Tray.zip'
Compress-Archive -LiteralPath $exe, $guide -DestinationPath $zip -Force
Write-Output "Package: $zip"
