using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace ZMSL.App.Services;

public class LogAnalysisService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DatabaseService _db;

    // 智穗AI 硬编码配置
    private const string ZhisuiApiUrl = "https://api.amethyst.ltd/v1/chat/completions";
    private const string ZhisuiApiKey = "sk-3cqKZOSLuPEA0OTwVUJndLrDOADuC5VxvfA1daDePvm90pf9";
    private const string ZhisuiModel = "glm-4.7";

    // 自定义配置默认值
    private const string DefaultApiUrl = "https://api.openai.com/v1/chat/completions";
    private const string DefaultModel = "gpt-4o-mini";

    public LogAnalysisService(IHttpClientFactory httpClientFactory, DatabaseService db)
    {
        _httpClientFactory = httpClientFactory;
        _db = db;
    }

    public async Task<LogAnalysisConfig> GetConfigAsync()
    {
        var settings = await _db.GetSettingsAsync();
        return new LogAnalysisConfig
        {
            ApiUrl = settings.LogAnalysisApiUrl ?? DefaultApiUrl,
            ApiKey = settings.LogAnalysisApiKey ?? "",
            Model = settings.LogAnalysisModel ?? DefaultModel,
            UseCustomConfig = settings.UseCustomLogAnalysisConfig
        };
    }

    public async Task SaveConfigAsync(LogAnalysisConfig config)
    {
        var settings = await _db.GetSettingsAsync();
        settings.LogAnalysisApiUrl = config.ApiUrl;
        settings.LogAnalysisApiKey = config.ApiKey;
        settings.LogAnalysisModel = config.Model;
        settings.UseCustomLogAnalysisConfig = config.UseCustomConfig;
        await _db.SaveSettingsAsync(settings);
    }

    /// <summary>
    /// 流式分析日志，通过回调实时返回内容 - 真正的 SSE 流式处理
    /// </summary>
    public async Task AnalyzeLogStreamAsync(
        string logContent,
        string analysisType,
        bool useZhisuiApi,
        Action<string> onContentReceived,
        Action<int>? onComplete = null,
        Action<string>? onReasoningReceived = null,
        CancellationToken cancellationToken = default)
    {
        string apiUrl, apiKey, model;

        if (useZhisuiApi)
        {
            apiUrl = ZhisuiApiUrl;
            apiKey = ZhisuiApiKey;
            model = ZhisuiModel;
        }
        else
        {
            var config = await GetConfigAsync();
            apiUrl = config.ApiUrl;
            apiKey = config.ApiKey;
            model = config.Model;

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                onContentReceived("错误: 请先配置自定义 API 密钥");
                return;
            }
        }

        var systemPrompt = GetSystemPrompt(analysisType);
        var userPrompt = $"请分析以下 Minecraft 服务器日志：\n\n```\n{logContent}\n```";

        try
        {
            // 创建一个新的 HttpClient，不使用工厂的默认配置
            using var handler = new HttpClientHandler();
            using var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromMinutes(5);

            var requestBody = new
            {
                model = model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                max_tokens = 4096,
                temperature = 0.3,
                stream = true
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            System.Diagnostics.Debug.WriteLine($"[SSE] 请求 URL: {apiUrl}");
            System.Diagnostics.Debug.WriteLine($"[SSE] 请求体: {jsonContent.Substring(0, Math.Min(200, jsonContent.Length))}...");

            using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
            request.Headers.Add("Accept", "text/event-stream");
            request.Headers.Add("Cache-Control", "no-cache");
            request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            // 关键：使用 ResponseHeadersRead 确保不等待完整响应
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            System.Diagnostics.Debug.WriteLine($"[SSE] 响应状态: {response.StatusCode}");
            System.Diagnostics.Debug.WriteLine($"[SSE] Content-Type: {response.Content.Headers.ContentType}");

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                onContentReceived($"API 请求失败: {response.StatusCode}\n{errorContent}");
                return;
            }

            // 使用 Stream 直接读取，避免任何缓冲
            using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(responseStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1);

            int totalTokens = 0;
            var lineBuilder = new StringBuilder();
            var charBuffer = new char[1];

            // 逐字符读取，实现真正的流式处理
            while (!cancellationToken.IsCancellationRequested)
            {
                var charsRead = await reader.ReadAsync(charBuffer, 0, 1);
                if (charsRead == 0) break; // 流结束

                var c = charBuffer[0];

                if (c == '\n')
                {
                    var line = lineBuilder.ToString().Trim();
                    lineBuilder.Clear();

                    if (string.IsNullOrEmpty(line)) continue;

                    System.Diagnostics.Debug.WriteLine($"[SSE] 行: {line.Substring(0, Math.Min(80, line.Length))}");

                    if (line.StartsWith("data: "))
                    {
                        var data = line.Substring(6);

                        if (data == "[DONE]")
                        {
                            System.Diagnostics.Debug.WriteLine("[SSE] 收到 [DONE]");
                            break;
                        }

                        try
                        {
                            var chunk = JsonSerializer.Deserialize<StreamChunk>(data);
                            if (chunk?.Choices?.Length > 0)
                            {
                                var delta = chunk.Choices[0].Delta;

                                // 处理思考过程（reasoning_content）
                                if (!string.IsNullOrEmpty(delta?.ReasoningContent))
                                {
                                    System.Diagnostics.Debug.WriteLine($"[SSE] 思考: '{delta.ReasoningContent}'");
                                    onReasoningReceived?.Invoke(delta.ReasoningContent);
                                }

                                // 处理正常内容
                                if (!string.IsNullOrEmpty(delta?.Content))
                                {
                                    System.Diagnostics.Debug.WriteLine($"[SSE] 内容: '{delta.Content}'");
                                    onContentReceived(delta.Content);
                                }
                            }

                            if (chunk?.Usage != null)
                            {
                                totalTokens = chunk.Usage.TotalTokens;
                            }
                        }
                        catch (JsonException ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[SSE] JSON解析错误: {ex.Message}");
                        }
                    }
                }
                else if (c != '\r')
                {
                    lineBuilder.Append(c);
                }
            }

            onComplete?.Invoke(totalTokens);
        }
        catch (OperationCanceledException)
        {
            onContentReceived("\n\n[分析已取消]");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SSE] 异常: {ex}");
            onContentReceived($"\n\n分析失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 非流式分析（保留兼容性）
    /// </summary>
    public async Task<LogAnalysisResult> AnalyzeLogAsync(string logContent, string analysisType, bool useZhisuiApi, CancellationToken cancellationToken = default)
    {
        var result = new StringBuilder();
        int tokens = 0;

        await AnalyzeLogStreamAsync(
            logContent,
            analysisType,
            useZhisuiApi,
            content => result.Append(content),
            totalTokens => tokens = totalTokens,
            null,  // onReasoningReceived
            cancellationToken);

        var text = result.ToString();

        if (text.StartsWith("错误:") || text.StartsWith("API 请求失败") || text.Contains("分析失败:"))
        {
            return new LogAnalysisResult
            {
                Success = false,
                ErrorMessage = text
            };
        }

        return new LogAnalysisResult
        {
            Success = true,
            Analysis = text,
            TokensUsed = tokens
        };
    }

    private static string GetSystemPrompt(string analysisType)
    {
        return analysisType switch
        {
            "crash" => """
                你是一个专业的 Minecraft 服务器崩溃分析专家。用户会提供以下信息：
                - 启动命令（包含Java路径、JVM参数、内存设置等）
                - 插件列表（如果有）
                - Mod列表（如果有）
                - 服务器日志

                请分析这些信息，并提供：
                1. **崩溃原因摘要**（简洁明了）
                2. **详细的错误分析**（结合启动命令、插件/Mod和日志综合判断）
                3. **可能的解决方案**（按优先级排序）
                4. **预防建议**

                如果发现是某个插件或Mod导致的问题，请明确指出是哪个。
                如果是JVM参数或内存配置问题，请给出具体的修改建议。
                请使用中文回复，使用 Markdown 格式，便于阅读。
                """,
            "error" => """
                你是一个专业的 Minecraft 服务器错误分析专家。请分析用户提供的错误日志，并提供：
                1. **错误类型和严重程度**
                2. **错误原因分析**
                3. **解决方案建议**
                4. **是否需要立即处理**

                请使用中文回复，使用 Markdown 格式。
                """,
            "performance" => """
                你是一个专业的 Minecraft 服务器性能分析专家。请分析用户提供的日志，关注：
                1. **TPS（每秒 tick 数）问题**
                2. **内存使用情况**
                3. **区块加载问题**
                4. **实体数量问题**
                5. **插件/mod 性能影响**

                请提供具体的优化建议，使用中文回复，使用 Markdown 格式。
                """,
            "security" => """
                你是一个专业的 Minecraft 服务器安全分析专家。请分析用户提供的日志，关注：
                1. **可疑的登录尝试**
                2. **潜在的攻击行为**
                3. **异常的玩家行为**
                4. **安全配置问题**

                请提供安全建议，使用中文回复，使用 Markdown 格式。
                """,
            _ => """
                你是一个专业的 Minecraft 服务器日志分析专家。请分析用户提供的日志，提供：
                1. **日志概述**
                2. **发现的问题**（如有）
                3. **建议和解决方案**

                请使用中文回复，使用 Markdown 格式。
                """
        };
    }
}

