using System;
using System.Threading;
using System.Windows;
using PDFQFZ.WebShell.Services;

namespace PDFQFZ.WebShell
{
    /// <summary>
    /// App.xaml 入口。沿用 WPF 版单实例约定（Mutex），重复启动提示后退出。
    /// </summary>
    public partial class App : Application
    {
        // V2.4.0.9：mutex 名加版本号——旧版本残留进程（如 V2.4.0.3 多个进程长期开着）持有旧名 mutex 时，
        // 新版本启动会误判"程序已经在运行"而退出；版本化后多版本可并行，同版本仍单实例拦截。
        // V368：按程序集版本动态生成（此前为固定名，本机 V334 残留进程持锁导致 V368 无法启动、且该进程权限
        // 高于当前进程无法被终止——版本化后不同版本互不阻塞，同版本仍单实例）。
        private static string SingleInstanceMutexName
        {
            get
            {
                var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return "PDFQFZ_Web_GG_SingleInstance_" + v.Major + "." + v.Minor + "." + v.Build + "." + v.Revision;
            }
        }
        private Mutex _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            // V1.0.0.37：INI 日志委托注入（Library 不再编译期引用 WebShell，此处桥接）
            PDFQFZ.Library.IniFileHelper.LogAction = PDFQFZ.WebShell.Services.CSharpBridge.WriteLog;
            // V2.4.0.5：旧布局迁移（V2.4.0.4 及更早 config.ini/印章库在 EXE 根目录 → 运行组件\），须先于一切配置读取
            PDFQFZ.WPF.Services.AppConfig.MigrateLegacyLayout();
            // V1.0.0.14：统一运行日志 app_log.log——启动轮转（保留上次+本次两段），须在崩溃处理器注册前初始化
            AppLog.Init();
            // V374：出厂说明文档（智能体调用手册/用户使用说明）嵌入 EXE，每次启动覆盖写出到软件目录（GUI 与批处理均执行）
            DocsBootstrap.EnsureDocsGenerated();
            // pdfium 引擎：启动即提取到 EXE 同目录并注册加载路径（与 WPF 版一致，必须先于首次渲染）
            PDFQFZ.WPF.Services.PdfiumBootstrap.EnsurePdfiumReady();
            // V373 命令行批处理模式：--batch <任务.json> [--log <路径>]——供智能体调用。
            // 静默执行（不显示窗口、跳过单实例锁）；Environment.Exit 强制退出，避免后台线程阻止进程结束。
            if (BatchCli.IsVersionMode(e.Args))
            {
                // V376：--version 自证接口——输出产品名+版本号到 stdout（智能体重定向捕获即可确认身份，不依赖文件名）。
                // 强制 UTF-8 字节写出：.NET Framework 重定向时 Console 默认用系统 ANSI（GBK），智能体按 UTF-8 读会乱码。
                var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                try
                {
                    byte[] line = System.Text.Encoding.UTF8.GetBytes("文件批量盖章与水印工具 " + v.ToString() + "\r\n");
                    using (var st = System.Console.OpenStandardOutput()) st.Write(line, 0, line.Length);
                }
                catch { }
                Environment.Exit(0);
                return;
            }
            if (BatchCli.IsStampInfoMode(e.Args))
            {
                // V1.0.0.33：--stamp-info 配置自证接口（智能体自检读取 config.ini 全部参数，只读不写）
                int code = BatchCli.RunStampInfo();
                Environment.Exit(code);
                return;
            }
            if (BatchCli.IsBatchMode(e.Args))
            {
                int code = BatchCli.Run(e.Args);
                Environment.Exit(code);
                return;
            }
            // V368：E2E 模式跳过单实例互斥（PDFQFZ_SHELL_NOMUTEX=1，仅 run_e2e.ps1 设置）——本机曾出现
            // 残留旧版本进程权限高于当前进程、无法被终止、持同名 mutex 导致 E2E 启动误判"已在运行"退出；
            // 产品正常启动（未设该变量）行为不变。
            if (Environment.GetEnvironmentVariable("PDFQFZ_SHELL_NOMUTEX") != "1")
            {
                bool createdNew;
                _mutex = new Mutex(true, SingleInstanceMutexName, out createdNew);
                if (!createdNew)
                {
                    MessageBox.Show("程序已经在运行，请到已打开的窗口中操作。", "文件批量盖章与水印工具",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    Shutdown();
                    return;
                }
            }
            // 全局未处理异常：写崩溃日志便于定位（E2E 曾出现"已停止工作"弹窗）
            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                try { AppLog.Write("[CRASH] " + ex.ExceptionObject); } catch { }
            };
            this.DispatcherUnhandledException += (s, ex) =>
            {
                try { AppLog.Write("[CRASH-UI] " + ex.Exception); } catch { }
            };
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { if (_mutex != null) { _mutex.ReleaseMutex(); _mutex.Dispose(); } }
            catch (ApplicationException) { /* 其他实例已接管 */ }
            base.OnExit(e);
        }
    }
}
