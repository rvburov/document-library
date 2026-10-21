# Библиотека PDF

<p align="center">
  <img src="https://img.shields.io/badge/version-0.1.0-blue" alt="Версия">
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white" alt=".NET">
  <img src="https://img.shields.io/badge/Avalonia_UI-11.2-8B44AC" alt="Avalonia">
  <img src="https://img.shields.io/badge/EF_Core-9.0-512BD4" alt="EF Core">
  <img src="https://img.shields.io/badge/SQLite-FTS5-003B57?logo=sqlite&logoColor=white" alt="SQLite">
  <img src="https://img.shields.io/badge/Windows-0078D4" alt="Windows">
  <img src="https://img.shields.io/badge/macOS-000000?logo=apple&logoColor=white" alt="macOS">
  <img src="https://img.shields.io/badge/Linux-FCC624?logo=linux&logoColor=black" alt="Linux">
</p>

Настольная программа для поиска и чтения PDF-документов: лента карточек с миниатюрами, поиск по
тексту внутри файлов с подсветкой найденного, панель чтения, метки. Документы остаются файлами на
своих местах — на диске или в сетевой папке; сервер и учетные записи не нужны.

---

## Возможности

- **Источники** — папки на диске и в сети, с подпапками; группы источников. Программа следит за
  папками сама: новые PDF появляются в ленте, измененные перечитываются, удаленные помечаются
  «файл не найден». Пропавшая сетевая папка не теряет документы.
- **Лента** — карточки с миниатюрой первой страницы, где лежит файл, тип документа, метки, размер,
  страницы и форматы листов («A1 × 1, A3 × 2»). Три размера карточки, сортировка.
- **Поиск** — по тексту внутри PDF и по названию с именем файла сразу; найденное подсвечено строками
  из текста. «е» и «е с точками» находят друг друга, числа ищутся как написаны.
- **Тип документа** определяется сам: по коду ЕСКД в имени файла («ВО», «РЭ», «Г3»), по словам в
  имени и в названии из свойств PDF, по папке. Фильтр «Тип документа».
- **Панель чтения** — справа от ленты: документ как в документе — настоящие страницы с подсветкой
  найденного на листах; переходы `F3` / `Shift+F3`, ширина панели — ручкой.
- **Метки** с цветом, выбор нескольких документов: метка, копирование в папку.
- **Фоновое чтение** текста с ходом и оценкой «осталось около …», остановка и продолжение. Сканы без
  текстового слоя ищутся по названию.
- **Оболочка** — светлая и темная темы, масштаб интерфейса и размер шрифта, автозапуск, работа в
  фоне со значком у часов, системные уведомления, журнал и «Диагностика».
- **Установка без прав администратора** тем же файлом `PdfLibrary-Setup`, обновление из общей папки
  в сети.

## Технологии

| Что | Чем |
|---|---|
| Интерфейс | Avalonia UI 11.2 (Fluent), CommunityToolkit.Mvvm |
| Данные | EF Core 9 + SQLite (`library.db`), кеш прочитанного — SQLite с FTS5 (`library-cache.db`) |
| PDF | PdfPig — текст и места слов; Docnet.Core (PDFium) — страницы и миниатюры; SkiaSharp |
| Журнал | Serilog в `logs.db` |
| Уведомления | DesktopNotifications (Windows, Linux), `osascript` (macOS) |
| Платформа | .NET 10, self-contained, один файл на Windows и Linux, `.app` на macOS |

Сети в программе нет: сетевые папки — это файлы, а сетевые API запрещены на уровне сборки.

## Как устроено

- `Core/` — PDF и библиотека без интерфейса: текст и форматы листов, тип документа, страницы и места
  слов, подсветка, сборка ленты.
- `Database/` — `library.db` (источники, группы, метки, документы, настройки) и кеш прочитанного.
- `Services/LibraryService` — библиотека целиком: проверка и наблюдение за папками, фоновое
  чтение, поиск. Память — в UI-потоке, тяжелая работа — в фоне.
- `Views/`, `Controls/`, `Assets/Styles/` — лента, панель чтения, первый запуск, настройки;
  общие стили, панели и окна.

Подробно — [docs/developer/architecture/overview.md](docs/developer/architecture/overview.md).

