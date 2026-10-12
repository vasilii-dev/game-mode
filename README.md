<p align="center">
  <img src="src/GameMode.Windows/Assets/Brand/Icon.png" width="220" height="220" alt="Логотип Game Mode">
</p>

<h1 align="center">Game Mode</h1>

<p align="center">Контроль фоновых приложений во время игры.</p>

<p align="center">
  <a href="https://github.com/vasilii-dev/game-mode/releases"><img src="https://img.shields.io/badge/Beta-0.1.1-2F6B48?style=for-the-badge&amp;labelColor=20252B" alt="Beta 0.1.1"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-275C91?style=for-the-badge&amp;labelColor=20252B" alt="Windows 10 | 11">
</p>

<p align="center">
  <a href="https://github.com/vasilii-dev/game-mode/releases">Скачать</a> ·
  <a href="docs/guide.md">Руководство</a> ·
  <a href="https://github.com/vasilii-dev/game-mode/issues/new/choose">Сообщить об ошибке</a>
</p>

Game Mode работает в трее и закрывает подходящие фоновые приложения, когда обнаружена игра. По умолчанию — **строго больше 200 МиБ Working Set**, проверка раз в **10 секунд**. Порог и интервал настраиваются.

## Что умеет

- **Обнаруживать игры:** Dota 2, Counter-Strike 2, Roblox и ваши `.exe`. Детект отдельных игр можно временно отключить.
- **Беречь важное:** игра, Windows, службы, антивирусы и приложения из исключений защищены. Непроверенные процессы пропускаются.
- **Закрывать по порядку:** сначала самые ресурсоёмкие приложения; обычное закрытие перед разрешённым принудительным завершением.
- **Возвращать приложения:** повторный запуск подходящих закрытых Game Mode программ свёрнутыми, без дубликатов. Возврат можно отключить.
- **Работать спокойно:** три состояния режима, автозагрузка, тихие уведомления, журнал, множественный выбор и настоящие иконки программ. Экспериментальный детект Steam включается отдельно.

> **Сохраните работу перед включением.** Закрытие может уничтожить несохранённые данные. Повторный запуск не восстанавливает документы, вкладки и прежнее состояние. Проверьте список исключений.

## Скачать и запустить

1. Скачайте [ZIP для Windows x64](https://github.com/vasilii-dev/game-mode/releases/download/Beta/GameMode-Beta-0.1.1-win-x64.zip) и распакуйте **всю папку** в постоянное место.
2. Запустите `GameMode.exe` — .NET уже включён в готовую сборку.
3. Прочитайте предупреждение, настройте исключения и включите режим. Закрытие окна оставляет программу в трее; **«Выход»** завершает её.

## Собрать и внести вклад

Нужен **.NET 10 SDK** на Windows x64. Из корня проекта:

```powershell
dotnet build GameMode.slnx -c Release
dotnet run --project src/GameMode.Windows -c Release
```

[Core](src/GameMode.Core) — режим и правила; [Windows](src/GameMode.Windows) — интерфейс, трей и Win32; [tests](tests) — проверки.

[Сборка, защита и ограничения](docs/guide.md) · [Ручные сценарии](docs/manual-testing.md) · [Результаты проверок](docs/validation.md) · [Правила вклада](CONTRIBUTING.md)

---

<p align="center">
  Автор: <a href="https://github.com/vasilii-dev">vasilii-dev</a> ·
  <a href="LICENSE">GNU GPL v3</a> ·
  <a href="THIRD-PARTY-NOTICES.md">Лицензии ассетов</a>
</p>
