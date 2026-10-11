# Авторские ассеты Game Mode

Оригиналы PNG находятся в `src/GameMode.Windows/Assets/Brand`. Они предоставлены пользователем; имя в окне «Авторы» — **vasilii-dev**.

| Файл | Где используется |
| --- | --- |
| `Icon.png` | Иконка приложения, заголовки окон, основной логотип |
| `enabled.png` | Активный режим: трей, карточка режима, зелёный фон |
| `waiting.png` | Ожидание: трей, карточка режима, нейтральный фон |
| `off.png` | Пользователь отключил режим: трей, карточка, красный фон |
| `exclamation.png` | Первый запуск и пояснение риска |
| `vasilii-dev.png` | Аватарка в отдельном окне «Авторы» |

Для замены рисунков сохраните новые PNG с теми же именами. После замены иконок экспортируйте ICO заново:

```powershell
.\src\GameMode.Windows\Tray\Assets\GenerateIcons.ps1
dotnet build GameMode.slnx -c Release
```

Экспорт сохраняет рисунок и формирует кадры 16, 20, 24, 32, 40, 48, 64, 128 и 256 пикселей. PNG-оригиналы не изменяются. `app.ico` используется Windows для файла приложения, три `state-*.ico` — в трее.

Служебные иконки — локальные SVG Google Material Symbols Rounded. Их оригиналы и лицензия лежат в `Assets/Material`; `UI/MaterialIcon.cs` отображает пути и координаты SVG через WPF. Сетевые запросы для отображения не нужны. Сведения о лицензиях доступны в [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) и в окне «О приложении» → «Лицензии».

В `MainWindow` фон, изображение и цвет основной кнопки обновляются только при смене состояния. Переход длится 220 мс; отключение анимаций Windows убирает переход и эффекты появления.

## Безопасный просмотр

Можно проверить внешний вид, не включая мониторинг или закрытие процессов:

```powershell
dotnet run --project src/GameMode.Windows -c Release -- --preview --preview-state enabled
dotnet run --project src/GameMode.Windows -c Release -- --preview --preview-state waiting
dotnet run --project src/GameMode.Windows -c Release -- --preview --preview-state off
dotnet run --project src/GameMode.Windows -c Release -- --preview --preview-window authors
dotnet run --project src/GameMode.Windows -c Release -- --preview --preview-window warning
```

В режиме просмотра большая кнопка только переключает демонстрационные состояния. Настройки пользователя не изменяются, мониторинг не запускается. `--preview-state` и `--preview-window` применяются только вместе с `--preview`. Для сохранения клиентской области окна добавьте `--screenshot C:\path\preview.png`; после сохранения просмотр завершится.

В Beta 0.1 окно «Авторы» показывает имя и аватарку без описания. Для просмотра дополнительных настроек используйте --preview --preview-window advanced; для читающего снимка процессов — --preview --preview-window processes. Последний режим читает список настоящих процессов, но не включает мониторинг или закрытие и не меняет автозагрузку.
