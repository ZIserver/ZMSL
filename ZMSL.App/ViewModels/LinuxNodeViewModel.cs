using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ZMSL.App.Models;
using ZMSL.App.Services;

namespace ZMSL.App.ViewModels;

public class LinuxNodeViewModel : INotifyPropertyChanged
{
    private readonly LinuxNodeService _nodeService;
    private ObservableCollection<LinuxNode> _nodes = new();
    private LinuxNode? _selectedNode;
    private bool _isLoading;

    public ObservableCollection<LinuxNode> Nodes
    {
        get => _nodes;
        set { _nodes = value; OnPropertyChanged(); }
    }

    public LinuxNode? SelectedNode
    {
        get => _selectedNode;
        set { _selectedNode = value; OnPropertyChanged(); }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    public LinuxNodeViewModel(LinuxNodeService nodeService)
    {
        _nodeService = nodeService;
    }

    public async Task LoadNodesAsync()
    {
        IsLoading = true;
        try
        {
            var nodes = await _nodeService.GetNodesAsync();
            Nodes.Clear();
            foreach (var node in nodes)
            {
                Nodes.Add(node);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task TestConnectionAsync(LinuxNode node)
    {
        var (success, message) = await _nodeService.TestConnectionAsync(node);
        // 刷新节点状态
        await LoadNodesAsync();
    }

    public async Task<(bool Success, string Message)> AddNodeAsync(LinuxNode node)
    {
        try
        {
            await _nodeService.AddNodeAsync(node);
            await LoadNodesAsync();
            return (true, "节点添加成功");
        }
        catch (Exception ex)
        {
            return (false, $"添加失败: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> UpdateNodeAsync(LinuxNode node)
    {
        try
        {
            await _nodeService.UpdateNodeAsync(node);
            await LoadNodesAsync();
            return (true, "节点更新成功");
        }
        catch (Exception ex)
        {
            return (false, $"更新失败: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> DeleteNodeAsync(LinuxNode node)
    {
        try
        {
            await _nodeService.DeleteNodeAsync(node.Id);
            await LoadNodesAsync();
            return (true, "节点删除成功");
        }
        catch (Exception ex)
        {
            return (false, $"删除失败: {ex.Message}");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
