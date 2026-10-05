param([string]$SignToolName = '')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'CafeArian.csproj'
$script = Join-Path $PSScriptRoot 'cafe-arian.iss'
$publishDir = Join-Path $root 'artifacts\publish-installer'
$compiler = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
$projectXml = [xml](Get-Content -LiteralPath $project -Raw)
$version = [string]$projectXml.Project.PropertyGroup.Version

if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw 'CafeArian.csproj must contain a three-part Version.'
}
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'Inno Setup 6 is required to build the installer.'
}
if ($SignToolName -and $SignToolName -notmatch '^[A-Za-z][A-Za-z0-9_-]{0,30}$') {
    throw 'SignToolName must be a configured Inno Setup sign tool name.'
}

& dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$defines = @("/DAppVersion=$version")
if ($SignToolName) { $defines += "/DSignToolName=$SignToolName" }
& $compiler @defines $script
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }

$setup = Join-Path $root "artifacts\cafe-arian-setup-$version.exe"
if ($SignToolName) {
    foreach ($file in @($setup, (Join-Path $publishDir 'CafeArian.exe'))) {
        $signature = Get-AuthenticodeSignature -LiteralPath $file
        if ($signature.Status -ne 'Valid') { throw "Invalid Authenticode signature: $file ($($signature.Status))" }
    }
}
Write-Output $setup
