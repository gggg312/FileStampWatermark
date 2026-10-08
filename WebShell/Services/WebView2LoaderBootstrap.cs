using System;
using System.IO;
using System.Reflection;

namespace PDFQFZ.WPF.Services
{
    /// <summary>
    /// WebView2Loader 引导器（阶段 10 单 EXE 打包）。
    /// 背景：Costura 只合并托管程序集；WebView2Loader.dll 是原生 DLL，
    /// 且必须位于 EXE 同目录（或系统可搜索目录）才能被 WebView2 初始化加载。
    /// 本类与 PdfiumBootstrap 同模式：把嵌入资源中的 x64 WebView2Loader.dll
    /// 提取到 EXE 同目录，保证交付形态为单 EXE（原生引擎启动时自动生成）。
    /// 必须在 EnsureCoreWebView2Async 之前调用。
    /// </summary>
    internal static class WebView2LoaderBootstrap
    {
        private const string DllName = "WebView2Loader.dll";

        // 嵌入资源名（与 WebShell.csproj 中 EmbeddedResource 的 LogicalName 一致）
        private const string ResourceX64 = "PDFQFZ.WebShell.assets.WebView2Loader-x64.dll";

        private static readonly object syncRoot = new object();
        private static bool ready;

        /// <summary>程序启动时调用：把 WebView2Loader.dll 提取到 EXE 同目录（已存在且大小一致则跳过）。</summary>
        public static void EnsureWebView2LoaderReady()
        {
            if (ready)
            {
                return;
            }

            lock (syncRoot)
            {
                if (ready)
                {
                    return;
                }

                try
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string dllPath = Path.Combine(baseDir, DllName);

                    // 1) 从嵌入资源读取（仅 64 位进程需要，本程序 Prefer32Bit=false 固定 64 位）
                    byte[] data = ReadEmbeddedResource(ResourceX64);
                    if (data == null || data.Length == 0)
                    {
                        return;
                    }

                    // 2) 写入 EXE 同目录（已存在且大小一致则跳过，避免每次启动都重写）
                    bool needWrite = !File.Exists(dllPath);
                    if (!needWrite)
                    {
                        try
                        {
                            needWrite = new FileInfo(dllPath).Length != data.Length;
                        }
                        catch
                        {
                            needWrite = true;
                        }
                    }
                    if (needWrite)
                    {
                        try
                        {
                            File.WriteAllBytes(dllPath, data);
                        }
                        catch
                        {
                            return;
                        }
                    }
                }
                catch
                {
                }
                finally
                {
                    ready = true;
                }
            }
        }

        /// <summary>从当前程序集读取嵌入资源。</summary>
        private static byte[] ReadEmbeddedResource(string resourceName)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    return null;
                }

                byte[] buffer = new byte[stream.Length];
                int offset = 0;
                while (offset < buffer.Length)
                {
                    int read = stream.Read(buffer, offset, buffer.Length - offset);
                    if (read <= 0)
                    {
                        break;
                    }

                    offset += read;
                }

                return offset == buffer.Length ? buffer : null;
            }
        }
    }
}
