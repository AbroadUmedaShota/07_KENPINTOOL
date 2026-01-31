param(
    [string]$EntryPoint = "src\\kenpintool\\main.py",
    [string]$OutputDir = "artifacts\\kenpintool"
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command pyinstaller -ErrorAction SilentlyContinue)) {
    Write-Error "pyinstaller が見つかりません。事前にインストールしてください。"
}

New-Item -ItemType Directory -Force $OutputDir | Out-Null

pyinstaller `
    --noconfirm `
    --onefile `
    --name kenpintool `
    --distpath $OutputDir `
    $EntryPoint
