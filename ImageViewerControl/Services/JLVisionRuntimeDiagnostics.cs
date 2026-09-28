using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ImageViewer.Services;

/// <summary>
/// Builds actionable diagnostics for the native JLVision runtime.
/// </summary>
internal static class JLVisionRuntimeDiagnostics
{
    internal const string NativeLibraryName = "JLVisionCore.dll";

    internal static string RuntimePath => Path.Combine(AppContext.BaseDirectory, NativeLibraryName);

    internal static string BuildMissingRuntimeMessage()
    {
        return $"JLVision 运行库不可用：未找到 '{RuntimePath}'。" +
               $" 当前进程架构为 {RuntimeInformation.ProcessArchitecture}，" +
               $"运行时目录为 '{AppContext.BaseDirectory}'。" +
               " 请将匹配架构的 JLVisionCore.dll 放在应用程序目录后重启。";
    }

    internal static string BuildLoadFailureMessage(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return $"JLVision 运行库加载失败：{exception.Message}。" +
               $" 文件路径：'{RuntimePath}'；进程架构：{RuntimeInformation.ProcessArchitecture}。" +
               " 请确认 DLL 与应用程序架构一致，并安装所需的 Visual C++ 运行库。";
    }
}
