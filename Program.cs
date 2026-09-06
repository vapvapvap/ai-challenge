using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

const int ExitOk = 0;
const int ExitApiError = 1;
const int ExitUsage = 2;

Console.OutputEncoding = new UTF8Encoding(false);

var JsonOpts = new JsonSerializerOptions
{
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true,
};

string? configPath = null;
bool stats = false;
var positionals = new List<string>();
var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

try
{
    for (int i = 0; i < args.Length; i++)
    {
        string arg = args[i];

        if (!arg.StartsWith('-') && !arg.StartsWith('/'))
        {
            positionals.Add(arg);
            continue;
        }

        string name = arg;
        string? inlineValue = null;
        int eq = arg.IndexOf('=');
        if (eq > 0)
        {
            name = arg[..eq];
            inlineValue = arg[(eq + 1)..];
        }

        string Value()
        {
            if (inlineValue is not null)
                return inlineValue;
            if (i + 1 >= args.Length)
                throw new ArgumentException($"после {name} ожидается значение.");
            return args[++i];
        }

        switch (name)
        {
            case "--config" or "-c":
                configPath = Value();
                break;
            case "--help" or "-h" or "/?":
                PrintUsage();
                return ExitOk;
            case "--model" or "-m":
                overrides["model"] = Value();
                break;
            case "--temperature" or "-t":
                overrides["temperature"] = Value();
                break;
            case "--top-p":
                overrides["top_p"] = Value();
                break;
            case "--max-tokens":
                overrides["max_tokens"] = Value();
                break;
            case "--presence-penalty":
                overrides["presence_penalty"] = Value();
                break;
            case "--frequency-penalty":
                overrides["frequency_penalty"] = Value();
                break;
            case "--thinking":
                overrides["thinking"] = Value();
                break;
            case "--timeout" or "--timeout-seconds":
                overrides["timeout_seconds"] = Value();
                break;
            case "--base-url":
                overrides["base_url"] = Value();
                break;
            case "--stats":
                stats = true;
                break;
            case "--stop":
                overrides["stop"] = Value();
                break;
            default:
                throw new ArgumentException($"неизвестный аргумент: {arg}.");
        }
    }
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"Ошибка: {ex.Message}");
    return ExitUsage;
}

if (positionals.Count == 0)
{
    Console.Error.WriteLine("Ошибка: не передан текст запроса.");
    PrintUsage();
    return ExitUsage;
}

string prompt = string.Join(' ', positionals);

string? resolvedConfig = ResolveConfigPath(configPath);
if (resolvedConfig is null)
{
    Console.Error.WriteLine("Ошибка: файл настроек не найден. Создайте app.properties рядом с исполняемым файлом или укажите --config <путь>.");
    return ExitUsage;
}

Dictionary<string, string> props;
try
{
    props = ParseProperties(resolvedConfig);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Ошибка чтения файла настроек '{resolvedConfig}': {ex.Message}");
    return ExitUsage;
}

string apiKey = props.GetValueOrDefault("api_key", "").Trim();
if (apiKey.Length == 0)
    apiKey = Environment.GetEnvironmentVariable("LLM_API_KEY") ?? "";
if (apiKey.Length == 0)
    apiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY") ?? "";

if (apiKey.Length == 0)
{
    Console.Error.WriteLine("Ошибка: не задан api_key (в файле настроек или переменных окружения LLM_API_KEY / DEEPSEEK_API_KEY).");
    return ExitUsage;
}

string GetSetting(string key, string? def)
{
    if (overrides.TryGetValue(key, out string? cliValue))
        return cliValue.Trim();
    if (props.TryGetValue(key, out string? fileValue))
        return fileValue.Trim();
    return def ?? "";
}

string baseUrl = GetSetting("base_url", "https://api.deepseek.com").TrimEnd('/');
if (baseUrl.Length == 0)
{
    Console.Error.WriteLine("Ошибка: пустой base_url.");
    return ExitUsage;
}

