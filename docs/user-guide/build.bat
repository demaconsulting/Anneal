@echo off
setlocal
pwsh -NoProfile -File "%~dp0..\build-doc.ps1" -Document user-guide -Name "Anneal User Guide" %*
