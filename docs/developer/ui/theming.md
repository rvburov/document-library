# Оформление: токены, темы, кнопки

## Назначение

Интерфейс собирается только из готовых токенов и тем контролов — цвета, шрифты, размеры, линии,
иконки, виды кнопок. Новое — по образцу соседнего. Так светлая и темная темы, масштаб и размер
шрифта работают везде сами, без правок отдельных экранов. Токены — из согласованного макета
([mockup.md](../../mockup.md)); имена — как в SmartBase, поэтому перенесенная разметка ложится на
них без правки.

## Где в коде

| Файл | Что |
|---|---|
| [Assets/Styles/Colors.axaml](../../../Assets/Styles/Colors.axaml) | цвета: словари `Light` и `Dark` (`ThemeDictionaries`) |
| [Assets/Styles/Typography.axaml](../../../Assets/Styles/Typography.axaml) | шрифт Inter и размеры: `FontSizeSmall` 11, `FontSizeBase` 13, `FontSizeMedium` 16, `FontSizeLarge` 20, `FontSizeHero` 28 |
| [Assets/Styles/Metrics.axaml](../../../Assets/Styles/Metrics.axaml) | высоты, ширины, скругления (`RadiusSmall` 6 … `RadiusPill` 15), линии-границы |
| [Assets/Styles/Icons.axaml](../../../Assets/Styles/Icons.axaml) | иконки-геометрии для [Controls/Icon.cs](../../../Controls/Icon.cs) |
| [Assets/Styles/Controls.axaml](../../../Assets/Styles/Controls.axaml) | темы контролов и классы: кнопки, переключатель, галочка, чипы, карточка, строки настроек |
| [Utils/ThemeManager.cs](../../../Utils/ThemeManager.cs) | светлая, темная, как в системе |
| [Utils/Scaling.cs](../../../Utils/Scaling.cs), [Utils/FontScale.cs](../../../Utils/FontScale.cs) | масштаб интерфейса 90–125 % и размер шрифта |
| [Views/Res.cs](../../../Views/Res.cs) | ресурсы приложения из кода |

## Как работает

### Цвета

Все кисти — в словарях тем, потребители берут их через `DynamicResource`: смена темы в
«Оформлении» перекрашивает окно на ходу. Основные токены: фоны `BrushBg`, `BrushSecondaryBg`,
`BrushHover`; линии `BrushSeparator`, `BrushLineStrong`; текст `BrushPrimaryText`,
`BrushSecondaryText`; акцент `BrushAccent`, `BrushAccentLight`; смысловые пары для чипов
(`BrushRed`/`BrushRedLight`, `BrushGreen`…, `BrushOrange`…); найденное `BrushHighlight`; лист
`BrushPage`. Подсветка на листах PDF (`BrushPageHit`, `BrushPageHitCurrent`,
`BrushPageHitOutline`) одна на обе темы: листы белые всегда.

### Кнопки

Вид кнопки — тема (`Theme="{StaticResource …}"`), а не набор свойств на месте: `PrimaryButton`
(синяя, главное действие), `SecondaryButton`, `LinkButton` («Прочитать», «Открыть» на карточке),
`BarButton`, `IconButton`, `PillButton`, `SegmentButton` (сегменты «Маленькая / Средняя /
Большая»), `NavButton`, `ActivityButton`, `MenuButton`, `RowButton`. Все основаны на
`ButtonCore`. Переключатель — тема `Switch` у `ToggleButton`.

### Классы разметки

Повторяющиеся элементы — классы стилей: `Border.card` и `.selected` (карточка ленты), `Border.chip`
с `.type`, `.red`, `.orange`, `.gray`, `.green` (чипы карточки), `Border.group` и `Border.srow`
(группа и строка экранов настроек), `Border.trow` (строка дерева источников), `Border.thumb`
(миниатюра на рельсе панели чтения), `TextBlock.h2` (заголовок группы настроек).

Имя класса — общее пространство на всю программу: стиль `Border.srow` настроек однажды дал
разделители строкам дерева источников, носившим то же имя. Новый класс — с проверкой, что имя
свободно.

### Ресурсы из кода

`this.FindResource` в конструкторе контрола до его входа в дерево возвращает `UnsetValue`, и
приведение к теме кнопки падает. Код берет ресурсы приложения через `Res.Theme`, `Res.Icon`,
`Res.Brush` — с учетом текущей темы.

### Масштаб и шрифт

«Масштаб интерфейса» (90, 100, 110, 125 %) меняет размер всего окна; «Размер шрифта» (мелкий,
стандартный, крупный) — только текст, карточки подстраиваются под него сами.

## Проверено и не проверено

Стенд экранов грузит все экраны в обеих темах: опечатка в ключе ресурса роняет его при загрузке,
а не у пользователя. Кадры сверены с макетом глазами. Вид на настоящих мониторах с масштабом ОС
150–200 % — по [manual-check.md](../qa-checklists/manual-check.md).

## Связанные документы

[[../features/library]], [[../features/reader]], [[../../mockup]]
