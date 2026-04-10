using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using ZMSL.App.Services;
using ZMSL.Shared.DTOs;

namespace ZMSL.App.ViewModels
{
    public partial class ForumPostDetailViewModel : ObservableObject
    {
        private readonly PlayerForumService _forumService;
        private long _postId;

        [ObservableProperty]
        public partial bool IsLoading { get; set; }

        [ObservableProperty]
        public partial ForumPostDto? Post { get; set; }

        [ObservableProperty]
        public partial ObservableCollection<string> PostContentParagraphs { get; set; } = new();

        [ObservableProperty]
        public partial string CommentContent { get; set; } = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanGoPrevious))]
        [NotifyPropertyChangedFor(nameof(CanGoNext))]
        public partial int CurrentPage { get; set; } = 1;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanGoNext))]
        public partial int TotalPages { get; set; } = 1;

        [ObservableProperty]
        public partial bool HasComments { get; set; }

        public bool CanGoPrevious => CurrentPage > 1;
        public bool CanGoNext => CurrentPage < TotalPages;

        [ObservableProperty]
        public partial string? StatusMessage { get; set; }

        [ObservableProperty]
        public partial string? SuccessMessage { get; set; }

        [ObservableProperty]
        public partial bool IsSuccessOpen { get; set; }

        [ObservableProperty]
        public partial ObservableCollection<ForumCommentItemViewModel> Comments { get; set; } = new();

        public ForumPostDetailViewModel(PlayerForumService forumService)
        {
            _forumService = forumService;
        }

        public async Task Initialize(long postId)
        {
            _postId = postId;
            CurrentPage = 1; // 重置页码
            await LoadPostDetail();
            await LoadComments();
        }

        private async Task LoadPostDetail()
        {
            IsLoading = true;
            StatusMessage = null;
            try
            {
                var post = await _forumService.GetPostDetailAsync(_postId);
                
                // Ensure UI updates happen on the UI thread to prevent MarkdownTextBlock crashes
                ZMSL.App.App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    Post = post;
                    
                    PostContentParagraphs.Clear();
                    if (Post?.Content != null)
                    {
                        // Split by newlines to avoid huge TextBlocks causing crashes
                        var lines = Post.Content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                        foreach (var line in lines)
                        {
                            PostContentParagraphs.Add(line);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载帖子详情失败: {ex.Message}";
            }
            finally
            {
                // Ensure IsLoading is updated on UI thread as well
                ZMSL.App.App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    IsLoading = false;
                });
            }
        }

        private async Task LoadComments()
        {
            IsLoading = true;
            try
            {
                // 每页 10 条
                var pagedResult = await _forumService.GetCommentsAsync(_postId, CurrentPage, 10);
                
                ZMSL.App.App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    Comments.Clear();
                    foreach (var commentDto in pagedResult.Items)
                    {
                        var commentVm = new ForumCommentItemViewModel(commentDto, ReplyToComment);
                        Comments.Add(commentVm);
                    }
                    TotalPages = Math.Max(1, pagedResult.TotalPages);
                    HasComments = Comments.Count > 0;
                });
            }
            catch (Exception ex)
            {
                ZMSL.App.App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    StatusMessage = $"加载评论失败: {ex.Message}";
                });
            }
            finally
            {
                ZMSL.App.App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    IsLoading = false;
                });
            }
        }

        private async Task ReplyToComment(long parentId, string content)
        {
            IsLoading = true;
            try
            {
                var request = new CreateCommentRequest
                {
                    PostId = _postId,
                    ParentId = parentId,
                    Content = content
                };

                var comment = await _forumService.CreateCommentAsync(request);
                
                ZMSL.App.App.MainWindow.DispatcherQueue.TryEnqueue(async () =>
                {
                    if (comment != null)
                    {
                        // 重新加载评论以显示新回复
                        // 或者更高效地，只将新回复添加到对应的父评论 VM 中（需要递归查找）
                        // 为简单起见，这里重新加载当前页
                        await LoadComments();
                        
                        SuccessMessage = "回复成功";
                        IsSuccessOpen = true;
                        StatusMessage = null;
                        
                        // 3秒后自动关闭
                        _ = Task.Delay(3000).ContinueWith(_ => 
                        {
                            ZMSL.App.App.MainWindow.DispatcherQueue.TryEnqueue(() => IsSuccessOpen = false);
                        });
                    }
                    else
                    {
                        StatusMessage = "回复失败";
                    }
                });
            }
            catch (Exception ex)
            {
                ZMSL.App.App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    StatusMessage = $"回复失败: {ex.Message}";
                });
            }
            finally
            {
                ZMSL.App.App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    IsLoading = false;
                });
            }
        }

        [RelayCommand]
        private async Task SubmitComment()
        {
            if (string.IsNullOrWhiteSpace(CommentContent)) return;

            IsLoading = true;
            try
            {
                var request = new CreateCommentRequest
                {
                    PostId = _postId,
                    Content = CommentContent
                };

                var comment = await _forumService.CreateCommentAsync(request);
                
                ZMSL.App.App.MainWindow.DispatcherQueue.TryEnqueue(async () =>
                {
                    if (comment != null)
                    {
                        CommentContent = string.Empty;
                        await LoadComments(); // 刷新评论列表
                        SuccessMessage = "评论发表成功";
                        IsSuccessOpen = true;
                        StatusMessage = null;
                        
                        // 3秒后自动关闭
                        _ = Task.Delay(3000).ContinueWith(_ => 
                        {
                            ZMSL.App.App.MainWindow.DispatcherQueue.TryEnqueue(() => IsSuccessOpen = false);
                        });
                    }
                    else
                    {
                        StatusMessage = "评论发表失败";
                    }
                });
            }
            catch (Exception ex)
            {
                ZMSL.App.App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    StatusMessage = $"发表评论失败: {ex.Message}";
                });
            }
            finally
            {
                ZMSL.App.App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    IsLoading = false;
                });
            }
        }

        [RelayCommand]
        private async Task LikePost()
        {
            if (Post == null) return;

            try
            {
                bool isLiked = await _forumService.LikePostAsync(_postId);
                
                ZMSL.App.App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    Post.IsLiked = isLiked;
                    var currentCount = Post.LikeCount ?? 0;
                    Post.LikeCount = isLiked ? currentCount + 1 : Math.Max(0, currentCount - 1);
                    OnPropertyChanged(nameof(Post)); // 通知UI更新
                });
            }
            catch (Exception ex)
            {
                ZMSL.App.App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    StatusMessage = $"操作失败: {ex.Message}";
                });
            }
        }

        [RelayCommand]
        private async Task FavoritePost()
        {
            if (Post == null) return;

            try
            {
                bool isFavorited = await _forumService.FavoritePostAsync(_postId);
                
                ZMSL.App.App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    Post.IsFavorited = isFavorited;
                    var currentCount = Post.FavoriteCount ?? 0;
                    Post.FavoriteCount = isFavorited ? currentCount + 1 : Math.Max(0, currentCount - 1);
                    OnPropertyChanged(nameof(Post)); // 通知UI更新
                });
            }
            catch (Exception ex)
            {
                ZMSL.App.App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    StatusMessage = $"操作失败: {ex.Message}";
                });
            }
        }

        [RelayCommand]
        private async Task LikeComment(ForumCommentItemViewModel commentVm)
        {
            if (commentVm == null) return;

            try
            {
                bool isLiked = await _forumService.LikeCommentAsync(commentVm.Id);
                
                ZMSL.App.App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    commentVm.Dto.IsLiked = isLiked;
                    var currentCount = commentVm.Dto.LikeCount ?? 0;
                    commentVm.Dto.LikeCount = isLiked ? currentCount + 1 : Math.Max(0, currentCount - 1);
                    commentVm.UpdateLikeStatus();
                });
            }
            catch (Exception ex)
            {
                ZMSL.App.App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    StatusMessage = $"操作失败: {ex.Message}";
                });
            }
        }

        [RelayCommand]
        private async Task PreviousPage()
        {
            if (CurrentPage > 1)
            {
                CurrentPage--;
                await LoadComments();
            }
        }

        [RelayCommand]
        private async Task NextPage()
        {
            if (CurrentPage < TotalPages)
            {
                CurrentPage++;
                await LoadComments();
            }
        }
    }
}
