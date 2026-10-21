# Сборка и запуск

Как запускать программу при разработке и как собирать готовые файлы для раздачи. Краткий старт —
в [README](../../../README.md); здесь полное описание с особенностями каждого режима.

## Назначение

Один набор команд на трех ОС: запуск из исходников, сборка дистрибутива скриптом, раскладка
вывода. Все команды — bash, из корня репозитория (на Windows — Git Bash).

## Запуск для разработки

```bash
dotnet run -f net10.0-windows10.0.17763    # Windows
dotnet run -f net10.0                      # macOS, Linux
```

Флаг `-f` обязателен: проект многотаргетный (см. [«Два таргета»](#два-таргета-и-почему-их-два)),
и без явного выбора `dotnet run` не знает, что запускать. **На Windows запускайте только
windows-таргет** — под `net10.0` программа работает, но системных уведомлений в ней нет.

`dotnet run` каждый раз проверяет изменения и при необходимости пересобирает проект. Когда код не
менялся:

```bash
dotnet run -f net10.0 --no-build                      # уже собранное, без пересборки
artifacts/bin/PdfLibrary/debug_net10.0/PdfLibrary     # прямой запуск (macOS, Linux)
artifacts/bin/PdfLibrary/debug_net10.0-windows10.0.17763/PdfLibrary.exe   # Windows
dotnet watch run -f net10.0                           # пересборка и перезапуск при сохранении
```

> ⚠️ `--no-build` и прямой запуск стартуют **последнюю собранную** версию. Менял код — сначала
> собери.

Отладочная сборка хранит данные в `.localdata/` в корне репозитория, а не в папке пользователя:
разработка не трогает настоящую библиотеку. Другую папку данных задает переменная
`PDFLIBRARY_DATA` — ею пользуются тесты и стенд экранов.

Программа однооконная и одна на папку данных (`instance.lock`): второй запуск отдает сигнал
первому (`show.signal`) и закрывается.

## Два таргета и почему их два

В [PdfLibrary.csproj](../../../PdfLibrary.csproj) объявлено `<TargetFrameworks>`:

| Таргет | Для чего |
|---|---|
| `net10.0` | macOS, Linux и все проекты `Tests/*` — они ссылаются на программу и собираются под этот таргет |
| `net10.0-windows10.0.17763` | **только Windows** — единственный, под которым работают системные уведомления |

NuGet выбирает файлы пакета по таргету, и под голым `net10.0` от `DesktopNotifications.Windows`
приезжают сборки **без WinRT**: программа запускается, а менеджер уведомлений падает с
`TypeLoadException`, и баннеры молча не показываются. Отсюда правила:

- **Windows собирать и запускать только windows-таргетом.** `Scripts/windows-build.sh` передает
  `-f` сам; при ручном `dotnet run` и `dotnet build` флаг указывать.
- Поднялся ли менеджер, видно в журнале («Настройки → Диагностика»): «Менеджер нативных
  уведомлений создан: …» или предупреждение со сбоем. На Linux без сеансовой шины D-Bus —
  «Сеансовой шины D-Bus нет — системные уведомления выключены».
- `EnableWindowsTargeting` в csproj разрешает собирать windows-таргет с macOS и Linux.

## Сборка для раздачи

Под каждую ОС — **один скрипт** в [Scripts/](../../../Scripts/) (имя = `<ОС>-build`). Он прячет
длинную команду `dotnet publish` и особенности платформы. Результат раскладывается по версиям — в
`dist/Version <X.Y.Z>/<rid>/`. Установка .NET пользователю не нужна (**self-contained**), бинарь
склеен в один файл (**single-file**, кроме macOS — там бандл `.app`), размер 130–160 МБ.

**Частичная предкомпиляция (ReadyToRun).** Сборки, которые работают на старте (сама программа,
Avalonia, EF Core и SQLite, Serilog, Skia), публикуются уже машинным кодом — старт не ждет
JIT-компиляции. Разбор PDF (PdfPig, Docnet.Core) компилируется при первом использовании. Список
исключений — `PublishReadyToRunExclude` в csproj: **новая библиотека, которая работает на старте, в
него не попадает, тяжелая и редкая — попадает.** Составной образ выключен явно
(`PublishReadyToRunComposite=false`): для single-file SDK включает его сам, а он вшивает всю
платформу .NET и не признает исключений.

Сигнатура у всех скриптов одинаковая:

```
Scripts/<ОС>-build.sh [RID] [Config] [Version]
```

- **RID** — `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-arm64`, `osx-x64`. По
  умолчанию — основной для своей ОС.
- **Config** — `Release` (по умолчанию) или `Debug`.
- **Version** — номер версии: **записывается в `<Version>`** в csproj и в бейдж README и остается
  текущим для следующих сборок. Без аргумента берется то, что уже в csproj.

Все платформы собираются с одной машины (кросс-сборка): проверено сборкой win-x64, linux-x64 и
osx-arm64 на Linux. Папка `dist/` — в `.gitignore`.

### Windows

```bash
Scripts/windows-build.sh                        # win-x64, версия из csproj
Scripts/windows-build.sh win-x64 Release 1.2.3  # с номером версии 1.2.3
```

Результат: `dist/Version <X.Y.Z>/win-x64/PdfLibrary.exe`, иконка вшита через `ApplicationIcon`.
Рядом — `PdfLibrary-Setup.exe`: тот же бинарь, по окончанию имени `-Setup` он работает
установщиком ([install-and-update.md](install-and-update.md)).

### Linux

```bash
Scripts/linux-build.sh                          # linux-x64, версия из csproj
Scripts/linux-build.sh linux-x64 Release 1.2.3  # с номером версии 1.2.3
```

Результат: `dist/Version <X.Y.Z>/linux-x64/PdfLibrary` (бит запуска выставлен) и
`PdfLibrary-Setup`: на целевой машине он ставит программу в `~/.local/opt/PdfLibrary`, пишет
`.desktop`-ярлыки и иконку и умеет `--uninstall`. `Scripts/linux-install.sh` — ручная альтернатива
(иконка и пункт меню для уже разложенного бинаря).

### macOS

Под Mac собирают **дважды** — Apple Silicon и Intel, с одной версией. Скрипт публикует бинарь и
упаковывает его в `.app` с иконкой (без бандла иконки в Dock нет).

```bash
Scripts/macos-build.sh                          # Apple Silicon (osx-arm64)
Scripts/macos-build.sh osx-x64                  # Intel (osx-x64)
Scripts/macos-build.sh osx-arm64 Release 1.2.3  # с номером версии
```

Результат: `dist/Version <X.Y.Z>/<rid>/PdfLibrary.app` и `PdfLibrary-Setup.app` (ставит в
`~/Applications`, умеет `--uninstall`). Версия в `Info.plist` проставляется `PlistBuddy` — он
есть только на macOS; при кросс-сборке на другой ОС в бандле остается версия из
`Assets/Branding/Info.plist`.

## Версия и дата сборки

- **Версия** — `<Version>` в [PdfLibrary.csproj](../../../PdfLibrary.csproj).
- **Дата сборки** — `AssemblyMetadata("BuildDate")`, ставится при сборке сама.
- Обе видны в программе: «Настройки → О программе».

## Раскладка вывода

| Папка | Что | В git |
|---|---|---|
| `artifacts/` | вся сборка (`bin`/`obj`/`publish`), `UseArtifactsOutput` в `Directory.Build.props` | нет |
| `packages/` | локальный кеш NuGet (`nuget.config`) | нет |
| `dist/` | готовые сборки для раздачи, `Version <X.Y.Z>/<rid>/` | нет |
| `.localdata/` | данные отладочного запуска: `library.db`, `library-cache.db`, `thumbs/`, `logs.db`, `instance.lock`, `show.signal`, `crash.log` | нет |

Очистить сборку — удалить `artifacts/` и `packages/`. `.localdata/` при этом не трогают.

## Иконки

Мастера в [Assets/Branding/](../../../Assets/Branding/): `icon-windows.png`, `icon-macos.png`,
`icon-linux.png` (1024×1024, по одному на систему). Временная иконка нарисована
`Scripts/draw-icon.py` (Python 3 с Pillow). Из мастеров `Scripts/generate-icons.sh`
собирает `icon.ico` и `icon.icns` (штатными утилитами macOS); на других ОС `.icns` можно собрать
Pillow, как это сделано для временной иконки.

## Скрипты ([Scripts/](../../../Scripts/))

| Скрипт | Назначение |
|---|---|
| `windows-build.sh` | сборка Windows → `PdfLibrary.exe` и `PdfLibrary-Setup.exe` |
| `linux-build.sh` | сборка Linux → `PdfLibrary` и `PdfLibrary-Setup` |
| `macos-build.sh` | сборка macOS → `PdfLibrary.app` и `PdfLibrary-Setup.app` |
| `linux-install.sh` | иконка и пункт меню на Linux-машине для уже разложенного бинаря |
| `generate-icons.sh` | проверка мастеров иконки и сборка `icon.ico` и `icon.icns` (только macOS) |
| `draw-icon.py` | рисует временные мастера иконки |

## Связанные документы

[[developer-guide]], [[install-and-update]], [[../architecture/overview]]