string model = GetSetting("model", "deepseek-v4-flash");
if (model.Length == 0)
{
    Console.Error.WriteLine("Ошибка: пустой model.");
    return ExitUsage;
}

double? temperature = TryParseDouble(GetSetting("temperature", null));
double? topP = TryParseDouble(GetSetting("top_p", null));
double? presencePenalty = TryParseDouble(GetSetting("presence_penalty", null));
double? frequencyPenalty = TryParseDouble(GetSetting("frequency_penalty", null));
int? maxTokens = TryParseInt(GetSetting("max_tokens", null));

string rawStop = GetSetting("stop", null);
List<string>? stops = null;
if (rawStop.Length > 0)
{
    stops = rawStop.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
    if (stops.Count == 0 || stops.Count > 16)
    {
        Console.Error.WriteLine("Ошибка: stop должен содержать от 1 до 16 последовательностей, разделённых '|'.");
        return ExitUsage;
    }
}

string rawTimeout = GetSetting("timeout_seconds", "120");
double timeoutSeconds = double.TryParse(rawTimeout, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double timeoutVal) && timeoutVal > 0 ? timeoutVal : 120;

ChatThinking? thinking = null;
string rawThinking = GetSetting("thinking", null);
if (rawThinking.Length > 0)
{
    thinking = rawThinking.ToLowerInvariant() switch
    {
        "enabled" or "on" => new ChatThinking { Type = "enabled" },
        "disabled" or "off" => new ChatThinking { Type = "disabled" },
        _ => null,
    };
    if (thinking is null)
    {
        Console.Error.WriteLine("Ошибка: thinking должен быть enabled, disabled, on или off.");
        return ExitUsage;
    }
}

var payload = new ChatRequest
{
    Model = model,
    Messages = new List<ChatMessage> { new() { Content = prompt } },
    Stream = false,
    Temperature = temperature,
    TopP = topP,
    MaxTokens = maxTokens,
    PresencePenalty = presencePenalty,
    FrequencyPenalty = frequencyPenalty,
    Thinking = thinking,
    Stop = stops,
};

string requestBody = JsonSerializer.Serialize(payload, JsonOpts);
string endpoint = baseUrl + "/chat/completions";

using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

string responseBody;
double elapsedMs = 0;
var requestStopwatch = System.Diagnostics.Stopwatch.StartNew();
try
{
    using var httpContent = new StringContent(requestBody, Encoding.UTF8, "application/json");
    using HttpResponseMessage response = await client.PostAsync(endpoint, httpContent);
    responseBody = await response.Content.ReadAsStringAsync();
    requestStopwatch.Stop();
    elapsedMs = requestStopwatch.Elapsed.TotalMilliseconds;

    if (!response.IsSuccessStatusCode)
    {
        string? detail = ExtractErrorMessage(responseBody);
        Console.Error.WriteLine($"Ошибка API: HTTP {(int)response.StatusCode} {response.ReasonPhrase}{(detail is null ? "" : $" — {detail}")}");
        return ExitApiError;
    }
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"Ошибка сети при обращении к {endpoint}: {ex.Message}");
    return ExitApiError;
}
catch (TaskCanceledException)
{
    Console.Error.WriteLine($"Ошибка: превышен таймаут запроса ({timeoutSeconds:0.#} с) к {endpoint}.");
    return ExitApiError;
}

try
{
    ChatResponse? parsed = JsonSerializer.Deserialize<ChatResponse>(responseBody, JsonOpts);
    string? text = parsed?.Choices?.FirstOrDefault()?.Message?.Content;

    if (string.IsNullOrEmpty(text))
    {
        string? detail = ExtractErrorMessage(responseBody);
        Console.Error.WriteLine($"Ошибка: в ответе API нет содержимого сообщения{(detail is null ? "." : $" ({detail})")}");
        return ExitApiError;
    }

    Console.WriteLine(text);

    if (stats)
        PrintStats(model, elapsedMs, parsed?.Usage);

    return ExitOk;
}
catch (JsonException ex)
{
    Console.Error.WriteLine($"Ошибка разбора ответа API: {ex.Message}");
    return ExitApiError;
}

