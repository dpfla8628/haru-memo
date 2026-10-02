$ErrorActionPreference = 'Stop'
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compilerPath = Join-Path $frameworkDir 'csc.exe'
$wpfDir = Join-Path $frameworkDir 'WPF'
Push-Location $PSScriptRoot
try {
    Add-Type -AssemblyName System.Drawing
    $bitmap = New-Object System.Drawing.Bitmap 64,64
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $paperBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#FFF2BB'))
    $shadowBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#D7C777'))
    $inkPen = New-Object System.Drawing.Pen ([System.Drawing.ColorTranslator]::FromHtml('#404A38')),3
    $linePen = New-Object System.Drawing.Pen ([System.Drawing.ColorTranslator]::FromHtml('#B3A772')),2
    $graphics.FillRectangle($shadowBrush,8,8,48,52)
    $graphics.FillRectangle($paperBrush,6,5,48,52)
    $graphics.FillPolygon($shadowBrush,[System.Drawing.Point[]]@([System.Drawing.Point]::new(42,5),[System.Drawing.Point]::new(54,17),[System.Drawing.Point]::new(42,17)))
    foreach($row in @(26,39)) {
        $graphics.DrawLines($inkPen,[System.Drawing.Point[]]@([System.Drawing.Point]::new(13,$row),[System.Drawing.Point]::new(17,$row+4),[System.Drawing.Point]::new(23,$row-4)))
        $graphics.DrawLine($linePen,29,$row,45,$row)
    }
    $nativeIcon = [System.Drawing.Icon]::FromHandle($bitmap.GetHicon())
    $iconStream = [System.IO.File]::Create((Join-Path $PSScriptRoot 'Note.ico'))
    $nativeIcon.Save($iconStream)
    $iconStream.Dispose()
    $nativeIcon.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
    $paperBrush.Dispose()
    $shadowBrush.Dispose()
    $inkPen.Dispose()
    $linePen.Dispose()
    $compileArgs = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/utf8output','/out:HaruMemo.exe',
        '/win32manifest:app.manifest','/win32icon:Note.ico','/resource:Main.xaml,Main.xaml','/resource:Note.ico,Note.ico',
        '/reference:System.dll','/reference:System.Core.dll','/reference:System.Xaml.dll','/reference:System.Web.Extensions.dll','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll',
        "/reference:$wpfDir\WindowsBase.dll", "/reference:$wpfDir\PresentationCore.dll", "/reference:$wpfDir\PresentationFramework.dll",'StickyTodo.cs')
    & $compilerPath @compileArgs
    if($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    Get-Item -LiteralPath 'HaruMemo.exe' | Select-Object FullName,Length,LastWriteTime
} finally { Pop-Location }
