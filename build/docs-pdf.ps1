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

Write-Host "Rendering $html"
& $browser --headless=new --disable-gpu --no-pdf-header-footer --print-to-pdf="$pdf" $uri 2>&1 | Out-Null

if (-not (Test-Path -LiteralPath $pdf)) {
    throw "PDF was not written: $pdf"
}

$info = Get-Item -LiteralPath $pdf
Write-Host ('PDF: {0} ({1:N0} KB)' -f $info.FullName, ($info.Length / 1KB))
