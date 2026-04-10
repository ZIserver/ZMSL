using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using System;
using ZMSL.App.ViewModels;

namespace ZMSL.App.Views
{
    public sealed partial class DocumentationPage : Page
    {
        public DocumentationViewModel ViewModel { get; } = new DocumentationViewModel();

        public DocumentationPage()
        {
            this.InitializeComponent();
            this.DataContext = ViewModel;
            // 选中第一项
            DocNav.SelectedItem = DocNav.MenuItems[0];
        }

        private void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is NavigationViewItem item && item.Tag is string key)
            {
                // 调用 ViewModel 切换内容
                ViewModel.SelectCategory(key);
            }
        }
    }

    public sealed class CategoryTitleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value switch
            {
                "GettingStarted" => "快速入门",
                "ServerManagement" => "服务器管理",
                "AdvancedFeatures" => "进阶功能",
                "Community" => "社区与支持",
                _ => "文档"
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
