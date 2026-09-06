# AGENTS.md

## Проект
`llm-cli` — консольный C#/.NET 8 CLI (top-level statements, один файл `Program.cs`, без внешних зависимостей) для запроса OpenAI-совместимого чат-API (DeepSeek по умолчанию). Подробности логики и конфигурации — в `Summary.md`, журнал изменений — в `changes.md`.

## Репозиторий и ветки (GitHub: vapvapvap/ai-challenge)
Репозиторий — публичная копия исходников. Локальная рабочая директория: `C:\Develop\llm-cli-source`. Исходная (не-репозиторий) директория с активными доработками: `C:\Develop\llm-cli`.

- `main` — базовая версия исходников (коммит `0490eac`).
- `L1` — от `main`, базовая версия (указывает на `0490eac`).
- `L2` — от `L1`, доработки: системный `master_prompt.txt`, `top_p`/`thinking`, фиксированный путь конфига `C:\Props\llm-cli.properties` (commit `7729aff`).
- `L3` — от `main`, базовая версия.
- `L5` — от `main`, эксперимент: сравнение трёх моделей (`deepseek-v4-flash`/`deepseek-v4-pro`/`deepseek-v4-flash-vision-exp`) на одном вопросе; в `Program.cs` добавлен флаг `--stats` (время, токены usage, стоимость CNY — в stderr); результаты — в отдельном файле `Results-L5.md`.

## Правила (обязательно)
- **Никогда не коммитить API-ключи** (`api_key`, токены) и реальный рабочий конфиг `C:\Props\llm-cli.properties`. В репозитории `app.properties` — только шаблон с пустым `api_key=`.
- **Не коммитить артефакты сборки**: `bin/`, `obj/`, `*.exe` (в `bin\...\publish\app.properties` может лежать рабочий ключ). Они исключены `.gitignore`.
- Коммитить только исходники/данные: `Program.cs`, `llm-cli.csproj`, `app.properties`, `master_prompt.txt`, документацию (`*.md`).
- Перед каждым коммитом проверять staged-файлы на секреты (поиск `sk-`, `github_pat_`, заполненного `api_key=`).
- Новые ветки создавать от нужной базы явно (например, `L3` от `main`, `L2` от `L1`).
- Значимые изменения фиксировать в `changes.md`.

## Конфигурация (справка)
- Ключи файла настроек: `base_url`, `api_key`, `model`, `temperature`, `top_p`, `max_tokens`, `presence_penalty`, `frequency_penalty`, `timeout_seconds`, `thinking` (`enabled`|`disabled`). `api_key` также из env `LLM_API_KEY`/`DEEPSEEK_API_KEY`.
- В версии `L2`: конфиг по умолчанию — фиксированный `C:\Props\llm-cli.properties` (вне репозитория); `master_prompt.txt` ищется рядом с exe, затем в текущей папке, и вставляется первым как `role=system`. Ветка `L2` ведётся как отдельная задача и CLI-флаги (см. ниже) в неё НЕ переносились.
- **CLI-опции (в `main` и производных `L1`/`L3`/`L4`/`L5`)**: параметры можно указывать прямо в командной строке, они **перекрывают** файл настроек. Приоритет: **опция CLI > файл настроек > значение по умолчанию**.
  - `--model|-m <id>`, `--temperature|-t <число>`, `--top-p <0..1>`, `--max-tokens <int>`, `--presence-penalty <число>`, `--frequency-penalty <число>`, `--thinking enabled|disabled|on|off`, `--timeout <сек>`, `--base-url <url>`. Допустима форма `--опция=значение`.
  - `--stats` (в ветке `L5`) — вывести в stderr метрики запроса: время, токены (`usage`), стоимость CNY (прайс DeepSeek встроен в `Program.cs`).
  - `--config|-c <путь>` — путь к файлу настроек; `-h|--help` — справка.
  - `api_key` через CLI не передаётся (только файл/env), чтобы ключ не попадал в список процессов.
  - **`top_k` API DeepSeek не поддерживает** — используйте `top_p`.

## Модели DeepSeek, доступные по токену
По токену из конфига (проверено GET `/models`) доступны ровно три модели:

| Модель | Назначение |
|---|---|
| `deepseek-v4-flash` | быстрая/дешёвая, ежедневные и короткие запросы (модель по умолчанию) |
| `deepseek-v4-pro` | старшая модель, сложные задачи, выше качество |
| `deepseek-v4-flash-vision-exp` | экспериментальная, поддержка vision-входа |

Как использовать — выбрать модель и параметры через CLI (файл остаётся шаблоном):
```
llm-cli -m deepseek-v4-pro --temperature 0.7 --top-p 1.0 "Текст запроса"
llm-cli -m deepseek-v4-flash --thinking disabled "Текст запроса"
llm-cli -m deepseek-v4-flash-vision-exp --max-tokens 2048 "Опиши изображение"
```
Можно указывать только переопределяемые параметры — остальные берутся из файла настроек (или значений по умолчанию). Список актуальных моделей можно получить и самому:
```
llm-cli --help   # справка по опциям; модели перечислены у --model
```

## Сборка
- `dotnet build -c Release`
- `dotnet publish -c Release -r win-x64 --self-contained true`
- При NU1100 проверить источник `nuget.org` в `%APPDATA%\NuGet\NuGet.Config`.
