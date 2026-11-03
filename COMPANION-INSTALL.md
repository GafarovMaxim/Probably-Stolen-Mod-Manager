# Probably Mod Manager 0.2.7 — Companion Installation

The companion is only needed for Workshop publishing through SteamCMD. Mod management, profiles, logs and direct restart work without it.

This guide is for those who have subscribed to "Probably Mod Manager" via the Steam Workshop. The Workshop item downloads the core mod, but it does not automatically launch the companion application.

## Installation

1. Subscribe to the mod in the Steam Workshop and wait for it to download.
2. In Steam, go to **Probably Stolen → Properties → Installed Files → Browse**. Locate `Probably Stolen.exe`.
3. Extract the `ProbablyModManager-Companion-0.2.7.zip` archive and copy `Probably Mod Manager.exe` into the same folder where `Probably Stolen.exe` is located.
4. Launch the companion manually once per gaming session. If the game is not yet running, the companion will launch it via Steam; if it is already running, the companion will connect to it. Alternatively, you can launch the game normally through Steam—the Workshop mod will load as usual—and then open the companion.

The companion requires the **.NET 10 Desktop Runtime for Windows x64**. You can find it on the official Microsoft website: [.NET 10 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) — select Windows → Desktop Runtime → x64. If the companion launches successfully, you do not need to install the runtime.

SteamCMD is optional and is only required for publishing Workshop mods. To install it, open PowerShell and run the following command, replacing the placeholders with the actual path to your game folder:

```powershell
& "C:\path\to\extracted\Install-SteamCMD.ps1" -GameDirectory "C:\path\to\Probably Stolen"
```

Logging into SteamCMD is only necessary when publishing mods. The installer places SteamCMD within the "Probably Mod Manager" tools folder inside your game directory.

## Where to get the archive

The companion is a separate download; the Steam Workshop item contains only the core mod and does not include the companion application. The archive is located in the `dist` folder of the [Probably-Stolen-Mod-Manager](https://github.com/GafarovMaxim/Probably-Stolen-Mod-Manager) repository. Open the `ProbablyModManager-Companion-0.2.7.zip` file and click the download button. A GitHub release has not been created yet; once it is published, the link to the release can be added to the Workshop description.

The archive contains only the companion EXE, these instructions, and an optional SteamCMD installer. It does not include credentials, settings, cache, or game DLLs.

*****

# Probably Mod Manager 0.2.7 — установка companion

Помощник нужен только для публикации через SteamCMD. Управление модами, журнал, профили и прямой рестарт работают без него.

Эта инструкция для тех, кто подписался на Probably Mod Manager в Steam Workshop. Workshop загружает основной мод, но сам companion автоматически не запускает.

## Установка

1. Подпишитесь на мод в Workshop и дождитесь загрузки.
2. В Steam откройте **Probably Stolen → Свойства → Установленные файлы → Обзор**. Найдите `Probably Stolen.exe`.
3. Распакуйте архив `ProbablyModManager-Companion-0.2.7.zip` и скопируйте `Probably Mod Manager.exe` в ту же папку, где находится `Probably Stolen.exe`.
4. Запускайте companion вручную один раз за игровую сессию. Если игра ещё не запущена, companion запустит её через Steam; если уже запущена — подключится к ней. Также можно сначала запустить игру обычным способом через Steam: Workshop-мод загрузится штатно, затем откройте companion.

Для companion требуется **.NET 10 Desktop Runtime для Windows x64**. Официальная страница Microsoft: [.NET 10 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0); выберите Windows → Desktop Runtime → x64. Если companion уже запускается, устанавливать runtime не нужно.

SteamCMD необязателен и нужен только для публикации Workshop-модов. Для его установки откройте PowerShell и выполните команду, подставив путь к папке игры:

```powershell
& "C:\путь\к\распакованному\Install-SteamCMD.ps1" -GameDirectory "C:\путь\к\Probably Stolen"
```

Вход в SteamCMD понадобится только при публикации. Установщик размещает SteamCMD в папке инструментов Probably Mod Manager внутри каталога игры.

## Где получить архив

Это отдельная загрузка companion; сам Workshop item содержит основной мод и не запускает помощник. Архив находится в папке `dist` репозитория [Probably-Stolen-Mod-Manager](https://github.com/GafarovMaxim/Probably-Stolen-Mod-Manager). Откройте файл `ProbablyModManager-Companion-0.2.7.zip` и нажмите кнопку скачивания. GitHub Release ещё не создавался; после его публикации ссылку на выпуск можно добавить в описание Workshop.

В архив входят только companion EXE, эта инструкция и необязательный установщик SteamCMD. Учётные данные, настройки, кэш и DLL игры в архив не входят.