string? ResolveConfigPath(string? explicitPath)
{
    if (explicitPath is not null)
    {
        return File.Exists(explicitPath) ? explicitPath : null;
    }

    string[] candidates =
    {
        Path.Combine(AppContext.BaseDirectory, "app.properties"),
        Path.Combine(Directory.GetCurrentDirectory(), "app.properties"),
    };

    return candidates.FirstOrDefault(File.Exists);
}

Dictionary<string, string> ParseProperties(string path)
{
    var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    foreach (string rawLine in File.ReadAllLines(path))
    {
        string line = rawLine.Trim();
        if (line.Length == 0 || line[0] is '#' or '!')
            continue;

        int eq = line.IndexOf('=');
        if (eq < 0)
            continue;

        string key = line[..eq].Trim();
        string value = line[(eq + 1)..].Trim();
        if (key.Length > 0)
            dict[key] = value;
    }

    return dict;
}

double? TryParseDouble(string? raw) =>
    raw is { } s && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : null;

int? TryParseInt(string? raw) =>
    raw is { } s && int.TryParse(s, out int v) && v > 0 ? v : null;

string? ExtractErrorMessage(string body)
{
    try
    {
        return JsonSerializer.Deserialize<ErrorEnvelope>(body, JsonOpts)?.Error?.Message;
    }
    catch (JsonException)
    {
        return body.Length > 0 ? body : null;
    }
}

(double InputCnyPerM, double OutputCnyPerM)? PriceCny(string model) => model switch
{
    "deepseek-v4-flash" => (1.5, 4.5),
    "deepseek-v4-pro" => (4.5, 13.5),
    "deepseek-v4-flash-vision-exp" => (1.5, 4.5),
    _ => null,
};

void PrintStats(string model, double elapsedMs, ChatUsage? usage)
{
    string Num(double v) => v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    Console.Error.WriteLine($"[stats] model={model}");
    Console.Error.WriteLine($"[stats] elapsed_ms={Num(elapsedMs)}");

    if (usage is null)
    {
        Console.Error.WriteLine("[stats] usage=недоступно (API не вернул usage)");
        return;
    }

    Console.Error.WriteLine($"[stats] prompt_tokens={usage.PromptTokens}");
    Console.Error.WriteLine($"[stats] completion_tokens={usage.CompletionTokens}");
    Console.Error.WriteLine($"[stats] total_tokens={usage.TotalTokens}");
    if (usage.CompletionTokens > 0 && elapsedMs > 0)
        Console.Error.WriteLine($"[stats] output_tokens_per_sec={Num(usage.CompletionTokens / (elapsedMs / 1000.0))}");

    (double InCny, double OutCny)? price = PriceCny(model);
    if (price is null)
    {
        Console.Error.WriteLine("[stats] cost=цена модели не задана в таблице");
        return;
    }

    double costOffPeak = usage.PromptTokens / 1_000_000.0 * price.Value.InCny
                        + usage.CompletionTokens / 1_000_000.0 * price.Value.OutCny;
    Console.Error.WriteLine($"[stats] cost_cny_offpeak={Num(costOffPeak)}");
    Console.Error.WriteLine($"[stats] cost_cny_peak={Num(costOffPeak * 2)}");
}

