<# Offline tools; uses the existing JDK, no Android SDK or downloaded dependency.
   ./tools/localai.ps1
   ./tools/localai.ps1 -Pcm tools/raw/local_light.raw -Output tools/out/local_light.csv
   ./tools/localai.ps1 -Baseline tools/localai-baseline.csv
#>
param(
    [string]$Pcm = '',
    [string]$Output = '',
    [string]$Baseline = '',
    [switch]$Sections
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$jdkCandidates = @($env:ATTENTION_JDK, $env:JAVA_HOME, 'D:\unity\jdk-17')
$jdkPath = $jdkCandidates | Where-Object { $_ -and (Test-Path (Join-Path $_ 'bin/javac.exe')) } | Select-Object -First 1
if (-not $jdkPath) { throw 'Set ATTENTION_JDK to a JDK 17 installation.' }
$classes = Join-Path $PSScriptRoot 'out/localai-classes'
New-Item -ItemType Directory -Force -Path $classes | Out-Null
$sourceRoot = Join-Path $projectRoot 'app/src/main/java/com/fallenangel/overlaybeat'
$sources = @((Join-Path $sourceRoot 'OnsetCurves.java'))
$sources += @(Get-ChildItem (Join-Path $sourceRoot 'localai') -Filter '*.java' | ForEach-Object { $_.FullName })
$sources += Join-Path $PSScriptRoot 'ObservationDump.java'
$sources += Join-Path $PSScriptRoot 'SectionDump.java'
& (Join-Path $jdkPath 'bin/javac.exe') -encoding UTF-8 -d $classes @sources
if ($LASTEXITCODE -ne 0) { throw 'Offline tool compilation failed.' }
$dumpClass = if ($Sections) { 'SectionDump' } else { 'ObservationDump' }
if ($Pcm) {
    if (-not $Output) { $Output = Join-Path $PSScriptRoot $(if ($Sections) { 'out/sections.csv' } else { 'out/observations.csv' }) }
    & (Join-Path $jdkPath 'bin/java.exe') -cp $classes $dumpClass $Pcm $Output
} else {
    if (-not $Output) { $Output = Join-Path $PSScriptRoot $(if ($Sections) { 'out/sections' } else { 'out/localai-regression.csv' }) }
    $toolArgs = @('-cp', $classes, $dumpClass, '--regression', (Join-Path $PSScriptRoot 'raw'), $Output)
    if ($Baseline) { $toolArgs += $Baseline }
    & (Join-Path $jdkPath 'bin/java.exe') @toolArgs
}
if ($LASTEXITCODE -ne 0) { throw 'Offline inference regression/dump failed.' }
Write-Host "Saved: $Output"
