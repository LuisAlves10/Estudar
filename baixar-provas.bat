@echo off
setlocal
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Download-Corpus.ps1" %*
set "result=%errorlevel%"
echo.
if not "%result%"=="0" echo Alguns arquivos precisam de nova tentativa. Execute este arquivo novamente para continuar.
pause
exit /b %result%
