[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot

if (-not (Test-Path -LiteralPath (Join-Path $projectRoot '.git'))) {
    throw 'Git repository not found. Run git init first, then run this script.'
}

& git -C $projectRoot config core.hooksPath .githooks
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to configure the Git hooks path.'
}

Write-Host 'Repository hooks enabled. Every commit will scan staged text for possible secrets.' -ForegroundColor Green
