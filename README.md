# ZMSL - Minecraft Server Launcher

ZMSL（ZMSL Minecraft Server Launcher）是一个功能强大的Minecraft服务器管理工具，提供一站式服务器创建、管理和监控解决方案。

## 项目结构

- **TinyPinyin** - 中文拼音处理库，提供中文转拼音功能
- **ZMSL.App** - 主应用程序，包含完整的服务器管理界面和功能
- **ZMSL.Shared** - 共享库，包含项目通用的数据模型和DTO

## 主要功能

### 服务器管理
- 🚀 一键创建本地/远程服务器
- ⚙️ 服务器配置管理
- 📊 实时服务器状态监控
- 🔄 自动备份功能
- 📝 日志分析和管理

### 多节点支持
- 🏠 本地服务器管理
- ☁️ 远程服务器连接
- 🐧 Linux节点支持

### 高级特性
- 🔒 用户认证系统
- 📱 移动端远程管理
- 🔧 插件管理
- 📚 服务器文档管理
- 🎮 玩家论坛功能

### 网络功能
- 🌐 Frp内网穿透集成
- ⚡ StarryFrp支持
- 🚇 MeFrp支持

### 技术特性
- 🎨 现代化WinUI 3界面
- 📱 响应式设计
- 🔄 自动更新系统
- 🛡️ 安全的通信协议

## 系统要求

- Windows 10 1903或更高版本
- .NET 6.0或更高版本
- 至少4GB RAM
- 至少10GB可用磁盘空间

## 安装方法

### 方法一：使用安装程序
1. 下载最新的安装程序
2. 运行安装程序并按照向导完成安装
3. 启动应用程序

### 方法二：直接运行
1. 下载发布的压缩包
2. 解压到任意目录
3. 运行 `ZMSL.App.exe`

## 快速开始

1. **首次启动**：运行应用程序，创建用户账户
2. **创建服务器**：点击"创建服务器"按钮，选择服务器类型和版本
3. **配置服务器**：设置服务器参数、端口、内存等
4. **启动服务器**：点击"启动"按钮开始运行服务器
5. **管理服务器**：使用控制台、文件管理器等工具管理服务器

## 开发环境

### 开发要求
- Visual Studio 2022或更高版本
- .NET 6.0 SDK
- Windows 10 SDK 10.0.19041.0或更高版本

### 构建步骤
1. 克隆仓库：`git clone https://github.com/ZIserver/ZMSL.git`
2. 打开解决方案文件：`ZMSL.sln`
3. 构建解决方案：`dotnet build`
4. 运行应用程序：`dotnet run --project ZMSL.App`

## 贡献指南

欢迎贡献代码！请遵循以下步骤：

1. Fork仓库
2. 创建特性分支：`git checkout -b feature/AmazingFeature`
3. 提交更改：`git commit -m 'Add some AmazingFeature'`
4. 推送到分支：`git push origin feature/AmazingFeature`
5. 创建Pull Request

## 许可证

本项目采用MIT许可证。详见[LICENSE](LICENSE)文件。

## 联系方式

- 📧 邮箱：contact@zmsl.dev
- 🌐 官网：https://zmsl.dev
- 📱 官方QQ群：123456789

## 更新日志

### v3.0.0
- 全新WinUI 3界面
- 增强的服务器管理功能
- 远程服务器支持
- 多语言支持
- 性能优化

## 致谢

感谢所有为项目做出贡献的开发者和用户！

---

**注意**：本项目仅用于学习和研究目的，请遵守Minecraft的使用条款和相关法律法规。