using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ZMSL.App.ViewModels;

namespace ZMSL.App.Views
{
    public sealed partial class ForumPage : Page
    {
        public PlayerForumViewModel ViewModel { get; }

        public ForumPage()
        {
            this.InitializeComponent();
            ViewModel = App.Services.GetRequiredService<PlayerForumViewModel>();
            this.DataContext = ViewModel;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await ViewModel.Initialize();
        }

        private void AutoSuggestBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (ViewModel.SearchThreadsCommand.CanExecute(null))
            {
                ViewModel.SearchThreadsCommand.Execute(null);
            }
        }
    }
}
