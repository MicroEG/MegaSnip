[CmdletBinding()]
param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$RuntimeIdentifier = "win-x64",

    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot "..\dist"
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "src\MegaSnip\MegaSnip.csproj"

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Project file not found: $projectPath"
}

[xml]$project = Get-Content -LiteralPath $projectPath
$version = $project.Project.PropertyGroup |
    ForEach-Object { $_.Version } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -First 1

if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Could not read the version from $projectPath"
}

$targetDirectory = [System.IO.Path]::GetFullPath(
    (Join-Path $OutputDirectory "MegaSnip-$version-$RuntimeIdentifier"))

Write-Host "Publishing MegaSnip $version ($RuntimeIdentifier)"
Write-Host "Output: $targetDirectory"

dotnet publish $projectPath `
    --configuration Release `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    --output $targetDirectory `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$executablePath = Join-Path $targetDirectory "MegaSnip.exe"
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Publish completed but MegaSnip.exe was not found in $targetDirectory"
}

$files = @(Get-ChildItem -LiteralPath $targetDirectory -File)
$sizeMb = [Math]::Round(
    (($files | Measure-Object -Property Length -Sum).Sum / 1MB),
    1)

Write-Host "Published $($files.Count) file(s), $sizeMb MB total:"
$files | ForEach-Object { Write-Host "  $($_.Name)" }
Write-Host "Executable: $executablePath"
