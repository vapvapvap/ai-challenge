using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

const int ExitOk = 0;
const int ExitApiError = 1;
const int ExitUsage = 2;

const string DefaultConfigPath = @"C:\Props\llm-cli.properties";

Console.OutputEncoding = new UTF8Encoding(false);

var JsonOpts = new JsonSerializerOptions
{
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true,
};

string? configPath = null;
var positionals = new List<string>();

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--config" or "-c":
            if (i + 1 >= args.Length)
            {
                Console.Error.WriteLine("Ошибка: после --config ожидается путь к файлу.");
                return ExitUsage;
            }
            configPath = args[++i];
            break;
        case "--help" or "-h" or "/?":
            PrintUsage();
            return ExitOk;
        default:
            if (args[i].StartsWith("--config=", StringComparison.Ordinal))
                configPath = args[i]["--config=".Length..];
            else
                positionals.Add(args[i]);
            break;
    }
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
    Console.Error.WriteLine("Ошибка: файл настроек не найден. Создайте C:\\Props\\llm-cli.properties или укажите --config <путь>.");
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

string baseUrl = props.GetValueOrDefault("base_url", "https://api.deepseek.com").Trim().TrimEnd('/');
if (baseUrl.Length == 0)
{
    Console.Error.WriteLine("Ошибка: пустой base_url в файле настроек.");
    return ExitUsage;
}

string model = props.GetValueOrDefault("model", "deepseek-v4-flash").Trim();
if (model.Length == 0)
{
    Console.Error.WriteLine("Ошибка: пустой model в файле настроек.");
    return ExitUsage;
}

double? temperature = TryParseDouble(props.GetValueOrDefault("temperature"));
double? topP = TryParseDouble(props.GetValueOrDefault("top_p"));
int? maxTokens = TryParseInt(props.GetValueOrDefault("max_tokens"));
double timeoutSeconds = props.GetValueOrDefault("timeout_seconds", "120") is { } rawT && double.TryParse(rawT, out double t) && t > 0 ? t : 120;

string? thinking = props.GetValueOrDefault("thinking")?.Trim().ToLowerInvariant();
if (thinking is not ("enabled" or "disabled"))
    thinking = null;

var payload = new ChatRequest
{
    Model = model,
    Messages = new List<ChatMessage> { new() { Content = prompt } },
    Stream = false,
    Temperature = temperature,
    TopP = topP,
    MaxTokens = maxTokens,
    Thinking = thinking is null ? null : new ThinkingMode { Type = thinking },
};

string? masterPath = ResolveMasterPromptPath();
if (masterPath is not null)
{
    string masterPrompt;
    try
    {
        masterPrompt = File.ReadAllText(masterPath).Trim();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Ошибка чтения файла '{masterPath}': {ex.Message}");
        return ExitUsage;
    }

    if (masterPrompt.Length > 0)
    {
        payload.Messages.Insert(0, new ChatMessage { Role = "system", Content = masterPrompt });
    }
}

string requestBody = JsonSerializer.Serialize(payload, JsonOpts);
string endpoint = baseUrl + "/chat/completions";

using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

string responseBody;
try
{
    using var httpContent = new StringContent(requestBody, Encoding.UTF8, "application/json");
    using HttpResponseMessage response = await client.PostAsync(endpoint, httpContent);
    responseBody = await response.Content.ReadAsStringAsync();

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

    return File.Exists(DefaultConfigPath) ? DefaultConfigPath : null;
}

string? ResolveMasterPromptPath()
{
    string[] candidates =
    {
        Path.Combine(AppContext.BaseDirectory, "master_prompt.txt"),
        Path.Combine(Directory.GetCurrentDirectory(), "master_prompt.txt"),
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

void PrintUsage()
{
    Console.WriteLine("Использование: llm-cli [--config <файл.properties>] \"<текст запроса>\"");
    Console.WriteLine();
    Console.WriteLine("Аргументы:");
    Console.WriteLine("  --config, -c <путь>   путь к файлу настроек (по умолчанию C:\\Props\\llm-cli.properties)");
    Console.WriteLine("  -h, --help            показать эту справку");
    Console.WriteLine();
    Console.WriteLine("Файл настроек (ключ=значение): base_url, api_key, model, temperature, top_p, max_tokens, timeout_seconds, thinking.");
    Console.WriteLine("thinking=enabled|disabled — режим размышлений модели (DeepSeek); для стабильных ответов используйте");
    Console.WriteLine("temperature=0 и thinking=disabled (полной детерминированности API не гарантирует).");
    Console.WriteLine("api_key также можно задать переменными окружения LLM_API_KEY или DEEPSEEK_API_KEY.");
    Console.WriteLine();
    Console.WriteLine("Файл master_prompt.txt (рядом с exe или в текущей папке): если существует и непустой,");
    Console.WriteLine("его содержимое добавляется в начало запроса как системное сообщение (role=system).");
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
    [JsonPropertyName("thinking")] public ThinkingMode? Thinking { get; set; }
}

internal sealed class ThinkingMode
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
}

internal sealed class ChatMessage
{
    [JsonPropertyName("role")] public string Role { get; set; } = "user";
    [JsonPropertyName("content")] public string Content { get; set; } = "";
}

internal sealed class ChatResponse
{
    [JsonPropertyName("choices")] public List<Choice>? Choices { get; set; }
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
