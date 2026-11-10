# Builds FiveMToolbox.exe from FiveMToolbox.cs with the C# compiler that is built into Windows.
# Nothing to install. Run:  powershell -ExecutionPolicy Bypass -File build.ps1
# Optional:  -OutDir <folder>  builds somewhere else (for tests) and leaves ..\app alone.
# The icon (app.ico) is drawn by make-icon.ps1.
# The source file is UTF-8 (the translations have accented letters), so the compiler is told that with /codepage:65001.
param([string]$OutDir = '')
$name = 'FiveMToolbox'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { Write-Host 'The Windows C# compiler was not found.' -ForegroundColor Red; exit 1 }
$out = if ($OutDir) { $OutDir } else { Join-Path $PSScriptRoot '..\app' }
New-Item -ItemType Directory -Force -Path $out | Out-Null
Push-Location $PSScriptRoot
if (-not (Test-Path 'app.ico')) { & powershell -NoProfile -ExecutionPolicy Bypass -File make-icon.ps1 | Out-Null }
& $csc /nologo /codepage:65001 /target:winexe /platform:x64 /optimize+ /win32icon:app.ico "/out:$out\$name.exe" `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Management.dll "$name.cs"
$code = $LASTEXITCODE
Pop-Location
if ($code -eq 0) { Write-Host "Built $out\$name.exe" -ForegroundColor Green } else { Write-Host 'Build failed.' -ForegroundColor Red }
exit $code
