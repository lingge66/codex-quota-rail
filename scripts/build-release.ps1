param(
    [switch]$SkipInstaller,
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-LastExitCode([string]$Step) {
    if ($LASTEXITCODE -ne 0) {
        throw "$Step failed with exit code $LASTEXITCODE."
    }
}

if ([string]::IsNullOrWhiteSpace($env:RELEASE_VERSION)) {
    throw 'RELEASE_VERSION is required, for example 0.1.0-rc.1.'
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repositoryRoot
if (-not $AllowDirty -and (git status --porcelain)) {
    throw 'Git working tree must be clean before creating release artifacts.'
}

$artifacts = Join-Path $repositoryRoot 'artifacts'
$publishDirectory = Join-Path $artifacts 'publish\win-x64'
if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
@(
    'CodexQuotaRail-win-x64.zip',
    'CodexQuotaRail-Setup.exe',
    'CodexQuotaRail.spdx.json',
    'THIRD-PARTY-NOTICES.md',
    'SHA256SUMS.txt'
) | ForEach-Object {
    $stalePath = Join-Path $artifacts $_
    if (Test-Path -LiteralPath $stalePath) {
        Remove-Item -LiteralPath $stalePath -Force
    }
}
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

dotnet restore --locked-mode
Assert-LastExitCode 'dotnet restore'
dotnet restore src\CodexQuotaRail.App\CodexQuotaRail.App.csproj --runtime win-x64 --locked-mode
Assert-LastExitCode 'runtime restore'
dotnet test --configuration Release --no-restore
Assert-LastExitCode 'dotnet test'
dotnet publish src\CodexQuotaRail.App\CodexQuotaRail.App.csproj `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --no-restore `
    -p:Version=$env:RELEASE_VERSION `
    -p:PublishSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    --output $publishDirectory
Assert-LastExitCode 'dotnet publish'

$publishedExecutable = Join-Path $publishDirectory 'CodexQuotaRail.App.exe'
if (-not (Test-Path -LiteralPath $publishedExecutable)) {
    throw "Published executable not found: $publishedExecutable"
}

$normalizedTimestamp = [datetime]::SpecifyKind([datetime]'2000-01-01T00:00:00', 'Utc')
Get-ChildItem -LiteralPath $publishDirectory -File -Recurse |
    ForEach-Object { $_.LastWriteTimeUtc = $normalizedTimestamp }
$zipPath = Join-Path $artifacts 'CodexQuotaRail-win-x64.zip'
Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $zipPath -Force

$setupPath = Join-Path $artifacts 'CodexQuotaRail-Setup.exe'
if (-not $SkipInstaller) {
    $makensis = Get-Command makensis.exe -ErrorAction SilentlyContinue
    if (-not $makensis) {
        $commonPath = Join-Path ${env:ProgramFiles(x86)} 'NSIS\makensis.exe'
        if (Test-Path -LiteralPath $commonPath) {
            $makensis = Get-Item -LiteralPath $commonPath
        }
    }
    if (-not $makensis) {
        throw 'NSIS 3.12 is required. Install it or use -SkipInstaller for portable QA only.'
    }

    $nsisVersion = (& $makensis.Source /VERSION | Select-Object -First 1).Trim()
    if ($nsisVersion -notmatch '^v?3\.12(?:\.0)?$') {
        throw "NSIS 3.12 is required; found $nsisVersion."
    }

    & $makensis.Source `
        "/DVERSION=$env:RELEASE_VERSION" `
        "/DPUBLISH_DIR=$publishDirectory" `
        "/DARTIFACT_DIR=$artifacts" `
        'packaging\nsis\CodexQuotaRail.nsi'
    Assert-LastExitCode 'makensis'
    if (-not (Test-Path -LiteralPath $setupPath)) {
        throw "Installer not found: $setupPath"
    }
}

dotnet tool restore
Assert-LastExitCode 'dotnet tool restore'
dotnet tool run sbom-tool generate `
    -b $publishDirectory `
    -bc $repositoryRoot `
    -pn CodexQuotaRail `
    -pv $env:RELEASE_VERSION `
    -ps LingGe `
    -nsb https://github.com/lingge66/codex-quota-rail
Assert-LastExitCode 'SBOM generation'
$generatedSbom = Join-Path $publishDirectory '_manifest\spdx_2.2\manifest.spdx.json'
if (-not (Test-Path -LiteralPath $generatedSbom)) {
    throw "SPDX SBOM not found: $generatedSbom"
}
$sbomPath = Join-Path $artifacts 'CodexQuotaRail.spdx.json'
Copy-Item -LiteralPath $generatedSbom -Destination $sbomPath -Force
Copy-Item -LiteralPath 'THIRD-PARTY-NOTICES.md' -Destination $artifacts -Force

$hashTargets = @($zipPath, $sbomPath, (Join-Path $artifacts 'THIRD-PARTY-NOTICES.md'))
if (Test-Path -LiteralPath $setupPath) {
    $hashTargets += $setupPath
}
Get-FileHash -LiteralPath $hashTargets -Algorithm SHA256 |
    ForEach-Object { "$($_.Hash.ToLowerInvariant())  $(Split-Path $_.Path -Leaf)" } |
    Set-Content -LiteralPath (Join-Path $artifacts 'SHA256SUMS.txt') -Encoding ascii

@($hashTargets) + @((Join-Path $artifacts 'SHA256SUMS.txt')) |
    Get-Item |
    Select-Object Name, Length, LastWriteTimeUtc
