$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $projectRoot 'build.ps1') -SkipFlutter
& (Join-Path $projectRoot 'build\OixNodeHelper.Tests.exe')
if ($LASTEXITCODE -ne 0) {
    throw "Tests failed with exit code: $LASTEXITCODE"
}
