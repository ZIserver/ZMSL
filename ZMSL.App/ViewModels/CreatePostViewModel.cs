using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using ZMSL.App.Services;
using ZMSL.Shared.DTOs;

namespace ZMSL.App.ViewModels
{
    public partial class CreatePostViewModel : ObservableObject
    {
        private readonly PlayerForumService _forumService;
        private long? _editPostId;

        [ObservableProperty]
        public partial bool IsLoading { get; set; }

        [ObservableProperty]
        public partial string Title { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string Content { get; set; } = string.Empty;

        [ObservableProperty]
        public partial ForumCategoryDto? SelectedCategory { get; set; }

        [ObservableProperty]
        public partial string StatusMessage { get; set; } = string.Empty;

        [ObservableProperty]
        public partial ObservableCollection<ForumCategoryDto> Categories { get; set; } = new();

        public event EventHandler? OnPostCreated;

        public CreatePostViewModel(PlayerForumService forumService)
        {
            _forumService = forumService;
        }

        public async Task Initialize(long? editPostId = null)
        {
            _editPostId = editPostId;
            await LoadCategories();

            if (_editPostId.HasValue)
            {
                await LoadPostForEdit(_editPostId.Value);
            }
        }

        private async Task LoadCategories()
        {
            var categories = await _forumService.GetCategoriesAsync();
            Categories.Clear();
            foreach (var category in categories)
            {
                Categories.Add(category);
            }
            
            if (Categories.Count > 0 && SelectedCategory == null)
            {
                SelectedCategory = Categories[0];
            }
        }

        private async Task LoadPostForEdit(long postId)
        {
            IsLoading = true;
            try
            {
                var post = await _forumService.GetPostDetailAsync(postId);
                if (post != null)
                {
                    Title = post.Title;
                    Content = post.Content;
                    // Find and select category
                    foreach (var cat in Categories)
                    {
                        if (cat.Id == post.CategoryId)
                        {
                            SelectedCategory = cat;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载帖子失败: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task SubmitPost()
        {
            if (string.IsNullOrWhiteSpace(Title))
            {
                StatusMessage = "请输入标题";
                return;
            }
            if (string.IsNullOrWhiteSpace(Content))
            {
                StatusMessage = "请输入内容";
                return;
            }
            if (SelectedCategory == null)
            {
                StatusMessage = "请选择版块";
                return;
            }

            IsLoading = true;
            try
            {
                var request = new CreatePostRequest
                {
                    Title = Title,
                    Content = Content,
                    CategoryId = SelectedCategory.Id,
                    Status = "PUBLISHED"
                };

                ForumPostDto? result;
                if (_editPostId.HasValue)
                {
                    result = await _forumService.UpdatePostAsync(_editPostId.Value, request);
                    StatusMessage = result != null ? "更新成功" : "更新失败";
                }
                else
                {
                    result = await _forumService.CreatePostAsync(request);
                    StatusMessage = result != null ? "发布成功" : "发布失败";
                }

                if (result != null)
                {
                    OnPostCreated?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"提交失败: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