public class LogAnalysisConfig
{
    public string ApiUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public bool UseCustomConfig { get; set; }
}

public class LogAnalysisResult
{
    public bool Success { get; set; }
    public string? Analysis { get; set; }
    public string? ErrorMessage { get; set; }
    public int TokensUsed { get; set; }
}

// 流式响应模型
public class StreamChunk
{
    [JsonPropertyName("choices")]
    public StreamChoice[]? Choices { get; set; }

    [JsonPropertyName("usage")]
    public OpenAiUsage? Usage { get; set; }
}

public class StreamChoice
{
    [JsonPropertyName("delta")]
    public StreamDelta? Delta { get; set; }
}

public class StreamDelta
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("reasoning_content")]
    public string? ReasoningContent { get; set; }
}

// OpenAI API 请求/响应模型
public class OpenAiRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<OpenAiMessage> Messages { get; set; } = new();

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 4096;

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.3;

    [JsonPropertyName("stream")]
    public bool Stream { get; set; } = false;
}

public class OpenAiMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

public class OpenAiResponse
{
    [JsonPropertyName("choices")]
    public List<OpenAiChoice>? Choices { get; set; }

    [JsonPropertyName("usage")]
    public OpenAiUsage? Usage { get; set; }
}

public class OpenAiChoice
{
    [JsonPropertyName("message")]
    public OpenAiMessage? Message { get; set; }
}

public class OpenAiUsage
{
    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}
