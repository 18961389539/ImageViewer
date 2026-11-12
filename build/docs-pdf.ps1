[CmdletBinding()]
param(
    [string] $HtmlPath,
    [string] $PdfPath
)

# 用本机 Edge（或 Chrome）的 headless 打印，把 docs/usage.html 输出成 docs/usage.pdf。
# 与 build/docs.py 同源：先跑 docs.py 生成 HTML，再跑本脚本生成 PDF。
# CI 不跑这一步：ubuntu runner 没有 Edge，PDF 作为交付物随仓库提交。

$root = Split-Path -Parent $PSScriptRoot
if (-not $HtmlPath) { $HtmlPath = Join-Path $root 'docs\usage.html' }
if (-not $PdfPath) { $PdfPath = Join-Path $root 'docs\usage.pdf' }

$candidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'),
    (Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe'),
    (Join-Path $env:ProgramFiles 'Google\Chrome\Application\chrome.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Google\Chrome\Application\chrome.exe')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

if (-not $candidates) {
    throw 'Edge/Chrome not found. Install Microsoft Edge or Google Chrome to build the PDF.'
}

if (-not (Test-Path -LiteralPath $HtmlPath)) {
    throw "HTML not found: $HtmlPath. Run 'python build/docs.py' first."
}

$browser = @($candidates)[0]
$html = (Resolve-Path -LiteralPath $HtmlPath).Path
$pdf = [System.IO.Path]::GetFullPath($PdfPath)
$uri = ([System.Uri]$html).AbsoluteUri
$pdfDirectory = Split-Path -Parent $pdf
if (-not (Test-Path -LiteralPath $pdfDirectory -PathType Container) -or
    (Test-Path -LiteralPath $pdf -PathType Container)) {
    throw "Invalid PDF output path: $pdf"
}
$stagedPdf = Join-Path $pdfDirectory ('.imageviewer-' + [guid]::NewGuid().ToString('N') + '.pdf')

Write-Host "Rendering $html"
try {
    # Edge/Chrome is a GUI executable on Windows. Wait for it before checking the staged output.
    $render = Start-Process -FilePath $browser -WindowStyle Hidden -Wait -PassThru -ArgumentList @(
        '--headless=new', '--disable-gpu', '--no-pdf-header-footer', "--print-to-pdf=`"$stagedPdf`"", $uri
    )
    $renderedFile = Get-Item -LiteralPath $stagedPdf -ErrorAction SilentlyContinue
    if ($render.ExitCode -ne 0 -or -not $renderedFile -or $renderedFile.Length -eq 0) {
        throw "PDF was not written: $pdf (browser exit code: $($render.ExitCode))"
    }

    Move-Item -LiteralPath $stagedPdf -Destination $pdf -Force -ErrorAction Stop
} finally {
    if (Test-Path -LiteralPath $stagedPdf -PathType Leaf) {
        Remove-Item -LiteralPath $stagedPdf -ErrorAction SilentlyContinue
    }
}

$info = Get-Item -LiteralPath $pdf
Write-Host ('PDF: {0} ({1:N0} KB)' -f $info.FullName, ($info.Length / 1KB))
