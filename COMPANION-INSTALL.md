# Probably Mod Manager 0.2.3 — установка companion

Эта инструкция для тех, кто подписался на Probably Mod Manager в Steam Workshop. Workshop загружает основной мод, но сам companion автоматически не запускает.

## Установка

1. Подпишитесь на мод в Workshop и дождитесь загрузки.
2. В Steam откройте **Probably Stolen → Свойства → Установленные файлы → Обзор**. Найдите `Probably Stolen.exe`.
3. Распакуйте архив `ProbablyModManager-Companion-0.2.3.zip` и скопируйте `Probably Mod Manager.exe` в ту же папку, где находится `Probably Stolen.exe`.
4. Запускайте companion вручную один раз за игровую сессию. Если игра ещё не запущена, companion запустит её через Steam; если уже запущена — подключится к ней. Также можно сначала запустить игру обычным способом через Steam: Workshop-мод загрузится штатно, затем откройте companion.

Для companion требуется **.NET 10 Desktop Runtime для Windows x64**. Официальная страница Microsoft: [.NET 10 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0); выберите Windows → Desktop Runtime → x64. Если companion уже запускается, устанавливать runtime не нужно.

SteamCMD необязателен и нужен только для публикации Workshop-модов. Для его установки откройте PowerShell и выполните команду, подставив путь к папке игры:

```powershell
& "C:\путь\к\распакованному\Install-SteamCMD.ps1" -GameDirectory "C:\путь\к\Probably Stolen"
```

Вход в SteamCMD понадобится только при публикации. Установщик размещает SteamCMD в папке инструментов Probably Mod Manager внутри каталога игры.

## Где получить архив

Это отдельная загрузка companion; сам Workshop item содержит основной мод и не запускает помощник. Архив находится в папке `dist` репозитория [Probably-Stolen-Mod-Manager](https://github.com/GafarovMaxim/Probably-Stolen-Mod-Manager). Откройте файл `ProbablyModManager-Companion-0.2.3.zip` и нажмите кнопку скачивания. GitHub Release ещё не создавался; после его публикации ссылку на выпуск можно добавить в описание Workshop.

В архив входят только companion EXE, эта инструкция и необязательный установщик SteamCMD. Учётные данные, настройки, кэш и DLL игры в архив не входят.

**English:** Subscribe to the Workshop item, place `Probably Mod Manager.exe` beside `Probably Stolen.exe`, then open it once per session. It starts the game through Steam if needed, or attaches to a running game. Requires the .NET 10 Desktop Runtime (Windows x64).
