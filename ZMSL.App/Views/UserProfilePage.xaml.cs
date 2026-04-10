using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Extensions.DependencyInjection;
using ZMSL.App.ViewModels;

namespace ZMSL.App.Views
{
    public sealed partial class UserProfilePage : Page
    {
        public UserProfileViewModel ViewModel { get; }

        public UserProfilePage()
        {
            this.InitializeComponent();
            ViewModel = App.Services.GetRequiredService<UserProfileViewModel>();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            
            if (e.Parameter is string tab && tab == "Notifications")
            {
                ViewModel.SelectedPivotIndex = 2; // 站内信 tab index
            }
            else
            {
                ViewModel.SelectedPivotIndex = 0;
            }

            _ = ViewModel.LoadDataCommand.ExecuteAsync(null);
        }

        private void PostListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is ZMSL.Shared.DTOs.ForumPostDto post)
            {
                ViewModel.ViewPostDetailCommand.Execute(post.Id);
            }
        }

        private void NotificationListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is ZMSL.Shared.DTOs.NotificationDto notification && notification.TargetId.HasValue)
            {
                ViewModel.ViewPostDetailCommand.Execute(notification.TargetId.Value);
            }
        }
    }
}
