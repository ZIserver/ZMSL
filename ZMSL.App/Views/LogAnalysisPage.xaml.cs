using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using ZMSL.App.Services;
using System.Text;
using System.Text.RegularExpressions;

namespace ZMSL.App.Views;

public sealed partial class LogAnalysisPage : Page
{
    private readonly LogAnalysisService _logAnalysisService;
    private CancellationTokenSource? _analysisCts;
    private bool _useZhisuiApi = true;
    private StringBuilder _fullResult = new();
    private StringBuilder _reasoningResult = new();
    private string? _prefilledLogContent;
    private DispatcherTimer? _renderTimer;
    private bool _needsRender = false;
    private bool _isReasoning = false;
    private readonly object _renderLock = new();

    public LogAnalysisPage()
    {
        this.InitializeComponent();
        _logAnalysisService = App.Services.GetRequiredService<LogAnalysisService>();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string logContent && !string.IsNullOrWhiteSpace(logContent))
        {
            _prefilledLogContent = logContent;
        }
        else if (e.Parameter is CrashAnalysisData crashData)
        {
            // 从崩溃检测传入的完整数据
            _prefilledLogContent = crashData.ToAnalysisContent();
        }
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        var config = await _logAnalysisService.GetConfigAsync();

        if (config.UseCustomConfig)
        {
            ApiProviderComboBox.SelectedIndex = 1;
            _useZhisuiApi = false;
            CustomApiUrlBox.Text = config.ApiUrl;
            CustomApiKeyBox.Password = config.ApiKey;
            CustomModelBox.Text = config.Model;
        }
        else
        {
            ApiProviderComboBox.SelectedIndex = 0;
            _useZhisuiApi = true;
        }

