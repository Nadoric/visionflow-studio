using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using HalconDotNet;
using HalconVisionStudio.Shared.Diagnostics;

namespace HalconVisionStudio.Services
{
    /// <summary>
    /// 在创建图像控件之前加载与 .NET 接口版本匹配的原生运行库。
    /// 只配置当前进程，不修改系统环境变量或 Halcon 安装目录。
    /// </summary>
    internal static class HalconRuntimeBootstrap
    {
        private static readonly object InitializationLock = new();
        private static bool initialized;
        private static IntPtr nativeHandle;

        public static bool IsAvailable { get; private set; }
        public static string NativeDirectory { get; private set; }
        public static string StatusMessage { get; private set; } = "HALCON 尚未初始化。";

        public static bool EnsureInitialized()
        {
            lock (InitializationLock)
            {
                if (initialized)
                {
                    return IsAvailable;
                }

                try
                {
                    var managedVersion = typeof(HOperatorSet).Assembly.GetName().Version;
                    var runtimeRoot = Path.Combine(AppContext.BaseDirectory, "HalconRuntime");
                    NativeDirectory = Path.Combine(runtimeRoot, "bin", "x64-win64");
                    var libraryPath = Path.Combine(NativeDirectory, "halcon.dll");
                    if (!File.Exists(libraryPath))
                    {
                        throw new FileNotFoundException("项目内的 HALCON 原生运行库缺失。", libraryPath);
                    }

                    var nativeVersion = FileVersionInfo.GetVersionInfo(libraryPath);
                    if (nativeVersion.FileMajorPart != managedVersion.Major ||
                        nativeVersion.FileMinorPart != managedVersion.Minor)
                    {
                        throw new BadImageFormatException("项目内的 HALCON 原生运行库与 .NET 接口版本不匹配。");
                    }

                    // 覆盖当前进程继承的安装路径，使运行库从应用目录读取配套资源和许可。
                    Environment.SetEnvironmentVariable("HALCONROOT", runtimeRoot);
                    Environment.SetEnvironmentVariable("HALCONARCH", "x64-win64");
                    nativeHandle = NativeLibrary.Load(
                        libraryPath,
                        typeof(HOperatorSet).Assembly,
                        DllImportSearchPath.UseDllDirectoryForDependencies |
                        DllImportSearchPath.SafeDirectories);

                    NativeLibrary.SetDllImportResolver(typeof(HOperatorSet).Assembly, ResolveNativeLibrary);

                    // 同时验证原生接口和许可，防止控件在 OnRender 中首次加载失败。
                    HOperatorSet.GenImageConst(out var probeImage, "byte", 1, 1);
                    probeImage.Dispose();

                    IsAvailable = true;
                    StatusMessage = $"HALCON {managedVersion.Major}.{managedVersion.Minor:00} 已就绪。";
                    ExecutionLogService.App($"{StatusMessage} 运行库目录：{NativeDirectory}");
                }
                catch (Exception ex)
                {
                    IsAvailable = false;
                    StatusMessage = "HALCON 暂不可用，请检查程序目录中的 HalconRuntime 运行库与 HALCON 许可。";
                    ExecutionLogService.App(StatusMessage, ExecLogLevel.Warn, ex.ToString());
                }

                initialized = true;
                return IsAvailable;
            }
        }

        private static IntPtr ResolveNativeLibrary(
            string libraryName,
            System.Reflection.Assembly assembly,
            DllImportSearchPath? searchPath)
        {
            if (string.Equals(libraryName, "halcon", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(libraryName, "halcon.dll", StringComparison.OrdinalIgnoreCase))
            {
                // 句柄保留到进程退出，避免原生图像或窗口仍存活时卸载运行库。
                return nativeHandle;
            }

            return IntPtr.Zero;
        }
    }
}
