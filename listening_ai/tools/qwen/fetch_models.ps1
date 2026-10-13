# Fetches the pieces listening_ai needs to run Qwen locally.
#
# Two things are pulled, and nothing else:
#   * llama.cpp's Windows build - the runtime that turns a .gguf into a process
#     that can be handed a few seconds of audio. CUDA, because the machine has a
#     4070 in it; the same binary still runs on the CPU if the card is busy.
#   * Qwen2.5-Omni-7B in Q4_K_M plus its mmproj - the model and the encoder that
#     lets it hear. Omni rather than a text-only Qwen on purpose: what this
#     project measures is what music does, and a model that cannot hear the clip
#     can only talk about it.
#
# Everything lands outside the repo by default (this machine's C: has ~1.5 GB
# free, so nothing large may go there), and the paths are reported at the end so
# the rest of the tooling can be pointed at them.
#
# Re-running is safe: a file that is already there at the right size is left
# alone, and a file that stopped halfway is resumed rather than restarted.

param(
    [string]$Root = 'D:\qwen',
    [string]$LlamaTag = 'b11577',
    # 7B for the desk, 3B for the phone. The audio frontend is the same size
    # either way - the mmproj does not shrink with the language model - so the
    # choice is 6.2 GB against 3.5 GB, and 4.5 GB of weights against 2.0.
    [ValidateSet('3B', '7B')]
    [string]$Size = '7B',
    [string]$Quant = 'Q4_K_M',
    # windows: the desk, with CUDA. android: the phone itself, so the app can
    # reach the model at 127.0.0.1 and neither the desk nor the network is in
    # the path at all.
    [ValidateSet('windows', 'android')]
    [string]$Platform = 'windows'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$dirs = @(
    (Join-Path $Root 'bin'),
    (Join-Path $Root 'models'),
    (Join-Path $Root 'downloads')
)
if ($Platform -eq 'android') { $dirs += (Join-Path $Root 'android') }
foreach ($d in $dirs) { New-Item -ItemType Directory -Force -Path $d | Out-Null }

$big = "https://huggingface.co/ggml-org/Qwen2.5-Omni-$Size-GGUF/resolve/main"
$rel = "https://github.com/ggml-org/llama.cpp/releases/download/$LlamaTag"

if ($Platform -eq 'windows') {
    $runtime = @(
        @{ Url = "$rel/llama-$LlamaTag-bin-win-cuda-12.4-x64.zip"
           Out = (Join-Path $Root "downloads\llama-$LlamaTag-bin-win-cuda-12.4-x64.zip") },
        @{ Url = "$rel/cudart-llama-bin-win-cuda-12.4-x64.zip"
           Out = (Join-Path $Root 'downloads\cudart-llama-bin-win-cuda-12.4-x64.zip') }
    )
} else {
    # Not the -snapdragon build: this phone is a Dimensity 9400, so there is no
    # Adreno in it and no OpenCL path to take. The plain arm64 build runs on the
    # CPU, which is exactly the thing worth measuring before believing it.
    $runtime = @(
        @{ Url = "$rel/llama-$LlamaTag-bin-android-arm64.tar.gz"
           Out = (Join-Path $Root "downloads\llama-$LlamaTag-bin-android-arm64.tar.gz") }
    )
}

$items = $runtime + @(
    @{ Url = "$big/Qwen2.5-Omni-$Size-$Quant.gguf"
       Out = (Join-Path $Root "models\Qwen2.5-Omni-$Size-$Quant.gguf") },
    @{ Url = "$big/mmproj-Qwen2.5-Omni-$Size-Q8_0.gguf"
       Out = (Join-Path $Root "models\mmproj-Qwen2.5-Omni-$Size-Q8_0.gguf") }
)

foreach ($item in $items) {
    $name = Split-Path $item.Out -Leaf
    if (Test-Path $item.Out) { $have = (Get-Item $item.Out).Length } else { $have = 0 }
    Write-Host ("{0}  ({1:N0} bytes already here)" -f $name, $have)
    # -C - picks up where a previous run stopped; -L follows the release/HF
    # redirects, which is the whole reason curl is used instead of the cmdlets.
    & curl.exe -L --fail --retry 5 --retry-delay 3 -C - -o $item.Out $item.Url
    if ($LASTEXITCODE -ne 0) {
        # A resumed request against a server that will not do ranges fails; in
        # that case the partial file is not trustworthy and is started over.
        Write-Host "resume refused for $name, downloading from the start"
        Remove-Item -Force $item.Out
        & curl.exe -L --fail --retry 5 --retry-delay 3 -o $item.Out $item.Url
        if ($LASTEXITCODE -ne 0) { throw "could not download $($item.Url)" }
    }
    Write-Host ("done {0}: {1:N0} bytes" -f $name, (Get-Item $item.Out).Length)
}

Write-Host ''
Write-Host 'unpacking llama.cpp'
if ($Platform -eq 'windows') {
    $bin = Join-Path $Root 'bin'
    Expand-Archive -Force -Path (Join-Path $Root "downloads\llama-$LlamaTag-bin-win-cuda-12.4-x64.zip") -DestinationPath $bin
    # The cudart zip is the CUDA runtime DLLs llama.cpp's binary links against;
    # it unpacks into the same folder so the executable finds them beside itself.
    Expand-Archive -Force -Path (Join-Path $Root 'downloads\cudart-llama-bin-win-cuda-12.4-x64.zip') -DestinationPath $bin
    $server = Get-ChildItem -Path $bin -Recurse -Filter 'llama-mtmd-cli.exe' | Select-Object -First 1
} else {
    # tar.exe ships with Windows and handles .tar.gz; Expand-Archive does not.
    $bin = Join-Path $Root 'android'
    & tar.exe -xzf (Join-Path $Root "downloads\llama-$LlamaTag-bin-android-arm64.tar.gz") -C $bin
    if ($LASTEXITCODE -ne 0) { throw 'could not unpack the android build' }
    $server = Get-ChildItem -Path $bin -Recurse -Filter 'llama-mtmd-cli' | Select-Object -First 1
}
Write-Host ''
Write-Host ("root      {0}" -f $Root)
if ($server) { $serverPath = $server.FullName } else { $serverPath = 'NOT FOUND' }
Write-Host ("mtmd cli  {0}" -f $serverPath)
Write-Host ("model     {0}" -f (Join-Path $Root "models\Qwen2.5-Omni-$Size-$Quant.gguf"))
Write-Host ("mmproj    {0}" -f (Join-Path $Root "models\mmproj-Qwen2.5-Omni-$Size-Q8_0.gguf"))