        // 如果有预填充的日志内容，填入并自动开始分析
        if (!string.IsNullOrWhiteSpace(_prefilledLogContent))
        {
            LogContentBox.Text = _prefilledLogContent;
            _prefilledLogContent = null;
            // 自动开始分析
            Analyze_Click(null!, null!);
        }
    }

    private void ApiProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ApiProviderComboBox.SelectedItem is ComboBoxItem item)
        {
            var tag = item.Tag?.ToString();
            _useZhisuiApi = tag == "zhisui";

            if (CustomApiPanel != null)
            {
                CustomApiPanel.Visibility = _useZhisuiApi ? Visibility.Collapsed : Visibility.Visible;
            }
        }
    }

    private async void SaveCustomConfig_Click(object sender, RoutedEventArgs e)
    {
        var config = new LogAnalysisConfig
        {
            ApiUrl = CustomApiUrlBox.Text,
            ApiKey = CustomApiKeyBox.Password,
            Model = CustomModelBox.Text,
            UseCustomConfig = true
        };
        await _logAnalysisService.SaveConfigAsync(config);
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        LogContentBox.Text = string.Empty;
    }

    private async void PasteLog_Click(object sender, RoutedEventArgs e)
    {
        var package = Clipboard.GetContent();
        if (package.Contains(StandardDataFormats.Text))
        {
            var text = await package.GetTextAsync();
            LogContentBox.Text = text;
        }
    }

    private string GetSelectedAnalysisType()
    {
        if (TypeCrash.IsChecked == true) return "crash";
        if (TypeError.IsChecked == true) return "error";
        if (TypePerformance.IsChecked == true) return "performance";
        if (TypeSecurity.IsChecked == true) return "security";
        return "general";
    }

    private string GetAnalysisTypeDisplayName(string type)
    {
        return type switch
        {
            "crash" => "崩溃分析",
            "error" => "错误分析",
            "performance" => "性能分析",
            "security" => "安全分析",
            _ => "通用分析"
        };
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(LogContentBox.Text))
        {
            TokensUsedText.Text = "请先输入要分析的日志内容";
            return;
        }

        // 在 UI 线程上获取所有需要的值
        var logContent = LogContentBox.Text;
        var analysisType = GetSelectedAnalysisType();
        var useZhisuiApi = _useZhisuiApi;

        AnalyzeButton.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Visible;
        TokensUsedText.Text = "正在连接 AI 服务...";

        EmptyStatePanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Visible;
        ResultTypeText.Text = GetAnalysisTypeDisplayName(analysisType);
        ResultTimeText.Text = DateTime.Now.ToString("HH:mm:ss");

        _fullResult.Clear();
        _reasoningResult.Clear();
        _analysisCts = new CancellationTokenSource();
        _needsRender = false;
        _isReasoning = false;

        // 初始化 RichTextBlock
        ResultRichText.Blocks.Clear();
        var initParagraph = new Paragraph();
        initParagraph.Inlines.Add(new Run { Text = "正在连接 AI 服务..." });
        ResultRichText.Blocks.Add(initParagraph);

        // 启动渲染定时器 - 每 30ms 检查一次是否需要更新 UI（更快的刷新率）
        _renderTimer = new DispatcherTimer();
        _renderTimer.Interval = TimeSpan.FromMilliseconds(30);
        _renderTimer.Tick += (s, args) =>
        {
            lock (_renderLock)
            {
                if (_needsRender)
                {
                    _needsRender = false;
                    RenderWithReasoning(ResultRichText, _reasoningResult.ToString(), _fullResult.ToString(), _isReasoning);
                    TokensUsedText.Text = _isReasoning ? "AI 正在思考..." : "正在生成...";
                }
            }
        };
        _renderTimer.Start();

        try
        {
            await Task.Run(async () =>
            {
                await _logAnalysisService.AnalyzeLogStreamAsync(
                    logContent,
                    analysisType,
                    useZhisuiApi,
                    content =>
                    {
                        lock (_renderLock)
                        {
                            _isReasoning = false;
                            _fullResult.Append(content);
                            _needsRender = true;
                        }
                    },
                    tokens =>
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            TokensUsedText.Text = $"消耗 {tokens} Tokens";
                        });
                    },
                    reasoning =>
                    {
                        lock (_renderLock)
                        {
                            _isReasoning = true;
                            _reasoningResult.Append(reasoning);
                            _needsRender = true;
                        }
                    },
                    _analysisCts.Token);
            });

            // 最终渲染
            DispatcherQueue.TryEnqueue(() =>
            {
                lock (_renderLock)
                {
                    RenderWithReasoning(ResultRichText, _reasoningResult.ToString(), _fullResult.ToString(), false);
                    if (TokensUsedText.Text == "正在生成..." || TokensUsedText.Text == "AI 正在思考...")
                    {
                        TokensUsedText.Text = "生成完成";
                    }
                }
            });
        }
        catch (OperationCanceledException)
        {
            TokensUsedText.Text = "已取消";
        }
        finally
        {
            _renderTimer?.Stop();
            _renderTimer = null;
            AnalyzeButton.Visibility = Visibility.Visible;
            CancelButton.Visibility = Visibility.Collapsed;
            _analysisCts?.Dispose();
            _analysisCts = null;
        }
    }

    private void CancelAnalysis_Click(object sender, RoutedEventArgs e)
    {
        _analysisCts?.Cancel();
        AnalyzeButton.Visibility = Visibility.Visible;
        CancelButton.Visibility = Visibility.Collapsed;
    }

    private void CopyResult_Click(object sender, RoutedEventArgs e)
    {
        var result = new StringBuilder();
        if (_reasoningResult.Length > 0)
        {
            result.AppendLine("【思考过程】");
            result.AppendLine(_reasoningResult.ToString());
            result.AppendLine();
            result.AppendLine("【分析结果】");
        }
        result.Append(_fullResult.ToString());

        if (result.Length > 0)
        {
            var package = new DataPackage();
            package.SetText(result.ToString());
            Clipboard.SetContent(package);
        }
    }

    /// <summary>
    /// 渲染包含思考过程的内容
    /// </summary>
    private void RenderWithReasoning(RichTextBlock richTextBlock, string reasoning, string content, bool isStillReasoning)
    {
        richTextBlock.Blocks.Clear();

        // 如果有思考过程，先渲染思考过程
        if (!string.IsNullOrEmpty(reasoning))
        {
            // 思考过程标题
            var reasoningHeader = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
            reasoningHeader.Inlines.Add(new Run
            {
                Text = "思考过程" + (isStillReasoning ? "..." : ""),
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                FontSize = 14,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.MediumPurple)
            });
            richTextBlock.Blocks.Add(reasoningHeader);

            // 思考过程内容（用斜体和较浅的颜色）
            var reasoningParagraph = new Paragraph
            {
                Margin = new Thickness(12, 0, 0, 16),
                TextIndent = 0
            };
            reasoningParagraph.Inlines.Add(new Run
            {
                Text = reasoning,
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray) 
            });
            richTextBlock.Blocks.Add(reasoningParagraph);

            // 分隔线
            if (!string.IsNullOrEmpty(content))
            {
                var separator = new Paragraph { Margin = new Thickness(0, 8, 0, 16) };
                separator.Inlines.Add(new Run
                {
                    Text = "────────────────────────────────",
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LightGray)
                });
                richTextBlock.Blocks.Add(separator);
            }
        }

        // 渲染正常内容
        if (!string.IsNullOrEmpty(content))
        {
            // 如果有思考过程，添加结果标题
            if (!string.IsNullOrEmpty(reasoning))
            {
                var resultHeader = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
                resultHeader.Inlines.Add(new Run
                {
                    Text = "📝 分析结果",
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    FontSize = 14
                });
                richTextBlock.Blocks.Add(resultHeader);
            }

            // 渲染 Markdown 内容
            RenderMarkdownContent(richTextBlock, content);
        }
        else if (string.IsNullOrEmpty(reasoning))
        {
            var p = new Paragraph();
            p.Inlines.Add(new Run { Text = "正在连接 AI 服务..." });
            richTextBlock.Blocks.Add(p);
        }
    }

    /// <summary>
    /// 渲染 Markdown 内容（不清空 richTextBlock）
    /// </summary>
    private void RenderMarkdownContent(RichTextBlock richTextBlock, string markdown)
    {
        var lines = markdown.Split('\n');
        Paragraph? currentParagraph = null;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (currentParagraph != null)
                {
                    richTextBlock.Blocks.Add(currentParagraph);
                    currentParagraph = null;
                }
                continue;
            }

            var trimmedLine = line.TrimStart();

            // 标题
            if (trimmedLine.StartsWith("### "))
            {
                if (currentParagraph != null) richTextBlock.Blocks.Add(currentParagraph);
                currentParagraph = new Paragraph { Margin = new Thickness(0, 8, 0, 4) };
                currentParagraph.Inlines.Add(new Run
                {
                    Text = trimmedLine.Substring(4),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    FontSize = 14
                });
                richTextBlock.Blocks.Add(currentParagraph);
                currentParagraph = null;
            }
            else if (trimmedLine.StartsWith("## "))
            {
                if (currentParagraph != null) richTextBlock.Blocks.Add(currentParagraph);
                currentParagraph = new Paragraph { Margin = new Thickness(0, 12, 0, 6) };
                currentParagraph.Inlines.Add(new Run
                {
                    Text = trimmedLine.Substring(3),
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    FontSize = 16
                });
                richTextBlock.Blocks.Add(currentParagraph);
                currentParagraph = null;
            }
            else if (trimmedLine.StartsWith("# "))
            {
                if (currentParagraph != null) richTextBlock.Blocks.Add(currentParagraph);
                currentParagraph = new Paragraph { Margin = new Thickness(0, 16, 0, 8) };
                currentParagraph.Inlines.Add(new Run
                {
                    Text = trimmedLine.Substring(2),
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    FontSize = 18
                });
                richTextBlock.Blocks.Add(currentParagraph);
                currentParagraph = null;
            }
            // 列表项
            else if (trimmedLine.StartsWith("- ") || trimmedLine.StartsWith("* "))
            {
                if (currentParagraph != null) richTextBlock.Blocks.Add(currentParagraph);
                currentParagraph = new Paragraph { Margin = new Thickness(16, 2, 0, 2) };
                currentParagraph.Inlines.Add(new Run { Text = "• " });
                AddFormattedText(currentParagraph, trimmedLine.Substring(2));
                richTextBlock.Blocks.Add(currentParagraph);
                currentParagraph = null;
            }
            // 数字列表
            else if (Regex.IsMatch(trimmedLine, @"^\d+\.\s"))
            {
                if (currentParagraph != null) richTextBlock.Blocks.Add(currentParagraph);
                var match = Regex.Match(trimmedLine, @"^(\d+\.)\s(.*)");
                if (match.Success)
                {
                    currentParagraph = new Paragraph { Margin = new Thickness(16, 2, 0, 2) };
                    currentParagraph.Inlines.Add(new Run { Text = match.Groups[1].Value + " " });
                    AddFormattedText(currentParagraph, match.Groups[2].Value);
                    richTextBlock.Blocks.Add(currentParagraph);
                    currentParagraph = null;
                }
            }
            // 代码块
            else if (trimmedLine.StartsWith("```"))
            {
                continue;
            }
            // 普通段落
            else
            {
                if (currentParagraph == null)
                {
                    currentParagraph = new Paragraph { Margin = new Thickness(0, 4, 0, 4) };
                }
                else
                {
                    currentParagraph.Inlines.Add(new Run { Text = " " });
                }
                AddFormattedText(currentParagraph, line);
            }
        }

        if (currentParagraph != null)
        {
            richTextBlock.Blocks.Add(currentParagraph);
        }
    }

    private void RenderMarkdown(RichTextBlock richTextBlock, string markdown)
    {
        richTextBlock.Blocks.Clear();

        if (string.IsNullOrEmpty(markdown))
        {
            var p = new Paragraph();
            p.Inlines.Add(new Run { Text = "正在分析..." });
            richTextBlock.Blocks.Add(p);
            return;
        }

        var lines = markdown.Split('\n');
        Paragraph? currentParagraph = null;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (currentParagraph != null)
                {
                    richTextBlock.Blocks.Add(currentParagraph);
                    currentParagraph = null;
                }
                continue;
            }

            var trimmedLine = line.TrimStart();

            if (trimmedLine.StartsWith("### "))
            {
                if (currentParagraph != null) richTextBlock.Blocks.Add(currentParagraph);
                currentParagraph = new Paragraph { Margin = new Thickness(0, 8, 0, 4) };
                currentParagraph.Inlines.Add(new Run
                {
                    Text = trimmedLine.Substring(4),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    FontSize = 14
                });
                richTextBlock.Blocks.Add(currentParagraph);
                currentParagraph = null;
            }
            else if (trimmedLine.StartsWith("## "))
            {
                if (currentParagraph != null) richTextBlock.Blocks.Add(currentParagraph);
                currentParagraph = new Paragraph { Margin = new Thickness(0, 12, 0, 6) };
                currentParagraph.Inlines.Add(new Run
                {
                    Text = trimmedLine.Substring(3),
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    FontSize = 16
                });
                richTextBlock.Blocks.Add(currentParagraph);
                currentParagraph = null;
            }
            else if (trimmedLine.StartsWith("# "))
            {
                if (currentParagraph != null) richTextBlock.Blocks.Add(currentParagraph);
                currentParagraph = new Paragraph { Margin = new Thickness(0, 16, 0, 8) };
                currentParagraph.Inlines.Add(new Run
                {
                    Text = trimmedLine.Substring(2),
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    FontSize = 18
                });
                richTextBlock.Blocks.Add(currentParagraph);
                currentParagraph = null;
            }
            else if (trimmedLine.StartsWith("- ") || trimmedLine.StartsWith("* "))
            {
                if (currentParagraph != null) richTextBlock.Blocks.Add(currentParagraph);
                currentParagraph = new Paragraph { Margin = new Thickness(16, 2, 0, 2) };
                currentParagraph.Inlines.Add(new Run { Text = "• " });
                AddFormattedText(currentParagraph, trimmedLine.Substring(2));
                richTextBlock.Blocks.Add(currentParagraph);
                currentParagraph = null;
            }
            else if (Regex.IsMatch(trimmedLine, @"^\d+\.\s"))
            {
                if (currentParagraph != null) richTextBlock.Blocks.Add(currentParagraph);
                var match = Regex.Match(trimmedLine, @"^(\d+\.)\s(.*)");
                if (match.Success)
                {
                    currentParagraph = new Paragraph { Margin = new Thickness(16, 2, 0, 2) };
                    currentParagraph.Inlines.Add(new Run { Text = match.Groups[1].Value + " " });
                    AddFormattedText(currentParagraph, match.Groups[2].Value);
                    richTextBlock.Blocks.Add(currentParagraph);
                    currentParagraph = null;
                }
            }
            else if (trimmedLine.StartsWith("```"))
            {
                continue;
            }
            else
            {
                if (currentParagraph == null)
                {
                    currentParagraph = new Paragraph { Margin = new Thickness(0, 4, 0, 4) };
                }
                else
                {
                    currentParagraph.Inlines.Add(new Run { Text = " " });
                }
                AddFormattedText(currentParagraph, line);
            }
        }

        if (currentParagraph != null)
        {
            richTextBlock.Blocks.Add(currentParagraph);
        }
    }

    private void AddFormattedText(Paragraph paragraph, string text)
    {
        var boldPattern = @"\*\*(.+?)\*\*|__(.+?)__";
        var codePattern = @"`(.+?)`";

        int lastIndex = 0;
        var matches = Regex.Matches(text, $"{boldPattern}|{codePattern}");

        foreach (Match match in matches)
        {
            if (match.Index > lastIndex)
            {
                paragraph.Inlines.Add(new Run { Text = text.Substring(lastIndex, match.Index - lastIndex) });
            }

            if (match.Value.StartsWith("**") || match.Value.StartsWith("__"))
            {
                var boldText = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                paragraph.Inlines.Add(new Run
                {
                    Text = boldText,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold
                });
            }
            else if (match.Value.StartsWith("`"))
            {
                paragraph.Inlines.Add(new Run
                {
                    Text = match.Groups[3].Value,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange)
                });
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
        {
            paragraph.Inlines.Add(new Run { Text = text.Substring(lastIndex) });
        }

        if (matches.Count == 0)
        {
            paragraph.Inlines.Add(new Run { Text = text });
        }
    }
}
