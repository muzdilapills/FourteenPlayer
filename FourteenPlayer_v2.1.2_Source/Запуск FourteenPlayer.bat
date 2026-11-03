@echo off
if exist "%~dp0publish\FourteenPlayer.exe" (
    start "" "%~dp0publish\FourteenPlayer.exe"
) else if exist "%~dp0bin\Release\net9.0-windows10.0.19041.0\FourteenPlayer.exe" (
    start "" "%~dp0bin\Release\net9.0-windows10.0.19041.0\FourteenPlayer.exe"
) else (
    start "" "%~dp0bin\Debug\net9.0-windows10.0.19041.0\FourteenPlayer.exe"
)