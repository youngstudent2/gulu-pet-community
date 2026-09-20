# Gulu Pet Community（咕噜桌宠社区版）

Gulu Pet Community 是一个离线优先、可换皮的 Windows 桌面宠物框架，基于 WPF、WinForms、.NET 10 与 SkiaSharp 构建。本仓库是一个干净的社区版本，面向学习、定制与二次开发。仓库内置一段经授权保留的咕噜动画（idle_breathe），以及咕噜的应用图标与部分 UI 素材；其余私有生产内容（回忆视频、明信片、饰品、以及其它动画片段）均不包含在内。完整的素材来源说明见 `ASSETS.md`。

## 已包含的功能

应用包含动画 WebP 播放、数据驱动的行为与对话、基于效用（utility）的行为选择、本地设置与进度持久化、托盘与开机启动集成，以及面向开发者的测试工具。部分可选的历史功能模块作为扩展点保留，但默认组合不依赖任何私有的明信片、回忆、饰品、日记、日志或更新服务。

默认构建不会执行任何远程日记生成、遥测上传、更新检查或运行时上下文查询。日记生成使用本地确定性实现。HTTP 客户端与外部服务提供方需要集成者自行提供配置并显式启用。

## 环境要求

开发需要 Windows 10 1809 或更高版本，以及 .NET SDK 10.0.302 或兼容的更高 10.0 补丁版本。Visual Studio 2026 或其它支持 .NET 桌面开发的 IDE 为可选项。Python 与 Pillow 仅在需要重新生成占位素材时才用到。

## 构建与运行

```powershell
dotnet restore GuluPetCommunity.sln
dotnet build GuluPetCommunity.sln -c Release
dotnet run --project src/GuluPet/GuluPet.csproj
```

运行可执行测试程序：

```powershell
dotnet run --project tests/GuluPet.Tests/GuluPet.Tests.csproj -c Release
```

## 替换角色（换皮）

运行时内容以 `src/GuluPet/Assets` 为根目录。编辑 `runtime-content.json`，在 `Animations` 下为每个动画建立一个目录，并在各自的 `clip.json` 中声明该片段。然后更新 `Data/behaviors.json` 与 `Data/dialogues.json`，确保所引用的每个动画片段 ID 与对话 ID 都存在。至少要有一个 `normal` 状态下的行为被标记为状态兜底（state fallback）。描述文件中的 `framesPerSecond` 与 `frameSize` 必须与每个片段以及 `RuntimeContentDescriptor.CommunityContract` 保持一致。

`tools/generate_sample_assets.py` 是供分叉使用的占位素材生成器：它为缺失的图标与 UI 素材补上简单的几何图形占位（不会覆盖已存在的文件）：

```powershell
python -m pip install Pillow
python tools/generate_sample_assets.py
```

持久化、开机启动注册与单实例处理所使用的身份标识集中定义在 `src/GuluPet/AppIdentity.cs`。分叉在以其它名称发布前，应连同安装器默认值一起修改这些常量。

## 发布与打包

生成依赖框架（framework-dependent）的 Windows 构建：

```powershell
dotnet publish src/GuluPet/GuluPet.csproj -c Release -r win-x64 --self-contained false -o publish/win-x64
```

Inno Setup 脚本为 `installer/GuluPet.iss`。编译时需传入应用版本号、发布目录与图标路径：

```powershell
ISCC.exe /DAppVersion=0.1.0 /DSourceDir=publish\win-x64 /DSetupIconPath=src\GuluPet\Assets\App.ico installer\GuluPet.iss
```

## 隐私与安全

默认的网络与本地数据行为见 `PRIVACY.md`，漏洞报告方式见 `SECURITY.md`，素材来源见 `ASSETS.md`。本项目采用 MIT 许可证；第三方软件包仍遵循其各自的许可证。
