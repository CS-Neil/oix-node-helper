[CmdletBinding()]
param(
    [switch]$Staged,
    [switch]$All
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Staged -and $All) {
    throw 'Use either -Staged or -All, not both.'
}
if (-not $Staged -and -not $All) {
    $All = $true
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$gitDirectory = Join-Path $projectRoot '.git'

$textExtensions = @(
    '.cs', '.dart', '.ps1', '.psm1', '.md', '.txt', '.json', '.yaml', '.yml',
    '.toml', '.ini', '.config', '.xml', '.properties', '.iss', '.isl', '.cmake',
    '.cpp', '.cc', '.c', '.h', '.hpp', '.rc', '.manifest', '.lock', '.example',
    '.gitignore', '.gitattributes'
)

$rules = @(
    @{ Name = 'Private key'; Regex = '-----BEGIN (?:RSA |EC |OPENSSH |DSA |PGP )?PRIVATE KEY-----'; AlwaysBlock = $true },
    @{ Name = 'GitHub token'; Regex = '\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,})\b' },
    @{ Name = 'OpenAI-style key'; Regex = '\bsk-(?:proj-)?[A-Za-z0-9_-]{20,}\b' },
    @{ Name = 'AWS access key'; Regex = '\b(?:AKIA|ASIA)[A-Z0-9]{16}\b' },
    @{ Name = 'Google API key'; Regex = '\bAIza[0-9A-Za-z_-]{35}\b' },
    @{ Name = 'GitLab token'; Regex = '\bglpat-[A-Za-z0-9_-]{20,}\b' },
    @{ Name = 'Slack token'; Regex = '\bxox[baprs]-[A-Za-z0-9-]{10,}\b' },
    @{ Name = 'Stripe secret'; Regex = '\b(?:sk|rk)_(?:live|test)_[A-Za-z0-9]{16,}\b' },
    @{ Name = 'JWT'; Regex = '\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b' },
    @{ Name = 'Bearer credential'; Regex = '(?i)\bBearer\s+(?<value>[A-Za-z0-9._~+/=-]{12,})\b' },
    @{ Name = 'Credential in URL'; Regex = '(?i)\b(?:https?|ftp)://(?<value>[^\s/:@]+:[^\s/@]+)@' },
    @{ Name = 'Quoted sensitive value'; Regex = '(?im)\b(?:[A-Za-z0-9_]*(?:api[_-]?key|secret|token|password|passwd|client[_-]?secret|access[_-]?token|refresh[_-]?token|private[_-]?key)[A-Za-z0-9_]*)\b[ \t]*=[ \t]*["''](?<value>[^"''\r\n]{8,})["'']' },
    @{ Name = 'Environment secret'; Regex = '(?m)^\s*(?:[A-Z0-9_]*(?:TOKEN|SECRET|PASSWORD|PASSWD|API_KEY|PRIVATE_KEY)[A-Z0-9_]*)[ \t]*=[ \t]*["'']?(?<value>[^\s"''#]{8,})' },
    @{ Name = 'Configuration secret'; Regex = '(?im)^\s*["'']?(?:token|secret|password|passwd|api[_-]?key|client[_-]?secret|access[_-]?token|refresh[_-]?token)["'']?[ \t]*:[ \t]*["'']?(?<value>[^\s"''#}{]{8,})'; ConfigOnly = $true }
)

function Test-Placeholder {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) { return $true }
    return $Value -match '(?i)(example|sample|dummy|fake|placeholder|change[_-]?me|replace[_-]?me|your[_-]?|not[_-]?a[_-]?real|test|tokenvalue|secretvalue|localhost)'
}

function Test-TextFile {
    param([string]$RelativePath)

    $name = [IO.Path]::GetFileName($RelativePath)
    if ($name -match '^\.env(?:\..+)?$') { return $true }
    return $textExtensions -contains [IO.Path]::GetExtension($RelativePath).ToLowerInvariant()
}

