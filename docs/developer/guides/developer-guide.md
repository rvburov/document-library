# Руководство разработчика

## Требования

- **.NET SDK 10** (закреплен в [global.json](../../../global.json)). На Windows — `winget install
  Microsoft.DotNet.SDK.10`, на macOS — `brew install --cask dotnet-sdk`, на Linux — пакет ОС
  `dotnet-sdk-10.0`.
- Git; на Windows все команды проекта — в Git Bash.
- Для временной иконки — Python 3 с Pillow (только если перерисовывать ее).

## Запуск и сборка

```bash
dotnet run -f net10.0-windows10.0.17763   # Windows
dotnet run -f net10.0                     # macOS, Linux
dotnet build PdfLibrary.csproj            # оба таргета
```

Подробно, со сборкой дистрибутивов — [build-and-run.md](build-and-run.md).

## Данные при разработке

В Debug данные лежат в `.localdata/` в корне репозитория (путь находится по маркеру
`PdfLibrary.csproj`, см. [AppSettings](../../../Utils/AppSettings.cs)): изолированно от настоящей
библиотеки, переживает снос `artifacts/`. В Release — папка данных пользователя ОС. Переменная
`PDFLIBRARY_DATA` задает любую другую папку. У рабочей папки агента своя `.localdata/`: программа,
запущенная оттуда, начинает с пустой библиотеки.

| Файл | Что | Если удалить |
|---|---|---|
| `library.db` | источники, группы, метки, документы, настройки (EF Core) | библиотека начнется заново |
| `library-cache.db` | прочитанный текст для поиска, страницы, форматы листов, названия (FTS5) | все прочитается заново |
| `thumbs/` | миниатюры первых страниц | нарисуются заново |
| `logs.db` | журнал, 30 дней | журнал начнется заново |
| `crash.log` | падения и сбои самого журнала | — |
| `installs.json` | папки установки | установщик не узнает прежние копии |

## Миграции EF Core

Схема `library.db` управляется миграциями EF Core; они применяются при старте
([LibraryStore](../../../Database/LibraryStore.cs), `Database.Migrate()`). Добавление новой:

```bash
dotnet tool restore
dotnet ef migrations add ИмяМиграции --framework net10.0 --output-dir Database/Migrations \
  --msbuildprojectextensionspath artifacts/obj/PdfLibrary
```

- `--msbuildprojectextensionspath` нужен из-за `UseArtifactsOutput`: `obj` лежит в
  `artifacts/obj/PdfLibrary`, и без пути EF CLI не находит проект.
- `--framework` — из-за многотаргетности: EF CLI требует один таргет; миграции от платформы не
  зависят.
- Версия `dotnet-ef` в [dotnet-tools.json](../../../dotnet-tools.json) совпадает с EF Core.

**Правило:** миграции только сохраняют данные — без drop/recreate и потери строк. Кеш
`library-cache.db` миграций не имеет: он пересобираемый, схему создает сам
([DocTextIndex](../../../Database/DocIndex/DocTextIndex.cs)), а при повреждении пересоздается.

## Правила, которые держат тесты

- **Сети в программе нет.** Сетевые API запрещены [BannedSymbols.txt](../../../BannedSymbols.txt)
  (ошибка сборки RS0030) и текстовым тестом `NoNetworkTests`. Сетевые папки — это файлы.
- **SQLite — только через [SqliteGate](../../../Database/SqliteGate.cs)**: обход гонки пула
  Microsoft.Data.Sqlite (`SqliteGateConventionTests`).
- **PDFium — только через [PdfGate](../../../Core/Documents/PdfGate.cs)**: библиотека не
  потокобезопасна, все вызовы идут одной очередью (`PdfGateConventionTests`).
- **Нет буквы «е с точками»** в текстах, комментариях и документации (`NoYoLetterTests`), кроме
  кода свертки при поиске.

## Диагностика сбоев

- **Журнал** — `logs.db`, экран «Настройки → Диагностика»: фильтр по уровню, выгрузка в CSV.
  Пишутся запуск, проверки папок, чтение, ошибки, медленные операции и зависания интерфейса
  (`UiWatchdog`, `FreezeDetector`).
- **`crash.log`** — независимый резерв: необработанные исключения (`AppDomain`, незамеченные
  задачи, исключения UI-цикла Avalonia) и отказы самого приемника журнала. Отчеты приемника о
  штатной работе туда не пишутся. У пользователя вылет не бесследный: просите `crash.log` и
  выгрузку журнала.

## Куда смотреть

- Устройство программы — [architecture/overview.md](../architecture/overview.md).
- Лента, поиск, типы документов, источники — [features/library.md](../features/library.md).
- Панель чтения — [features/reader.md](../features/reader.md).
- Оформление — [ui/theming.md](../ui/theming.md).
- Правила для ИИ-агентов и параллельная работа — [AGENTS.md](../../../AGENTS.md).

## Связанные документы

[[build-and-run]], [[install-and-update]], [[../architecture/overview]]
