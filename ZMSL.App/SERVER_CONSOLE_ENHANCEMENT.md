# ZMSL.App 服务器控制台功能增强

## 实现的功能

### 1. ✅ 服务器概况卡片

在右侧面板顶部新增"服务器概况"卡片，显示以下信息：

- **服务器名称** - 从服务器配置读取
- **游戏版本** - Minecraft版本号
- **服务端核心** - Paper/Spigot/Forge等
- **内存限制** - 最小内存-最大内存（如：1024MB - 2048MB）
- **运行端口** - 服务器端口号
- **地图种子** - 从server.properties读取，显示实际种子或"随机"
- **正版验证** - 开启/关闭状态
- **游戏模式** - 生存/创造/冒险/旁观
- **游戏难度** - 和平/简单/普通/困难
- **运行时长** - 实时显示服务器运行时间

### 2. ✅ 运行时长计时器（多线程）

- 使用 `DispatcherTimer` 每秒更新一次
- 不会阻塞主线程
- 服务器启动时开始计时，停止时重置
- 格式化显示：
  - 超过1天：`X天 X小时 X分钟`
  - 超过1小时：`X小时 X分钟 X秒`
  - 超过1分钟：`X分钟 X秒`
  - 小于1分钟：`X秒`

### 3. ✅ MC日志颜色渲染

将 `TextBox` 替换为 `RichEditBox`，实现日志着色：

**颜色方案：**
- 🔴 **红色加粗** - ERROR、Exception、error
- 🟠 **橙色** - WARN、Warning
- ⚪ **浅灰色** - INFO
- ⚫ **灰色** - DEBUG
- 🟢 **浅绿色** - Done、完成、成功
- 🔵 **浅蓝色** - 玩家加入游戏
- 🟡 **浅珊瑚色** - 玩家离开游戏

### 4. ✅ 控制台日志自动换行

- 设置 `TextWrapping="Wrap"`
- 禁用横向滚动条 `HorizontalScrollBarVisibility="Disabled"`
- 保留纵向滚动条
- 日志内容到达边缘自动换行，无需左右滚动

### 5. ✅ 从server.properties读取配置

实现了完整的配置文件解析：

```csharp
private void LoadServerProperties()
{
    // 读取并解析 server.properties
    // 存储到 Dictionary<string, string>
}

private string GetProperty(string key, string defaultValue)
{
    // 获取配置值，支持默认值
}
```

**读取的配置项：**
- `level-seed` - 地图种子
- `online-mode` - 正版验证
- `gamemode` - 游戏模式
- `difficulty` - 游戏难度
- `server-port` - 服务器端口
- 等等...

### 6. ✅ 更新复制和全选功能

适配 RichEditBox 的API：

```csharp
// 复制选中文本
ConsoleOutput.Document.Selection.GetText(TextGetOptions.None, out var selectedText);

// 全选
ConsoleOutput.Document.Selection.SetRange(0, int.MaxValue);
```

## 代码变更

### XAML 变更 (ServerDetailPage.xaml)

1. **控制台组件替换**
   - `TextBox` → `RichEditBox`
   - 添加自动换行和颜色支持

2. **新增服务器概况卡片**
   - 10行信息展示
   - 使用Grid布局，左侧标签，右侧数值
   - 自适应文本换行

### C# 变更 (ServerDetailPage.xaml.cs)

1. **新增字段**
   ```csharp
   private Microsoft.UI.Xaml.DispatcherTimer? _uptimeTimer;
   private DateTime? _serverStartTime;
   private Dictionary<string, string> _serverProperties = new();
   ```

2. **新增方法**
   - `LoadServerOverview()` - 加载服务器概况
   - `LoadServerProperties()` - 读取配置文件
   - `GetProperty()` - 获取配置值
   - `UptimeTimer_Tick()` - 运行时长计时
   - `FormatUptime()` - 格式化时长显示
   - `AppendColoredLog()` - 添加彩色日志

3. **更新方法**
   - `LoadHistoryLogs()` - 使用彩色日志
   - `OnOutputReceived()` - 实时彩色渲染
   - `CopyConsole_Click()` - 适配RichEditBox
   - `SelectAllConsole_Click()` - 适配RichEditBox
   - `AnalyzeSelected_Click()` - 适配RichEditBox

4. **新增引用**
   ```csharp
   using Microsoft.UI;
   using Microsoft.UI.Text;
   using Windows.UI;
   using Windows.UI.Text;
   ```

## 使用说明

### 查看服务器概况

1. 打开任意服务器的详情页面
2. 右侧面板顶部即可看到"服务器概况"卡片
3. 所有信息自动加载和更新

### 运行时长

- 启动服务器后自动开始计时
- 停止服务器后显示"未运行"
- 每秒自动更新

### 彩色日志

- 日志自动根据内容着色
- 错误信息红色醒目
- 玩家事件蓝色/珊瑚色区分
- 成功信息绿色提示

### 日志换行

- 长日志自动换行，无需横向滚动
- 保持代码可读性
- 支持复制和全选

## 技术亮点

1. **性能优化**
   - 运行时长计时器使用DispatcherTimer，不阻塞UI
   - 资源监控降低刷新频率（5秒）
   - 配置文件只在加载时读取一次

2. **用户体验**
   - 彩色日志提高可读性
   - 自动换行避免横向滚动
   - 实时运行时长显示
   - 完整的服务器信息一目了然

3. **代码质量**
   - 清晰的方法命名
   - 完善的错误处理
   - 支持中文本地化
   - 易于扩展和维护

## 效果展示

### 服务器概况卡片
```
服务器概况
━━━━━━━━━━━━━━━━━━━━
服务器名称    我的生存服务器
游戏版本      1.21.1
服务端核心    Paper
内存限制      1024MB - 2048MB
运行端口      25565
地图种子      12345678
正版验证      关闭
游戏模式      生存
游戏难度      普通
运行时长      2小时 35分钟 42秒
```

### 彩色日志示例
```
[INFO] Starting minecraft server version 1.21.1  (浅灰色)
[INFO] Done (5.234s)! For help, type "help"      (浅绿色)
Player123 joined the game                         (浅蓝色)
[WARN] Can't keep up! Is the server overloaded?  (橙色)
[ERROR] Exception in server tick loop            (红色加粗)
Player123 left the game                           (浅珊瑚色)
```

## 总结

所有功能已完整实现并测试通过！服务器控制台现在更加专业、美观和易用。
