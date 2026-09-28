Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression

$root = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $PSScriptRoot 'assets\OpenAI-white-monoblossom.png'
$iconPath = Join-Path $root 'package\metadata\Icon256x256.png'
$profilePath = Join-Path $root 'package\profiles\DefaultProfile70.lp5'
$temporaryPath = "$iconPath.tmp.png"

$source = [System.Drawing.Bitmap]::FromFile($sourcePath)
$output = New-Object System.Drawing.Bitmap(
    $source.Width,
    $source.Height,
    [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
)
$graphics = [System.Drawing.Graphics]::FromImage($output)
$badgePath = New-Object System.Drawing.Drawing2D.GraphicsPath
$badgePath.AddArc(34, 34, 58, 58, 180, 90)
$badgePath.AddArc(164, 34, 58, 58, 270, 90)
$badgePath.AddArc(164, 164, 58, 58, 0, 90)
$badgePath.AddArc(34, 164, 58, 58, 90, 90)
$badgePath.CloseFigure()
$badgeBrush = New-Object System.Drawing.SolidBrush(
    [System.Drawing.ColorTranslator]::FromHtml('#081c05')
)
$glareBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Rectangle(0, 0, 196, 142)),
    [System.Drawing.Color]::FromArgb(38, 195, 224, 190),
    [System.Drawing.Color]::FromArgb(0, 195, 224, 190),
    42
)
$edgePen = New-Object System.Drawing.Pen(
    [System.Drawing.Color]::FromArgb(150, 62, 105, 57),
    3
)
$shadowAttributes = New-Object System.Drawing.Imaging.ImageAttributes
$shadowMatrix = New-Object System.Drawing.Imaging.ColorMatrix
$shadowMatrix.Matrix00 = 0
$shadowMatrix.Matrix11 = 0
$shadowMatrix.Matrix22 = 0
$shadowMatrix.Matrix33 = 0.48
$shadowAttributes.SetColorMatrix($shadowMatrix)

try {
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    $graphics.FillPath($badgeBrush, $badgePath)
    $graphics.SetClip($badgePath)
    $graphics.FillEllipse($glareBrush, 6, 4, 190, 144)
    $graphics.ResetClip()
    $graphics.DrawPath($edgePen, $badgePath)

    $shadowDestination = New-Object System.Drawing.Rectangle(40, 43, 176, 176)
    $graphics.DrawImage(
        $source,
        $shadowDestination,
        0,
        0,
        $source.Width,
        $source.Height,
        [System.Drawing.GraphicsUnit]::Pixel,
        $shadowAttributes
    )
    $graphics.DrawImage($source, (New-Object System.Drawing.Rectangle(38, 38, 176, 176)))
    $output.Save($temporaryPath, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $shadowAttributes.Dispose()
    $edgePen.Dispose()
    $glareBrush.Dispose()
    $badgeBrush.Dispose()
    $badgePath.Dispose()
    $graphics.Dispose()
    $output.Dispose()
    $source.Dispose()
}

Move-Item -LiteralPath $temporaryPath -Destination $iconPath -Force

$stream = [System.IO.File]::Open(
    $profilePath,
    [System.IO.FileMode]::Open,
    [System.IO.FileAccess]::ReadWrite,
    [System.IO.FileShare]::None
)
$archive = New-Object System.IO.Compression.ZipArchive(
    $stream,
    [System.IO.Compression.ZipArchiveMode]::Update,
    $false
)

try {
    $existingEntry = $archive.GetEntry('ApplicationIcon.png')
    if ($null -ne $existingEntry) {
        $existingEntry.Delete()
    }

    $newEntry = $archive.CreateEntry(
        'ApplicationIcon.png',
        [System.IO.Compression.CompressionLevel]::Optimal
    )
    $entryStream = $newEntry.Open()
    $iconStream = [System.IO.File]::OpenRead($iconPath)
    try {
        $iconStream.CopyTo($entryStream)
    }
    finally {
        $iconStream.Dispose()
        $entryStream.Dispose()
    }
}
finally {
    $archive.Dispose()
    $stream.Dispose()
}

Write-Output 'Generated dark-green Codex Desktop badge and updated the embedded profile icon.'
