# changes.md — журнал изменений

Формат: дата, ветка, краткое описание, ссылка на коммит (SHA).

## 2026-09-06

### Ветка S1 — нативные stop-последовательности (конфиг + CLI)
- `S1` создана от `main`.
- `Program.cs`: добавлена поддержка нативного параметра API `stop` (обрыв генерации на стороне сервера):
  - CLI-опция `--stop <seq1>|<seq2>…` (форма `--опция=значение` тоже работает);
  - ключ конфига `stop`, значение — список последовательностей через `|`, до 16 (лимит API DeepSeek); пусто/отсутствует = выключено;
  - валидация: 0 элементов (пустое значение или только разделители) или >16 → ошибка использования (exit 2);
  - в `ChatRequest` добавлено поле `[JsonPropertyName("stop")] List<string>? Stop` — при незаданном значении поле не сериализуется (`WhenWritingNull`), поведение по умолчанию не меняется.
- `app.properties`: закомментированный образец `#stop=END|END-OF-ANSWER`.
- `Summary.md` / `AGENTS.md`: обновлены (описание ключа `stop`, опции `--stop`, ветка `S1`).
- Решение по механизму (обсуждение): отказ от «просить модель в system-промпте ставить маркер в конце» — инструкция в промпте не обрывает зациклившуюся генерацию; остановку делает только серверный `stop`. Поле `stop`/`--stop` дефолтно выключено.
- Тесты пройдены (негативные сценарии exit 2, позитивные с реальным API, регресс без `stop`, эффект обрыва на маркере).
- Commit: `e026cf3`

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

### Ветка L2 — merge `main` → `L2`
- `Program.cs`: принят код `main` (все CLI-опции, `--stop`, штрафы, `ChatThinking`) и сохранены фичи `L2`: фиксированный путь конфига по умолчанию `C:\Props\llm-cli.properties` (`ResolveConfigPath`) и вставка `master_prompt.txt` первым системным сообщением (`ResolveMasterPromptPath`).
- `app.properties`: оставлены активные настройки `L2` (`temperature=0`, `top_p=0.01`, `thinking=disabled`); добавлены закомментированные примеры новых ключей `presence_penalty`, `frequency_penalty`, `stop`.
- `AGENTS.md` / `Summary.md`: уточнено, что `L2` теперь соответствует `main` (CLI-опции присутствуют), но с фикс. путём конфига и `master_prompt.txt`.
- Сборка и смоук-тесты пройдены (фикс. путь без `--config`, `--stop`, инъекция `master_prompt.txt`).
- Commit: `bb21edb`

### Ветка L1 — от main
- Создана от `main` и запушена; указывает на `0490eac`.

### Ветка main — публикация проекта на GitHub
- Репозиторий `vapvapvap/ai-challenge` (публичный), поверх существовавшего `a1acb7f` (`.gitignore`).
- Закоммичены только исходники: `Program.cs`, `llm-cli.csproj`, `app.properties` (шаблон с пустым `api_key=`).
- Commit: `0490eac` (запушен).
- Проверено: в коммитах нет API-ключей и артефактов; `bin/obj` исключены `.gitignore`.
