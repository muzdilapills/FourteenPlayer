@echo off
chcp 65001 > nul
title Сборка FourteenPlayer Setup v2.1.2
cd /d "%~dp0"
echo ========================================================
echo  Сборка FourteenPlayer_v2.1.2_Setup (Release)...
echo ========================================================
echo.
dotnet publish FourteenPlayer_Installer.csproj -c Release -o "%~dp0publish"
if %errorlevel% equ 0 (
    echo.
    echo ========================================================
    echo  [УСПЕХ] Сборка инсталлятора успешно завершена!
    echo  Готовый файл: %~dp0publish\FourteenPlayer_v2.1.2_Setup.exe
    echo ========================================================
) else (
    echo.
    echo [ОШИБКА] Произошла ошибка сборки!
)
pause
