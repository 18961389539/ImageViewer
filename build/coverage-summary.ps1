[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $CoverageRoot,

    [string] $AssemblyPattern,

    [double] $MinimumLineRate = 0
)

$coverageFiles = Get-ChildItem -Path $CoverageRoot -Filter coverage.cobertura.xml -File -Recurse -ErrorAction SilentlyContinue
if ($coverageFiles.Count -eq 0) {
    throw "No Cobertura coverage file was found under '$CoverageRoot'."
}

$selectedModules = @()
$coveredLines = 0
$totalLines = 0
foreach ($coverageFile in $coverageFiles) {
    [xml] $document = Get-Content -LiteralPath $coverageFile.FullName -Raw
    foreach ($package in @($document.coverage.packages.package)) {
        foreach ($class in @($package.classes.class)) {
            $moduleName = [string] $class.filename
            if ($AssemblyPattern -and $moduleName -notmatch $AssemblyPattern) {
                continue
            }

            $selectedModules += $moduleName
            foreach ($line in @($class.lines.line)) {
                $totalLines++
                if ([int] $line.hits -gt 0) {
                    $coveredLines++
                }
            }
        }
    }
}

if ($totalLines -eq 0) {
    throw "Coverage files contained no lines after filtering."
}

$lineRate = $coveredLines / $totalLines
$percentage = $lineRate * 100
$uniqueModules = @($selectedModules | Sort-Object -Unique)
Write-Output ("Coverage: {0}/{1} lines ({2:N2}%), modules={3}" -f $coveredLines, $totalLines, $percentage, $uniqueModules.Count)

if ($MinimumLineRate -gt 0 -and $lineRate -lt $MinimumLineRate) {
    throw ("Coverage {0:P2} is below the required minimum {1:P2}." -f $lineRate, $MinimumLineRate)
}
