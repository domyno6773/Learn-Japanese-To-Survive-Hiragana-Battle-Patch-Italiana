$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $PSScriptRoot
try {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (!(Test-Path -LiteralPath $csc)) {
        $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
    }
    New-Item -ItemType Directory -Force -Path dist | Out-Null
    & $csc /nologo /optimize+ /target:winexe /platform:anycpu /codepage:65001 /reference:System.Windows.Forms.dll,System.Drawing.dll,System.IO.Compression.dll /resource:patch.zip,HiraganaPatch /out:dist\Hiragana-Battle-Patch-Italiana.exe Installer.cs
    if ($LASTEXITCODE -ne 0) { throw "Compilazione non riuscita ($LASTEXITCODE)." }
    Get-FileHash -Algorithm SHA256 dist\Hiragana-Battle-Patch-Italiana.exe
} finally {
    Pop-Location
}

