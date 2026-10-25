$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$source = Join-Path $root 'src\Program.cs'
$versionFile = Join-Path $root 'launcher-version.txt'
$artwork = Join-Path $root 'assets\LauncherArtwork.png'
$executable = Join-Path $root 'WoAClientLauncher.exe'
$bundle = Join-Path $root 'WoAClientLauncher-bundle.zip'
$feed = Join-Path $root 'launcher-feed'

foreach ($required in @($compiler, $source, $versionFile, $artwork, (Join-Path $feed 'manifest.json'))) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Required launcher build input not found: $required"
    }
}

$launcherVersion = (Get-Content -LiteralPath $versionFile -Raw).Trim()
if ($launcherVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw "Launcher version must use numeric major.minor.patch format: $launcherVersion"
}

$versionSource = Join-Path ([System.IO.Path]::GetTempPath()) ('WoALauncherVersion-' + [Guid]::NewGuid().ToString('N') + '.cs')
$versionSourceText = @"
namespace WrathOfAzerothLauncher
{
    internal static class LauncherBuildInfo
    {
        internal const string Version = "$launcherVersion";
    }
}
"@
[System.IO.File]::WriteAllText($versionSource, $versionSourceText, (New-Object System.Text.UTF8Encoding($false)))

& $compiler /nologo /target:winexe /optimize+ `
    "/out:$executable" `
    /reference:System.Windows.Forms.dll `
    /reference:System.Drawing.dll `
    /reference:System.Net.Http.dll `
    /reference:System.IO.Compression.dll `
    /reference:System.IO.Compression.FileSystem.dll `
    /reference:System.Web.Extensions.dll `
    $source `
    $versionSource

Remove-Item -LiteralPath $versionSource -Force

if ($LASTEXITCODE -ne 0) {
    throw "Launcher compilation failed with exit code $LASTEXITCODE"
}

$versionedLauncherName = "WoAClientLauncher-$launcherVersion.exe"
$versionedLauncherPath = Join-Path (Join-Path $feed 'files') $versionedLauncherName
Copy-Item -LiteralPath $executable -Destination $versionedLauncherPath -Force
$launcherHash = (Get-FileHash -LiteralPath $versionedLauncherPath -Algorithm SHA256).Hash.ToLowerInvariant()
$manifestPath = Join-Path $feed 'manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$launcherUpdate = [ordered]@{
    version = $launcherVersion
    sha256 = $launcherHash
    url = "files/$versionedLauncherName"
}
if ($manifest.PSObject.Properties.Name -contains 'launcher') {
    $manifest.launcher = $launcherUpdate
} else {
    $manifest | Add-Member -MemberType NoteProperty -Name launcher -Value $launcherUpdate
}
$manifestJson = $manifest | ConvertTo-Json -Depth 20
[System.IO.File]::WriteAllText($manifestPath, $manifestJson + [Environment]::NewLine, (New-Object System.Text.UTF8Encoding($false)))

Copy-Item -LiteralPath $artwork -Destination (Join-Path $root 'LauncherArtwork.png') -Force
$staging = Join-Path ([System.IO.Path]::GetTempPath()) ('WoALauncherBundle-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    Copy-Item -LiteralPath $executable -Destination $staging
    Copy-Item -LiteralPath (Join-Path $root 'LauncherArtwork.png') -Destination $staging
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
