using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using ZMSL.App.ViewModels;

namespace ZMSL.App.Views;

public sealed partial class GroupServerListPage : Page
{
    public GroupServerListViewModel ViewModel { get; }

    public GroupServerListPage()
    {
        this.InitializeComponent();
        this.Name = "RootPage"; 
        ViewModel = ActivatorUtilities.CreateInstance<GroupServerListViewModel>(App.Services);
    }

    private void Page_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _ = ViewModel.LoadGroupServersAsync();
    }

    private void ServerItem_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ZMSL.App.Models.ServerViewModel server)
        {
            ViewModel.OpenServerDetailCommand.Execute(server);
        }
    }

    private void PluginButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ZMSL.App.Models.ServerViewModel server)
        {
            if (server.LocalServer != null)
            {
                Frame.Navigate(typeof(PluginManagerPage), server.LocalServer);
            }
        }
    }
}