void PrintUsage()
{
    Console.WriteLine("Использование: llm-cli [опции] \"<текст запроса>\"");
    Console.WriteLine();
    Console.WriteLine("Аргументы:");
    Console.WriteLine("  --config, -c <путь>   путь к файлу настроек (по умолчанию app.properties рядом с exe, затем текущая папка)");
    Console.WriteLine("  -h, --help            показать эту справку");
    Console.WriteLine();
    Console.WriteLine("Опции переопределяют файл настроек (приоритет: опция > файл > значение по умолчанию):");
    Console.WriteLine("  --model, -m <id>          модель (deepseek-v4-flash | deepseek-v4-pro | deepseek-v4-flash-vision-exp)");
    Console.WriteLine("  --temperature, -t <число> температура сэмплирования");
    Console.WriteLine("  --top-p <0..1>            nucleus sampling (top_k API DeepSeek не поддерживает)");
    Console.WriteLine("  --max-tokens <int>        максимум токенов в ответе");
    Console.WriteLine("  --presence-penalty <число>  штраф за повторение тем (-2..2)");
    Console.WriteLine("  --frequency-penalty <число> штраф за повторение токенов (-2..2)");
    Console.WriteLine("  --thinking <enabled|disabled|on|off>  режим рассуждений модели");
    Console.WriteLine("  --timeout, --timeout-seconds <сек>    таймаут запроса");
    Console.WriteLine("  --base-url <url>          адрес API (по умолчанию https://api.deepseek.com)");
    Console.WriteLine("  --stats                   вывести в stderr метрики запроса: время, токены (usage), стоимость (CNY)");
    Console.WriteLine("  --stop <seq1>|<seq2>...    до 16 последовательностей через '|'; API обрывает генерацию");
    Console.WriteLine("  Допустима форма --опция=значение, например --model=deepseek-v4-pro.");
    Console.WriteLine();
    Console.WriteLine("Файл настроек (ключ=значение): base_url, api_key, model, temperature, top_p, max_tokens,");
    Console.WriteLine("presence_penalty, frequency_penalty, thinking, timeout_seconds, stop.");
    Console.WriteLine("api_key также можно задать переменными окружения LLM_API_KEY или DEEPSEEK_API_KEY.");
    Console.WriteLine();
    Console.WriteLine("Коды возврата: 0 — успех; 1 — ошибка API/сети; 2 — ошибка использования/конфига.");
}

internal sealed class ChatRequest
{
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    [JsonPropertyName("messages")] public List<ChatMessage> Messages { get; set; } = new();
    [JsonPropertyName("stream")] public bool Stream { get; set; }
    [JsonPropertyName("temperature")] public double? Temperature { get; set; }
    [JsonPropertyName("top_p")] public double? TopP { get; set; }
    [JsonPropertyName("max_tokens")] public int? MaxTokens { get; set; }
    [JsonPropertyName("presence_penalty")] public double? PresencePenalty { get; set; }
    [JsonPropertyName("frequency_penalty")] public double? FrequencyPenalty { get; set; }
    [JsonPropertyName("thinking")] public ChatThinking? Thinking { get; set; }
    [JsonPropertyName("stop")] public List<string>? Stop { get; set; }
}

internal sealed class ChatMessage
{
    [JsonPropertyName("role")] public string Role { get; set; } = "user";
    [JsonPropertyName("content")] public string Content { get; set; } = "";
}

internal sealed class ChatThinking
{
    [JsonPropertyName("type")] public string Type { get; set; } = "enabled";
}

internal sealed class ChatResponse
{
    [JsonPropertyName("choices")] public List<Choice>? Choices { get; set; }
    [JsonPropertyName("usage")] public ChatUsage? Usage { get; set; }
}

internal sealed class ChatUsage
{
    [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; set; }
    [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; set; }
    [JsonPropertyName("total_tokens")] public int TotalTokens { get; set; }
}

internal sealed class Choice
{
    [JsonPropertyName("message")] public ResponseMessage? Message { get; set; }
}

internal sealed class ResponseMessage
{
    [JsonPropertyName("content")] public string? Content { get; set; }
}

internal sealed class ErrorEnvelope
{
    [JsonPropertyName("error")] public ApiError? Error { get; set; }
}

internal sealed class ApiError
{
    [JsonPropertyName("message")] public string? Message { get; set; }
}
