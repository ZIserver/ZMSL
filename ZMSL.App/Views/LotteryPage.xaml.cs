using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using ZMSL.App.Models;
using ZMSL.App.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace ZMSL.App.Views;

public sealed partial class LotteryPage : Page
{
    private readonly ApiService _apiService;
    public ObservableCollection<Lottery> Lotteries { get; } = new();
    public ObservableCollection<LotteryWinner> Winners { get; } = new();
    
    public static readonly DependencyProperty IsLoadingProperty = DependencyProperty.Register(
        nameof(IsLoading), typeof(bool), typeof(LotteryPage), new PropertyMetadata(false));

    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }
        
    public static readonly DependencyProperty IsEmptyProperty = DependencyProperty.Register(
        nameof(IsEmpty), typeof(bool), typeof(LotteryPage), new PropertyMetadata(false));

    public bool IsEmpty
    {
        get => (bool)GetValue(IsEmptyProperty);
        set => SetValue(IsEmptyProperty, value);
    }

    public static readonly DependencyProperty SelectedLotteryProperty = DependencyProperty.Register(
        nameof(SelectedLottery), typeof(Lottery), typeof(LotteryPage), new PropertyMetadata(null));

    public Lottery SelectedLottery
    {
        get => (Lottery)GetValue(SelectedLotteryProperty);
        set => SetValue(SelectedLotteryProperty, value);
    }

    public LotteryPage()
    {
        this.InitializeComponent();
        _apiService = App.Services.GetRequiredService<ApiService>();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadDataAsync();
    }
    
    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        IsLoading = true;
        IsEmpty = false;
        Lotteries.Clear();
        
        try 
        {
            var response = await _apiService.GetLotteriesAsync();
            if (response.Success && response.Data != null)
            {
                foreach (var item in response.Data)
                {
                    Lotteries.Add(item);
                }
                IsEmpty = Lotteries.Count == 0;
            }
            else
            {
                 if (!string.IsNullOrEmpty(response.Message))
                 {
                     ShowError(response.Message);
                 }
            }
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    private async void ViewDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is long id)
        {
            var lottery = Lotteries.FirstOrDefault(l => l.Id == id);
            if (lottery == null) return;
            
            SelectedLottery = lottery;
            
            // Reset dialog state
            DialogPasswordBox.Text = string.Empty;
            DialogErrorMessage.Visibility = Visibility.Collapsed;
            DialogErrorMessage.Text = "";
            
            // Explicitly set visibility to ensure it's correct regardless of binding timing
            DialogPasswordPanel.Visibility = lottery.IsProtected ? Visibility.Visible : Visibility.Collapsed;
            
            if (lottery.HasJoined)
            {
                LotteryDetailsDialog.PrimaryButtonText = "已参与";
                LotteryDetailsDialog.IsPrimaryButtonEnabled = false;
            }
            else if (lottery.Status != "ACTIVE")
            {
                 LotteryDetailsDialog.PrimaryButtonText = "无法参与";
                 LotteryDetailsDialog.IsPrimaryButtonEnabled = false;
            }
            else
            {
                LotteryDetailsDialog.PrimaryButtonText = "立即参与";
                LotteryDetailsDialog.IsPrimaryButtonEnabled = true;
            }

            await LotteryDetailsDialog.ShowAsync();
        }
    }

    private async void LotteryDetailsDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        args.Cancel = true; 

        try
        {
            DialogErrorMessage.Visibility = Visibility.Collapsed;
            var lottery = SelectedLottery;
            if (lottery == null) 
            {
                deferral.Complete();
                sender.Hide();
                return;
            }

            string? code = null;
            if (lottery.IsProtected)
            {
                code = DialogPasswordBox.Text;
                if (string.IsNullOrWhiteSpace(code))
                {
                    DialogErrorMessage.Text = "请输入参与口令";
                    DialogErrorMessage.Visibility = Visibility.Visible;
                    deferral.Complete();
                    return; 
                }
            }

            sender.IsPrimaryButtonEnabled = false;

            var response = await _apiService.JoinLotteryAsync(lottery.Id, code);
            
            if (response.Success)
            {
                await LoadDataAsync();
                sender.Hide();
                ShowMessage("参与成功", "祝你好运！");
            }
            else
            {
                DialogErrorMessage.Text = response.Message;
                DialogErrorMessage.Visibility = Visibility.Visible;
                sender.IsPrimaryButtonEnabled = true;
            }
        }
        catch (Exception ex)
        {
            DialogErrorMessage.Text = "发生错误: " + ex.Message;
            DialogErrorMessage.Visibility = Visibility.Visible;
            sender.IsPrimaryButtonEnabled = true;
        }
        finally
        {
            deferral.Complete();
        }
    }
    
    private async void ViewWinners_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is long id)
        {
            Winners.Clear();
            var response = await _apiService.GetLotteryWinnersAsync(id);
            if (response.Success && response.Data != null)
            {
                foreach (var w in response.Data)
                {
                    Winners.Add(w);
                }
                await WinnersDialog.ShowAsync();
            }
            else
            {
                ShowError(response.Message ?? "未知错误");
            }
        }
    }
    
    private async void ShowError(string message)
    {
        if (this.XamlRoot == null) return;
        var dialog = new ContentDialog
        {
            Title = "错误",
            Content = message,
            CloseButtonText = "确定",
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
    }
    
    private async void ShowMessage(string title, string message)
    {
        if (this.XamlRoot == null) return;
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "确定",
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
    }
}
