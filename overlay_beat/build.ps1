<#
  Builds the Attention Annotator APK without Android Studio.

  This machine already has everything it needs inside the Unity installation:
  a JDK 17, an Android SDK, and a full Gradle distribution under
  PlaybackEngines/AndroidPlayer/Tools/gradle. So the project is a normal Gradle
  project that happens to be launched through Unity's Gradle jars.

  Override any of these with environment variables if your paths differ:
    ATTENTION_JDK         JDK home (needs bin\java.exe)
    ATTENTION_SDK         Android SDK root (needs platforms\, build-tools\)
    ATTENTION_GRADLE_LIB  directory holding gradle-launcher-*.jar

  Examples:
    .\build.ps1                  # debug APK
    .\build.ps1 -Task test       # run the unit tests
    .\build.ps1 -Task assembleRelease

  Runs without a Gradle daemon by default. Pass -Daemon if you want one for
  repeated builds; note that a daemon inherits this script's output handles, so
  piping the script's output while a daemon is used can make the pipe hang.
#>
param(
    [string]$Task = "assembleDebug",
    [switch]$Daemon
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$unityAndroidDefault = "D:\unity\2022.3.62f3c1\Editor\Data\PlaybackEngines\AndroidPlayer"

function Resolve-FirstExisting([string[]]$Candidates, [string]$Probe) {
    foreach ($candidate in $Candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
        if (Test-Path (Join-Path $candidate $Probe)) { return (Resolve-Path $candidate).Path }
    }
    return $null
}

$jdk = Resolve-FirstExisting @($env:ATTENTION_JDK, $env:JAVA_HOME, "D:\unity\jdk-17") "bin\java.exe"
if (-not $jdk) {
    throw "No JDK found. Set ATTENTION_JDK to a JDK 17 home directory."
}

$sdk = Resolve-FirstExisting @($env:ATTENTION_SDK, $env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, (Join-Path $unityAndroidDefault "SDK")) "platforms"
if (-not $sdk) {
    throw "No Android SDK found. Set ATTENTION_SDK to an SDK root containing platforms\ and build-tools\."
}

$gradleLib = Resolve-FirstExisting @($env:ATTENTION_GRADLE_LIB, (Join-Path $unityAndroidDefault "Tools\gradle\lib")) "gradle-launcher-7.5.1.jar"
if (-not $gradleLib) {
    throw "No Gradle launcher found. Set ATTENTION_GRADLE_LIB to a directory containing gradle-launcher-*.jar."
}

Write-Host "jdk    : $jdk"
Write-Host "sdk    : $sdk"
Write-Host "gradle : $gradleLib"
Write-Host "task   : $Task"

# Android Studio reads this too, so the project opens normally as well.
$localProperties = Join-Path $root "local.properties"
"sdk.dir=$($sdk -replace '\\','\\')" | Set-Content -Path $localProperties -Encoding ASCII

$env:ANDROID_HOME = $sdk
$env:ANDROID_SDK_ROOT = $sdk
# This machine runs a Chinese locale, which makes javac print GBK diagnostics
# that turn into mojibake anywhere UTF-8 is assumed. Ask every forked JVM for
# English so a compiler error stays readable.
$env:JAVA_TOOL_OPTIONS = "-Duser.language=en -Duser.country=US"

$arguments = @(
    "-cp", "$gradleLib\*",
    "org.gradle.launcher.GradleMain",
    $Task,
    "--project-dir", $root,
    "-Dorg.gradle.java.home=$jdk"
)
if (-not $Daemon) { $arguments += "--no-daemon" }

& (Join-Path $jdk "bin\java.exe") @arguments
$code = $LASTEXITCODE

if ($code -eq 0 -and $Task -like "assemble*") {
    $apks = Get-ChildItem -Path (Join-Path $root "app\build\outputs\apk") -Recurse -Filter *.apk -ErrorAction SilentlyContinue
    foreach ($apk in $apks) {
        Write-Host ("apk    : {0}  ({1:N1} MB)" -f $apk.FullName, ($apk.Length / 1MB))
    }
}

exit $code
