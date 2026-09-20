# Regenerates the tiny synthetic MP4 fixtures used by playback-window tests
# under tests/GuluPet.Tests/TestMedia. These are solid-color clips (~1.5 KB
# each, 64x96 or 96x64, 0.5 s, H.264/yuv420p) and contain no private media.
#
# Requires an ffmpeg binary. The easiest dependency-free source on Windows:
#   python -m pip install imageio-ffmpeg
#   python -c "import imageio_ffmpeg; print(imageio_ffmpeg.get_ffmpeg_exe())"
# Or set $Ffmpeg to any ffmpeg executable.

param(
    [string]$Ffmpeg = "ffmpeg",
    [string]$OutDir = (Join-Path $PSScriptRoot "..\tests\GuluPet.Tests\TestMedia")
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function New-TestClip {
    param([string]$Name, [string]$Color, [int]$Width, [int]$Height)
    $target = Join-Path $OutDir $Name
    & $Ffmpeg -y -loglevel error `
        -f lavfi -i "color=c=${Color}:s=${Width}x${Height}:r=8:d=0.5" `
        -c:v libx264 -preset ultrafast -crf 45 -pix_fmt yuv420p `
        -movflags +faststart $target
    Write-Host "generated $target"
}

# Portrait and landscape fixtures used by mixed-orientation playback tests.
New-TestClip "memory-01.mp4" "0x2D6A4F" 64 96
New-TestClip "memory-02.mp4" "0x40916C" 96 64

# Nine-segment sequence for the memory-08 mixed-orientation test.
New-TestClip "memory-08-01.mp4" "0x52B788" 64 96
New-TestClip "memory-08-02.mp4" "0x52B788" 64 96
New-TestClip "memory-08-03.mp4" "0x52B788" 64 96
New-TestClip "memory-08-04.mp4" "0x52B788" 64 96
New-TestClip "memory-08-05.mp4" "0x74C69D" 96 64
New-TestClip "memory-08-06.mp4" "0x95D5B2" 64 96
New-TestClip "memory-08-07.mp4" "0xB7E4C7" 64 96
New-TestClip "memory-08-08.mp4" "0xD8F3DC" 96 64
New-TestClip "memory-08-09.mp4" "0x1B4332" 64 96
