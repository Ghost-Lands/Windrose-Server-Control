$dir = $PSScriptRoot
$dst = Join-Path $dir "windrose.ico"

Add-Type -AssemblyName System.Drawing

$png = Join-Path $dir "windrose.png"
if (Test-Path $png) {
    try {
        $sizes = @(256, 64, 48, 32, 16)
        $ms = New-Object System.IO.MemoryStream
        $bw = New-Object System.IO.BinaryWriter($ms)

        $bw.Write([uint16]0)
        $bw.Write([uint16]1)
        $bw.Write([uint16]$sizes.Count)

        $imageData = @()
        foreach ($sz in $sizes) {
            $bmp   = [System.Drawing.Image]::FromFile($png)
            $thumb = $bmp.GetThumbnailImage($sz, $sz, $null, [IntPtr]::Zero)
            $imgMs = New-Object System.IO.MemoryStream
            $thumb.Save($imgMs, [System.Drawing.Imaging.ImageFormat]::Png)
            $imageData += ,$imgMs.ToArray()
            $bmp.Dispose(); $thumb.Dispose(); $imgMs.Dispose()
        }

        $offset = 6 + ($sizes.Count * 16)
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $sz = $sizes[$i]
            if ($sz -eq 256) { $w = 0 } else { $w = $sz }
            $bw.Write([byte]$w)
            $bw.Write([byte]$w)
            $bw.Write([byte]0)
            $bw.Write([byte]0)
            $bw.Write([uint16]1)
            $bw.Write([uint16]32)
            $bw.Write([uint32]$imageData[$i].Length)
            $bw.Write([uint32]$offset)
            $offset += $imageData[$i].Length
        }
        foreach ($data in $imageData) { $bw.Write($data) }
        [System.IO.File]::WriteAllBytes($dst, $ms.ToArray())
        Write-Host "Icon created from windrose.png"
        exit 0
    } catch {
        Write-Host "PNG conversion failed: $_"
    }
}

# Steam cache fallback
$sources = @(
    "C:\Program Files (x86)\Steam\appcache\librarycache\3041230_icon.jpg",
    "C:\Program Files (x86)\Steam\appcache\librarycache\3041230_header.jpg"
)
foreach ($src in $sources) {
    if (Test-Path $src) {
        try {
            $bmp   = [System.Drawing.Image]::FromFile($src)
            $thumb = $bmp.GetThumbnailImage(32, 32, $null, [IntPtr]::Zero)
            $icon  = [System.Drawing.Icon]::FromHandle($thumb.GetHicon())
            $fs    = [System.IO.File]::OpenWrite($dst)
            $icon.Save($fs); $fs.Close()
            Write-Host "Icon from Steam cache: $src"
            exit 0
        } catch { }
    }
}

Write-Host "No icon found - using default."
exit 1
