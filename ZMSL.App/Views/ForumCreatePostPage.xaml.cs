using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Text.Encodings.Web;
using ZMSL.App.ViewModels;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;

namespace ZMSL.App.Views
{
    public sealed partial class ForumCreatePostPage : Page
    {
        public CreatePostViewModel ViewModel { get; }
        private ScrollViewer? _editorScrollViewer;
        private DispatcherTimer _debounceTimer;

        public ForumCreatePostPage()
        {
            this.InitializeComponent();
            ViewModel = App.Services.GetRequiredService<CreatePostViewModel>();
            this.DataContext = ViewModel;

            ViewModel.OnPostCreated += ViewModel_OnPostCreated;

            // 初始化防抖定时器
            _debounceTimer = new DispatcherTimer();
            _debounceTimer.Interval = System.TimeSpan.FromMilliseconds(500); // 延长至 500ms，给用户更多输入时间
            _debounceTimer.Tick += DebounceTimer_Tick;
        }

        private void ViewModel_OnPostCreated(object? sender, System.EventArgs e)
        {
            // Navigate back to ForumPage
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
            else
            {
                Frame.Navigate(typeof(ForumPage));
            }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is long postId)
            {
                await ViewModel.Initialize(postId);
            }
            else
            {
                await ViewModel.Initialize();
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            _debounceTimer.Stop();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
            else
            {
                Frame.Navigate(typeof(ForumPage));
            }
        }

        private void SubmitButton_Click(object sender, RoutedEventArgs e)
        {
            // 提交内容
            ViewModel.Content = EditorBox.Text;
            if (ViewModel.SubmitPostCommand.CanExecute(null))
            {
                ViewModel.SubmitPostCommand.Execute(null);
            }
        }

        // --- 编辑器逻辑 ---

        private void EditorBox_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateWordCount();
            
            // 查找 TextBox 内部的 ScrollViewer
            _editorScrollViewer = FindChild<ScrollViewer>(EditorBox);
            if (_editorScrollViewer != null)
            {
                _editorScrollViewer.ViewChanged += EditorScrollViewer_ViewChanged;
            }
        }

