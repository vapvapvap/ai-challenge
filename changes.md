# changes.md — журнал изменений

Формат: дата, ветка, краткое описание, ссылка на коммит (SHA).

## 2026-09-06

### Ветка L5 — эксперимент: сравнение трёх моделей на одной задаче
- `L5` создана от `main` (tip `2c8f134`).
- `Program.cs`: флаг `--stats` — выводит в **stderr** метрики запроса: полное время (Stopwatch), токены из `usage` (`prompt/completion/total`), выходные токены/с, стоимость по официальному прайсу DeepSeek (CNY/1M, тарифы непик и пик; встроенная таблица для `deepseek-v4-flash` 1.5/4.5, `deepseek-v4-pro` 4.5/13.5, `deepseek-v4-flash-vision-exp` 1.5/4.5; неизвестная модель — цена недоступна). Стандартный вывод (stdout) не меняется.
- Прогон одного вопроса («Сорока летит, а собака на хвосте сидит…») на трёх моделях при `temperature=0`, `thinking=disabled`; результаты — отдельный файл эксперимента `Results-L5.md`.
- Прайс взят с https://api-docs.deepseek.com/quick_start/pricing (CNY за 1M токенов, вход без кэш-хита).
- Commit: `1779b59`

### Ветка main — CLI-опции выбора модели и параметров
- `Program.cs`: добавлены CLI-флаги, перекрывающие файл настроек (приоритет: CLI > файл > дефолт): `--model|-m`, `--temperature|-t`, `--top-p`, `--max-tokens`, `--presence-penalty`, `--frequency-penalty`, `--thinking enabled|disabled|on|off`, `--timeout|--timeout-seconds`, `--base-url`. Поддержка формы `--опция=значение`; неизвестные опции — ошибка использования (exit 2).
- В запрос добавлены поля `top_p`, `presence_penalty`, `frequency_penalty`, `thinking` (`{"type":"enabled"|"disabled"}`); `top_k` не поддерживается API DeepSeek.
- `app.properties`: добавлены закомментированные примеры новых ключей.
- `AGENTS.md`: секция «Модели DeepSeek, доступные по токену» (`deepseek-v4-flash`, `deepseek-v4-pro`, `deepseek-v4-flash-vision-exp`) с примерами запуска; обновлена «Конфигурация».
- Распространено в ветки `L1`, `L3`, `L4` (merge из `main`); **`L2` не затронута** (отдельная задача).
- Commit: `5878b9d` (запушен).

### Ветка L3 — создана от main
- `L3` создана от `main` (коммит `0490eac`) и запушена на GitHub.
- Содержимое: базовая версия исходников (без доработок `L2`).
- Commit: `0490eac`

### Ветка L2 — доработки из C:\Develop\llm-cli (перенесены из основной разработки)
- Создана от `L1`.
- Перенесены изменения из `C:\Develop\llm-cli` (исходная рабочая директория):
  - `Program.cs`: фиксированный путь конфига по умолчанию `C:\Props\llm-cli.properties`; поддержка ключей `top_p` и `thinking` (`enabled`/`disabled`); перед user-сообщением добавляется системное сообщение из `master_prompt.txt` (если файл существует и непустой);
  - `llm-cli.csproj`: `master_prompt.txt` копируется в выходную папку (`CopyToOutputDirectory`);
  - `app.properties` (шаблон, ключ пустой): `temperature=0`, `top_p=0.01`, `thinking=disabled`;
  - новый файл `master_prompt.txt` — DIY-сценарий: ответ строго JSON-схемой (`materials`, `tools`, `difficulty`, `errorText`).
- Commit: `7729aff` (запушен).
- Исключено: исполняемые/сборочные артефакты (`bin/`, `obj/`), реальные API-ключи.

### Ветка L1 — от main
- Создана от `main` и запушена; указывает на `0490eac`.

### Ветка main — публикация проекта на GitHub
- Репозиторий `vapvapvap/ai-challenge` (публичный), поверх существовавшего `a1acb7f` (`.gitignore`).
- Закоммичены только исходники: `Program.cs`, `llm-cli.csproj`, `app.properties` (шаблон с пустым `api_key=`).
- Commit: `0490eac` (запушен).
- Проверено: в коммитах нет API-ключей и артефактов; `bin/obj` исключены `.gitignore`.
