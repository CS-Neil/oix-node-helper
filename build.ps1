param(
    [switch]$Clean,
    [switch]$SkipFlutter,
    [switch]$LegacyOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceRoot = Join-Path $projectRoot 'src'
$installerRoot = Join-Path $projectRoot 'installer'
$buildRoot = Join-Path $projectRoot 'build'

if ($Clean -and (Test-Path -LiteralPath $buildRoot)) {
    Remove-Item -LiteralPath $buildRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null

$compilerCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compiler) {
    throw '.NET Framework C# compiler was not found. Enable or install .NET Framework 4.x.'
}

$references = @(
    'System.dll',
    'System.Core.dll',
    'System.Drawing.dll',
    'System.Security.dll',
    'System.Web.Extensions.dll',
    'System.Windows.Forms.dll'
) | ForEach-Object { "/reference:$_" }

$sources = Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' | ForEach-Object { $_.FullName }
$appSources = $sources | Where-Object { (Split-Path -Leaf $_) -ne 'TestProgram.cs' }
$appOut = Join-Path $buildRoot 'OixNodeHelper.exe'
$hostOut = Join-Path $buildRoot 'OixNodeHost.exe'
$testOut = Join-Path $buildRoot 'OixNodeHelper.Tests.exe'
$uninstallerOut = Join-Path $buildRoot 'Uninstall.exe'
$setupOut = Join-Path $buildRoot 'OixNodeHelper-Setup.exe'

& $compiler /nologo /codepage:65001 /target:winexe /optimize+ /main:OixNodeHelper.Program "/out:$appOut" $references $appSources
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }

& $compiler /nologo /codepage:65001 /target:winexe /optimize+ /main:OixNodeHelper.HostProgram "/out:$hostOut" $references $appSources
if ($LASTEXITCODE -ne 0) { throw 'Host compilation failed.' }

& $compiler /nologo /codepage:65001 /target:exe /optimize+ /main:OixNodeHelper.TestProgram "/out:$testOut" $references $sources
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }

& $compiler /nologo /codepage:65001 /target:winexe /optimize+ "/out:$uninstallerOut" $references (Join-Path $installerRoot 'UninstallerProgram.cs')
if ($LASTEXITCODE -ne 0) { throw 'Uninstaller compilation failed.' }

$installerResources = @(
    "/resource:$appOut,OixNodeHelper.exe",
    "/resource:$(Join-Path $projectRoot 'core\mihomo-oix.exe'),mihomo-oix.exe",
    "/resource:$uninstallerOut,Uninstall.exe",
    "/resource:$(Join-Path $projectRoot 'README.md'),README.md",
    "/resource:$(Join-Path $projectRoot 'config\flclash-provider.yaml.example'),flclash-provider.yaml.example"
)
& $compiler /nologo /codepage:65001 /target:winexe /optimize+ "/out:$setupOut" $references $installerResources (Join-Path $installerRoot 'InstallerProgram.cs')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }

Write-Host "Application: $appOut"
Write-Host "Host:        $hostOut"
Write-Host "Installer:   $setupOut"

if (-not $LegacyOnly -and -not $SkipFlutter) {
    $flutterCommand = Get-Command flutter -ErrorAction SilentlyContinue
    $flutterPath = if ($flutterCommand) { $flutterCommand.Source } else { $null }
    if (-not $flutterPath) {
        $flutterCandidates = @(
            $(if ($env:FLUTTER_ROOT) { Join-Path $env:FLUTTER_ROOT 'bin\flutter.bat' }),
            $(if ($env:USERPROFILE) { Join-Path $env:USERPROFILE 'develop\flutter\bin\flutter.bat' })
        ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
        $flutterPath = $flutterCandidates | Select-Object -First 1
    }
    if (-not $flutterPath) {
        Write-Warning 'Flutter SDK was not found. The C# host and legacy fallback were built; install Flutter and rerun build.ps1 to build the new GUI.'
        return
    }

    $flutterRoot = Join-Path $projectRoot 'app'
    $windowsRunner = Join-Path $flutterRoot 'windows'
    if (-not (Test-Path -LiteralPath $windowsRunner)) {
        $scaffoldRoot = Join-Path $buildRoot 'flutter-scaffold'
        if (Test-Path -LiteralPath $scaffoldRoot) {
            Remove-Item -LiteralPath $scaffoldRoot -Recurse -Force
        }
        & $flutterPath create --platforms=windows --org com.oixnodehelper --project-name oix_node_helper $scaffoldRoot
        if ($LASTEXITCODE -ne 0) { throw 'Flutter Windows runner generation failed.' }
        Copy-Item -LiteralPath (Join-Path $scaffoldRoot 'windows') -Destination $windowsRunner -Recurse
    }
    Push-Location $flutterRoot
    try {
        & $flutterPath pub get
        if ($LASTEXITCODE -ne 0) { throw 'flutter pub get failed.' }
        & $flutterPath build windows --release
        if ($LASTEXITCODE -ne 0) { throw 'Flutter Windows build failed.' }
    }
    finally {
        Pop-Location
    }

    $flutterRelease = Join-Path $flutterRoot 'build\windows\x64\runner\Release'
    Copy-Item -LiteralPath $hostOut -Destination (Join-Path $flutterRelease 'OixNodeHost.exe') -Force
    $releaseCore = Join-Path $flutterRelease 'core'
    New-Item -ItemType Directory -Path $releaseCore -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'core\mihomo-oix.exe') -Destination (Join-Path $releaseCore 'mihomo-oix.exe') -Force
    $runnerIcon = Join-Path $windowsRunner 'runner\resources\app_icon.ico'
    if (Test-Path -LiteralPath $runnerIcon) {
        Copy-Item -LiteralPath $runnerIcon -Destination (Join-Path $flutterRelease 'app_icon.ico') -Force
    }
    Write-Host "Flutter GUI: $flutterRelease"

    $isccCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $iscc = if ($isccCommand) { $isccCommand.Source } else { $null }
    $isccCandidates = @(
        $(if ($env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe' }),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
    if (-not $iscc) {
        $iscc = $isccCandidates | Select-Object -First 1
    }
    if ($iscc) {
        & $iscc (Join-Path $installerRoot 'OixNodeHelper-Flutter.iss')
        if ($LASTEXITCODE -ne 0) { throw 'Inno Setup packaging failed.' }
    }
    else {
        Write-Warning 'Inno Setup 6 was not found. The portable Flutter release is ready, but the Flutter installer was not generated.'
    }
}
