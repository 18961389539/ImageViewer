[CmdletBinding()]
param(
    [string] $Version = "0.1.0",
    [string] $OutputRoot = "artifacts/packages"
)

if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$') {
    throw "Version '$Version' is not a valid SemVer 2.0 version."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $repoRoot $OutputRoot
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null

Push-Location $repoRoot
try {
    dotnet restore ImageViewer.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }

    dotnet build ImageViewer.sln --configuration Release --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed." }

    dotnet pack ImageViewer.Core\ImageViewer.Core.csproj --configuration Release --no-build --no-restore --nologo -p:PackageVersion=$Version -o $outputPath
    if ($LASTEXITCODE -ne 0) { throw "ImageViewer.Core pack failed." }

    dotnet pack third_party\JLVision\JLVisionLib.csproj --configuration Release --no-build --no-restore --nologo -p:PackageVersion=$Version -o $outputPath
    if ($LASTEXITCODE -ne 0) { throw "JLVisionLib pack failed." }

    dotnet pack ImageViewerControl\ImageViewerControl.csproj --configuration Release --no-build --no-restore --nologo -p:PackageVersion=$Version -o $outputPath
    if ($LASTEXITCODE -ne 0) { throw "ImageViewerControl pack failed." }

    $expected = @(
        "ImageViewer.Core.$Version.nupkg",
        "ImageViewer.Core.$Version.snupkg",
        "JLVisionLib.$Version.nupkg",
        "JLVisionLib.$Version.snupkg",
        "ImageViewerControl.$Version.nupkg",
        "ImageViewerControl.$Version.snupkg"
    )
    foreach ($name in $expected) {
        if (-not (Test-Path (Join-Path $outputPath $name))) {
            throw "Expected package artifact '$name' was not generated."
        }
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    function Get-ZipEntryNames([string] $packagePath) {
        $archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
        try {
            return @($archive.Entries | ForEach-Object FullName)
        }
        finally {
            $archive.Dispose()
        }
    }

    $jlvisionNativeEntry = "runtimes/win-x64/native/JLVisionCore.dll"
    $jlvPackageEntries = Get-ZipEntryNames (Join-Path $outputPath "JLVisionLib.$Version.nupkg")
    if ($jlvPackageEntries -notcontains $jlvisionNativeEntry) {
        throw "JLVisionLib package does not contain '$jlvisionNativeEntry'."
    }

    $controlPackageEntries = Get-ZipEntryNames (Join-Path $outputPath "ImageViewerControl.$Version.nupkg")
    if (@($controlPackageEntries | Where-Object { $_ -match '(?i)(^|/)(JLVisionCore\.dll)$' }).Count -gt 0) {
        throw "ImageViewerControl package must depend on JLVisionLib instead of carrying a duplicate native core DLL."
    }

    Get-ChildItem $outputPath -File |
        Where-Object { $_.Name -in $expected } |
        Select-Object Name, Length, LastWriteTime
}
finally {
    Pop-Location
}
