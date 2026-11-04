# FourteenPlayer Setup v2.1.2 — Исходный код инсталлятора

Исходный код официального установщика **FourteenPlayer v2.1.2** (`FourteenPlayer_v2.1.2_Setup.exe`).

## Файлы проекта
- `FourteenPlayer_Installer.csproj` — проект .NET 9 (Windows Forms) с поддержкой SingleFile и внедрением ресурсов.
- `Program.cs` — точка входа, логика тихой установки (`/S`, `/SILENT`) и запуск графического интерфейса.
- `InstallerForm.cs` — интерфейс установщика в стиле Modern Dark UI (поддержка Dark Mode Windows DWM, выбор директории установки, создание ярлыков на рабочем столе и в меню «Пуск», регистрация в реестре Windows).
- `app.zip` — внедрённый архив с готовой сборкой плеера v2.1.2.
- `app_icon.ico` — иконка инсталлятора.
- `Сборка Setup.bat` — скрипт для автоматической сборки в один клик.

## Сборка
Требуется установленный [.NET SDK 9.0](https://dotnet.microsoft.com/download).

Запустите **`Сборка Setup.bat`** или выполните в терминале:
```bash
dotnet publish FourteenPlayer_Installer.csproj -c Release -o ./publish
```
Собранный исполняемый файл будет находиться в директории `publish\FourteenPlayer_v2.1.2_Setup.exe`.
