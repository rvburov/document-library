#!/bin/bash
#
# Ставит иконку и пункт меню приложения для текущего пользователя Linux.
# Запускать НА Linux-машине после установки приложения.
# Использует напрямую Assets/Branding/icon-linux.png — мастер, нарисованный под рабочие
# столы Linux (во всю площадь холста, скругление ~16 %). Отдельные размеры не нужны:
# GNOME/KDE и прочие масштабируют его сами.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC="$ROOT/Assets/Branding"
DATA="${XDG_DATA_HOME:-$HOME/.local/share}"

install -Dm644 "$SRC/icon-linux.png"  "$DATA/icons/hicolor/512x512/apps/pdflibrary.png"
install -Dm644 "$SRC/icon-linux.png"  "$DATA/pixmaps/pdflibrary.png"
install -Dm644 "$SRC/PdfLibrary.desktop" "$DATA/applications/PdfLibrary.desktop"

command -v gtk-update-icon-cache >/dev/null && gtk-update-icon-cache -f "$DATA/icons/hicolor" || true
command -v update-desktop-database >/dev/null && update-desktop-database "$DATA/applications" || true

echo "Готово. Иконка и ярлык PdfLibrary установлены в меню приложений."
echo "ВАЖНО: строка Exec= в PdfLibrary.desktop должна указывать на бинарь PdfLibrary"
echo "       (полный путь или бинарь в PATH)."
