using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ZMSL.App.Services;
using ZMSL.App.Views;
using ZMSL.Shared.DTOs;

namespace ZMSL.App.ViewModels
{
    /// <summary>
    /// 玩家论坛主页视图模型
    /// </summary>
    public partial class PlayerForumViewModel : ObservableObject
    {
        private readonly PlayerForumService _forumService;
        private readonly AuthService _authService;

        [ObservableProperty]
        public partial bool IsLoading { get; set; }

        [ObservableProperty]
        public partial string SearchKeyword { get; set; } = string.Empty;

        [ObservableProperty]
        public partial long SelectedCategoryId { get; set; }

        [ObservableProperty]
        public partial int CurrentPage { get; set; } = 1;

        [ObservableProperty]
        public partial int TotalPages { get; set; } = 1;

        [ObservableProperty]
        public partial string StatusMessage { get; set; } = "准备就绪";

        [ObservableProperty]
        public partial ImageSource? AvatarImage { get; set; }

        [ObservableProperty]
        public partial ObservableCollection<ForumCategoryDto> Categories { get; set; } = new();

        [ObservableProperty]
        public partial ObservableCollection<ForumPostDto> Threads { get; set; } = new();

        [ObservableProperty]
        public partial long UnreadCount { get; set; }

        public bool IsLoggedIn => _authService.IsLoggedIn;
        public string CurrentUsername => _authService.CurrentUser?.Username ?? "未登录";

        public PlayerForumViewModel(PlayerForumService forumService, AuthService authService)
        {
            _forumService = forumService;
            _authService = authService;
            InitializeEventHandlers();
            
            _authService.LoginStateChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(IsLoggedIn));
                OnPropertyChanged(nameof(CurrentUsername));
                UpdateAvatar();
                _ = CheckUnreadNotifications();
            };
            UpdateAvatar();
        }

        private void UpdateAvatar()
        {
            var url = _authService.CurrentUser?.AvatarUrl;
            if (!string.IsNullOrEmpty(url))
            {
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
            else
            {
                AvatarImage = null;
            }
        }

        public async Task Initialize()
        {
            await LoadForumData();
            await CheckUnreadNotifications();
        }

        private async Task CheckUnreadNotifications()
        {
            if (IsLoggedIn)
            {
                try
                {
                    UnreadCount = await _forumService.GetUnreadNotificationCountAsync();
                }
                catch { }
            }
            else
            {
                UnreadCount = 0;
            }
        }

        [RelayCommand]
        private void NavigateToInbox()
        {
            ZMSL.App.App.MainWindowInstance?.NavigateToPage("UserProfile", "Notifications");
        }

        [RelayCommand]
        private void NavigateToProfile()
        {
            ZMSL.App.App.MainWindowInstance?.NavigateToPage("UserProfile");
        }

        private void InitializeEventHandlers()
        {
            _forumService.OnThreadCreated += (sender, e) => _ = LoadForumData();
            _forumService.OnThreadDeleted += (sender, e) => _ = LoadForumData();
        }

        [RelayCommand]
        private async Task LoadForumData()
        {
            IsLoading = true;
            StatusMessage = "正在加载论坛数据...";

            try
            {
                await LoadCategories();
                await LoadThreadsByCategory(SelectedCategoryId);
                StatusMessage = "加载完成";
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载失败: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadCategories()
        {
            var categories = await _forumService.GetCategoriesAsync();
            Categories.Clear();
            
            // 添加"全部"选项
            Categories.Add(new ForumCategoryDto { Id = 0, Name = "全部版块", Description = "所有帖子" });
            
            foreach (var category in categories)
            {
                Categories.Add(category);
            }
        }

        private async Task LoadThreadsByCategory(long categoryId)
        {
            IsLoading = true;
            try
            {
                var pagedResult = await _forumService.GetPostsAsync(CurrentPage, 20, categoryId > 0 ? categoryId : null);
                
                Threads.Clear();
                foreach (var thread in pagedResult.Items)
                {
                    Threads.Add(thread);
                }

                TotalPages = pagedResult.TotalPages;
                StatusMessage = $"第 {CurrentPage}/{TotalPages} 页";
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载失败: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task SearchThreads()
        {
            if (string.IsNullOrWhiteSpace(SearchKeyword))
            {
                await LoadThreadsByCategory(SelectedCategoryId);
                return;
            }

            IsLoading = true;
            StatusMessage = "正在搜索...";

            try
            {
                var pagedResult = await _forumService.SearchPostsAsync(SearchKeyword, CurrentPage, 20);
                
                Threads.Clear();
                foreach (var thread in pagedResult.Items)
                {
                    Threads.Add(thread);
                }

                TotalPages = pagedResult.TotalPages;
                StatusMessage = $"搜索结果: {pagedResult.TotalCount} 条";
            }
            catch (Exception ex)
            {
                StatusMessage = $"搜索失败: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task SwitchCategory(ForumCategoryDto category)
        {
            if (category == null) return;
            SelectedCategoryId = category.Id;
            CurrentPage = 1;
            await LoadThreadsByCategory(SelectedCategoryId);
        }

        [RelayCommand]
        private async Task PreviousPage()
        {
            if (CurrentPage > 1)
            {
                CurrentPage--;
                if (string.IsNullOrWhiteSpace(SearchKeyword))
                    await LoadThreadsByCategory(SelectedCategoryId);
                else
                    await SearchThreads();
            }
        }

        [RelayCommand]
        private async Task NextPage()
        {
            if (CurrentPage < TotalPages)
            {
                CurrentPage++;
                if (string.IsNullOrWhiteSpace(SearchKeyword))
                    await LoadThreadsByCategory(SelectedCategoryId);
                else
                    await SearchThreads();
            }
        }

        [RelayCommand]
        private async Task RefreshData()
        {
            await LoadForumData();
        }
        

        [RelayCommand]
        private void CreateNewThread()
        {
            App.MainWindowInstance?.ContentFramePublic.Navigate(typeof(ForumCreatePostPage));
        }

        [RelayCommand]
        private void ViewThreadDetails(long threadId)
        {
            App.MainWindowInstance?.ContentFramePublic.Navigate(typeof(ForumPostDetailPage), threadId);
        }
    }
}
