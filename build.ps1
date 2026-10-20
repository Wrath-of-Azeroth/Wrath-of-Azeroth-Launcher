$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$source = Join-Path $root 'src\Program.cs'
$artwork = Join-Path $root 'assets\Frostgate.png'
$executable = Join-Path $root 'WoAClientLauncher.exe'
$bundle = Join-Path $root 'WoAClientLauncher-bundle.zip'
$feed = Join-Path $root 'launcher-feed'

foreach ($required in @($compiler, $source, $artwork, (Join-Path $feed 'manifest.json'))) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Required launcher build input not found: $required"
    }
}

& $compiler /nologo /target:winexe /optimize+ `
    "/out:$executable" `
    /reference:System.Windows.Forms.dll `
    /reference:System.Drawing.dll `
    /reference:System.Net.Http.dll `
    /reference:System.IO.Compression.dll `
    /reference:System.IO.Compression.FileSystem.dll `
    /reference:System.Web.Extensions.dll `
    $source

if ($LASTEXITCODE -ne 0) {
    throw "Launcher compilation failed with exit code $LASTEXITCODE"
}

Copy-Item -LiteralPath $artwork -Destination (Join-Path $root 'Frostgate.png') -Force
$staging = Join-Path ([System.IO.Path]::GetTempPath()) ('WoALauncherBundle-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    Copy-Item -LiteralPath $executable -Destination $staging
    Copy-Item -LiteralPath (Join-Path $root 'Frostgate.png') -Destination $staging
    Copy-Item -LiteralPath (Join-Path $root 'README.txt') -Destination $staging
    Copy-Item -LiteralPath $feed -Destination $staging -Recurse
    if (Test-Path -LiteralPath $bundle) {
        Remove-Item -LiteralPath $bundle -Force
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $staging,
        $bundle,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false
    )
}
finally {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
}

Write-Host "Built $executable"
Write-Host "Built $bundle"
