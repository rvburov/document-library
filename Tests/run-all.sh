#!/usr/bin/env bash
# Прогон автотестов «Библиотеки PDF».
#   ./Tests/run-all.sh   — Unit (быстрый гейт) + Integration (настоящая SQLite и PDF)
#                          + стенд экранов (Tests/Screens, живая Avalonia без экрана)
#
# Стенд — отдельный проект, в тесты он не входит и, пока его не гонял общий прогон, мог бы стоять
# красным месяцами. Расхождение — код возврата 1, и прогон останавливается.
set -euo pipefail
cd "$(dirname "$0")/.."   # корень репозитория

# Avalonia стенда иначе пробует отправить телеметрию сборки.
export AVALONIA_TELEMETRY_OPTOUT=1

echo "== Unit (быстрый гейт) =="
dotnet test Tests/Unit

echo
echo "== Integration (SQLite, PDF, кеш и поиск, журнал) =="
dotnet test Tests/Integration

echo
echo "== Стенд экранов =="
dotnet run --project Tests/Screens
