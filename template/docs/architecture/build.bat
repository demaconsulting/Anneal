@echo off
setlocal
rem TEMPLATE-DIRECTIVE: replace {ProjectName} with the value from AGENTS.md project-name.
pwsh -NoProfile -File "%~dp0..\build-doc.ps1" -Document architecture -Name "{ProjectName} Software Architecture" %*
