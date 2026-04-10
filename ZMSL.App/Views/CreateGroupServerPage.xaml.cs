using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using ZMSL.App.ViewModels;
using ZMSL.App.Models;
using System;

namespace ZMSL.App.Views;

public sealed partial class CreateGroupServerPage : Page
{
    public CreateGroupServerViewModel ViewModel { get; }

    public CreateGroupServerPage()
    {
        this.InitializeComponent();
        
        ViewModel = ActivatorUtilities.CreateInstance<CreateGroupServerViewModel>(App.Services);
        this.Loaded += Page_Loaded;
    }

    private void Page_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _ = ViewModel.LoadDataAsync();
    }
}
