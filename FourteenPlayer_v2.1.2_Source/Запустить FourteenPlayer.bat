@echo off
if exist "%~dp0bin\Release\net9.0-windows\FourteenPlayer.exe" (
    start "" "%~dp0bin\Release\net9.0-windows\FourteenPlayer.exe"
) else (
    start "" "%~dp0bin\Debug\net9.0-windows\FourteenPlayer.exe"
)
