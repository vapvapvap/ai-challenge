# llm-cli — сводка контекста разработки

## Суть проекта
Консольное приложение (CLI) на **C# / .NET 8**, запускаемое из командной строки/агентских систем. Получает одним строковым аргументом текст запроса (промпт), читает файл настроек `app.properties` (формат `ключ=значение`), обращается к OpenAI-совместимому Chat Completions API (по умолчанию DeepSeek) и печатает ответ модели в **stdout**. Ошибки — в **stderr**.

## Язык/стек (обоснование)
- Выбран C#/.NET 8: single-file self-contained exe, нет внешних зависимостей, рантайм на целевой машине не нужен.
- В системе был установлен только .NET-хост без SDK; пользователь установил **.NET SDK 8.0.424**.
- Альтернатива (не выбрана): Go — тоже один статический exe.

## Расположение
- Проект: `C:\Develop\llm-cli`
- Исходники: `Program.cs` (~385 строк, вся логика), `llm-cli.csproj`, шаблон `app.properties`
- Опубликованный exe: `C:\Develop\llm-cli\bin\Release\net8.0\win-x64\publish\llm-cli.exe` (single-file, self-contained ~64 МБ), рядом лежит рабочий `app.properties`
- Конфиг `app.properties` копируется в выходную папку (в csproj: `CopyToOutputDirectory="PreserveNewest"`)

## Файл настроек `app.properties` (ключи)
- `base_url` — адрес API (по умолчанию `https://api.deepseek.com`), OpenAI-совместимый, любой провайдер
- `api_key` — ключ; можно переопределить env: `LLM_API_KEY` или `DEEPSEEK_API_KEY`
- `model` — по умолчанию `deepseek-v4-flash` (доступны `deepseek-v4-pro`, `deepseek-v4-flash-vision-exp`)
- `temperature` (double, опц.), `top_p` (0..1, опц.), `max_tokens` (int>0, опц.), `presence_penalty`/`frequency_penalty` (double, опц.), `thinking` (`enabled`|`disabled`), `timeout_seconds` (по умолч. 120), `stop` (список через `|`, до 16, по умолчанию выключен)
- **Любой ключ можно переопределить из командной строки**: приоритет «опция CLI > файл > дефолт».

## Поведение/логика (Program.cs)
1. Аргументы: `--config|-c <путь>` и позиционный текст запроса; `-h|--help` — справка (exit 0). CLI-опции `--model|-m`, `--temperature|-t`, `--top-p`, `--max-tokens`, `--presence-penalty`, `--frequency-penalty`, `--thinking enabled|disabled|on|off`, `--timeout`, `--base-url`, `--stop <seq1>|<seq2>…` **перекрывают** файл настроек (приоритет: CLI > файл > дефолт). Несколько позиционных склеиваются пробелом.
2. Поиск конфига: явный `--config` → рядом с exe (`AppContext.BaseDirectory`) → текущая папка.
3. Парсинг properties: строки `ключ=значение`, trim, игнор пустых и начинающихся с `#`/`!`, без `=`, без экранирования.
4. Валидация: есть промпт, `api_key`, `base_url`, `model`; иначе ошибка в stderr.
5. Запрос: `POST {base_url}/chat/completions`, headers `Content-Type: application/json`, `Authorization: Bearer <key>`, `Accept: application/json`. Тело — JSON с `model`, `messages: [{role:"user", content:<промпт>}]`, `stream:false`, `temperature`/`top_p`/`max_tokens`/`presence_penalty`/`frequency_penalty`/`stop` (если заданы) и `thinking:{"type":"enabled"|"disabled"}` (если задан). `stop` — массив строк (до 16) или строка; задаётся списком через `|` в конфиге/CLI. Поля, равные null, не сериализуются (`DefaultIgnoreCondition.WhenWritingNull`).
6. Ответ: печатает `choices[0].message.content` в stdout; JSON-десериализация `PropertyNameCaseInsensitive`.
7. Ошибки: HTTP≠200 → извлекается `error.message` из тела; сеть (`HttpRequestException`), таймаут (`TaskCanceledException`), ошибка разбора JSON — всё в stderr.
8. Кодировка вывода: `Console.OutputEncoding = UTF8` (корректный вывод кириллицы).
9. **Коды возврата**: `0` успех; `1` ошибка API/сети; `2` ошибка использования/конфига.

## Смоук-тест — пройден
`llm-cli.exe "Напиши стих о море из четырёх строк."` → модель ответила стихом на русском, exit=0. Отрицательные сценарии (нет аргумента, нет конфига, нет ключа) дают корректные коды.

## Важные архитектурные факты/решения
- **API DeepSeek stateless**: сессий на сервере нет; «контекст» = то, что передано в `messages`. Приложение шлёт **один** `user`-месседж — каждый запуск = новый контекст, модель не помнит прошлые запросы.
- Запрос к эндпоинту формируется вручную через `Program.cs:102-107` (один элемент в `Messages`).
- **`stop`-последовательности реализованы в ветке `S1`** (нативный параметр API): ключ конфига `stop` + CLI `--stop`, список до 16 последовательностей через `|`, дефолт — выключено (поле в запрос не попадает). API обрывает генерацию на стороне сервера в момент начала совпадения (`finish_reason="stop"`), маркер в вывод не включается. Это серверный механизм — в отличие от «попросить модель поставить маркер в конце» через system-промпт, что зацикливание НЕ предотвращает (обсуждено при реализации).
- Персистентная история/интерактивный диалог не реализованы (обсуждалось как возможная доработка: REPL-режим, `--session <id>` с сохранением истории в файл).

## Окружение (важно для сборки)
- Windows (win32), AMD64. PowerShell 5.1.
- В пользовательском `NuGet.Config` (`%APPDATA%\NuGet\NuGet.Config`) **добавлен источник `nuget.org`** — до этого список источников был пуст, из-за чего падало восстановление runtime-пакетов (ошибка NU1100). Требуется для любой сборки/публикации.
- Публикация: `dotnet publish -c Release -r win-x64 --self-contained true` (в csproj уже заданы `PublishSingleFile`, `SelfContained`, `InvariantGlobalization`).
- Сборка: `dotnet build -c Release`.

## Контактный API (актуально на 2026)
- Base URL: `https://api.deepseek.com`; эндпоинт чата: `POST /chat/completions`.
- Модели: `deepseek-v4-flash`, `deepseek-v4-pro`, `deepseek-v4-flash-vision-exp` (vision — эксперим.). `thinking` по умолч. enabled (можно `{"thinking":{"type":"disabled"}}`).
- Документация: https://api-docs.deepseek.com