## Быстрый старт

### Что нужно установить

**.NET SDK 10** — для запуска из исходников. Готовым сборкам ничего не нужно.

```bash
winget install Microsoft.DotNet.SDK.10        # Windows
brew install --cask dotnet-sdk                # macOS
sudo apt-get install -y dotnet-sdk-10.0       # Linux (Ubuntu, Debian)
```

### Запуск из исходников

```bash
git clone https://github.com/rvburov/document-library.git
cd document-library
dotnet run -f net10.0-windows10.0.17763   # Windows
dotnet run -f net10.0                     # macOS, Linux
```

Проект многотаргетный, `-f` обязателен; на Windows — только windows-таргет (иначе нет системных
уведомлений). Отладочный запуск хранит данные в `.localdata/` в корне репозитория.

### Тесты

```bash
dotnet test Tests/Unit                 # быстрый гейт
dotnet test Tests/Integration          # настоящая SQLite и PDF
dotnet run --project Tests/Screens     # стенд экранов без экрана, кадры в artifacts/screens
./Tests/run-all.sh                     # все три подряд
```

Карта тестов — [Tests/README.md](Tests/README.md).

### Сборка для раздачи

```bash
Scripts/windows-build.sh                # dist/Version <X.Y.Z>/win-x64/PdfLibrary.exe и PdfLibrary-Setup.exe
Scripts/linux-build.sh                  # dist/Version <X.Y.Z>/linux-x64/PdfLibrary и PdfLibrary-Setup
Scripts/macos-build.sh                  # dist/Version <X.Y.Z>/osx-arm64/PdfLibrary.app и PdfLibrary-Setup.app
Scripts/macos-build.sh osx-x64          # Intel
```

Третий аргумент — номер версии: `Scripts/linux-build.sh linux-x64 Release 1.2.3` записывает его в
`PdfLibrary.csproj`. Подробно — [docs/developer/guides/build-and-run.md](docs/developer/guides/build-and-run.md).

## Куда что устанавливается

| ОС | Программа | Данные |
|---|---|---|
| Windows | `%LOCALAPPDATA%\Programs\PdfLibrary` | `%LOCALAPPDATA%\PdfLibrary` |
| Linux | `~/.local/opt/PdfLibrary` | `~/.local/share/PdfLibrary` |
| macOS | `~/Applications/PdfLibrary.app` | `~/Library/Application Support/PdfLibrary` |

В папке данных: `library.db` — источники, группы, метки; `library-cache.db` — прочитанный текст
для поиска (можно очистить, прочитается заново); `thumbs/` — миниатюры; `logs.db` — журнал;
`crash.log` — падения. Установка, удаление и обновление —
[docs/developer/guides/install-and-update.md](docs/developer/guides/install-and-update.md).

## Структура проекта

```
PdfLibrary.csproj    программа (два таргета: net10.0 и net10.0-windows10.0.17763)
Program.cs, App.*    запуск, режимы установщика и обновления, подготовка в фоне
Core/                PDF и библиотека без интерфейса
Database/            library.db (EF Core, миграции), кеш прочитанного DocIndex/, SqliteGate
Services/            LibraryService, действия с файлами
Views/, Controls/    интерфейс; Assets/Styles — токены, темы, иконки
Setup/               установщик, удаление, обновление
Logging/             журнал, crash.log, медленные операции и зависания
Notifications/, Utils/
Tests/               Unit, Integration, стенд экранов Screens
Scripts/             сборка под ОС, иконки, рабочие папки агентов
docs/                документация, ROADMAP, макет
```

## Документация

- [docs/README.md](docs/README.md) — карта документов для разработчиков.
- [docs/ROADMAP.md](docs/ROADMAP.md) — что сделано и что дальше.
- [docs/mockup.md](docs/mockup.md) — согласованный макет; интерактивный макет —
  <https://claude.ai/artifact/5m3u3Mtq5j9djAuvVpMiZp>.
- [docs/infrastructure.md](docs/infrastructure.md) — инфраструктура разработки и принятые решения.
- [AGENTS.md](AGENTS.md) — правила для ИИ-агентов.

## Лицензия

[Apache License 2.0](LICENSE). Сторонние компоненты — [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
