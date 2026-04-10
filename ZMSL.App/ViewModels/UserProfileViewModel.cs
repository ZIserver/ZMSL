using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using ZMSL.App.Services;
using ZMSL.Shared.DTOs;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ZMSL.App.ViewModels
{
    public partial class UserProfileViewModel : ObservableObject
    {
        private readonly PlayerForumService _forumService;
        private readonly AuthService _authService;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DisplayName))]
        public partial UserDto? User { get; set; }

        public string DisplayName => User?.Nickname ?? User?.Username ?? "Unknown";

        [ObservableProperty]
        public partial ImageSource? AvatarImage { get; set; }

        partial void OnUserChanged(UserDto? value)
        {
            if (value != null && !string.IsNullOrEmpty(value.AvatarUrl))
            {
                var url = value.AvatarUrl;
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

        [ObservableProperty]
        public partial bool IsLoading { get; set; }

        [ObservableProperty]
        public partial string? StatusMessage { get; set; }

        // My Posts
        public ObservableCollection<ForumPostDto> MyPosts { get; } = new();
        [ObservableProperty] public partial int PostsPage { get; set; } = 1;
        [ObservableProperty] public partial bool HasMorePosts { get; set; }
        [ObservableProperty] public partial bool IsMyPostsEmpty { get; set; }

        // My Favorites
        public ObservableCollection<ForumPostDto> MyFavorites { get; } = new();
        [ObservableProperty] public partial int FavoritesPage { get; set; } = 1;
        [ObservableProperty] public partial bool HasMoreFavorites { get; set; }

        // Notifications
        public ObservableCollection<NotificationDto> Notifications { get; } = new();
        [ObservableProperty] public partial int NotificationsPage { get; set; } = 1;
        [ObservableProperty] public partial bool HasMoreNotifications { get; set; }
        [ObservableProperty] public partial long UnreadCount { get; set; }
        
        [ObservableProperty] public partial int SelectedPivotIndex { get; set; }

        public UserProfileViewModel(PlayerForumService forumService, AuthService authService)
        {
            _forumService = forumService;
            _authService = authService;
            User = _authService.CurrentUser;
        }

        [RelayCommand]
        private void Logout()
        {
            _authService.Logout();
            // Navigate back or to login page handled by MainWindow state change
        }

        [RelayCommand]
        private async Task LoadDataAsync()
        {
            if (User == null) return;

            IsLoading = true;
            try
            {
                await Task.WhenAll(
                    LoadPostsAsync(true),
                    LoadFavoritesAsync(true)
                );
                
                // UnreadCount = await _forumService.GetUnreadNotificationCountAsync();
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task LoadPostsAsync(bool refresh = false)
        {
            if (refresh)
            {
                PostsPage = 1;
                MyPosts.Clear();
                IsMyPostsEmpty = false;
            }

            var result = await _forumService.GetMyPostsAsync(PostsPage, 20);
            if (result.Items != null)
            {
                foreach (var item in result.Items)
                {
                    MyPosts.Add(item);
                }
                HasMorePosts = PostsPage < result.TotalPages;
                if (HasMorePosts) PostsPage++;
            }
            
            IsMyPostsEmpty = MyPosts.Count == 0;
        }

        [RelayCommand]
        private async Task LoadFavoritesAsync(bool refresh = false)
        {
            if (refresh)
            {
                FavoritesPage = 1;
                MyFavorites.Clear();
            }

            var result = await _forumService.GetMyFavoritesAsync(FavoritesPage, 20);
            if (result.Items != null)
            {
                foreach (var item in result.Items)
                {
                    MyFavorites.Add(item);
                }
                HasMoreFavorites = FavoritesPage < result.TotalPages;
                if (HasMoreFavorites) FavoritesPage++;
            }
        }

        [RelayCommand]
        private async Task LoadNotificationsAsync(bool refresh = false)
        {
            if (refresh)
            {
                NotificationsPage = 1;
                Notifications.Clear();
            }

            try
            {
                var result = await _forumService.GetNotificationsAsync(NotificationsPage, 20);
                if (result.Items != null && result.Items.Count > 0)
                {
                    foreach (var item in result.Items)
                    {
                        Notifications.Add(item);
                    }
                    HasMoreNotifications = NotificationsPage < result.TotalPages;
                    if (HasMoreNotifications) NotificationsPage++;
                }
                else
                {
                    HasMoreNotifications = false;
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Error("Exception loading notifications", "UserProfileViewModel", ex);
            }
        }

        [RelayCommand]
        private async Task MarkAllReadAsync()
        {
            if (await _forumService.MarkAllNotificationsAsReadAsync())
            {
                UnreadCount = 0;
                foreach (var n in Notifications)
                {
                    n.IsRead = true;
                }
            }
        }

        [RelayCommand]
        private void ViewPostDetail(long threadId)
        {
            App.MainWindowInstance?.ContentFramePublic.Navigate(typeof(Views.ForumPostDetailPage), threadId);
        }
    }
}
