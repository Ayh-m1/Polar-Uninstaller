# Preserve source artwork. Enlarge only Windows-icon composition as requested;
# the original transparent UI logo files remain byte-for-byte unchanged.
param([string]$SourceRoot=(Join-Path $PSScriptRoot '../../Logo-Candidates/Polar_Icons'),
      [string]$ProjectRoot=(Join-Path $PSScriptRoot '..'))
$ErrorActionPreference='Stop'
$assets=[IO.Path]::GetFullPath((Join-Path $ProjectRoot 'Assets'))
$branding=Join-Path $assets 'Branding'
New-Item -ItemType Directory -Path $branding -Force|Out-Null
Copy-Item -LiteralPath (Join-Path $SourceRoot 'ui_logo_white/PU_white_flat_128.png') -Destination (Join-Path $assets 'PolarLogo.png') -Force
foreach($color in @('white','black')) {
 foreach($item in @(@('flat',24),@('flat',64),@('shadow',128))) {
  $name="PU_${color}_$($item[0])_$($item[1]).png"
  Copy-Item -LiteralPath (Join-Path $SourceRoot "ui_logo_$color/$name") -Destination (Join-Path $branding $name) -Force
 }
}
Add-Type -AssemblyName PresentationCore,PresentationFramework,WindowsBase
$source=[IO.Path]::GetFullPath((Join-Path $SourceRoot 'ui_logo_white/PU_white_flat_512.png'))
$decoder=[Windows.Media.Imaging.BitmapDecoder]::Create([Uri]::new($source),[Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,[Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
$art=$decoder.Frames[0]
$scan=[Windows.Media.Imaging.FormatConvertedBitmap]::new($art,[Windows.Media.PixelFormats]::Bgra32,$null,0)
$w=$scan.PixelWidth;$h=$scan.PixelHeight;$pixels=[byte[]]::new($w*$h*4);$scan.CopyPixels($pixels,$w*4,0)
$left=$w;$top=$h;$right=0;$bottom=0
for($y=0;$y -lt $h;$y++){for($x=0;$x -lt $w;$x++){
 if($pixels[($y*$w+$x)*4+3] -gt 4){$left=[Math]::Min($left,$x);$right=[Math]::Max($right,$x);$top=[Math]::Min($top,$y);$bottom=[Math]::Max($bottom,$y)}
}}
$glyph=[Windows.Media.Imaging.CroppedBitmap]::new($art,[Windows.Int32Rect]::new($left,$top,$right-$left+1,$bottom-$top+1))
$background=[Windows.Media.LinearGradientBrush]::new([Windows.Media.ColorConverter]::ConvertFromString('#364555'),[Windows.Media.ColorConverter]::ConvertFromString('#1A232C'),45)
$payloads=@()
foreach($size in @(16,20,24,30,32,36,40,48,64,128,256)) {
 $visual=[Windows.Media.DrawingVisual]::new();$drawing=$visual.RenderOpen()
 [Windows.Media.RenderOptions]::SetBitmapScalingMode($visual,[Windows.Media.BitmapScalingMode]::HighQuality)
 # Compose once from the original flat letterforms: no enlarged low-resolution
 # screenshot, compounded resampling, or heavy black shadow at small sizes.
 $drawing.DrawRoundedRectangle($background,$null,[Windows.Rect]::new(0,0,$size,$size),$size*.18,$size*.18)
 $scale=[Math]::Min($size*.60/$glyph.PixelWidth,$size*.68/$glyph.PixelHeight)
 $gw=$glyph.PixelWidth*$scale;$gh=$glyph.PixelHeight*$scale
 $drawing.DrawImage($glyph,[Windows.Rect]::new(($size-$gw)/2,($size-$gh)/2,$gw,$gh));$drawing.Close()
 $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new($size,$size,96,96,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($visual)
 $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
 $memory=[IO.MemoryStream]::new();try{$encoder.Save($memory);$bytes=$memory.ToArray()}finally{$memory.Dispose()}
 $payloads+=,@{Size=$size;Bytes=$bytes}
 if($size -eq 256){[IO.File]::WriteAllBytes((Join-Path $assets 'PolarIcon.png'),$bytes)}
}
$stream=[IO.File]::Create((Join-Path $assets 'Polar.ico'));$writer=[IO.BinaryWriter]::new($stream)
try {
 $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$payloads.Count)
 $offset=6+16*$payloads.Count
 foreach($item in $payloads){
  $dimension=if($item.Size -eq 256){0}else{$item.Size}
  $writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([byte]0);$writer.Write([byte]0)
  $writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$item.Bytes.Length);$writer.Write([uint32]$offset)
  $offset+=$item.Bytes.Length
 }
 foreach($item in $payloads){$writer.Write([byte[]]$item.Bytes)}
}finally{$writer.Dispose()}
# MUI2 requires BMP wizard panels, using the same enlarged icon composition.
foreach($layout in @(@('InstallerWelcome.bmp',164,314,128),@('InstallerHeader.bmp',150,57,40))) {
 $width=[int]$layout[1];$height=[int]$layout[2];$size=[int]$layout[3]
 $source=Join-Path $assets 'PolarIcon.png'
 $decoder=[Windows.Media.Imaging.BitmapDecoder]::Create([Uri]::new($source),[Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,[Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
 $visual=[Windows.Media.DrawingVisual]::new();$drawing=$visual.RenderOpen()
 $drawing.DrawRectangle([Windows.Media.SolidColorBrush]::new([Windows.Media.ColorConverter]::ConvertFromString('#0F1B2D')),$null,[Windows.Rect]::new(0,0,$width,$height))
 $drawing.DrawImage($decoder.Frames[0],[Windows.Rect]::new([Math]::Floor(($width-$size)/2),[Math]::Floor(($height-$size)/2),$size,$size));$drawing.Close()
 $render=[Windows.Media.Imaging.RenderTargetBitmap]::new($width,$height,96,96,[Windows.Media.PixelFormats]::Pbgra32);$render.Render($visual)
 $bgr=[Windows.Media.Imaging.FormatConvertedBitmap]::new($render,[Windows.Media.PixelFormats]::Bgr24,$null,0)
 $encoder=[Windows.Media.Imaging.BmpBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bgr))
 $stream=[IO.File]::Create((Join-Path $assets $layout[0]));try{$encoder.Save($stream)}finally{$stream.Dispose()}
}
'Built clearer, larger PU icon frames directly from flat source letterforms; original source files preserved.'
