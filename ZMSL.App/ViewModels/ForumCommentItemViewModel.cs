using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ZMSL.Shared.DTOs;
using System;

namespace ZMSL.App.ViewModels
{
    public partial class ForumCommentItemViewModel : ObservableObject
    {
        private readonly ForumCommentDto _dto;
        private readonly Func<long, string, Task> _replyCallback;

        public ForumCommentItemViewModel(ForumCommentDto dto, Func<long, string, Task> replyCallback)
        {
            _dto = dto;
            _replyCallback = replyCallback;

            if (!string.IsNullOrEmpty(dto.AvatarUrl))
            {
                var url = dto.AvatarUrl;
                if (!url.StartsWith("http"))
                {
                        if (!url.StartsWith("/")) url = "/" + url;
                        url = "https://msl.v2.zhsdev.top" + url;
                }
                try
                {
                    AvatarImage = new BitmapImage(new Uri(url));
                }
                catch
                {
                    AvatarImage = null;
                }
            }

            if (dto.Replies != null)
            {
                // 递归创建回复的 ViewModel
                var replyVms = dto.Replies.Select(r => new ForumCommentItemViewModel(r, replyCallback)).ToList();
                AllReplies = new ObservableCollection<ForumCommentItemViewModel>(replyVms);
            }
            else
            {
                AllReplies = new ObservableCollection<ForumCommentItemViewModel>();
            }

            UpdateVisibleReplies();
        }

        public ForumCommentDto Dto => _dto;

        public long Id => _dto.Id;
        public string Username => _dto.Username;
        public string Content => _dto.Content;
        public string CreatedAt => _dto.CreatedAt.ToString("yyyy-MM-dd HH:mm");
        public int LikeCount => _dto.LikeCount ?? 0;
        public bool IsLiked => _dto.IsLiked ?? false;
        
        [ObservableProperty]
        public partial ImageSource? AvatarImage { get; set; }

        // 原始回复列表
        public ObservableCollection<ForumCommentItemViewModel> AllReplies { get; }

        // UI 显示的回复列表
        [ObservableProperty]
        public partial ObservableCollection<ForumCommentItemViewModel> VisibleReplies { get; set; } = new();

        // 是否展开所有回复
        [ObservableProperty]
        public partial bool IsRepliesExpanded { get; set; }

        // 是否显示回复输入框
        [ObservableProperty]
        public partial bool IsReplyBoxVisible { get; set; }

        // 回复内容
        [ObservableProperty]
        public partial string ReplyContent { get; set; } = string.Empty;

        // 是否可以切换回复展开状态（回复数 > 1）
        public bool CanToggleReplies => AllReplies.Count > 1;

        // 切换按钮文本
        public string ToggleRepliesText => IsRepliesExpanded ? "收起回复" : $"查看剩余 {AllReplies.Count - 1} 条回复";

        [RelayCommand]
        private void ToggleReplies()
        {
            IsRepliesExpanded = !IsRepliesExpanded;
            UpdateVisibleReplies();
            OnPropertyChanged(nameof(CanToggleReplies));
            OnPropertyChanged(nameof(ToggleRepliesText));
        }

        [RelayCommand]
        private void ToggleReplyBox()
        {
            IsReplyBoxVisible = !IsReplyBoxVisible;
            if (IsReplyBoxVisible)
            {
                // Focus logic could go here via messaging or behavior
            }
        }

        [RelayCommand]
        private async Task SubmitReply()
        {
            if (string.IsNullOrWhiteSpace(ReplyContent)) return;

            await _replyCallback(Id, ReplyContent);
            
            // 清空并关闭
            ReplyContent = string.Empty;
            IsReplyBoxVisible = false;
        }

        private void UpdateVisibleReplies()
        {
            if (AllReplies.Count == 0)
            {
                VisibleReplies.Clear();
                return;
            }

            if (IsRepliesExpanded)
            {
                // 显示所有
                VisibleReplies = new ObservableCollection<ForumCommentItemViewModel>(AllReplies);
            }
            else
            {
                // 默认只显示 1 条
                VisibleReplies = new ObservableCollection<ForumCommentItemViewModel>(AllReplies.Take(1));
            }
        }
        
        public void UpdateLikeStatus()
        {
            OnPropertyChanged(nameof(LikeCount));
            OnPropertyChanged(nameof(IsLiked));
        }
    }
}