using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ZMSL.App.ViewModels;

namespace ZMSL.App.Views
{
    public sealed partial class ForumPostDetailPage : Page
    {
        public ForumPostDetailViewModel ViewModel { get; }

        public ForumPostDetailPage()
        {
            this.InitializeComponent();
            ViewModel = App.Services.GetRequiredService<ForumPostDetailViewModel>();
            this.DataContext = ViewModel;
            
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ForumPostDetailViewModel.Post))
            {
                UpdatePostContent();
            }
        }

        private void UpdatePostContent()
        {
            if (ViewModel.Post?.Content == null) return;

            PostContentText.Text = ViewModel.Post.Content;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is long postId)
            {
                await ViewModel.Initialize(postId);
            }
            else if (e.Parameter is int id) // Fallback for int
            {
                await ViewModel.Initialize(id);
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
            else
            {
                // Navigate back to ForumPage if can't go back (e.g. direct navigation)
                Frame.Navigate(typeof(ForumPage));
            }
        }
    }
}
