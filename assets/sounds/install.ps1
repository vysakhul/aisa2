# Copies the WAV files in this folder into Aisa's runtime sound folder.
# Usage:
#   .\install.ps1          install WAV files
#   .\install.ps1 -Clear   remove installed sounds; cues will be silent
# Restart Aisa afterwards; SoundService caches loaded files per process.
param([switch]$Clear)

$known = @(
    'Peek', 'Open', 'Close', 'Send', 'Finish', 'Blip', 'Error', 'Proud', 'Pop', 'Hover',
    'Love', 'Slap', 'Annoyed', 'Dizzy', 'Happy', 'Greeting',
    'AlarmLoop', 'ReminderChime'
)

$dest = Join-Path $env:APPDATA 'Aisa\sounds'
New-Item -ItemType Directory -Force $dest | Out-Null

if ($Clear) {
    Get-ChildItem $dest -Filter *.wav -File -ErrorAction SilentlyContinue | Remove-Item -Force
    Write-Host "Removed WAV files from $dest. Cues without files will be silent. Restart Aisa."
    exit 0
}

$files = @(Get-ChildItem $PSScriptRoot -Filter *.wav -File)
if ($files.Count -eq 0) {
    Write-Host "No .wav files in $PSScriptRoot."
    exit 0
}

foreach ($f in $files | Where-Object { $known -notcontains $_.BaseName }) {
    Write-Warning "'$($f.Name)' does not match a cue name and will be ignored by Aisa."
}

$valid = @($files | Where-Object { $known -contains $_.BaseName })
$valid | Copy-Item -Destination $dest -Force
Write-Host "Copied $($valid.Count) of $($files.Count) file(s) to $dest."

$missing = @($known | Where-Object { -not (Test-Path (Join-Path $dest "$_.wav")) })
if ($missing.Count -gt 0) {
    Write-Host "No sound will play for missing cues: $($missing -join ', ')"
}
Write-Host "Restart Aisa to hear changes."