        private void EditorScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            SyncScroll();
        }

        private void EditorBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 重置定时器，实现防抖
            _debounceTimer.Stop();
            _debounceTimer.Start();

            UpdateWordCount();
            
            // 同步滚动
            SyncScroll();
        }

        private void DebounceTimer_Tick(object? sender, object e)
        {
            _debounceTimer.Stop();
            UpdatePreviewSafe();
        }

        private void UpdatePreviewSafe()
        {
            try
            {
                string text = EditorBox.Text;

                // 使用普通 TextBlock 显示纯文本
                PreviewText.Text = text;
                PreviewText.Visibility = Visibility.Visible;
                PreviewErrorText.Visibility = Visibility.Collapsed;
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Preview rendering failed: {ex}");

                // 渲染失败时显示错误信息
                PreviewErrorText.Text = $"预览渲染失败: {ex.Message}";
                PreviewErrorText.Visibility = Visibility.Visible;
            }
        }

        private bool IsIncompleteTable(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            
            // 快速检查：如果不包含管道符，大概率不是表格（虽然代码块里可能有，但不易崩）
            if (!text.Contains('|')) return false;

            // 获取最后一行
            var lines = text.Split('\n');
            var lastLine = lines[lines.Length - 1].Trim();

            // 如果最后一行以 | 开头但没有以 | 结尾 -> 危险
            if (lastLine.StartsWith("|") && !lastLine.EndsWith("|")) return true;
            
            // 如果最后一行虽然以 | 结尾，但我们处于表格的中间行（上一行是表格分隔线），
            // 且这一行看起来还没写完（比如管道符数量很少），也可以考虑暂缓
            // 但这可能误伤，主要还是防止 "正在输入" 的状态
            
            // 检查上一行是否是表格分隔线，如 |---|
            if (lines.Length > 1)
            {
                var prevLine = lines[lines.Length - 2].Trim();
                if (IsTableDivider(prevLine))
                {
                     // 上一行是分隔线，说明当前行是表格内容
                     // 如果当前行非空且不以 | 结尾，必须拦截
                     if (!string.IsNullOrEmpty(lastLine) && !lastLine.EndsWith("|")) return true;
                }
            }

            return false;
        }

        private bool IsTableDivider(string line)
        {
            if (!line.StartsWith("|") || !line.EndsWith("|")) return false;
            
            // 简单的分隔线检查：| --- | --- |
            // 去掉空格和管道符，应该只剩下 - 和 :
            var content = line.Replace("|", "").Replace(" ", "");
            return content.Length > 0 && content.All(c => c == '-' || c == ':');
        }

        private void UpdateWordCount()
        {
            var text = EditorBox.Text;
            if (string.IsNullOrEmpty(text))
            {
                WordCountText.Text = "0 字符";
            }
            else
            {
                WordCountText.Text = $"{text.Length} 字符";
            }
        }

        private void SyncScroll()
        {
            if (_editorScrollViewer == null || PreviewScrollViewer == null) return;

            // 获取编辑器的滚动比例
            double scrollableHeight = _editorScrollViewer.ScrollableHeight;
            if (scrollableHeight > 0)
            {
                double ratio = _editorScrollViewer.VerticalOffset / scrollableHeight;
                
                // 应用到预览区域
                double previewScrollableHeight = PreviewScrollViewer.ScrollableHeight;
                double targetOffset = ratio * previewScrollableHeight;
                
                PreviewScrollViewer.ChangeView(null, targetOffset, null);
            }
        }

        private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                {
                    return typedChild;
                }

                var result = FindChild<T>(child);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }

        private void MarkdownAction_Click(object sender, RoutedEventArgs e)
        {
            string? tag = null;
            if (sender is Button btn)
            {
                tag = btn.Tag as string;
            }
            else if (sender is AppBarButton button)
            {
                tag = button.Tag as string;
            }
            else if (sender is MenuFlyoutItem item)
            {
                tag = item.Tag as string;
            }

            if (tag != null)
            {
                switch (tag)
                {
                    case "Bold":
                        InsertMarkdown("**", "**");
                        break;
                    case "Italic":
                        InsertMarkdown("*", "*");
                        break;
                    case "Strikethrough":
                        InsertMarkdown("~~", "~~");
                        break;
                    case "H1":
                        InsertMarkdown("# ", "");
                        break;
                    case "H2":
                        InsertMarkdown("## ", "");
                        break;
                    case "H3":
                        InsertMarkdown("### ", "");
                        break;
                    case "H4":
                        InsertMarkdown("#### ", "");
                        break;
                    case "H5":
                        InsertMarkdown("##### ", "");
                        break;
                    case "H6":
                        InsertMarkdown("###### ", "");
                        break;
                    case "Quote":
                        InsertMarkdown("> ", "");
                        break;
                    case "CodeBlock":
                        InsertMarkdown("```\n", "\n```");
                        break;
                    case "Link":
                        InsertMarkdown("[", "](url)");
                        break;
                    case "Image":
                        InsertMarkdown("![", "](image_url)");
                        break;
                    case "List":
                        InsertMarkdown("- ", "");
                        break;
                    case "Table":
                        InsertMarkdown("| Header 1 | Header 2 |\n| -------- | -------- |\n| Cell 1   | Cell 2   |", "");
                        break;
                }
            }
        }

        private void InsertMarkdown(string prefix, string suffix)
        {
            var textBox = EditorBox;
            int selectionStart = textBox.SelectionStart;
            int selectionLength = textBox.SelectionLength;
            string text = textBox.Text;
            string selectedText = textBox.SelectedText;

            // 简单的文本插入
            string newText = text.Substring(0, selectionStart) + 
                             prefix + selectedText + suffix + 
                             text.Substring(selectionStart + selectionLength);

            textBox.Text = newText;
            
            // 恢复焦点和光标位置
            textBox.Focus(FocusState.Programmatic);
            textBox.SelectionStart = selectionStart + prefix.Length;
            textBox.SelectionLength = selectionLength;
        }

        private void EditorBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
            bool isCtrlDown = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

            if (isCtrlDown)
            {
                switch (e.Key)
                {
                    case VirtualKey.B:
                        InsertMarkdown("**", "**");
                        e.Handled = true;
                        break;
                    case VirtualKey.I:
                        InsertMarkdown("*", "*");
                        e.Handled = true;
                        break;
                    case VirtualKey.K:
                        InsertMarkdown("[", "](url)");
                        e.Handled = true;
                        break;
                }
            }
            else if (e.Key == VirtualKey.Tab)
            {
                // 处理 Tab 缩进
                InsertMarkdown("    ", "");
                e.Handled = true;
            }
        }

        private void PreviewToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is AppBarToggleButton toggle)
            {
                if (toggle.IsChecked == true)
                {
                    // 显示预览
                    PreviewColumn.Width = new GridLength(1, GridUnitType.Star);
                    PreviewScrollViewer.Visibility = Visibility.Visible;
                }
                else
                {
                    // 隐藏预览
                    PreviewColumn.Width = new GridLength(0);
                    PreviewScrollViewer.Visibility = Visibility.Collapsed;
                }
            }
        }
    }
}
