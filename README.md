# ImageViewer

ImageViewer 是一个面向 Windows 桌面应用的 WPF 图像查看、ROI 标注与体数据浏览控件。它提供可嵌入业务系统的控件库、可运行的演示宿主，以及覆盖交互、持久化与 WPF 集成路径的自动化测试。

项目目标是把图像浏览、测量、标注、分析、3D/MPR 浏览与可交付项目文件收敛为稳定的桌面端组件能力。

## 功能概览

### 图像、ROI 与分析

- 打开 `png`、`jpg`、`jpeg`、`bmp`、`tif`、`tiff` 图像，支持缩放、平移、适配视图和实际尺寸浏览。
- 使用矩形、圆、椭圆、多边形、折线等 ROI 进行标注；支持标签、颜色、可见性与锁定状态管理。
- 提供长度、折线、面积、角度、卡尺、点坐标、自动边缘吸附点、自动圆测量、圆心距、三点圆、拟合椭圆与梯度检测对齐等测量和分析辅助工具。
- 显示像素信息、统计摘要、剖面图、直方图、比例尺和属性面板。
- 基于图像分位数给出对比度、亮度与伪彩显示建议；建议不会自动修改当前显示参数。
- 大图会按像素规模自动启用多分辨率金字塔、分块渲染和自适应缓存，缩放平移时只处理当前视口。

### 体数据与 3D 浏览

- 基于 HelixToolkit SharpDX 渲染体数据的 3D 视图。
- 提供轴位、冠状、矢状 MPR 视图，并在 2D MPR 与 3D 交叉切面之间同步位置。
- 在冠状或矢状视图中按 `↑` / `↓` 切换切片；轴位视图支持滑块和鼠标滚轮切换。
- demo 支持多选图像切片导入体数据；冠状和矢状视图提供切片滑块、上一层/下一层按钮和鼠标滚轮切层，并与 3D 交叉切面保持同步。
- 支持 3D 相机预设、重置、适配体数据、切面显示和透明度控制。

### 导出、会话与项目交付

- 导出当前视图 PNG、分析 CSV 与 ROI JSON；分析 CSV 同时生成包含源图像指纹、标定、渲染设置和完整 ROI 输入的 `.metadata.json`。
- 保存 `.ivsession` 会话，包括图像路径、ROI、标定和视图状态。
- 导出 `.ivpkg` 项目包，将会话及图像资产一并打包，适合归档与交接。
- 删除所选 ROI 或清空 ROI 后可使用 `Ctrl+Z` 恢复。
- 分割候选接受后会生成 Blob ROI 并纳入撤销栈；会话修改会显示未保存状态，启动时可恢复自动保存内容。

## 快速开始

### 前置条件

- Windows 10 或更高版本。
- .NET SDK `10.0.302` 或与 [global.json](global.json) 兼容的后续功能带 SDK。
- 支持 WPF 的桌面环境和可用的 DirectX 图形驱动程序。

从仓库根目录执行：

```powershell
dotnet restore ImageViewer.sln --locked-mode
dotnet build ImageViewer.sln --configuration Release --no-restore --nologo
dotnet test ImageViewer.sln --configuration Release --no-build --nologo
dotnet run --project ImageViewerDemo/ImageViewerDemo.csproj
```

演示程序启动后，可点击“打开图像”或按 `Ctrl+O` 选择图像文件。右键菜单支持直接键入命令搜索；按 `Ctrl+F` 可将图像适应窗口。

## 仓库结构

| 目录 | 用途 |
| --- | --- |
| [ImageViewerControl](ImageViewerControl/) | WPF 控件、ROI 模型、渲染、菜单、对话框、分析、导出和宿主服务。 |
| [ImageViewerDemo](ImageViewerDemo/) | 演示应用，用于手动验证与集成参考。 |
| [ImageViewerControl.Tests](ImageViewerControl.Tests/) | xUnit 测试，覆盖命令、交互、持久化、体数据和 WPF smoke 场景。 |
| [docs](docs/) | 用户操作、文件格式、导入导出规则、术语与架构边界文档。 |

## 嵌入宿主应用

使用 `Microsoft.Extensions.DependencyInjection` 的 WPF 宿主可注册 `ImageViewerHost` 和 `ImageViewer`：

```csharp
using ImageViewer.Controls;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddImageViewerHost();

using ServiceProvider serviceProvider = services.BuildServiceProvider();
ImageViewer viewer = serviceProvider.GetRequiredService<ImageViewer>();
```

通过 `AddImageViewerHost(builder => { ... })` 可配置宿主构建器；也可以在注册前替换以下服务以接入现有应用基础设施：

- `IImageViewerLatestTaskSchedulerFactory`
- `IImageViewerPeriodicTaskSchedulerFactory`
- `IImageViewerRefreshSchedulerFactory`
- `IImageViewerAnalysisDiagnostics`

`IImageViewerTelemetry` 使用 `ActivitySource` 和 `Meter` 发出非关键分析异常及性能诊断信号，可由 OpenTelemetry 订阅。默认日志写入 `Trace`；使用 `Microsoft.Extensions.Logging` 时，先调用 `services.AddLogging()`，再调用 `services.AddImageViewerMicrosoftLogging()`。

## 文件与安全边界

| 文件 | 内容 | 适用场景 |
| --- | --- | --- |
| ROI JSON | ROI 集合和标定信息 | 交换标注或算法结果。 |
| `.ivsession` | 图像路径、ROI、标定与视图状态 | 继续本地工作。 |
| `.ivpkg` | 会话和可选图像资产的 ZIP 容器 | 归档或向他人交付完整项目。 |

导出 `.ivpkg` 时先生成同目录临时文件，成功后才替换目标文件。加载项目包会校验路径、条目数量和解压后大小，以防止 Zip Slip 和异常归档消耗过多资源：最多 `1,024` 个条目、单条目最多 `256 MiB`、总解压内容最多 `512 MiB`。

详细格式见 [docs/project-file-format.md](docs/project-file-format.md)，导入导出兼容性见 [docs/import-export-conventions.md](docs/import-export-conventions.md)。

## 开发与验证

项目使用集中式包版本、NuGet 锁文件、依赖审计、.NET 分析器和警告即错误策略。提交前建议执行完整验证：

```powershell
dotnet restore ImageViewer.sln --locked-mode
dotnet build ImageViewer.sln --configuration Release --no-restore --nologo
dotnet test ImageViewer.sln --configuration Release --no-build --filter "Category!=Smoke&Category!=Performance" --nologo
dotnet test ImageViewerControl.Tests/ImageViewerControl.Tests.csproj --configuration Release --no-build --filter "Category=Smoke" --nologo
```

所有新增用户可见文本应放在 `ImageViewerControl/Resources/UiText.resx` 或 `ImageViewerDemo/Resources/DemoText.resx` 中。WPF 相关实现保留在控件工程；新建可跨平台算法、数据契约和序列化逻辑应遵循 [docs/architecture-boundaries.md](docs/architecture-boundaries.md) 的依赖边界。

## 文档索引

- [使用说明](docs/usage.md)
- [项目文件格式](docs/project-file-format.md)
- [导入导出约定](docs/import-export-conventions.md)
- [术语与本地化边界](docs/terminology.md)
- [架构边界](docs/architecture-boundaries.md)
