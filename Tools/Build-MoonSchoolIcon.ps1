$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$path=Join-Path $PSScriptRoot '../GameResources/ui/Icons/sakuga/iconSchoolHuyenNguyet.png'
$bitmap=New-Object Drawing.Bitmap 32,32
try {
    for($y=0;$y -lt 32;$y++) {
        for($x=0;$x -lt 32;$x++) {
            $dx=$x-15.5; $dy=$y-15.5; $r=$dx*$dx+$dy*$dy
            if($r -gt 225){continue}
            $color=[Drawing.Color]::FromArgb(255,17,30,46)
            if($r -gt 190){$color=[Drawing.Color]::FromArgb(255,83,132,170)}
            $moon=($x-14)*($x-14)+($y-15)*($y-15) -le 90
            $cut=($x-19)*($x-19)+($y-11)*($y-11) -le 81
            if($moon -and !$cut){$color=[Drawing.Color]::FromArgb(255,205,233,252); if($y -lt 16){$color=[Drawing.Color]::FromArgb(255,245,251,255)}}
            if(($x -eq 23 -and $y -ge 9 -and $y -le 13) -or ($y -eq 11 -and $x -ge 21 -and $x -le 25)){$color=[Drawing.Color]::White}
            $bitmap.SetPixel($x,$y,$color)
        }
    }
    $bitmap.Save([IO.Path]::GetFullPath($path),[Drawing.Imaging.ImageFormat]::Png)
} finally {$bitmap.Dispose()}
