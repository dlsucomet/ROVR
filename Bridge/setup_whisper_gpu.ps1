# Bridge/setup_whisper_gpu.ps1 (run it with setup_whisper_gpu.bat)
# Sets up speech to text on this PC's NVIDIA GPU for the bridge, once per PC:
#   1. whisper.cpp's CUDA build into Bridge\whisper\cuda-12.4\ (about 650 MB from GitHub), unless a CUDA
#      build is already there;
#   2. the small.en model, Bridge\whisper\ggml-small.en.bin (about 470 MB from Hugging Face), unless it's there;
#   3. a warm-up: starts the Whisper server on a spare port and transcribes a short test sentence (made with
#      Windows' own text to speech), twice.
# The warm-up matters on RTX 50-series cards: the CUDA 12.4 build has no kernels made for them, so the
# driver compiles its own the first time Whisper runs (it can take a minute or more) and keeps them in its
# cache. Done here, a participant's first sentence never waits for it. Run this again after a driver update.
# None of it goes into git (Bridge\whisper\ is ignored). At the end it says whether Whisper ran on the GPU.
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"   # Invoke-WebRequest is many times faster without its progress bar

$bridge = $PSScriptRoot
$dir = Join-Path $bridge "whisper"
$cuda = Join-Path $dir "cuda-12.4"
$model = Join-Path $dir "ggml-small.en.bin"
$zipUrl = "https://github.com/ggml-org/whisper.cpp/releases/download/b5454/whisper-bin-win-cuda-12.4.0-x64.zip"
$modelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.en.bin"
$port = 8799

function Say($text, $colour = "Gray") { Write-Host $text -ForegroundColor $colour }

Say "ROVR: Whisper on the GPU" "Cyan"
New-Item -ItemType Directory -Force $dir | Out-Null

# ---- the GPU
$smi = Get-Command nvidia-smi -ErrorAction SilentlyContinue
if ($smi) {
    $gpu = (& nvidia-smi --query-gpu=name,driver_version,memory.total --format=csv,noheader) -join "; "
    Say "GPU: $gpu"
} else {
    Say "No nvidia-smi: is the NVIDIA driver installed? Whisper will run on the CPU (slower)." "Yellow"
}

# ---- 1. the CUDA build
$server = Get-ChildItem $dir -Recurse -Filter whisper-server.exe -ErrorAction SilentlyContinue |
          Where-Object { Test-Path (Join-Path $_.DirectoryName "ggml-cuda.dll") } | Select-Object -First 1
if ($server) {
    Say "CUDA build: already there ($($server.FullName))"
} else {
    $zip = Join-Path $dir "whisper-bin-win-cuda-12.4.0-x64.zip"
    if (-not (Test-Path $zip) -or (Get-Item $zip).Length -lt 600MB) {
        Say "Downloading whisper.cpp's CUDA build (about 650 MB)..."
        Invoke-WebRequest -Uri $zipUrl -OutFile $zip -UseBasicParsing
    }
    Say "Unzipping it into $cuda..."
    Expand-Archive -Path $zip -DestinationPath $cuda -Force
    $server = Get-ChildItem $cuda -Recurse -Filter whisper-server.exe | Select-Object -First 1
    if (-not $server) { throw "No whisper-server.exe in the CUDA build" }
}

# ---- 2. the model
if ((Test-Path $model) -and (Get-Item $model).Length -gt 400MB) {
    Say "Model: already there ($model)"
} else {
    Say "Downloading the small.en model (about 470 MB)..."
    Invoke-WebRequest -Uri $modelUrl -OutFile $model -UseBasicParsing
}

# ---- 3. the warm-up
$wav = Join-Path $env:TEMP "rovr_whisper_test.wav"
Add-Type -AssemblyName System.Speech
$format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(16000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
$voice = New-Object System.Speech.Synthesis.SpeechSynthesizer
$voice.SetOutputToWaveFile($wav, $format)
$voice.Speak("Move forward, then go to the bookshelf.")
$voice.Dispose()

$log = Join-Path $env:TEMP "rovr_whisper_setup.txt"
Say "Starting the Whisper server on port $port for the warm-up..."
$proc = Start-Process -FilePath $server.FullName -WorkingDirectory $server.DirectoryName -NoNewWindow -PassThru `
        -ArgumentList @("-m", "`"$model`"", "-l", "en", "-nt", "--host", "127.0.0.1", "--port", "$port") `
        -RedirectStandardError $log -RedirectStandardOutput "$log.out"
try {
    $ready = $false
    for ($i = 0; $i -lt 240 -and -not $ready; $i++) {
        Start-Sleep -Milliseconds 500
        if ($proc.HasExited) { throw "The Whisper server stopped at start (exit code $($proc.ExitCode)). See $log" }
        & curl.exe -s -m 2 -o NUL "http://127.0.0.1:$port/" 2>$null
        $ready = ($LASTEXITCODE -eq 0)
    }
    if (-not $ready) { throw "The Whisper server didn't start within 2 minutes. See $log" }

    $startup = (Get-Content $log -Raw) + (Get-Content "$log.out" -Raw -ErrorAction SilentlyContinue)
    $onGpu = $startup -match "using (CUDA|Vulkan)\d* backend"
    if ($onGpu) { Say "Whisper loaded on the GPU." "Green" } else { Say "Whisper loaded on the CPU, not the GPU (see $log)." "Yellow" }

    foreach ($pass in 1, 2) {
        if ($pass -eq 1) { Say "First transcription: on an RTX 50-series card the driver compiles Whisper's GPU code now; this can take a few minutes..." }
        $watch = [System.Diagnostics.Stopwatch]::StartNew()
        $reply = & curl.exe -s -m 900 -F "file=@$wav" -F "response_format=json" -F "temperature=0.0" "http://127.0.0.1:$port/inference"
        $watch.Stop()
        if ($LASTEXITCODE -ne 0 -or $proc.HasExited) { throw "Transcription $pass failed (the server stopped?). See $log" }
        $text = (($reply -join "") | ConvertFrom-Json).text.Trim()
        Say ("Transcription {0}: {1:N2} s  ""{2}""" -f $pass, $watch.Elapsed.TotalSeconds, $text)
    }
} finally {
    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
    Remove-Item $wav -ErrorAction SilentlyContinue
}

Say ""
if ($onGpu) {
    Say "Done. The bridge will use the CUDA build and small.en: start it as usual (run_bridge.bat); it should say" "Green"
    Say "  Speech to text: whisper.cpp on this PC, model ggml-small.en.bin, on the GPU (...)" "Green"
    Say "and the control page says the same. The second transcription above is what a sentence takes now." "Green"
} else {
    Say "Done, but Whisper is on the CPU: check the NVIDIA driver (CUDA 12.4 needs driver 551 or newer), then run this again." "Yellow"
}