function Get-LineNumber {
    param(
        [string]$Content,
        [int]$Index
    )

    if ($Index -le 0) { return 1 }
    return 1 + ([regex]::Matches($Content.Substring(0, $Index), "`n")).Count
}

function Get-ContentToScan {
    param([string]$RelativePath)

    if ($Staged) {
        $stagedContent = & git -C $projectRoot show --no-textconv ":$RelativePath" 2>$null
        if ($LASTEXITCODE -ne 0) { return $null }
        return ($stagedContent -join "`n")
    }

    $fullPath = Join-Path $projectRoot $RelativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { return $null }
    if ((Get-Item -LiteralPath $fullPath).Length -gt 5MB) { return $null }
    return [IO.File]::ReadAllText($fullPath)
}

if ($Staged -and -not (Test-Path -LiteralPath $gitDirectory)) {
    throw 'This directory is not a Git repository. Run the scanner with -All before git init.'
}

if (Test-Path -LiteralPath $gitDirectory) {
    if ($Staged) {
        $paths = @(& git -C $projectRoot -c core.quotepath=false diff --cached --name-only --diff-filter=ACMR)
    }
    else {
        $paths = @(& git -C $projectRoot -c core.quotepath=false ls-files --cached --others --exclude-standard)
    }
}
else {
    $paths = @(Get-ChildItem -LiteralPath $projectRoot -Recurse -Force -File | Where-Object {
        $_.FullName -notmatch '[\\/](?:\.git|build|bin|obj|\.dart_tool)[\\/]' -and
        $_.Name -notin @('.flutter-plugins', '.flutter-plugins-dependencies')
    } | ForEach-Object {
        $_.FullName.Substring($projectRoot.Length + 1)
    })
}

$findings = New-Object System.Collections.Generic.List[object]

foreach ($relativePath in ($paths | Sort-Object -Unique)) {
    if ([string]::IsNullOrWhiteSpace($relativePath) -or -not (Test-TextFile $relativePath)) {
        continue
    }

    $content = Get-ContentToScan $relativePath
    if ($null -eq $content -or $content.IndexOf([char]0) -ge 0) {
        continue
    }

    foreach ($rule in $rules) {
        $configOnly = $rule.ContainsKey('ConfigOnly') -and $rule.ConfigOnly
        if ($configOnly -and [IO.Path]::GetExtension($relativePath).ToLowerInvariant() -notin @('.yaml', '.yml', '.json', '.toml', '.ini', '.config', '.properties', '.env', '.example')) {
            continue
        }
        foreach ($match in [regex]::Matches($content, $rule.Regex)) {
            $hasCapturedValue = $match.Groups['value'].Success
            $value = if ($hasCapturedValue) { $match.Groups['value'].Value } else { '' }
            $alwaysBlock = $rule.ContainsKey('AlwaysBlock') -and $rule.AlwaysBlock
            if (-not $alwaysBlock -and $hasCapturedValue -and (Test-Placeholder $value)) {
                continue
            }

            $findings.Add([pscustomobject]@{
                Path = $relativePath.Replace('\', '/')
                Line = Get-LineNumber -Content $content -Index $match.Index
                Rule = $rule.Name
            })
        }
    }
}

$uniqueFindings = @($findings | Sort-Object Path, Line, Rule -Unique)
if ($uniqueFindings.Count -gt 0) {
    Write-Host "Potential secrets detected ($($uniqueFindings.Count)). Values are intentionally hidden." -ForegroundColor Red
    foreach ($finding in $uniqueFindings) {
        Write-Host ("  {0}:{1} [{2}]" -f $finding.Path, $finding.Line, $finding.Rule) -ForegroundColor Red
    }
    Write-Host 'Do not bypass this check. Remove the value and rotate it if it was ever real.' -ForegroundColor Yellow
    exit 1
}

Write-Host ("Secret scan passed: {0} text files checked; no credential patterns found." -f ($paths | Where-Object { Test-TextFile $_ }).Count) -ForegroundColor Green
