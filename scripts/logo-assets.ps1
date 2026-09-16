# Regenerates logo.png (440px), apple-touch-icon.png and favicon.png at the repo root from
# the full-size crest art, knocking its flat black backdrop out to transparency so it sits
# on the site's charcoal gradient without a visible square. Run from PowerShell:
#   & .\scripts\logo-assets.ps1 -Src <path to crest.png> -OutDir .
param([string]$Src, [string]$OutDir)
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class Logo {
  // Knock the flat black backdrop out to transparency: alpha ramps with brightness
  // below a threshold, and the colour is un-premultiplied so dark edges don't halo.
  public static Bitmap Knockout(Bitmap src) {
    var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bmp)) g.DrawImage(src, 0, 0, src.Width, src.Height);
    var r = new Rectangle(0, 0, bmp.Width, bmp.Height);
    var d = bmp.LockBits(r, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
    int n = bmp.Width * bmp.Height * 4;
    var px = new byte[n];
    Marshal.Copy(d.Scan0, px, 0, n);
    const int lo = 6, hi = 40;
    for (int i = 0; i < n; i += 4) {
      int m = Math.Max(px[i], Math.Max(px[i+1], px[i+2]));
      if (m >= hi) continue;
      if (m <= lo) { px[i] = px[i+1] = px[i+2] = px[i+3] = 0; continue; }
      double a = (m - lo) / (double)(hi - lo);
      px[i+3] = (byte)(a * 255);
      for (int c = 0; c < 3; c++) px[i+c] = (byte)Math.Min(255, px[i+c] / a);
    }
    Marshal.Copy(px, 0, d.Scan0, n);
    bmp.UnlockBits(d);
    return bmp;
  }
  public static void Save(Bitmap src, int size, string path) {
    using (var b = new Bitmap(size, size, PixelFormat.Format32bppArgb))
    using (var g = Graphics.FromImage(b)) {
      g.InterpolationMode = InterpolationMode.HighQualityBicubic;
      g.PixelOffsetMode = PixelOffsetMode.HighQuality;
      g.CompositingQuality = CompositingQuality.HighQuality;
      g.DrawImage(src, 0, 0, size, size);
      b.Save(path, ImageFormat.Png);
    }
  }
}
'@
$img = [System.Drawing.Bitmap]::FromFile($Src)
$k = [Logo]::Knockout($img)
[Logo]::Save($k, 440, (Join-Path $OutDir 'logo.png'))
[Logo]::Save($k, 180, (Join-Path $OutDir 'apple-touch-icon.png'))
[Logo]::Save($k, 64, (Join-Path $OutDir 'favicon.png'))
