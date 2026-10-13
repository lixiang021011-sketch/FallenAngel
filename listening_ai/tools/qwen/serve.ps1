# Serves the local Qwen to the phone over the API the app already speaks.
#
# The app's explanation path (clip/ExplanationClient.java) posts an
# OpenAI-shaped chat request to whatever endpoint is typed into its settings,
# with the audio attached as an `input_audio` part. llama-server accepts exactly
# that shape, so this script is the whole of the wiring: run it, put the address
# it prints into the app, and the cloud call is a local call.
#
# That also means nothing has to be rebuilt or reinstalled to try this, and
# turning the server off returns the app to its own rules rather than breaking
# it - the fallback is the path that was there before.
#
#   .\serve.ps1                 # background, logs to D:\qwen\server.log
#   .\serve.ps1 -Foreground     # same thing, with the log on screen
#   .\serve.ps1 -Stop           # give the GPU back
#   .\serve.ps1 -Port 8081 -Ctx 8192

param(
    [string]$Root = 'D:\qwen',
    [int]$Port = 8080,
    [int]$Ctx = 4096,
    [int]$GpuLayers = 99,
    # Which size to serve. The 7B answers better and needs about 6.2 GB of the
    # card; on a desktop that is already using the rest, Windows starts paging
    # video memory and the model drops from 51 tokens a second to well under one -
    # measured, not guessed. The 3B needs 3.5 GB, which is the one to fall back to
    # when the machine has other things to draw.
    [ValidateSet('auto', '3B', '7B')]
    [string]$Size = 'auto',
    [string]$Quant = 'Q4_K_M',
    [string]$ApiKey = 'local',
    [switch]$Foreground,
    [switch]$Stop
)

$ErrorActionPreference = 'Stop'

# The model sits in 6.3 GB of the card's 8 GB, which is worth having back the
# moment nobody is listening - so stopping is one word rather than a hunt
# through the task manager.
if ($Stop) {
    $running = Get-Process llama-server -ErrorAction SilentlyContinue
    if (-not $running) { Write-Host 'no llama-server is running'; exit 0 }
    $running | Stop-Process -Force
    Write-Host ("stopped {0} process(es); the card is free" -f $running.Count)
    exit 0
}

# Already up? Then this copy is a second one - a logon landing on a server that
# never went away, or a double click on the startup shortcut - and starting
# another would fight for the port and for the card. Silence is the right
# answer here; the caller wanted a server, and there is one.
$already = $false
try {
    $reply = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/health" -UseBasicParsing -TimeoutSec 3
    if ($reply.StatusCode -eq 200) { $already = $true }
} catch { }
if ($already) {
    Write-Host "a model is already answering on port $Port; leaving it alone"
    exit 0
}

# Which size, decided by what the card has free rather than by which answers
# better. The card is shared with whatever else is open on this desk, and when
# it runs out of room Windows starts paging video memory - at which point the
# model keeps answering, at a quarter of a token a second, and the phone gives up
# long before it does. Measured 2026-10-13: with 7.7 GB of 8 GB in use, the 7B
# went from 51 tokens a second to 0.25 and a question that took 1.6 seconds never
# finished in 180. Nothing said why; the fix was a smaller model.
if ($Size -eq 'auto') {
    $free = $null
    try {
        $free = [int](& nvidia-smi --query-gpu=memory.free --format=csv,noheader,nounits | Select-Object -First 1)
    } catch { }
    if ($free -eq $null) {
        $Size = '3B'
        Write-Host 'could not read the card; serving the 3B, which fits in more cases'
    } elseif ($free -ge 7600) {
        $Size = '7B'
        Write-Host ("card has {0} MiB free; serving the 7B" -f $free)
    } else {
        $Size = '3B'
        Write-Host ("card has {0} MiB free; serving the 3B so the answer does not stall" -f $free)
    }
}

$server = Join-Path $Root 'bin\llama-server.exe'
$model = Join-Path $Root "models\Qwen2.5-Omni-$Size-$Quant.gguf"
$mmproj = Join-Path $Root "models\mmproj-Qwen2.5-Omni-$Size-Q8_0.gguf"
foreach ($f in @($server, $model, $mmproj)) {
    if (-not (Test-Path $f)) { throw "missing $f - run fetch_models.ps1 first" }
}

$arguments = @(
    '-m', $model,
    '--mmproj', $mmproj,
    '--host', '0.0.0.0',
    '--port', "$Port",
    '-ngl', "$GpuLayers",
    '-c', "$Ctx",
    '-fa', 'on',              # flash attention: the KV cache is the thing that
                              # would not fit beside a 7B model on 8 GB
    '-n', '250',              # an explanation that needs more than this is not
                              # the answer we asked for; and the cap is what keeps
                              # the wait inside what the app will sit through
    '--jinja',                # the model's own chat template
    # Bound to every interface so the phone can reach it, which also means the
    # rest of the network can: the key is what keeps this from being an open
    # endpoint on the LAN, and it is the same string the app sends as its key.
    '--api-key', $ApiKey,
    '--alias', "qwen2.5-omni-$Size"
)

# The address the phone has to be given. Printed rather than assumed: a laptop
# with a VPN or a second adapter has more than one, and the wrong one looks
# exactly like a server that is not running.
$lan = Get-NetIPAddress -AddressFamily IPv4 |
    Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
    Sort-Object -Property InterfaceMetric |
    Select-Object -First 1
$ip = if ($lan) { $lan.IPAddress } else { '<this machine>' }

Write-Host "model    $model"
Write-Host "listens  http://0.0.0.0:$Port"
Write-Host "for the app's endpoint field:  http://${ip}:$Port/v1/chat/completions"
Write-Host "for the app's key field:       $ApiKey"
Write-Host ''

if ($Foreground) {
    & $server @arguments
    exit $LASTEXITCODE
}

$log = Join-Path $Root 'server.log'
$err = Join-Path $Root 'server.err.log'
$process = Start-Process -FilePath $server -ArgumentList $arguments -WindowStyle Hidden `
    -RedirectStandardOutput $log -RedirectStandardError $err -PassThru
Write-Host "started pid $($process.Id); log: $log"
Start-Sleep -Seconds 3
Get-Content $err -ErrorAction SilentlyContinue | Select-Object -Last 6
