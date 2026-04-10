using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text;

namespace ZMSL.App.Views;

public sealed partial class ServerPropertiesDialog : ContentDialog
{
    private readonly string _propertiesPath;
    private Dictionary<string, string> _properties = new();

    public ServerPropertiesDialog(string serverPath)
    {
        this.InitializeComponent();
        _propertiesPath = Path.Combine(serverPath, "server.properties");
        LoadProperties();
    }

    private void LoadProperties()
    {
        if (!File.Exists(_propertiesPath))
        {
            SetDefaults();
            return;
        }

        try
        {
            var lines = File.ReadAllLines(_propertiesPath);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                    continue;

                var idx = line.IndexOf('=');
                if (idx > 0)
                {
                    var key = line.Substring(0, idx).Trim();
                    var value = line.Substring(idx + 1).Trim();
                    _properties[key] = value;
                }
            }

            ApplyToUI();
        }
        catch
        {
            SetDefaults();
        }
    }

    private void SetDefaults()
    {
        GamemodeCombo.SelectedIndex = 0;
        DifficultyCombo.SelectedIndex = 1;
        MaxPlayersBox.Value = 20;
        ServerPortBox.Value = 25565;
        MotdBox.Text = "A Minecraft Server";
        LevelNameBox.Text = "world";
        LevelSeedBox.Text = "";
        LevelTypeCombo.SelectedIndex = 0;
        ViewDistanceBox.Value = 10;
        SimDistanceBox.Value = 10;
        SpawnProtectionBox.Value = 16;
        PvpToggle.IsOn = true;
        HardcoreToggle.IsOn = false;
        AllowFlightToggle.IsOn = false;
        AllowNetherToggle.IsOn = true;
        SpawnMonstersToggle.IsOn = true;
        SpawnAnimalsToggle.IsOn = true;
        SpawnNpcsToggle.IsOn = true;
        GenerateStructuresToggle.IsOn = true;
        CommandBlockToggle.IsOn = false;
        ForceGamemodeToggle.IsOn = false;
        OnlineModeToggle.IsOn = true;
        WhitelistToggle.IsOn = false;
        EnableQueryToggle.IsOn = false;
        EnableRconToggle.IsOn = false;
    }

    private void ApplyToUI()
    {
        // 基础设置
        SelectComboByTag(GamemodeCombo, GetProperty("gamemode", "survival"));
        SelectComboByTag(DifficultyCombo, GetProperty("difficulty", "easy"));
        MaxPlayersBox.Value = int.TryParse(GetProperty("max-players", "20"), out var mp) ? mp : 20;
        ServerPortBox.Value = int.TryParse(GetProperty("server-port", "25565"), out var sp) ? sp : 25565;
        MotdBox.Text = GetProperty("motd", "A Minecraft Server");

        // 世界设置
        LevelNameBox.Text = GetProperty("level-name", "world");
        LevelSeedBox.Text = GetProperty("level-seed", "");
        SelectComboByTag(LevelTypeCombo, GetProperty("level-type", "minecraft:normal"));
        ViewDistanceBox.Value = int.TryParse(GetProperty("view-distance", "10"), out var vd) ? vd : 10;
        SimDistanceBox.Value = int.TryParse(GetProperty("simulation-distance", "10"), out var sd) ? sd : 10;
        SpawnProtectionBox.Value = int.TryParse(GetProperty("spawn-protection", "16"), out var sprot) ? sprot : 16;

        // 游戏规则
        PvpToggle.IsOn = GetProperty("pvp", "true") == "true";
        HardcoreToggle.IsOn = GetProperty("hardcore", "false") == "true";
        AllowFlightToggle.IsOn = GetProperty("allow-flight", "false") == "true";
        AllowNetherToggle.IsOn = GetProperty("allow-nether", "true") == "true";
        SpawnMonstersToggle.IsOn = GetProperty("spawn-monsters", "true") == "true";
        SpawnAnimalsToggle.IsOn = GetProperty("spawn-animals", "true") == "true";
        SpawnNpcsToggle.IsOn = GetProperty("spawn-npcs", "true") == "true";
        GenerateStructuresToggle.IsOn = GetProperty("generate-structures", "true") == "true";
        CommandBlockToggle.IsOn = GetProperty("enable-command-block", "false") == "true";
        ForceGamemodeToggle.IsOn = GetProperty("force-gamemode", "false") == "true";

        // 网络设置
        OnlineModeToggle.IsOn = GetProperty("online-mode", "true") == "true";
        WhitelistToggle.IsOn = GetProperty("white-list", "false") == "true";
        EnableQueryToggle.IsOn = GetProperty("enable-query", "false") == "true";
        EnableRconToggle.IsOn = GetProperty("enable-rcon", "false") == "true";
    }

    private string GetProperty(string key, string defaultValue)
    {
        return _properties.TryGetValue(key, out var value) ? value : defaultValue;
    }

    private void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (item.Tag?.ToString() == tag)
            {
                combo.SelectedItem = item;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private string GetComboTag(ComboBox combo, string defaultValue)
    {
        return (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? defaultValue;
    }

    private void Save_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        try
        {
            // 更新属性
            _properties["gamemode"] = GetComboTag(GamemodeCombo, "survival");
            _properties["difficulty"] = GetComboTag(DifficultyCombo, "easy");
            _properties["max-players"] = ((int)MaxPlayersBox.Value).ToString();
            _properties["server-port"] = ((int)ServerPortBox.Value).ToString();
            _properties["motd"] = MotdBox.Text;

            _properties["level-name"] = LevelNameBox.Text;
            _properties["level-seed"] = LevelSeedBox.Text;
            _properties["level-type"] = GetComboTag(LevelTypeCombo, "minecraft:normal");
            _properties["view-distance"] = ((int)ViewDistanceBox.Value).ToString();
            _properties["simulation-distance"] = ((int)SimDistanceBox.Value).ToString();
            _properties["spawn-protection"] = ((int)SpawnProtectionBox.Value).ToString();

            _properties["pvp"] = PvpToggle.IsOn.ToString().ToLower();
            _properties["hardcore"] = HardcoreToggle.IsOn.ToString().ToLower();
            _properties["allow-flight"] = AllowFlightToggle.IsOn.ToString().ToLower();
            _properties["allow-nether"] = AllowNetherToggle.IsOn.ToString().ToLower();
            _properties["spawn-monsters"] = SpawnMonstersToggle.IsOn.ToString().ToLower();
            _properties["spawn-animals"] = SpawnAnimalsToggle.IsOn.ToString().ToLower();
            _properties["spawn-npcs"] = SpawnNpcsToggle.IsOn.ToString().ToLower();
            _properties["generate-structures"] = GenerateStructuresToggle.IsOn.ToString().ToLower();
            _properties["enable-command-block"] = CommandBlockToggle.IsOn.ToString().ToLower();
            _properties["force-gamemode"] = ForceGamemodeToggle.IsOn.ToString().ToLower();

            _properties["online-mode"] = OnlineModeToggle.IsOn.ToString().ToLower();
            _properties["white-list"] = WhitelistToggle.IsOn.ToString().ToLower();
            _properties["enable-query"] = EnableQueryToggle.IsOn.ToString().ToLower();
            _properties["enable-rcon"] = EnableRconToggle.IsOn.ToString().ToLower();

            // 保存文件
            var sb = new StringBuilder();
            sb.AppendLine("#Minecraft server properties");
            sb.AppendLine($"#Generated by ZMSL at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            
            foreach (var kvp in _properties.OrderBy(x => x.Key))
            {
                sb.AppendLine($"{kvp.Key}={kvp.Value}");
            }

            File.WriteAllText(_propertiesPath, sb.ToString());
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            ShowError($"保存失败: {ex.Message}");
        }
    }

    private async void ShowError(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "错误",
            Content = message,
            CloseButtonText = "确定",
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
    }
}
