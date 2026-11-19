# Builds Bridge\dist\AIManagerBridge.exe: one file, no Python needed on the user's PC.
# Needs a normal Windows Python 3.9+ (python.org, with tkinter) on the PC that builds it.
#   powershell -ExecutionPolicy Bypass -File Bridge\build_exe.ps1
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
python -m venv .build-venv
& .\.build-venv\Scripts\python.exe -m pip install --quiet --upgrade pip pyinstaller
& .\.build-venv\Scripts\pyinstaller.exe --onefile --clean --name AIManagerBridge `
    --hidden-import bridge_app --hidden-import claude_setup --hidden-import aim `
    aim_bridge.py
Write-Host ""
Write-Host "Built: $PSScriptRoot\dist\AIManagerBridge.exe"
Write-Host "Double-click it. It shows a pairing code; type that code in the headset."
