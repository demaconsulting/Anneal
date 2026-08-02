@echo off
setlocal
pwsh -NoProfile -File "%~dp0..\build-doc.ps1" -Document architecture -Name "Anneal Software Architecture" %*
