# NuGet 发布

仓库发布三个包：

- `ImageViewer.Core`：仅依赖 `net10.0`，提供与 WPF 无关的测量统计和核心契约。
- `ImageViewerControl`：目标框架为 `net10.0-windows`，依赖同版本的 `ImageViewer.Core`、`JLVisionLib`、HelixToolkit 和 Microsoft.Extensions 运行时包。它不重复携带原生 `JLVisionCore.dll`。
- `JLVisionLib`：提供托管视觉封装和 `win-x64` 的 `runtimes/win-x64/native/JLVisionCore.dll`。

每个包都会生成 `.nupkg` 和 `.snupkg`。版本由 SemVer 2.0 字符串控制，项目默认版本为 `0.1.0`；发布流水线会用 Git 标签或手动输入版本覆盖它。

本地只打包不发布：

```powershell
./build/pack.ps1 -Version 0.1.0-rc.1
```

发布前应检查 `artifacts/packages` 中的六个文件，并确认控件包 nuspec 依赖相同版本的 Core 与 JLVisionLib 包；同时确认原生 DLL 只出现在 JLVisionLib 包中。

GitHub Actions 的 `Publish NuGet packages` 工作流有两种入口：

1. 推送 `v0.1.0` 标签，会自动打包并进入发布 job。
2. 手动运行工作流，输入版本；`publish=false` 只生成并上传包，`publish=true` 才会推送到 NuGet.org。

发布 job 使用 `nuget` environment 中的 `NUGET_API_KEY`，并启用 `--skip-duplicate`，重复版本不会导致第二次推送失败。普通分支和普通 CI 不会推送包。
