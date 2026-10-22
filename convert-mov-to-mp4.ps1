$ErrorActionPreference = 'Stop'

$videoFolder = 'C:\Users\mesek\Github\xampp\htdocs\Maui-app\MyTube\Resources\Raw\videos'

if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) {
    throw 'ffmpeg was not found in PATH. Install ffmpeg or add its bin folder to PATH.'
}

if (-not (Test-Path -LiteralPath $videoFolder -PathType Container)) {
    throw "Video folder not found: $videoFolder"
}

$files = Get-ChildItem -LiteralPath $videoFolder -File |
    Where-Object { $_.Extension -ieq '.mov' } |
    Sort-Object Name

if ($files.Count -eq 0) {
    Write-Host "No MOV files found in $videoFolder"
    exit 0
}

function Get-NormalizedStem([string]$name) {
    $stem = [IO.Path]::GetFileNameWithoutExtension($name)
    $stem = $stem -replace '\s*\(\d+\)$', ''
    $stem = $stem -replace '-converted$', ''
    return $stem.Trim().ToLowerInvariant()
}

$seen = @{}
$existingMp4 = Get-ChildItem -LiteralPath $videoFolder -File |
    Where-Object { $_.Extension -ieq '.mp4' } |
    ForEach-Object { Get-NormalizedStem $_.Name }

foreach ($file in $files) {
    $normalizedStem = Get-NormalizedStem $file.Name
    if ($seen.ContainsKey($normalizedStem) -or $existingMp4 -contains $normalizedStem) {
        Write-Host "Skipping duplicate: $($file.Name)"
        continue
    }
    $seen[$normalizedStem] = $true

    $output = Join-Path $file.DirectoryName ($file.BaseName + '-converted.mp4')

    if (Test-Path -LiteralPath $output) {
        Write-Host "Skipping existing file: $output"
        continue
    }

    Write-Host "Converting: $($file.Name) -> $(Split-Path $output -Leaf)"

    & ffmpeg -hide_banner -y `
        -i $file.FullName `
        -vf "scale='min(1280,iw)':-2:force_original_aspect_ratio=decrease,format=yuv420p" `
        -c:v libx264 -profile:v main -level 4.0 -preset fast -crf 23 `
        -c:a aac -b:a 128k `
        -movflags +faststart `
        $output

    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $output)) {
        Write-Warning "Conversion failed: $($file.Name)"
    } else {
        Remove-Item -LiteralPath $file.FullName -Force
        Write-Host "Deleted original: $($file.Name)"
    }
}

Write-Host 'Conversion finished.'
