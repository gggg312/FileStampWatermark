using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using PDFQFZ.WebShell.Services;
using PDFQFZ.WPF.Services;

namespace PDFQFZ.WebShell
{
    /// <summary>
    /// 壳主窗口（阶段 1）：以虚拟域名 app:// 映射 WebUI\prototype 目录并加载 index.html。
    /// WebRoot 定位顺序：① EXE 同目录 WebUI\prototype（交付形态）→ ② 源码侧 Git仓库\WebUI\prototype（开发形态）。
    /// </summary>
    public partial class MainWindow : Window
    {
        private string _webRoot;
        private CSharpBridge _bridge;
        // V2.4.0.21：Win32 OLE 拖放接管（WebView2 内容区拖入拿真实路径——文件/文件夹/多文件）
        private PDFQFZ.WebShell.Services.OleDropTarget _oleTarget;

        public MainWindow()
        {
            InitializeComponent();
            // V300: 标题栏版本号自动关联 AssemblyInfo.cs
            // V2.4.0.398：主标题改「文件批量盖章与水印工具」+ 可定制后缀（AppConfig.TitleSuffix，赞赏/定制版入口；默认（GG 优化版））
            try { RefreshTitle(); } catch { }
            Loaded += OnLoadedAsync;
            // V2.4.0.24：关闭时保存窗口位置/大小（对齐 WPF Closing += SaveWindowState）
            Closing += (s, e) =>
            {
                try { PDFQFZ.WPF.Services.AppConfig.SaveWindowState(Left, Top, Width, Height); } catch { }
            };
            // V2.4.0.39：窗口显示前预加载配置并按保存尺寸定位——消除启动"先大后小"跳变
            // （XAML 初始尺寸已与 config 默认一致 1280×950；FitWindowToScreen 需窗口加载后的 DPI/屏幕修正，仍在 Loaded 执行）
            try { PDFQFZ.WPF.Services.AppConfig.LoadFromIni(); } catch { }
            // V2.4.0.402：vip.ini（收费版）可携带 titleSuffix，加载后刷新窗口标题（默认（GG 优化版）不受影响）
            try { RefreshTitle(); } catch { }
            try
            {
                if (PDFQFZ.WPF.Services.AppConfig.WindowWidth > 0) Width = PDFQFZ.WPF.Services.AppConfig.WindowWidth;
                if (PDFQFZ.WPF.Services.AppConfig.WindowHeight > 0) Height = PDFQFZ.WPF.Services.AppConfig.WindowHeight;
                if (PDFQFZ.WPF.Services.AppConfig.WindowLeft > -1 && PDFQFZ.WPF.Services.AppConfig.WindowTop > -1)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = PDFQFZ.WPF.Services.AppConfig.WindowLeft;
                    Top = PDFQFZ.WPF.Services.AppConfig.WindowTop;
                }
            }
            catch { }
        }

        /// <summary>V2.4.0.405：刷新窗口标题（vip.ini titleSuffix 修改后调用；收费版自定义标题生效）</summary>
        public static void RefreshTitle()
        {
            try
            {
                var w = System.Windows.Application.Current != null ? System.Windows.Application.Current.MainWindow : null;
                if (w == null) return;
                var vv = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                w.Title = "文件批量盖章与水印工具 v" + vv.Major + "." + vv.Minor + "." + vv.Build + PDFQFZ.WPF.Services.AppConfig.TitleSuffix; // V1.0.0.14：发布版标题栏三位版本号（四位在交付文件夹/软件内展示）
            }
            catch { }
        }

        private async void OnLoadedAsync(object sender, RoutedEventArgs e)
        {
            try
            {
                _webRoot = LocateWebRoot();
                if (_webRoot == null)
                {
                    MessageBox.Show("未找到 WebUI\\prototype 目录（含 index.html 与 lib\\）。\n请确认程序放在完整文件夹内。",
                        "文件批量盖章与水印工具", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                // 阶段 10 单 EXE：WebView2Loader.dll 原生引擎启动时提取到 EXE 同目录（须先于 EnsureCoreWebView2Async）
                PDFQFZ.WPF.Services.WebView2LoaderBootstrap.EnsureWebView2LoaderReady();
                // V2.4.0.5：WebView2 用户数据（缓存）指定到 运行组件\WebView2缓存，不再散在 EXE 根目录（整文件夹拷贝可换电脑）
                string wv2Dir = System.IO.Path.Combine(PDFQFZ.WPF.Services.AppConfig.RuntimeDir, "WebView2缓存");
                try { System.IO.Directory.CreateDirectory(wv2Dir); } catch { wv2Dir = null; }
                var wv2Env = wv2Dir != null ? await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, wv2Dir) : null;
                if (wv2Env != null) await webView.EnsureCoreWebView2Async(wv2Env); else await webView.EnsureCoreWebView2Async();
                // V2.4.0.39：WebView2 加载前背景设界面底色（#F5F6FA），避免启动"大白窗"刺眼
                try { webView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 245, 246, 250); } catch { }
                // V2.4.0.17→回退：不禁用 WebView2 外部拖放（实测 AllowExternalDrop=false 后 HwndHost 拦截拖放消息，
                // 窗口级 DragOver/Drop 在 WebView 区域收不到 → 拖入显示禁止图标 🚫，文件/文件夹都拖不进来）。
                // 恢复默认 true：WebView 区域拖放由前端 document 级监听处理（文件走 base64，目录项有 f.path 走 openDir）；
                // 窗口标题栏/边框（非 WebView 区域）仍由窗口级 Win_DragOver/Win_Drop 处理（真实路径，文件+文件夹）。
                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "app", _webRoot, CoreWebView2HostResourceAccessKind.Allow);
                // 阶段 4：预览渲染缓存目录（虚拟域名 https://cache/ 直接引用渲染出的 PNG）
                // V2.4.0.9：缓存目录按进程隔离（%TEMP%\pdfqfz_web_preview_<pid>）——旧版本残留进程
                // 与新版共用 %TEMP%\pdfqfz_web_preview 会互相踩 page_*.png（新版清空时旧版正在显示/写入，
                // 导致新版渲染图片缺失/被覆盖，预览区空白）。进程隔离后多版本并行互不干扰。
                string cacheDir = Path.Combine(Path.GetTempPath(), "pdfqfz_web_preview_" + System.Diagnostics.Process.GetCurrentProcess().Id);
                try
                {
                    Directory.CreateDirectory(cacheDir);
                    foreach (var f in Directory.GetFiles(cacheDir, "page_*.png")) File.Delete(f);
                }
                catch { }
                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "cache", cacheDir, CoreWebView2HostResourceAccessKind.Allow);
                // 注入 C# 桥对象（JS 端 window.CSharpBridge），事件出口 = PostWebMessageAsJson
                // V2.4.0.24：恢复上次窗口位置/大小（对齐 WPF LoadConfigToUi + FitWindowToScreen，越界修正）
                // V2.4.0.39：LoadFromIni 已提前到构造函数（窗口显示前），此处仅做尺寸恢复+越界修正
                RestoreWindowState();
                _bridge = new CSharpBridge(json =>
                {
                    // 后台线程（生成批处理）推事件必须切回 UI 线程再 PostWebMessageAsJson
                    try { webView.Dispatcher.Invoke(() => webView.CoreWebView2.PostWebMessageAsJson(json)); }
                    catch { }
                });
                _bridge.SetRenderCache(cacheDir, "https://cache");
                webView.CoreWebView2.AddHostObjectToScript("CSharpBridge", _bridge);
                webView.CoreWebView2.Navigate("https://app/index.html");
                // 加载完成后推送一条"壳就绪"事件（JS 端 bridge.js 可借此感知桥可用）
                webView.CoreWebView2.NavigationCompleted += (s2, e2) =>
                {
                    if (!e2.IsSuccess) return;
                    webView.CoreWebView2.PostWebMessageAsJson(
                        "{\"kind\":\"shell-ready\",\"payload\":{\"version\":\"" + _bridge.GetVersion() + "\"}}");
                    // V2.4.0.21：Win32 OLE 拖放接管——注册到 WebView2 窗口整树（host+全部子窗口，内容区在子窗口），
                    // 内容区拖入文件/文件夹拿真实系统路径（WebView2 内容区 HTML5 拖放拿不到 File.path，此为治本通道；
                    // 每次导航后幂等重注册自愈）
                    // 注册结果由 E2E OLEPROBE 探针（PDFQFZ_SHELL_OLEPROBE=1）写入 dump 确认（hr==0 成功）
                    try
                    {
                        if (_oleTarget == null) _oleTarget = new PDFQFZ.WebShell.Services.OleDropTarget(OnOleDrop);
                        PDFQFZ.WebShell.Services.OleDragDrop.RegisterTree(webView.Handle, _oleTarget);
                    }
                    catch { }
                };
                // 开发期 DOM 自检（仅设置环境变量 PDFQFZ_SHELL_DUMP 时启用，不影响正常使用）
                string dumpPath = Environment.GetEnvironmentVariable("PDFQFZ_SHELL_DUMP");
                if (!string.IsNullOrEmpty(dumpPath))
                {
                    webView.CoreWebView2.NavigationCompleted += async (s2, e2) =>
                    {
                        if (!e2.IsSuccess) return;
                        try
                        {
                            await System.Threading.Tasks.Task.Delay(2500);
                            string j1 = await webView.CoreWebView2.ExecuteScriptAsync("String(1+1)");
                            System.IO.File.WriteAllText(dumpPath, "step1:" + j1);
                            // OLEPROBE（V2.4.0.21）：确认 Win32 OLE 拖放注册成功（仅 PDFQFZ_SHELL_OLEPROBE=1 时；hr==0 成功）
                            // 三路诊断：标准 WinForms 窗口 vs 主窗口 vs WebView 整树 + OleInitialize 结果
                            if (Environment.GetEnvironmentVariable("PDFQFZ_SHELL_OLEPROBE") == "1")
                            {
                                try
                                {
                                    int initHr = PDFQFZ.WebShell.Services.OleDragDrop.InitializeResult();
                                    var wfForm = new System.Windows.Forms.Form();
                                    wfForm.CreateControl();
                                    int hrWf = PDFQFZ.WebShell.Services.OleDragDrop.Register(wfForm.Handle, _oleTarget);
                                    IntPtr mainHwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                                    int hrMain = PDFQFZ.WebShell.Services.OleDragDrop.Register(mainHwnd, _oleTarget);
                                    int treeOk = PDFQFZ.WebShell.Services.OleDragDrop.RegisterTree(webView.Handle, _oleTarget);
                                    System.IO.File.AppendAllText(dumpPath, "\noleprobe:init=" + initHr
                                        + " hrWf=" + hrWf + " hrMain=" + hrMain + " treeOk=" + treeOk
                                        + " mainHwnd=" + mainHwnd + " wvHwnd=" + webView.Handle);
                                    wfForm.Dispose();
                                }
                                catch (Exception oleEx) { System.IO.File.AppendAllText(dumpPath, "\noleprobe:ERR " + oleEx.Message); }
                            }
                            await System.Threading.Tasks.Task.Delay(800);
                            string j2 = await webView.CoreWebView2.ExecuteScriptAsync(
                                "String(typeof window.Bridge)+'|'+String(!!window.chrome)+'|'+String(!!(window.chrome&&window.chrome.webview))+'|'+String(!!(window.chrome&&window.chrome.webview&&window.chrome.webview.hostObjects&&window.chrome.webview.hostObjects.CSharpBridge))");
                            System.IO.File.AppendAllText(dumpPath, "\nstep2:" + j2);
                            await System.Threading.Tasks.Task.Delay(800);
                            string j3a = await webView.CoreWebView2.ExecuteScriptAsync(
                                "String(typeof window.chrome.webview.hostObjects.CSharpBridge.Ping)");
                            System.IO.File.AppendAllText(dumpPath, "\nstep3a:" + j3a);
                            await System.Threading.Tasks.Task.Delay(500);
                            // 对照1：纯 JS async IIFE（验证 ExecuteScriptAsync 对 async 的支持）
                            string c1 = await webView.CoreWebView2.ExecuteScriptAsync("(async()=>{return 'plain-async-ok'})()");
                            System.IO.File.AppendAllText(dumpPath, "\nctrl1:" + c1);
                            await System.Threading.Tasks.Task.Delay(500);
                            // 对照2：同步调用 COM 方法，看返回形态（Promise 还是直值）
                            string c2 = await webView.CoreWebView2.ExecuteScriptAsync(
                                "(function(){try{var r=window.chrome.webview.hostObjects.CSharpBridge.Ping();return 'sync-ret typeof='+typeof r+' isPromise='+String(!!(r&&r.then))}catch(e){return 'sync-err:'+e.message}})()");
                            System.IO.File.AppendAllText(dumpPath, "\nctrl2:" + c2);
                            await System.Threading.Tasks.Task.Delay(500);
                            // 对照3：拿到 Promise 后立即 then 同步转字符串（不走顶层 await）
                            string c3 = await webView.CoreWebView2.ExecuteScriptAsync(
                                "(function(){try{var p=window.chrome.webview.hostObjects.CSharpBridge.Ping();p.then(function(v){window.__pingResult=v});return 'then-attached'}catch(e){return 'then-err:'+e.message}})()");
                            System.IO.File.AppendAllText(dumpPath, "\nctrl3:" + c3);
                            await System.Threading.Tasks.Task.Delay(1200);
                            string c4 = await webView.CoreWebView2.ExecuteScriptAsync("String(window.__pingResult||'-')");
                            System.IO.File.AppendAllText(dumpPath, "\nctrl4:" + c4);
                            await System.Threading.Tasks.Task.Delay(500);
                            // Bridge.invoke 通路验证（then-attach 模式，兼容 ExecuteScriptAsync 不支持顶层 await）
                            string b1 = await webView.CoreWebView2.ExecuteScriptAsync(
                                "(function(){try{var p=window.Bridge.invoke('Ping');if(p&&p.then){p.then(function(v){window.__b1=v},function(e){window.__b1='rej:'+e.message})}else{window.__b1='nonpromise'}return 'attached'}catch(e){return 'b1err:'+e.message}})()");
                            System.IO.File.AppendAllText(dumpPath, "\nbridge1:" + b1);
                            await System.Threading.Tasks.Task.Delay(1200);
                            string b2 = await webView.CoreWebView2.ExecuteScriptAsync("String(window.__b1||'-')");
                            System.IO.File.AppendAllText(dumpPath, "\nbridge2:" + b2);
                            // L1 冒烟（V2.4.0.32+ 验证流程）：PDFQFZ_SHELL_LIGHT=1 时只验证 启动+桥+关键DOM，跑完即关闭退出
                            // （替代每次全量：纯 UI/布局改动只跑本段；涉及功能的改动再跑全量快速版 SKIPS9=1）
                            if (Environment.GetEnvironmentVariable("PDFQFZ_SHELL_LIGHT") == "1")
                            {
                                try
                                {
                                    string sm1 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;if(!vm)return JSON.stringify({vm:false});var q=function(s){return !!document.querySelector(s)};return JSON.stringify({vm:true,titlebar:q('.titlebar'),main:q('.main'),left:q('.left'),leftScroll:q('.left-scroll'),bottomArea:q('.bottom-area'),preview:q('.preview'),genBtn:q('.btn-generate'),logPanel:q('.log-panel'),ver:((document.title.match(/v[\\d.]+/)||[''])[0])||''});})()");
                                    System.IO.File.AppendAllText(dumpPath, "\nsmoke:" + sm1);
                                    // s27（V2.4.0.37）：el-select 高度与 el-input 统一 30px（EP select 硬编码 min-height:32px 不吃 --el-component-size:30px，
                                    // 全局 CSS 覆盖 .el-select__wrapper 为 min-height:30px+padding 3px 12px；断言实际渲染高度与输入框相等）
                                    string s27 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var s=document.querySelector('.el-select__wrapper');var i=document.querySelector('.el-input__wrapper');if(!s||!i)return JSON.stringify({err:'missing',sel:!!s,inp:!!i});var sc=getComputedStyle(s),ic=getComputedStyle(i);return JSON.stringify({selH:s.offsetHeight,selMin:sc.minHeight,selPadT:sc.paddingTop,selPadB:sc.paddingBottom,inpH:i.offsetHeight,inpMin:ic.minHeight,equal:s.offsetHeight===i.offsetHeight});})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns27:" + s27);
                                }
                                catch (Exception smokeEx) { System.IO.File.AppendAllText(dumpPath, "\nsmokeERR:" + smokeEx.Message); }
                                System.IO.File.AppendAllText(dumpPath, "\nDONE");
                                System.Windows.Application.Current.Dispatcher.Invoke(
                                    () => System.Windows.Application.Current.MainWindow.Close());
                                return;
                            }
                            // 阶段3：读取前端实际渲染的控件值（DOM 方式，兼容 Vue prod 构建）
                            string s5c = await webView.CoreWebView2.ExecuteScriptAsync(
                                "(function(){try{var nums=document.querySelectorAll('.el-input-number input');var segs=document.querySelectorAll('.seg-item.active');var ins=document.querySelectorAll('input');var out={nums:Array.prototype.map.call(nums,function(i){return i.value}).join(','),segs:Array.prototype.map.call(segs,function(s){return s.textContent.trim()}).join(','),inputs:Array.prototype.map.call(ins,function(i){return i.value}).filter(function(v){return v!==''}).join('|')};return JSON.stringify(out)}catch(e){return 'err:'+e.message}})()");
                            System.IO.File.AppendAllText(dumpPath, "\nstage3:" + s5c);
                            await System.Threading.Tasks.Task.Delay(600);
                            // 直接看桥返回的配置 JSON（then-attach）
                            string s5d = await webView.CoreWebView2.ExecuteScriptAsync(
                                "(function(){try{var p=window.Bridge.invoke('GetUiConfig');p.then(function(v){window.__cfg=v});var q=window.Bridge.invoke('GetStampParams','公章');q.then(function(v){window.__sp=v});return 'attached'}catch(e){return 'err:'+e.message}})()");
                            System.IO.File.AppendAllText(dumpPath, "\nstage4:" + s5d);
                            await System.Threading.Tasks.Task.Delay(1200);
                            string s5e = await webView.CoreWebView2.ExecuteScriptAsync("String((window.__cfg||'').substring(0,400))+'|||'+(window.__sp||'').substring(0,500)");
                            System.IO.File.AppendAllText(dumpPath, "\nstage5:" + s5e);
                            await System.Threading.Tasks.Task.Delay(500);
                            // s11a 调试页专项（V2.4.0.3，此时未加载用户文件）：默认调试页显示 + 可手动盖章 + 范围/生成拦截
                            string s11aRet = await webView.CoreWebView2.ExecuteScriptAsync(
                                "(function(){window.__s11a=null;var out={};try{var vm=window.__vm;" +
                                "out.hasVm=!!vm; out.dbg=vm?{debugActive:vm.debugActive,pdfLoaded:vm.pdfLoaded,pageCount:vm.pageCount,curPage:vm.curPage,curFile:vm.curFile,zoomText:vm.zoomText,imgW:vm.imgW}:null;" +
                                "window.Bridge.invoke('GetPageStamps',1).then(function(g0){" +
                                "var before=(typeof g0==='string'?JSON.parse(g0).length:g0.length);" +
                                "window.Bridge.invoke('AddManualStamp',0.5,0.5,1).then(function(j){" +
                                "window.Bridge.invoke('GetPageStamps',1).then(function(g1){" +
                                "out.dbgStamp={before:before,after:(typeof g1==='string'?JSON.parse(g1).length:g1.length)};" +
                                "vm.generateFiles(); out.genHint=vm.opHint;" +
                                "vm.openRangeDlg(); out.rangeHint=vm.opHint;" +
                                "window.__s11a=JSON.stringify(out);" +
                                "},function(e){out.dbgStamp='g1-err:'+e;window.__s11a=JSON.stringify(out);});" +
                                "},function(e){out.dbgStamp='add-err:'+e;window.__s11a=JSON.stringify(out);});" +
                                "},function(e){out.dbgStamp='g0-err:'+e;window.__s11a=JSON.stringify(out);});" +
                                "}catch(e){out.syncErr=String(e);window.__s11a=JSON.stringify(out);}return 'ok';})()");
                            System.IO.File.AppendAllText(dumpPath, "\ns11aRet:" + s11aRet);
                            string s11a = "";
                            for (int gi = 0; gi < 12; gi++)
                            {
                                await System.Threading.Tasks.Task.Delay(500);
                                string rr = await webView.CoreWebView2.ExecuteScriptAsync("(function(){return window.__s11a?window.__s11a:''})()");
                                if (!string.IsNullOrEmpty(rr)) { s11a = rr; break; }
                            }
                            System.IO.File.AppendAllText(dumpPath, "\ns11a:" + s11a);
                            // s13a V2.4.0.6（调试页状态）：① 文案无"调试页"三字；② 双页切换被拦截；③ 输入框居中/路径左对齐
                            string s13a = await webView.CoreWebView2.ExecuteScriptAsync(
                                "(function(){var vm=window.__vm;var out={};try{" +
                                "out.curFile=vm.curFile; out.debugActive=vm.debugActive;" +
                                "vm.setView('double'); out.modeAfterDouble=vm.viewMode; out.hintAfterDouble=vm.opHint;" +
                                "var zi=document.querySelector('.zoom-input .el-input__inner'); var ci=document.querySelector('.fake-file-row .grow .el-input__inner');" +
                                "out.zoomAlign=zi?getComputedStyle(zi).textAlign:'no-zoom'; out.fileAlign=ci?getComputedStyle(ci).textAlign:'no-file';" +
                                "window.__s13a=JSON.stringify(out);" +
                                "}catch(e){out.err=String(e);window.__s13a=JSON.stringify(out);}return 'ok';})()");
                            System.IO.File.AppendAllText(dumpPath, "\ns13aRet:" + s13a);
                            string s13aV = "";
                            for (int gi = 0; gi < 10; gi++)
                            {
                                await System.Threading.Tasks.Task.Delay(400);
                                string rr = await webView.CoreWebView2.ExecuteScriptAsync("(function(){return window.__s13a?window.__s13a:''})()");
                                if (!string.IsNullOrEmpty(rr)) { s13aV = rr; break; }
                            }
                            System.IO.File.AppendAllText(dumpPath, "\ns13a:" + s13aV);
                            // 阶段4：PDF 渲染验证（打开测试 PDF → 渲染第1页 → 检查缓存）
                            if (Environment.GetEnvironmentVariable("PDFQFZ_SHELL_PDFTEST") == "1")
                            {
                                string testPdf = Environment.GetEnvironmentVariable("PDFQFZ_SHELL_PDFPATH");
                                if (string.IsNullOrWhiteSpace(testPdf))
                                    testPdf = @"D:\gg笔记本\CODEX项目汇总\tasks\GitHub项目\PDFQFZ\Git仓库\PDFQFZ\PDFQFZ.WPF\Assets\debug_page.pdf";
                                string testPathJs = testPdf.Replace("\\", "\\\\");
                                string r1 = await webView.CoreWebView2.ExecuteScriptAsync(
                                    "(function(){try{var p=window.Bridge.invoke('OpenPdf','" + testPathJs + "');p.then(function(v){window.__r1=v});return 'attached'}catch(e){return 'err:'+e.message}})()");
                                System.IO.File.AppendAllText(dumpPath, "\nrender1:" + r1);
                                await System.Threading.Tasks.Task.Delay(1500);
                                string r2 = await webView.CoreWebView2.ExecuteScriptAsync(
                                    "(function(){try{var p=window.Bridge.invoke('RenderPage',0);p.then(function(v){window.__r2=v});return 'attached'}catch(e){return 'err:'+e.message}})()");
                                System.IO.File.AppendAllText(dumpPath, "\nrender2:" + r2);
                                await System.Threading.Tasks.Task.Delay(2000);
                                string r3 = await webView.CoreWebView2.ExecuteScriptAsync("String(window.__r1)+'|||'+String(window.__r2)");
                                System.IO.File.AppendAllText(dumpPath, "\nrender3:" + r3);
                                // 越界页应报错
                                string r4 = await webView.CoreWebView2.ExecuteScriptAsync(
                                    "(function(){try{var p=window.Bridge.invoke('RenderPage',99);p.then(function(v){window.__r4=v});return 'attached'}catch(e){return 'err:'+e.message}})()");
                                await System.Threading.Tasks.Task.Delay(1500);
                                string r5 = await webView.CoreWebView2.ExecuteScriptAsync("String(window.__r4)");
                                System.IO.File.AppendAllText(dumpPath, "\nrender4:" + r5);
                                string r6 = await webView.CoreWebView2.ExecuteScriptAsync("String(window.Bridge.invoke('ClosePdf'))");
                                System.IO.File.AppendAllText(dumpPath, "\nrender5:" + r6);
                                // 前端 E2E：走 Vue 方法 openPdf → 检查 DOM 渲染
                                string e1 = await webView.CoreWebView2.ExecuteScriptAsync(
                                    "(function(){try{window.__vm.openPdf('" + testPathJs + "');return 'called'}catch(e){return 'err:'+e.message}})()");
                                System.IO.File.AppendAllText(dumpPath, "\ne2e1:" + e1);
                                await System.Threading.Tasks.Task.Delay(3500);
                                string e2a = await webView.CoreWebView2.ExecuteScriptAsync(
                                    "(function(){try{var img=document.querySelector('.page-img');var pr=document.querySelector('.pr-page');var src=img?img.getAttribute('src'):'-';var tw=pr?pr.textContent.trim():'-';var fi=document.querySelector('.fake-file-row input');return JSON.stringify({imgSrc:src,pageText:tw,curFile:fi?fi.value:'-'})}catch(e){return 'err:'+e.message}})()");
                                System.IO.File.AppendAllText(dumpPath, "\ne2e2:" + e2a);
                                // 翻页到第2页（验证多页翻页）
                                string e3a = await webView.CoreWebView2.ExecuteScriptAsync(
                                    "(function(){try{window.__vm.nextPage();return 'next-called'}catch(e){return 'err:'+e.message}})()");
                                System.IO.File.AppendAllText(dumpPath, "\ne2e3:" + e3a);
                                await System.Threading.Tasks.Task.Delay(2000);
                                string e4a = await webView.CoreWebView2.ExecuteScriptAsync(
                                    "(function(){try{var img=document.querySelector('.page-img');var pr=document.querySelector('.pr-page');return JSON.stringify({imgSrc:img?img.getAttribute('src'):'-',pageText:pr?pr.textContent.trim():'-'})}catch(e){return 'err:'+e.message}})()");
                                System.IO.File.AppendAllText(dumpPath, "\ne2e4:" + e4a);
                                // 再翻到第3页
                                string e5 = await webView.CoreWebView2.ExecuteScriptAsync(
                                    "(function(){try{window.__vm.nextPage();return 'next2'}catch(e){return 'err:'+e.message}})()");
                                await System.Threading.Tasks.Task.Delay(2000);
                                string e6 = await webView.CoreWebView2.ExecuteScriptAsync(
                                    "(function(){try{var img=document.querySelector('.page-img');var pr=document.querySelector('.pr-page');var pi=document.querySelector('.pr-page-input input');return JSON.stringify({imgSrc:img?img.getAttribute('src'):'-',pageText:pr?pr.textContent.trim():'-',pageInput:pi?pi.value:'-'})}catch(e){return 'err:'+e.message}})()");
                                System.IO.File.AppendAllText(dumpPath, "\ne2e5:" + e6);
                                // 页码输入跳转：直接跳第2页
                                string e7 = await webView.CoreWebView2.ExecuteScriptAsync(
                                    "(function(){try{window.__vm.curPageInput='2';window.__vm.goPage();return 'go-called'}catch(e){return 'err:'+e.message}})()");
                                System.IO.File.AppendAllText(dumpPath, "\ne2e6:" + e7);
                                await System.Threading.Tasks.Task.Delay(2000);
                                string e8 = await webView.CoreWebView2.ExecuteScriptAsync(
                                    "(function(){try{var img=document.querySelector('.page-img');var pi=document.querySelector('.pr-page-input input');return JSON.stringify({imgSrc:img?img.getAttribute('src'):'-',pageInput:pi?pi.value:'-'})}catch(e){return 'err:'+e.message}})()");
                                System.IO.File.AppendAllText(dumpPath, "\ne2e7:" + e8);
                                // 双页视图验证：切 double → 双页渲染；nextPage 步进2
                                string d1 = await webView.CoreWebView2.ExecuteScriptAsync(
                                    "(function(){try{window.__vm.viewMode='double';return 'switched'}catch(e){return 'err:'+e.message}})()");
                                System.IO.File.AppendAllText(dumpPath, "\ndbl1:" + d1);
                                await System.Threading.Tasks.Task.Delay(3000);
                                string d2 = await webView.CoreWebView2.ExecuteScriptAsync(
                                    "(function(){try{var imgs=document.querySelectorAll('.page-pair .page-img');var pi=document.querySelector('.pr-page-input input');return JSON.stringify({pairCount:imgs.length,srcs:Array.prototype.map.call(imgs,function(i){return i.getAttribute('src')}),page:pi?pi.value:'-'})}catch(e){return 'err:'+e.message}})()");
                                System.IO.File.AppendAllText(dumpPath, "\ndbl2:" + d2);
                                string d3 = await webView.CoreWebView2.ExecuteScriptAsync(
                                    "(function(){try{window.__vm.nextPage();return 'next'}catch(e){return 'err:'+e.message}})()");
                                System.IO.File.AppendAllText(dumpPath, "\ndbl3:" + d3);
                                await System.Threading.Tasks.Task.Delay(2500);
                                string d4 = await webView.CoreWebView2.ExecuteScriptAsync(
                                    "(function(){try{var imgs=document.querySelectorAll('.page-pair .page-img');var pi=document.querySelector('.pr-page-input input');return JSON.stringify({pairCount:imgs.length,srcs:Array.prototype.map.call(imgs,function(i){return i.getAttribute('src')}),page:pi?pi.value:'-'})}catch(e){return 'err:'+e.message}})()");
                                System.IO.File.AppendAllText(dumpPath, "\ndbl4:" + d4);
                                // 拖放通道验证：C# 直调 OpenPdfFromBytes（base64 → 临时文件 → OpenPdf）
                                try
                                {
                                    string b64 = Convert.ToBase64String(System.IO.File.ReadAllBytes(testPdf));
                                    string fb = _bridge.OpenPdfFromBytes(b64, "s8_text.pdf");
                                    System.IO.File.AppendAllText(dumpPath, "\ndrop3:" + fb);
                                    string fb2 = _bridge.OpenPdfFromBytes("", "empty.pdf");
                                    System.IO.File.AppendAllText(dumpPath, "\ndrop4:" + fb2);
                                }
                                catch (Exception ex) { System.IO.File.AppendAllText(dumpPath, "\ndropErr:" + ex.Message); }
                            }
                            // V2.4.0.44 专项：浮动面板三回归验证（首帧位置 / overlay 事件穿透 / cache-bust 已盖章重绘）
                            if (Environment.GetEnvironmentVariable("PDFQFZ_SHELL_V44") == "1")
                            {
                                string v44Pdf = Environment.GetEnvironmentVariable("PDFQFZ_SHELL_PDF8");
                                if (string.IsNullOrWhiteSpace(v44Pdf) || !File.Exists(v44Pdf))
                                    v44Pdf = System.IO.Directory.GetFiles(
                                        System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop), "*.pdf")
                                        .FirstOrDefault(f => !Path.GetFileName(f).Contains("已盖章")) ?? "";
                                string v44PathJs = v44Pdf.Replace("\\", "\\\\");
                                // v44-1 首帧位置：开 render 开关+面板，轮询捕获元素首次出现位置（CSS 变量应已预置 → 首帧即目标位）
                                string v1 = await webView.CoreWebView2.ExecuteScriptAsync(
                                    "(function(){try{window.__v44f=null;var t0=performance.now();var iv=setInterval(function(){var hd=document.querySelector('.float-hd[data-key=\"render\"]');if(hd&&!window.__v44f){var dlg=hd.closest('.el-dialog');window.__v44f={left:getComputedStyle(dlg).left,top:getComputedStyle(dlg).top,ms:Math.round(performance.now()-t0)};clearInterval(iv)}},5);var vm=window.__vm;vm.renderEnabled=true;setTimeout(function(){var b=document.querySelectorAll('.mod-entry')[1];if(b)b.click()},400);return 'armed'}catch(e){return 'err:'+e.message}})()");
                                System.IO.File.AppendAllText(dumpPath, "\nv44a:" + v1);
                                await System.Threading.Tasks.Task.Delay(2500);
                                string v2 = await webView.CoreWebView2.ExecuteScriptAsync(
                                    "(function(){try{var hd=document.querySelector('.float-hd[data-key=\"render\"]');var dlg=hd?hd.closest('.el-dialog'):null;var ov=dlg?dlg.parentElement&&dlg.parentElement.parentElement:null;return JSON.stringify({first:window.__v44f,ovPE:ov?getComputedStyle(ov).pointerEvents:'no',dlgPE:dlg?getComputedStyle(dlg).pointerEvents:'no'})}catch(e){return 'err:'+e.message}})()");
                                System.IO.File.AppendAllText(dumpPath, "\nv44b:" + v2);
                                // v44-2 面板打开时盖章可用：OpenPdf 加载（面板保持打开）→ 预览区触发盖章 → 章应出现
                                string v3 = await webView.CoreWebView2.ExecuteScriptAsync(
                                    "(function(){try{window.__vm.openPdf('" + v44PathJs + "');return 'opened'}catch(e){return 'err:'+e.message}})()");
                                System.IO.File.AppendAllText(dumpPath, "\nv44c:" + v3);
                                await System.Threading.Tasks.Task.Delay(3500);
                                string v4 = await webView.CoreWebView2.ExecuteScriptAsync(
                                    "(function(){try{var w=document.querySelector('.page-wrap');if(!w)return 'no-wrap';var r=w.getBoundingClientRect();var x=r.left+r.width*0.4,y=r.top+r.height*0.4;var el=w;function fire(t,px,py){var ev=new MouseEvent(t,{bubbles:true,cancelable:true,clientX:px,clientY:py,button:0});el.dispatchEvent(ev)}fire('mousedown',x,y);fire('mouseup',x,y);fire('click',x,y);return JSON.stringify({x:x,y:y})}catch(e){return 'err:'+e.message}})()");
                                System.IO.File.AppendAllText(dumpPath, "\nv44d:" + v4);
                                await System.Threading.Tasks.Task.Delay(2500);
                                string v5 = await webView.CoreWebView2.ExecuteScriptAsync(
                                    "(function(){try{var pnl=document.querySelector('.float-hd[data-key=\"render\"]')?true:false;var ovs=document.querySelectorAll('.stamp-ov').length;var hd=document.querySelector('.float-hd[data-key=\"render\"]');var dlg=hd?hd.closest('.el-dialog'):null;var ov=dlg?dlg.parentElement&&dlg.parentElement.parentElement:null;return JSON.stringify({panelOpen:pnl,stampOv:ovs,ovPE:ov?getComputedStyle(ov).pointerEvents:'no'})}catch(e){return 'err:'+e.message}})()");
                                System.IO.File.AppendAllText(dumpPath, "\nv44e:" + v5);
                                // v44-3 cache-bust 已盖章重绘：读前端章 src → 调滑块(tex.brightness=90，触发 watcher→syncTex→refreshPageStamps) → 再读 src（应带 ?t= 且变化）
                                string v6 = await webView.CoreWebView2.ExecuteScriptAsync(
                                    "(function(){try{var imgs=document.querySelectorAll('.stamp-ov');return JSON.stringify(Array.prototype.map.call(imgs,function(i){return i.getAttribute('src')}))}catch(e){return 'err:'+e.message}})()");
                                System.IO.File.AppendAllText(dumpPath, "\nv44f:" + v6);
                                string v7 = await webView.CoreWebView2.ExecuteScriptAsync(
                                    "(function(){try{var vm=window.__vm;vm.tex.brightness=90;return 'set90'}catch(e){return 'err:'+e.message}})()");
                                System.IO.File.AppendAllText(dumpPath, "\nv44g:" + v7);
                                await System.Threading.Tasks.Task.Delay(2500);
                                string v8 = await webView.CoreWebView2.ExecuteScriptAsync(
                                    "(function(){try{var imgs=document.querySelectorAll('.stamp-ov');return JSON.stringify(Array.prototype.map.call(imgs,function(i){return i.getAttribute('src')}))}catch(e){return 'err:'+e.message}})()");
                                System.IO.File.AppendAllText(dumpPath, "\nv44h:" + v8);
                                // v44-4 关面板后盖新章 → 新章 url（新参数渲染）
                                string v9 = await webView.CoreWebView2.ExecuteScriptAsync(
                                    "(function(){try{var hd=document.querySelector('.float-hd[data-key=\"render\"]');var c=hd?hd.parentElement.querySelector('.float-close'):null;if(c)c.click();return 'closed'}catch(e){return 'err:'+e.message}})()");
                                System.IO.File.AppendAllText(dumpPath, "\nv44i:" + v9);
                                await System.Threading.Tasks.Task.Delay(1200);
                                string v10 = await webView.CoreWebView2.ExecuteScriptAsync(
                                    "(function(){try{var w=document.querySelector('.page-wrap');var r=w.getBoundingClientRect();var x=r.left+r.width*0.7,y=r.top+r.height*0.7;var el=w;function fire(t,px,py){var ev=new MouseEvent(t,{bubbles:true,cancelable:true,clientX:px,clientY:py,button:0});el.dispatchEvent(ev)}fire('mousedown',x,y);fire('mouseup',x,y);fire('click',x,y);return 'fired2'}catch(e){return 'err:'+e.message}})()");
                                System.IO.File.AppendAllText(dumpPath, "\nv44j:" + v10);
                                await System.Threading.Tasks.Task.Delay(2500);
                                string v11 = await webView.CoreWebView2.ExecuteScriptAsync(
                                    "(function(){try{var imgs=document.querySelectorAll('.stamp-ov');return JSON.stringify(Array.prototype.map.call(imgs,function(i){return i.getAttribute('src')}))}catch(e){return 'err:'+e.message}})()");
                                System.IO.File.AppendAllText(dumpPath, "\nv44k:" + v11);
                                System.IO.File.AppendAllText(dumpPath, "\nv44done:");
                            }
                            await System.Threading.Tasks.Task.Delay(500);
                            // 阶段3回写验证：修改配置（仅当环境变量 PDFQFZ_SHELL_WRITEBACK=1）
                            if (Environment.GetEnvironmentVariable("PDFQFZ_SHELL_WRITEBACK") == "1")
                            {
                                string w1 = await webView.CoreWebView2.ExecuteScriptAsync(
                                    "(function(){try{var a=window.Bridge.invoke('SetUiConfig',JSON.stringify({outputQualityDpi:300,outputNameMark:'已盖章V2'}));a.then(function(v){window.__w1=v});var b=window.Bridge.invoke('SetStampParams','公章',JSON.stringify({opacity:75,size:55}));b.then(function(v){window.__w2=v});return 'attached'}catch(e){return 'err:'+e.message}})()");
                                System.IO.File.AppendAllText(dumpPath, "\nwriteback1:" + w1);
                                await System.Threading.Tasks.Task.Delay(1200);
                                string w2 = await webView.CoreWebView2.ExecuteScriptAsync("String(window.__w1)+'|'+String(window.__w2)");
                                System.IO.File.AppendAllText(dumpPath, "\nwriteback2:" + w2);
                            }
                            await System.Threading.Tasks.Task.Delay(500);
                            string j4 = await webView.CoreWebView2.ExecuteScriptAsync(
                                "String(document.documentElement.getAttribute('data-shell')||'-')+'|'+String(document.documentElement.getAttribute('data-shell-version')||'-')");
                            System.IO.File.AppendAllText(dumpPath, "\nstep4:" + j4);
                            // 阶段5：印章库 + 纹理方案验证（环境变量 PDFQFZ_SHELL_STAMP5=1 启用）
                            if (Environment.GetEnvironmentVariable("PDFQFZ_SHELL_STAMP5") == "1")
                            {
                                try
                                {
                                    string libDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "印章库");
                                    string srcStamp = @"D:\gg笔记本\CODEX项目汇总\tasks\GitHub项目\PDFQFZ\交付版本\印章库\公章.png";
                                    string dstStamp = System.IO.Path.Combine(libDir, "公章.png");
                                    if (System.IO.File.Exists(srcStamp) && !System.IO.File.Exists(dstStamp))
                                    {
                                        System.IO.Directory.CreateDirectory(libDir);
                                        System.IO.File.Copy(srcStamp, dstStamp);
                                    }
                                    var entries = AppConfig.LoadStampEntries();
                                    bool hasGongzhang = entries.Any(en => string.Equals(en.DisplayName, "公章", StringComparison.OrdinalIgnoreCase));
                                    if (!hasGongzhang && System.IO.File.Exists(dstStamp))
                                    {
                                        AppConfig.AppendStampEntry("公章", dstStamp);
                                    }
                                    System.IO.File.AppendAllText(dumpPath, "\ns5list0:" + _bridge.GetStampList());
                                    // JS：GetStampList + 前端列表状态
                                    string s5a = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){try{var p=window.Bridge.invoke('GetStampList');p.then(function(v){window.__s5l=v});return 'attached'}catch(e){return 'err:'+e.message}})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns5a1:" + s5a);
                                    await System.Threading.Tasks.Task.Delay(1500);
                                    string s5b = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){try{var b=window.Bridge?String(typeof window.Bridge.invoke):'-';var r=window.__vm?window.__vm.loadStamps():null;return JSON.stringify({bridge:b,manual:r?String(r):'-',curSeal:window.__vm?window.__vm.curSeal:'-',sealCount:window.__vm?(window.__vm.seals||[]).length:'-',seals:(window.__vm&&window.__vm.seals)?window.__vm.seals.join(','):'-',preset:window.__vm?window.__vm.preset:'-',presetExists:window.__vm?JSON.stringify(window.__vm.presetExists):'-'})}catch(e){return 'err:'+e.message}})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns5a2:" + s5b);
                                    await System.Threading.Tasks.Task.Delay(2000);
                                    string s5b2 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){try{return JSON.stringify({curSeal:window.__vm?window.__vm.curSeal:'-',sealCount:window.__vm?(window.__vm.seals||[]).length:'-',seals:(window.__vm&&window.__vm.seals)?window.__vm.seals.join(','):'-'})}catch(e){return 'err:'+e.message}})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns5a3:" + s5b2);
                                    // 纹理方案：SetTexPreset(1) → GetTexPreset(1)/GetTexPreset(2)
                                    string st3 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){try{var a=window.Bridge.invoke('SetTexPreset',1,JSON.stringify({values:[10,20,30,40,50,60,70]}));a.then(function(v){window.__s5t1=v});var b=window.Bridge.invoke('GetTexPreset',1);b.then(function(v){window.__s5t2=v});var c=window.Bridge.invoke('GetTexPreset',2);c.then(function(v){window.__s5t3=v});return 'attached'}catch(e){return 'err:'+e.message}})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns5b1:" + st3);
                                    await System.Threading.Tasks.Task.Delay(1800);
                                    string st4 = await webView.CoreWebView2.ExecuteScriptAsync("String(window.__s5t1)+'|'+String(window.__s5t2)+'|'+String(window.__s5t3)");
                                    System.IO.File.AppendAllText(dumpPath, "\ns5b2:" + st4);
                                    // 重命名：公章→测试章（应 ok）；再改不存在名（应 err）
                                    string st5 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){try{var a=window.Bridge.invoke('RenameStamp','公章','测试章');a.then(function(v){window.__s5r1=v});var b=window.Bridge.invoke('RenameStamp','公章','测试章2');b.then(function(v){window.__s5r2=v});return 'attached'}catch(e){return 'err:'+e.message}})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns5c1:" + st5);
                                    await System.Threading.Tasks.Task.Delay(1800);
                                    string s5f = await webView.CoreWebView2.ExecuteScriptAsync("String(window.__s5r1)+'|'+String(window.__s5r2)+'|||list='+String(window.__s5l)");
                                    System.IO.File.AppendAllText(dumpPath, "\ns5c2:" + s5f);
                                    // 删除：测试章（应 ok）+ 不存在章（容错 ok）+ 列表
                                    string s5g = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){try{var a=window.Bridge.invoke('DeleteStamp','测试章');a.then(function(v){window.__s5d1=v});var b=window.Bridge.invoke('DeleteStamp','不存在章');b.then(function(v){window.__s5d2=v});var c=window.Bridge.invoke('GetStampList');c.then(function(v){window.__s5d3=v});return 'attached'}catch(e){return 'err:'+e.message}})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns5d1:" + s5g);
                                    await System.Threading.Tasks.Task.Delay(1800);
                                    string s5h = await webView.CoreWebView2.ExecuteScriptAsync("String(window.__s5d1)+'|'+String(window.__s5d2)+'|||'+String(window.__s5d3)");
                                    System.IO.File.AppendAllText(dumpPath, "\ns5d2:" + s5h);
                                    // 恢复公章条目（保持后续阶段可用）
                                    if (!AppConfig.LoadStampEntries().Any(en => string.Equals(en.DisplayName, "公章", StringComparison.OrdinalIgnoreCase))
                                        && System.IO.File.Exists(dstStamp))
                                    {
                                        AppConfig.AppendStampEntry("公章", dstStamp);
                                    }
                                    System.IO.File.AppendAllText(dumpPath, "\ns5final:" + _bridge.GetStampList());
                                }
                                catch (Exception ex) { System.IO.File.AppendAllText(dumpPath, "\ns5ERR:" + ex.Message); }
                            }
                            // 阶段6：手动盖章 + 叠加层验证（环境变量 PDFQFZ_SHELL_STAMP6=1 启用）
                            if (Environment.GetEnvironmentVariable("PDFQFZ_SHELL_STAMP6") == "1")
                            {
                                try
                                {
                                    string pdfTest = Environment.GetEnvironmentVariable("PDFQFZ_SHELL_PDFPATH");
                                    if (string.IsNullOrWhiteSpace(pdfTest))
                                        pdfTest = @"D:\gg笔记本\CODEX项目汇总\tasks\GitHub项目\PDFQFZ\Git仓库\PDFQFZ\PDFQFZ.WPF\Assets\debug_page.pdf";
                                    System.IO.File.AppendAllText(dumpPath, "\ns6open:" + _bridge.OpenPdf(pdfTest));
                                    System.IO.File.AppendAllText(dumpPath, "\ns6sel:" + _bridge.SelectStamp("公章"));
                                    // 6a 放置：页1 两枚 + 页2 一枚（中心比例，含随机位移/旋转）
                                    string a1 = _bridge.AddManualStamp(0.5f, 0.5f, 1);
                                    string a2 = _bridge.AddManualStamp(0.3f, 0.4f, 1);
                                    string a3 = _bridge.AddManualStamp(0.8f, 0.6f, 2);
                                    System.IO.File.AppendAllText(dumpPath, "\ns6a1:" + a1);
                                    System.IO.File.AppendAllText(dumpPath, "\ns6a2:" + a2);
                                    System.IO.File.AppendAllText(dumpPath, "\ns6a3:" + a3);
                                    // 6b 列表：页1 应 2 枚、页2 应 1 枚、页3 应 []
                                    System.IO.File.AppendAllText(dumpPath, "\ns6b1:" + _bridge.GetPageStamps(1));
                                    System.IO.File.AppendAllText(dumpPath, "\ns6b2:" + _bridge.GetPageStamps(2));
                                    System.IO.File.AppendAllText(dumpPath, "\ns6b3:" + _bridge.GetPageStamps(3));
                                    // 6c 纹理参数同步（种子不变）：列表应仍 2 枚且参数上限更新
                                    System.IO.File.AppendAllText(dumpPath, "\ns6c1:" + _bridge.SyncTextureParams(
                                        "{\"textureBrightness\":55,\"textureBlob\":44,\"textureGradient\":33,\"textureWhite\":22,\"textureSpot\":11,\"textureRadial\":66,\"textureCast\":77}"));
                                    System.IO.File.AppendAllText(dumpPath, "\ns6c2:" + _bridge.GetPageStamps(1));
                                    // 6d 删除：删第 1 枚 → 页1 应 1 枚；删不存在 id → err
                                    int id1 = 0;
                                    var m1 = System.Text.RegularExpressions.Regex.Match(a1, "\"id\":(\\d+)");
                                    if (m1.Success) int.TryParse(m1.Groups[1].Value, out id1);
                                    System.IO.File.AppendAllText(dumpPath, "\ns6d1:" + _bridge.DeleteStampById(id1));
                                    System.IO.File.AppendAllText(dumpPath, "\ns6d2:" + _bridge.DeleteStampById(99999));
                                    System.IO.File.AppendAllText(dumpPath, "\ns6d3:" + _bridge.GetPageStamps(1));
                                    // 6e keepStampData：同文档重开 → 印章保留
                                    System.IO.File.AppendAllText(dumpPath, "\ns6e1:" + _bridge.OpenPdf(pdfTest));
                                    System.IO.File.AppendAllText(dumpPath, "\ns6e2:" + _bridge.GetPageStamps(1));
                                    // 6f 换文档 → 清空；恢复打开原文档
                                    string other = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "s6_other.pdf");
                                    try { System.IO.File.Copy(pdfTest, other, true); } catch { }
                                    System.IO.File.AppendAllText(dumpPath, "\ns6f1:" + _bridge.OpenPdf(other));
                                    System.IO.File.AppendAllText(dumpPath, "\ns6f2:" + _bridge.GetPageStamps(1));
                                    System.IO.File.AppendAllText(dumpPath, "\ns6f3:" + _bridge.OpenPdf(pdfTest));
                                    // 6g 前端叠加 DOM 检查：page-wrap/page-overlay/stamp-ov 存在
                                    string s6g = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){try{return JSON.stringify({wrap:document.querySelectorAll('.page-wrap').length,ov:document.querySelectorAll('.page-overlay').length,img:document.querySelectorAll('.page-img').length,stampOv:document.querySelectorAll('.stamp-ov').length,hasOnClick:!!window.__vm&&typeof window.__vm.onPreviewClick==='function',hasRefresh:!!window.__vm&&typeof window.__vm.refreshPageStamps==='function'})}catch(e){return 'err:'+e.message}})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns6g:" + s6g);
                                    // 6h 前端完整链路：openPdf → 模拟点击盖章 → 叠加渲染（等页面 JS 就绪）
                                    await System.Threading.Tasks.Task.Delay(1500);
                                    string s6h = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){try{var vm=window.__vm;if(!vm){return 'no-vm'} vm.openPdf('" + pdfTest.Replace("\\", "\\\\") + "'); return 'opened'}catch(e){return 'err:'+e.message}})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns6h1:" + s6h);
                                    await System.Threading.Tasks.Task.Delay(3000);
                                    string s6h2 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){try{var w=document.querySelector('.page-wrap'); if(!w){return 'no-wrap'} var r=w.getBoundingClientRect(); var ev=new MouseEvent('click',{clientX:r.left+r.width*0.5,clientY:r.top+r.height*0.5,bubbles:true}); w.dispatchEvent(ev); return 'clicked:'+Math.round(r.width)+'x'+Math.round(r.height)}catch(e){return 'err:'+e.message}})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns6h2:" + s6h2);
                                    await System.Threading.Tasks.Task.Delay(3000);
                                    string s6h3 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){try{return JSON.stringify({stampOv:document.querySelectorAll('.stamp-ov').length,pageStamps:window.__vm?window.__vm.pageStamps.length:'-',wrap:document.querySelectorAll('.page-wrap').length,imgW:window.__vm?window.__vm.imgW:'-',opHint:window.__vm?window.__vm.opHint:'-',pdfLoaded:window.__vm?window.__vm.pdfLoaded:'-'})}catch(e){return 'err:'+e.message}})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns6h3:" + s6h3);
                                    // 6h4 右键删除模拟：contextmenu → stamp-ov 应减少
                                    string s6h4 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){try{var s=document.querySelector('.stamp-ov'); if(!s){return 'no-stamp'} var ev=new MouseEvent('contextmenu',{bubbles:true,cancelable:true}); s.dispatchEvent(ev); return 'fired'}catch(e){return 'err:'+e.message}})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns6h4:" + s6h4);
                                    await System.Threading.Tasks.Task.Delay(2000);
                                    string s6h5 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){try{return JSON.stringify({stampOv:document.querySelectorAll('.stamp-ov').length,pageStamps:window.__vm?window.__vm.pageStamps.length:'-'})}catch(e){return 'err:'+e.message}})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns6h5:" + s6h5);
                                    // 6i 范围模式：3 页 PDF → AddRangeStamps(1-3) → 每页 1 枚同批次 → 批次删除
                                    string pdf3 = Environment.GetEnvironmentVariable("PDFQFZ_SHELL_PDF3");
                                    if (string.IsNullOrWhiteSpace(pdf3)) pdf3 = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "s6_multi.pdf");
                                    if (System.IO.File.Exists(pdf3))
                                    {
                                        System.IO.File.AppendAllText(dumpPath, "\ns6i1:" + _bridge.OpenPdf(pdf3));
                                        System.IO.File.AppendAllText(dumpPath, "\ns6i2:" + _bridge.AddRangeStamps(0.5f, 0.5f, 1, 3));
                                        System.IO.File.AppendAllText(dumpPath, "\ns6i3:" + _bridge.GetPageStamps(1));
                                        System.IO.File.AppendAllText(dumpPath, "\ns6i4:" + _bridge.GetPageStamps(2));
                                        System.IO.File.AppendAllText(dumpPath, "\ns6i5:" + _bridge.GetPageStamps(3));
                                        // 越界校验：范围 1-5 应 err
                                        System.IO.File.AppendAllText(dumpPath, "\ns6i6:" + _bridge.AddRangeStamps(0.5f, 0.5f, 1, 5));
                                        // 批次删除：整批删除 → 各页 0
                                        int batchId = 0;
                                        var m3 = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(dumpPath), "\"batchId\":(\\d+)");
                                        // 直接用 s6i2 返回值解析
                                        string s6i2v = _bridge.GetPageStamps(1);
                                        var m4 = System.Text.RegularExpressions.Regex.Match(s6i2v, "\"batchId\":(\\d+)");
                                        if (m4.Success) int.TryParse(m4.Groups[1].Value, out batchId);
                                        System.IO.File.AppendAllText(dumpPath, "\ns6i7:" + _bridge.DeleteStampBatch(batchId));
                                        System.IO.File.AppendAllText(dumpPath, "\ns6i8:" + _bridge.GetPageStamps(1));
                                        System.IO.File.AppendAllText(dumpPath, "\ns6i9:" + _bridge.GetPageStamps(2));
                                        System.IO.File.AppendAllText(dumpPath, "\ns6i10:" + _bridge.GetPageStamps(3));
                                    }
                                    System.IO.File.AppendAllText(dumpPath, "\ns6final:" + _bridge.GetStampList());
                                }
                                catch (Exception ex) { System.IO.File.AppendAllText(dumpPath, "\ns6ERR:" + ex.Message); }
                            }
                            // 阶段7：按文字盖章（环境变量 PDFQFZ_SHELL_STAMP7=1 启用；PDF7=含文字 PDF 路径）
                            if (Environment.GetEnvironmentVariable("PDFQFZ_SHELL_STAMP7") == "1")
                            {
                                try
                                {
                                    string pdf7 = Environment.GetEnvironmentVariable("PDFQFZ_SHELL_PDF7");
                                    if (string.IsNullOrWhiteSpace(pdf7)) pdf7 = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "s7_text.pdf");
                                    if (System.IO.File.Exists(pdf7))
                                    {
                                        System.IO.File.AppendAllText(dumpPath, "\ns7open:" + _bridge.OpenPdf(pdf7));
                                        System.IO.File.AppendAllText(dumpPath, "\ns7sel:" + _bridge.SelectStamp("公章"));
                                        // 7a 基础搜索：找"盖章"（3 页各 1 处）
                                        System.IO.File.AppendAllText(dumpPath, "\ns7a:" + _bridge.AutoStamp("盖章", "", 10, true, false, false, 0, 0));
                                        System.IO.File.AppendAllText(dumpPath, "\ns7b:" + _bridge.GetPageStamps(1));
                                        System.IO.File.AppendAllText(dumpPath, "\ns7c:" + _bridge.GetPageStamps(3));
                                        // 7d 上下文过滤："投标"附近 range=30 含"公章"
                                        System.IO.File.AppendAllText(dumpPath, "\ns7d:" + _bridge.AutoStamp("投标", "公章", 30, true, false, false, 0, 0));
                                        // 7e 找不到：应返回"没找到"
                                        System.IO.File.AppendAllText(dumpPath, "\ns7e:" + _bridge.AutoStamp("完全不存在的词XYZ", "", 10, true, false, false, 0, 0));
                                        // 7f 中心偏移记忆：带偏移盖章 → GetCenterOffsetForKeyword 回读
                                        System.IO.File.AppendAllText(dumpPath, "\ns7f1:" + _bridge.AutoStamp("盖章", "", 10, true, false, true, 12, -8));
                                        System.IO.File.AppendAllText(dumpPath, "\ns7f2:" + _bridge.GetCenterOffsetForKeyword("盖章"));
                                        // 7g 撤销：撤销最近一次（7f 的"盖章"批次）
                                        System.IO.File.AppendAllText(dumpPath, "\ns7g:" + _bridge.UndoAutoStamp());
                                        System.IO.File.AppendAllText(dumpPath, "\ns7g2:" + _bridge.GetPageStamps(1));
                                        // 7h 历史：应含"盖章"/"投标"（V79：RemoveAutoStampKeyword 桥已删，删除测试点移除）
                                        System.IO.File.AppendAllText(dumpPath, "\ns7h1:" + _bridge.GetAutoStampHistory());
                                        System.IO.File.AppendAllText(dumpPath, "\ns7h3:" + _bridge.GetAutoStampHistory());
                                        // 7i 前端链路：placeTextStamps 方法存在 + 关键词联动方法存在
                                        string s7i = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){try{var vm=window.__vm;return JSON.stringify({place:!!vm&&typeof vm.placeTextStamps==='function',undo:!!vm&&typeof vm.undoTextStamp==='function',loadCO:!!vm&&typeof vm.loadCenterOffsetForSearch==='function',hist:vm?(vm.searchHistory||[]).length:'-'})}catch(e){return 'err:'+e.message}})()");
                                        System.IO.File.AppendAllText(dumpPath, "\ns7i:" + s7i);
                                    }
                                    System.IO.File.AppendAllText(dumpPath, "\ns7final:" + _bridge.GetStampList());
                                }
                                catch (Exception ex) { System.IO.File.AppendAllText(dumpPath, "\ns7ERR:" + ex.Message); }
                            }
                            // 阶段8：输出与命名（环境变量 PDFQFZ_SHELL_STAMP8=1 启用；PDF8=源 PDF、OUT8=输出目录）
                            if (Environment.GetEnvironmentVariable("PDFQFZ_SHELL_STAMP8") == "1")
                            {
                                string out8 = "";
                                try
                                {
                                    string pdf8 = Environment.GetEnvironmentVariable("PDFQFZ_SHELL_PDF8");
                                    // B 方案（全动态化）：环境变量优先；缺失/不存在时自动发现桌面第一个 PDF（排除"已盖章"），
                                    // 再无则 temp 兜底（s8_text.pdf，仅部分场景适用）。不再依赖固定桌面 29 页测试文件。
                                    if (string.IsNullOrWhiteSpace(pdf8) || !File.Exists(pdf8))
                                    {
                                        pdf8 = System.IO.Directory.GetFiles(
                                            System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop), "*.pdf")
                                            .FirstOrDefault(f => !Path.GetFileName(f).Contains("已盖章"));
                                        if (string.IsNullOrWhiteSpace(pdf8)) pdf8 = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "s8_text.pdf");
                                    }
                                    string pdf8Name = Path.GetFileName(pdf8);
                                    out8 = Environment.GetEnvironmentVariable("PDFQFZ_SHELL_OUT8") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "s8_out");
                                    Directory.CreateDirectory(out8);
                                    foreach (var f in Directory.GetFiles(out8, "*.pdf")) { try { File.Delete(f); } catch { } }

                                    async System.Threading.Tasks.Task<string> WaitGen(string tag)
                                    {
                                        for (int i = 0; i < 90; i++)
                                        {
                                            string js = await webView.CoreWebView2.ExecuteScriptAsync(
                                                "(function(){return window.__genResult?JSON.stringify(window.__genResult):''})()");
                                            if (js != "\"\"")
                                            {
                                                System.IO.File.AppendAllText(dumpPath, "\n" + tag + ":" + js);
                                                return js;
                                            }
                                            await System.Threading.Tasks.Task.Delay(500);
                                        }
                                        System.IO.File.AppendAllText(dumpPath, "\n" + tag + ":TIMEOUT");
                                        return "";
                                    }

                                                                        // V2.4.0.28：确保测试印章条目存在（E2E 未启用 STAMP5 时 config.ini 可能无公章，导致 AddManualStamp 选章失败）
                                    if (!PDFQFZ.WPF.Services.AppConfig.LoadStampEntries().Any(en => string.Equals(en.DisplayName, "公章", StringComparison.OrdinalIgnoreCase)))
                                    {
                                        string dstStamp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "s8_stamp_gz.png");
                                        if (!System.IO.File.Exists(dstStamp))
                                        {
                                            string srcStamp = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "Assets", "公章.png"));
                                            if (System.IO.File.Exists(srcStamp)) System.IO.File.Copy(srcStamp, dstStamp, true);
                                        }
                                        if (System.IO.File.Exists(dstStamp)) PDFQFZ.WPF.Services.AppConfig.AppendStampEntry("公章", dstStamp);
                                    }
                                    System.IO.File.AppendAllText(dumpPath, "\ns8open:" + _bridge.OpenPdf(pdf8));
                                    System.IO.File.AppendAllText(dumpPath, "\ns8sel:" + _bridge.SelectStamp("公章"));
                                    System.IO.File.AppendAllText(dumpPath, "\ns8st1:" + _bridge.AddManualStamp(0.3f, 0.3f, 1));
                                    System.IO.File.AppendAllText(dumpPath, "\ns8st2:" + _bridge.AddManualStamp(0.7f, 0.6f, 1));
                                    // s87 V2.4.0.86：重新打开同一 PDF（非切换，keepStampData=false）章应清空——对应"单文件盖章生成后重新拖入同一 PDF"场景
                                    System.IO.File.AppendAllText(dumpPath, "\ns87a1:" + _bridge.OpenPdf(pdf8));
                                    System.IO.File.AppendAllText(dumpPath, "\ns87a2:" + _bridge.GetPageStamps(1));
                                    await webView.CoreWebView2.ExecuteScriptAsync(
                                        "window.__genResult=null;window.Bridge.on('generate-done',function(p){window.__genResult=p;});");
                                    // 8a 叠加模式（不加骑缝章 qfzType=1），mark=已盖章V
                                    await webView.CoreWebView2.ExecuteScriptAsync("window.__genResult=null;");
                                    string g1 = _bridge.GenerateFiles(out8, "overlay", 150, "已盖章V", 0, 0, 1, false, "yyyyMMdd", 1, 0, 50, 20, true, 0, 1, 1, 50, 80, true, false);
                                    System.IO.File.AppendAllText(dumpPath, "\ns8a:" + g1);
                                    await WaitGen("s8aR");
                                    string baseName = System.IO.Path.GetFileNameWithoutExtension(pdf8);
                                    string outV1 = System.IO.Path.Combine(out8, baseName + "_已盖章V1.pdf");
                                    System.IO.File.AppendAllText(dumpPath, "\ns8b:" + (System.IO.File.Exists(outV1) ? ("exists:" + new FileInfo(outV1).Length) : "missing"));
                                    // 8c 叠加保留文字层 + 章真实写入：OpenPdf(输出) → GetPageStamps 应 2 枚；AutoStamp 应能找到文字
                                    System.IO.File.AppendAllText(dumpPath, "\ns8c1:" + _bridge.OpenPdf(outV1));
                                    System.IO.File.AppendAllText(dumpPath, "\ns8c2:" + _bridge.GetPageStamps(1));
                                    System.IO.File.AppendAllText(dumpPath, "\ns8c3:" + _bridge.AutoStamp("盖章", "", 10, true, false, false, 0, 0));
                                    // 8d 合并模式（300DPI）
                                    await webView.CoreWebView2.ExecuteScriptAsync("window.__genResult=null;");
                                    System.IO.File.AppendAllText(dumpPath, "\ns8d1:" + _bridge.OpenPdf(pdf8));
                                    System.IO.File.AppendAllText(dumpPath, "\ns8d2:" + _bridge.AddManualStamp(0.4f, 0.4f, 1));
                                    string g2 = _bridge.GenerateFiles(out8, "merge", 300, "已盖章V", 0, 0, 1, false, "yyyyMMdd", 1, 0, 50, 20, true, 0, 1, 1, 50, 80, true, false);
                                    System.IO.File.AppendAllText(dumpPath, "\ns8d3:" + g2);
                                    await WaitGen("s8dR");
                                    string outV2 = System.IO.Path.Combine(out8, baseName + "_已盖章V2.pdf");
                                    System.IO.File.AppendAllText(dumpPath, "\ns8e:" + (System.IO.File.Exists(outV2) ? ("exists:" + new FileInfo(outV2).Length) : "missing"));
                                    // 8f 合并扁平化：无文字层（AutoStamp 应报图片型）+ 章写入 1 枚
                                    System.IO.File.AppendAllText(dumpPath, "\ns8f1:" + _bridge.OpenPdf(outV2));
                                    System.IO.File.AppendAllText(dumpPath, "\ns8f2:" + _bridge.AutoStamp("盖章", "", 10, true, false, false, 0, 0));
                                    System.IO.File.AppendAllText(dumpPath, "\ns8f3:" + _bridge.GetPageStamps(1));
                                    // 8g 无章无骑缝：未确认 → needConfirm
                                    System.IO.File.AppendAllText(dumpPath, "\ns8g1:" + _bridge.OpenPdf(pdf8));
                                    System.IO.File.AppendAllText(dumpPath, "\ns8g2:" + _bridge.GenerateFiles(out8, "overlay", 150, "已盖章V", 0, 0, 1, false, "yyyyMMdd", 1, 0, 50, 20, false, 0, 1, 1, 50, 80, true, false));
                                    // 8h 确认后仅输出文件
                                    await webView.CoreWebView2.ExecuteScriptAsync("window.__genResult=null;");
                                    System.IO.File.AppendAllText(dumpPath, "\ns8h1:" + _bridge.GenerateFiles(out8, "overlay", 150, "已盖章V", 0, 0, 1, false, "yyyyMMdd", 1, 0, 50, 20, true, 0, 1, 1, 50, 80, true, false));
                                    await WaitGen("s8hR");
                                    // 8i 命名递增：连续生成 V1/V2 后应已有 V3（8h 是第三次）
                                    string outV3 = System.IO.Path.Combine(out8, baseName + "_已盖章V3.pdf");
                                    System.IO.File.AppendAllText(dumpPath, "\ns8i:" + (System.IO.File.Exists(outV3) ? "exists" : "missing"));
                                    // 8j 前端链路
                                    string s8j = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){try{var vm=window.__vm;return JSON.stringify({gen:!!vm&&typeof vm.generateFiles==='function',pick:!!vm&&typeof vm.pickOutDir==='function',pre:vm?vm.namePreviewText:'-',seam:vm?vm.seamType:'-',dpiDim:vm?(vm.outputMode==='overlay'?'dim':'ok'):'-'})}catch(e){return 'err:'+e.message}})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns8j:" + s8j);

                                    // ---- s8k 目录模式（8.4）----
                                    string dir8 = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "s8_dir");
                                    if (System.IO.Directory.Exists(dir8)) System.IO.Directory.Delete(dir8, true);
                                    System.IO.Directory.CreateDirectory(dir8);
                                    for (int di = 1; di <= 3; di++)
                                    {
                                        string name = di == 1 ? "s8_a" : (di == 2 ? "s8_b" : "s8_c");
                                        int pages = di == 1 ? 6 : (di == 2 ? 3 : 2);
                                        var html = new System.Text.StringBuilder();
                                        html.Append("<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page{size:A4;margin:0}body{margin:0;font-family:'Microsoft YaHei'}div{page-break-after:always;height:297mm;padding:20mm;box-sizing:border-box}span{position:absolute;left:130mm;top:100mm;font-size:14pt}</style></head><body>");
                                        for (int pi = 1; pi <= pages; pi++)
                                            html.Append("<div>文件 " + name + " 第 " + pi + " 页<br><span>盖章测试 " + name + "-" + pi + "</span></div>");
                                        html.Append("</body></html>");
                                        string hf = System.IO.Path.Combine(dir8, name + ".html");
                                        System.IO.File.WriteAllText(hf, html.ToString(), new System.Text.UTF8Encoding(true));
                                        string pf = System.IO.Path.Combine(dir8, name + ".pdf");
                                        var psi = new System.Diagnostics.ProcessStartInfo(@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe")
                                        {
                                            UseShellExecute = false,
                                            RedirectStandardOutput = true,
                                            RedirectStandardError = true,
                                            CreateNoWindow = true
                                        };
                                        psi.Arguments = "--headless --disable-gpu --print-to-pdf=\"" + pf + "\" --no-pdf-header-footer \"file:///" + hf.Replace("\\", "/") + "\"";
                                        var ep = System.Diagnostics.Process.Start(psi);
                                        if (ep != null && !ep.WaitForExit(20000)) { try { ep.Kill(); } catch { } }
                                        System.IO.File.Delete(hf);
                                    }
                                    string s8k1 = _bridge.OpenDirectory(dir8);
                                    System.IO.File.AppendAllText(dumpPath, "\ns8k1:" + s8k1);
                                    System.IO.File.AppendAllText(dumpPath, "\ns8k2:" + _bridge.GetPageStamps(1));
                                    System.IO.File.AppendAllText(dumpPath, "\ns8k3:" + _bridge.AddManualStamp(0.3f, 0.3f, 1));
                                    await webView.CoreWebView2.ExecuteScriptAsync("window.__genResult=null;");
                                    System.IO.File.AppendAllText(dumpPath, "\ns8k4:" + _bridge.GenerateFiles(System.IO.Path.Combine(dir8, "已盖章"), "overlay", 150, "已盖章V", 0, 0, 1, false, "yyyyMMdd", 1, 0, 50, 20, true, 0, 1, 1, 50, 80, true, false));
                                    string outK = System.IO.Path.Combine(dir8, "已盖章");
                                    // V2.4.0.68：文件夹模式已异步化，直调 GenerateFiles 不写 __genResult——改为轮询输出文件（最多 40s）
                                    for (int iw = 0; iw < 80; iw++)
                                    {
                                        if (System.IO.Directory.Exists(outK) && System.IO.Directory.GetFiles(outK, "*.pdf").Length >= 3) break;
                                        await System.Threading.Tasks.Task.Delay(500);
                                    }
                                    System.IO.File.AppendAllText(dumpPath, "\ns8k5:" + (System.IO.Directory.Exists(outK) ? string.Join(",", System.IO.Directory.GetFiles(outK, "*.pdf").Select(System.IO.Path.GetFileName).OrderBy(x => x)) : "无输出目录"));

                                    // s8k9（V2.4.0.28）：目录模式手动章输出检查——渲染输出 PDF 第1页检测红色像素（章是否真的落上）
                                    try
                                    {
                                        string outPdf = "";
                                        if (System.IO.Directory.Exists(outK))
                                        {
                                            var outs = System.IO.Directory.GetFiles(outK, "*.pdf").OrderBy(x => x).ToList();
                                            if (outs.Count > 0) outPdf = outs[0];
                                        }
                                        int redPx = -1;
                                        if (!string.IsNullOrEmpty(outPdf) && System.IO.File.Exists(outPdf))
                                        {
                                            using (var rr = PDFQFZ.Library.PdfiumDocumentRenderer.Open(outPdf))
                                            {
                                                using (var bmp = rr.RenderPage(0, 144))
                                                {
                                                    redPx = 0;
                                                    for (int yy = 0; yy < bmp.Height; yy += 3)
                                                    {
                                                        for (int xx = 0; xx < bmp.Width; xx += 3)
                                                        {
                                                            var px = bmp.GetPixel(xx, yy);
                                                            // 半透明红章在白底上 G/B 通道会升高：用 R 明显高于 G/B 判定红色
                                                            if (px.R > 150 && (px.R - px.G) > 30 && (px.R - px.B) > 30) redPx++;
                                                        }
                                                    }
                                                    try { bmp.Save(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "s8k9_out.png")); } catch { }
                                                }
                                            }
                                        }
                                                                                int imgCount = 0;
                                        if (!string.IsNullOrEmpty(outPdf) && System.IO.File.Exists(outPdf))
                                        {
                                            byte[] pb = System.IO.File.ReadAllBytes(outPdf);
                                            for (int i = 0; i + 5 < pb.Length; i++)
                                            {
                                                if (pb[i] == (byte)'/' && pb[i+1] == (byte)'I' && pb[i+2] == (byte)'m' && pb[i+3] == (byte)'a' && pb[i+4] == (byte)'g' && pb[i+5] == (byte)'e') imgCount++;
                                            }
                                        }
                                        System.IO.File.AppendAllText(dumpPath, "\ns8k9:" + (string.IsNullOrEmpty(outPdf) ? "无输出" : (System.IO.Path.GetFileName(outPdf) + " redPx=" + redPx + " imgCount=" + imgCount)));
                                    }
                                    catch (Exception ex9) { System.IO.File.AppendAllText(dumpPath, "\ns8k9EX:" + ex9.Message); }

                                    // s8m（V2.4.0.68）：文件夹模式"放置3批→撤销全部→无章生成"复现——needConfirm 链路 + 空章生成（用户报告按钮卡"正在生成中"）
                                    try
                                    {
                                        string s8m1 = _bridge.OpenDirectory(dir8);
                                        await System.Threading.Tasks.Task.Delay(600);
                                        string s8m2 = _bridge.BatchPreviewAll(0, 1, 1, 50, 80, true, dir8);
                                        string s8m3 = _bridge.BatchPreviewAll(0, 1, 1, 52, 82, true, dir8);
                                        string s8m4 = _bridge.BatchPreviewAll(0, 1, 1, 54, 84, true, dir8);
                                        string s8m5 = _bridge.RemoveBatchPreviewAll();
                                        string s8m6 = _bridge.RemoveBatchPreviewAll();
                                        string s8m7 = _bridge.RemoveBatchPreviewAll();
                                        string s8mN = _bridge.GenerateFiles(System.IO.Path.Combine(dir8, "已盖章"), "overlay", 150, "已盖章V", 0, 0, 1, false, "yyyyMMdd", 1, 0, 50, 20, false, 0, 1, 1, 50, 80, true, false);
                                        string s8mF = _bridge.GenerateFiles(System.IO.Path.Combine(dir8, "已盖章"), "overlay", 150, "已盖章V", 0, 0, 1, false, "yyyyMMdd", 1, 0, 50, 20, true, 0, 1, 1, 50, 80, true, false);
                                        string outM = System.IO.Path.Combine(dir8, "已盖章");
                                        // V2.4.0.69：取消无章确认后 s8mN/s8mF 均直接生成（s8k4 已有 V1×3，s8mN 产 V2×3，s8mF 产 V3×3 → 共 9）
                                        for (int im = 0; im < 80; im++)
                                        {
                                            if (System.IO.Directory.Exists(outM) && System.IO.Directory.GetFiles(outM, "*.pdf").Length >= 9) break;
                                            await System.Threading.Tasks.Task.Delay(500);
                                        }
                                        string s8mOut = System.IO.Directory.Exists(outM) ? string.Join(",", System.IO.Directory.GetFiles(outM, "*.pdf").Select(System.IO.Path.GetFileName).OrderBy(x => x)) : "无输出目录";
                                        System.IO.File.AppendAllText(dumpPath, "\ns8m1:" + s8m1 + "\ns8m2:" + s8m2 + "\ns8m3:" + s8m3 + "\ns8m4:" + s8m4 + "\ns8m5:" + s8m5 + "\ns8m6:" + s8m6 + "\ns8m7:" + s8m7 + "\ns8mN:" + s8mN + "\ns8mF:" + s8mF + "\ns8mOut:" + s8mOut);
                                    }
                                    catch (Exception exm) { System.IO.File.AppendAllText(dumpPath, "\ns8mEX:" + exm.Message); }

                                    // s8n（V2.4.0.68）：前端链路"放置3批→撤销全部→generateFiles()无参"复现——needConfirm 弹窗路径（用户实际操作链路，s8m 为直调 Bridge）
                                    try
                                    {
                                        await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;vm.openDir('" + dir8.Replace("\\", "\\\\") + "');return 'ok';})()");
                                        await System.Threading.Tasks.Task.Delay(2000);
                                        await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;vm.previewBatchStamp();vm.previewBatchStamp();vm.previewBatchStamp();return 'ok';})()");
                                        await System.Threading.Tasks.Task.Delay(3000);
                                        string s8n1 = await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;return JSON.stringify({gen:vm.generating,ps:vm.pageStamps.length,seal:!!vm.curSeal});})()");
                                        await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;vm.undoBatchStamp();vm.undoBatchStamp();vm.undoBatchStamp();return 'ok';})()");
                                        await System.Threading.Tasks.Task.Delay(3000);
                                        string s8n2 = await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;return JSON.stringify({gen:vm.generating,ps:vm.pageStamps.length});})()");
                                        await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;vm.generateFiles();return 'ok';})()");
                                        await System.Threading.Tasks.Task.Delay(1200);
                                        string s8n3 = await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;return JSON.stringify({gen:vm.generating,op:vm.opHint,mb:!!document.querySelector('.el-message-box')});})()");
                                        await System.Threading.Tasks.Task.Delay(5000);
                                        string s8n4 = await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;var m=vm.logs&&vm.logs.length?vm.logs[vm.logs.length-1].msg:'';return JSON.stringify({gen:vm.generating,op:vm.opHint,log:m});})()");
                                        System.IO.File.AppendAllText(dumpPath, "\ns8n1:" + s8n1 + "\ns8n2:" + s8n2 + "\ns8n3:" + s8n3 + "\ns8n4:" + s8n4);
                                    }
                                    catch (Exception exn) { System.IO.File.AppendAllText(dumpPath, "\ns8nEX:" + exn.Message); }

                                    // s88（V2.4.0.88）：章类型数据模型——type/keyword/page 字段 + 前端右键弹窗矩阵（手动=直接删；范围/批量=3项；按文字单枚=3项、多枚=4项含"仅删除当前印章"）
                                    try
                                    {
                                        System.IO.File.AppendAllText(dumpPath, "\ns88a1:" + _bridge.OpenPdf(pdf8));
                                        System.IO.File.AppendAllText(dumpPath, "\ns88a2:" + _bridge.AddManualStamp(0.35f, 0.35f, 1));
                                        System.IO.File.AppendAllText(dumpPath, "\ns88a3:" + _bridge.AddRangeStamps(0.6f, 0.6f, 1, 2));
                                        System.IO.File.AppendAllText(dumpPath, "\ns88a4:" + _bridge.AutoStamp("服务器", "", 10, true, false, false, 0, 0));
                                        System.IO.File.AppendAllText(dumpPath, "\ns88a5:" + _bridge.GetPageStamps(1));
                                        // s88a6：文字型 PDF 验证 text 章字段（pdf8 图片型无文字层时；s8k 生成的 s8_a.pdf 含"盖章测试"文字）
                                        string altPdf = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "s8_dir", "s8_a.pdf");
                                        if (System.IO.File.Exists(altPdf))
                                        {
                                            System.IO.File.AppendAllText(dumpPath, "\ns88a6a:" + _bridge.OpenPdf(altPdf));
                                            System.IO.File.AppendAllText(dumpPath, "\ns88a6b:" + _bridge.AutoStamp("盖章测试", "", 10, true, false, false, 0, 0));
                                            System.IO.File.AppendAllText(dumpPath, "\ns88a6c:" + _bridge.GetPageStamps(1));
                                        }
                                        // s88b 三段式（ExecuteScriptAsync 不等待 async promise）：先打开弹窗 → Delay → 读 DOM → 关弹窗开 dlgOps → Delay → 汇总
                                        await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){var vm=window.__vm;vm.batchDelShowSingle=true;vm.batchDel={id:-999,page:1,batchId:9,type:'text'};vm.dlgBatchDel=true;return 'ok';})()");
                                        await System.Threading.Tasks.Task.Delay(400);
                                        string s88b1 = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){var t='';var bs=document.querySelectorAll('.el-dialog button');for(var i=0;i<bs.length;i++){t+=bs[i].textContent;}window.__dlgBtn=t;var vm=window.__vm;vm.dlgBatchDel=false;vm.dlgOps=true;return t;})()");
                                        System.IO.File.AppendAllText(dumpPath, "\ns88b1:" + s88b1);
                                        await System.Threading.Tasks.Task.Delay(400);
                                        string s88b = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){try{var vm=window.__vm;var m=(vm.$options&&vm.$options.methods)||{};var src=(m.removeStamp||'').toString()||'';var bc=(m.batchDelChoice||'').toString()||'';var out={tManual:src.indexOf('manual')>=0,tText:src.indexOf('text')>=0,showSingle:src.indexOf('batchDelShowSingle')>=0,choice4:bc.indexOf('DeleteStampById')>=0||bc.indexOf('choice===4')>=0,singleBtn:(window.__dlgBtn||'').indexOf('仅删除当前印章')>=0,ops:!!document.querySelector('.ops-table'),ac:!!document.querySelector('.el-autocomplete')};vm.dlgOps=false;return JSON.stringify(out);}catch(e){return 'err:'+e.message}})()");
                                        System.IO.File.AppendAllText(dumpPath, "\ns88b:" + s88b);
                                    }
                                    catch (Exception ex88) { System.IO.File.AppendAllText(dumpPath, "\ns88EX:" + ex88.Message); }

                                    // s89（V2.4.0.89）：弹窗/图标规范化——删除叉与重命名常显、历史排序、dlgBatchDel 480 一行、help-square 组件、ops 表格、app-dialog 统一类
                                    try
                                    {
                                        // s89a：历史排序（GetAutoStampHistory 最新在前）+ 删除叉/重命名常显（autocomplete 下拉内 .opt-del/.opt-ren visible）
                                        System.IO.File.AppendAllText(dumpPath, "\ns89a:" + _bridge.GetAutoStampHistory());
                                        string s89a2 = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){try{var vm=window.__vm;vm.searchHistory=['最近词','早先词'];var inp=document.querySelector('.el-autocomplete input');inp.focus();inp.dispatchEvent(new Event('input',{bubbles:true}));return 'ok';}catch(e){return 'err:'+e.message}})()");
                                        await System.Threading.Tasks.Task.Delay(700);
                                        string s89a3 = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){try{var d=document.querySelectorAll('.el-autocomplete-suggestion .opt-del,.el-select-dropdown__item .opt-del');if(!d.length)return JSON.stringify({del:false});var cs=getComputedStyle(d[0]);var r=document.querySelectorAll('.el-autocomplete-suggestion .opt-ren,.el-select-dropdown__item .opt-ren');var rv=r.length?getComputedStyle(r[0]).visibility:'';return JSON.stringify({del:true,v:cs.visibility,renV:rv});}catch(e){return 'err:'+e.message}})()");
                                        System.IO.File.AppendAllText(dumpPath, "\ns89a2:" + s89a2 + "\ns89a3:" + s89a3);
                                        // s89b：dlgBatchDel 480px + 4 按钮一行 + 主按钮 primary + 取消 plain + app-dialog 类 + 简化第二行
                                        string s89b1 = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){try{var vm=window.__vm;vm.batchDelShowSingle=true;vm.batchDel={id:-999,page:1,batchId:9,type:'text'};vm.dlgBatchDel=true;return 'ok';}catch(e){return 'err:'+e.message}})()");
                                        await System.Threading.Tasks.Task.Delay(500);
                                        string s89b = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){try{var d=document.querySelector('.el-dialog.app-dialog');var btns=d?d.querySelectorAll('.el-dialog__body .el-button'):[];var tops=[];for(var i=0;i<btns.length;i++)tops.push(btns[i].offsetTop);var same=tops.length>1&&tops.every(function(t){return t===tops[0]});var bodyTxt=d?d.querySelector('.el-dialog__body').innerText:'';return JSON.stringify({w:d?d.offsetWidth:0,n:btns.length,oneLine:same,primary:!!(d&&d.querySelector('.el-button--primary')),plain:!!(d&&d.querySelector('.is-plain')),simplified:bodyTxt.indexOf('请选择删除范围')>=0});}catch(e){return 'err:'+e.message}})()");
                                        await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;vm.dlgBatchDel=false;return 'ok';})()");
                                        System.IO.File.AppendAllText(dumpPath, "\ns89b1:" + s89b1 + "\ns89b:" + s89b);
                                        // s89c：help-square 组件（span 非 button 包裹）+ ops 表格（表头 2 列 6 行）
                                        string s89c1 = await webView.CoreWebView2.ExecuteScriptAsync("(function(){try{var vm=window.__vm;vm.dlgOps=true;return 'ok';}catch(e){return 'err:'+e.message}})()");
                                        await System.Threading.Tasks.Task.Delay(500);
                                        string s89c = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){try{var t=document.querySelector('.ops-table');var svg=document.querySelector('.pt-ico svg');var bw=document.querySelector('.pt-ico');return JSON.stringify({table:!!t,ths:t?t.querySelectorAll('thead th').length:0,trs:t?t.querySelectorAll('tbody tr').length:0,svgInBtn:!!svg,btnWrap:!!bw,isButton:bw?bw.tagName.toLowerCase()==='button':false});}catch(e){return 'err:'+e.message}})()");
                                        await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;vm.dlgOps=false;return 'ok';})()");
                                        System.IO.File.AppendAllText(dumpPath, "\ns89c1:" + s89c1 + "\ns89c:" + s89c);
                                        // s89d：已渲染 el-dialog 均带 app-dialog 或 float-dialog 统一类
                                        string s89d = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){try{var all=document.querySelectorAll('.el-dialog');var ok=all.length>0&&Array.prototype.every.call(all,function(d){return d.classList.contains('app-dialog')||d.classList.contains('float-dialog');});return JSON.stringify({all:all.length,ok:ok});}catch(e){return 'err:'+e.message}})()");
                                        System.IO.File.AppendAllText(dumpPath, "\ns89d:" + s89d);
                                    }
                                    catch (Exception ex89) { System.IO.File.AppendAllText(dumpPath, "\ns89EX:" + ex89.Message); }

                                    // s90（V2.4.0.90）：弹窗规范定稿——重命名弹窗 el-dialog 化（dlgRename 替代 window.prompt）、help 纯问号 24px el-button 同构、dlgRange/helpOpen footer 按钮
                                    try
                                    {
                                        // s90a：dlgRename 弹窗（标题/el-input/app-dialog 浅色）
                                        string s90a1 = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){try{var vm=window.__vm;vm.wmDlgSave=false;vm.randDlg=false;vm.dlgOps=false;vm.dlgRange=false;vm.helpOpen=false;vm.dlgBatchDel=false;vm.dlgRename=false;vm.renameVal='测试公章';vm.dlgRename=true;return 'ok';}catch(e){return 'err:'+e.message}})()");
                                        await System.Threading.Tasks.Task.Delay(500);
                                        string s90a = await webView.CoreWebView2.ExecuteScriptAsync(
                                             "(function(){try{var ds=Array.prototype.filter.call(document.querySelectorAll('.el-dialog.app-dialog'),function(x){return x.offsetParent!==null||x.getBoundingClientRect().width>0;});var d=null;for(var qi=0;qi<ds.length;qi++){if(ds[qi].querySelector('.el-input__inner')){d=ds[qi];break}}if(!d&&ds.length)d=ds[0];var t=d?d.querySelector('.el-dialog__title'):null;var inp=d?d.querySelector('.el-input__inner'):null;var bg=d?getComputedStyle(d).backgroundColor:'';var m=bg.match(/\\d+/g);var light=!!(m&&m.length>=3&&(Number(m[0])>100||Number(m[1])>100||Number(m[2])>100));return JSON.stringify({dlg:!!d,title:t?t.textContent:'',input:!!inp,light:light,bg:bg});}catch(e){return 'err:'+e.message}})()");
                                        await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;vm.dlgRename=false;return 'ok';})()");
                                        System.IO.File.AppendAllText(dumpPath, "\ns90a1:" + s90a1 + "\ns90a:" + s90a);
                                        // s90b：help 纯问号 24px + el-button 同构（无 pt-ico-btn / 无 help-square 标签）
                                        string s90b = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){try{var btn=document.querySelector('.pt-ico-btn');var hs=document.querySelectorAll('help-square').length;var h=document.querySelector('.pt-ico-help svg');var fs=h?getComputedStyle(h).fontSize:'';var inBtn=!!(h&&h.closest('button.el-button'));var b=h?h.closest('button.el-button'):null;var bg=b?getComputedStyle(b).backgroundColor:'';var col=h?getComputedStyle(h).color:'';return JSON.stringify({ptBtn:!!btn,hs:hs,help:!!h,fs:fs,inBtn:inBtn,bg:bg,col:col});}catch(e){return 'err:'+e.message}})()");
                                        System.IO.File.AppendAllText(dumpPath, "\ns90b:" + s90b);
                                        // s90c：dlgRange footer 按钮
                                        string s90c1 = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){try{var vm=window.__vm;vm.dlgRange=true;return 'ok';}catch(e){return 'err:'+e.message}})()");
                                        await System.Threading.Tasks.Task.Delay(500);
                                        string s90c = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){try{var ds=Array.prototype.filter.call(document.querySelectorAll('.el-dialog.app-dialog'),function(x){return x.offsetParent!==null||x.getBoundingClientRect().width>0;});var d=ds[0]||null;var f=d?d.querySelector('.el-dialog__footer'):null;var btns=f?f.querySelectorAll('.el-button').length:0;var plain=!!(f&&f.querySelector('.is-plain'));return JSON.stringify({footer:!!f,btns:btns,plain:plain});}catch(e){return 'err:'+e.message}})()");
                                        await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;vm.dlgRange=false;return 'ok';})()");
                                        System.IO.File.AppendAllText(dumpPath, "\ns90c1:" + s90c1 + "\ns90c:" + s90c);
                                        // s90d：helpOpen footer 关闭
                                        string s90d1 = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){try{var vm=window.__vm;vm.helpOpen=true;return 'ok';}catch(e){return 'err:'+e.message}})()");
                                        await System.Threading.Tasks.Task.Delay(500);
                                        string s90d = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){try{var ds=Array.prototype.filter.call(document.querySelectorAll('.el-dialog.app-dialog'),function(x){return x.offsetParent!==null||x.getBoundingClientRect().width>0;});var d=ds[0]||null;var f=d?d.querySelector('.el-dialog__footer'):null;return JSON.stringify({footer:!!f,close:!!(f&&f.querySelector('.el-button--primary'))});}catch(e){return 'err:'+e.message}})()");
                                        await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;vm.helpOpen=false;return 'ok';})()");
                                        System.IO.File.AppendAllText(dumpPath, "\ns90d1:" + s90d1 + "\ns90d:" + s90d);
                                    }
                                    catch (Exception ex90) { System.IO.File.AppendAllText(dumpPath, "\ns90EX:" + ex90.Message); }

                                    // s91（V2.4.0.91）：帮助排版——helpText 结构化 HTML + 弹窗 720px
                                    try
                                    {
                                        string s91a1 = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){try{var vm=window.__vm;vm.helpOpen=true;return 'ok';}catch(e){return 'err:'+e.message}})()");
                                        await System.Threading.Tasks.Task.Delay(500);
                                        string s91a = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){try{var ds=Array.prototype.filter.call(document.querySelectorAll('.el-dialog.app-dialog'),function(x){return x.offsetParent!==null||x.getBoundingClientRect().width>0;});var d=ds[0]||null;var h=d?d.querySelectorAll('.dlg-help .h').length:0;var ul=d?d.querySelectorAll('.dlg-help ul, .dlg-help ol').length:0;var qa=d?d.querySelectorAll('.dlg-help .qa').length:0;var f=d?d.querySelector('.el-dialog__footer'):null;var w=d?Math.round(d.getBoundingClientRect().width):0;return JSON.stringify({dlg:!!d,hs:h,uls:ul,qas:qa,footer:!!(f&&f.querySelector('.el-button--primary')),w:w});}catch(e){return 'err:'+e.message}})()");
                                        await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;vm.helpOpen=false;return 'ok';})()");
                                        System.IO.File.AppendAllText(dumpPath, "\ns91a1:" + s91a1 + "\ns91a:" + s91a);
                                    }
                                    catch (Exception ex91) { System.IO.File.AppendAllText(dumpPath, "\ns91EX:" + ex91.Message); }

                                    // s92（V2.4.0.92）：骑缝章预览重拉——换文档（内容相同页数相同）/同文件重开/不同名副本/目录切文件均须刷新 seamAll
                                    try
                                    {
                                        // V2.4.0.347：s92 测试 PDF 优先用项目内测试资源（E2E\TestResources\测试组A.pdf，10页多页 PDF），
                                        // 不再依赖桌面文件（用户曾手动清理桌面测试文件致 s92 常失败）；支持环境变量 PDFQFZ_SHELL_PDF92 覆盖。
                                        string srcPdf = Environment.GetEnvironmentVariable("PDFQFZ_SHELL_PDF92");
                                        if (string.IsNullOrWhiteSpace(srcPdf) || !System.IO.File.Exists(srcPdf))
                                        {
                                            string proj92 = System.IO.Path.GetFullPath(
                                                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                                @"..\..\..\..\E2E\TestResources\测试组A.pdf"));
                                            if (System.IO.File.Exists(proj92)) srcPdf = proj92;
                                            else srcPdf = @"C:\Users\admin\Desktop\关于服务器批量硬盘异常问题及处置诉求的函.pdf";
                                        }
                                        if (!System.IO.File.Exists(srcPdf)) { System.IO.File.AppendAllText(dumpPath, "\ns92EX:no-test-pdf"); }
                                        else {
                                        string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PDFQFZ_E2E_92_" + System.Guid.NewGuid().ToString("N"));
                                        System.IO.Directory.CreateDirectory(tmpDir);
                                        string copyB = System.IO.Path.Combine(tmpDir, "副本B_内容相同.pdf");
                                        string copyC = System.IO.Path.Combine(tmpDir, "副本C_内容相同.pdf");
                                        System.IO.File.Copy(srcPdf, copyB, true);
                                        System.IO.File.Copy(srcPdf, copyC, true);
                                        string sb = copyB.Replace("\\", "/");
                                        string sc = copyC.Replace("\\", "/");
                                        // s92a：当前文档（原 PDF）开骑缝章 -> seamAll 有切片
                                        string s92a1 = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){try{var vm=window.__vm;vm.seamType=0;return 'ok';}catch(e){return 'err:'+e.message}})()");
                                        await System.Threading.Tasks.Task.Delay(900);
                                        string s92a = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){try{var vm=window.__vm;return JSON.stringify({n:(vm.seamAll||[]).length,cur:vm.curFile});}catch(e){return 'err:'+e.message}})()");
                                        System.IO.File.AppendAllText(dumpPath, "\ns92a1:" + s92a1 + "\ns92a:" + s92a);
                                        // s92b：同文件重开（V87 场景）-> seamAll 刷新
                                        string s92b1 = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){try{var vm=window.__vm;vm.openPdf('" + srcPdf.Replace("\\", "/") + "',false);return 'ok';}catch(e){return 'err:'+e.message}})()");
                                        await System.Threading.Tasks.Task.Delay(1200);
                                        string s92b = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){try{var vm=window.__vm;return JSON.stringify({n:(vm.seamAll||[]).length,cur:vm.curFile});}catch(e){return 'err:'+e.message}})()");
                                        System.IO.File.AppendAllText(dumpPath, "\ns92b1:" + s92b1 + "\ns92b:" + s92b);
                                        // s92c：不同名副本（内容相同）-> seamAll 刷新
                                        string s92c1 = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){try{var vm=window.__vm;vm.openPdf('" + sb + "',false);return 'ok';}catch(e){return 'err:'+e.message}})()");
                                        await System.Threading.Tasks.Task.Delay(1200);
                                        string s92c = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){try{var vm=window.__vm;return JSON.stringify({n:(vm.seamAll||[]).length,cur:vm.curFile});}catch(e){return 'err:'+e.message}})()");
                                        System.IO.File.AppendAllText(dumpPath, "\ns92c1:" + s92c1 + "\ns92c:" + s92c);
                                        // s92d：目录切文件（OpenPdfKeep）-> seamAll 刷新
                                        string s92d1 = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){try{var vm=window.__vm;vm.openPdf('" + sc + "',true);return 'ok';}catch(e){return 'err:'+e.message}})()");
                                        await System.Threading.Tasks.Task.Delay(1200);
                                        string s92d = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){try{var vm=window.__vm;return JSON.stringify({n:(vm.seamAll||[]).length,cur:vm.curFile});}catch(e){return 'err:'+e.message}})()");
                                        System.IO.File.AppendAllText(dumpPath, "\ns92d1:" + s92d1 + "\ns92d:" + s92d);
                                        // 恢复：切回原 PDF 并关闭骑缝章
                                        await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){try{var vm=window.__vm;vm.openPdf('" + srcPdf.Replace("\\", "/") + "',false);return 'ok';}catch(e){return 'err:'+e.message}})()");
                                        await System.Threading.Tasks.Task.Delay(800);
                                        await webView.CoreWebView2.ExecuteScriptAsync("(function(){try{var vm=window.__vm;vm.seamType=1;return 'ok';}catch(e){return 'err:'+e.message}})()");
                                        } // V2.4.0.347：闭合 s92 的 else（srcPdf 存在分支）
                                    }
                                    catch (Exception ex92) { System.IO.File.AppendAllText(dumpPath, "\ns92EX:" + ex92.Message); }

                                    // s24（V2.4.0.28）：前端完整流程验证（openDir→点击盖章→generateFiles→输出章+saveDir+收起）——最接近用户实际操作，一次覆盖三个问题
                                    try
                                    {
                                        await webView.CoreWebView2.ExecuteScriptAsync("window.__genResult=null;");
                                        await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;vm.openDir('" + dir8.Replace("\\", "\\\\") + "');return 'ok';})()");
                                        await System.Threading.Tasks.Task.Delay(2200);
                                        string s24a = await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;return JSON.stringify({dirMode:vm.dirMode,listLen:vm.curFileList.length,saveDir:vm.saveDir});})()");
                                        await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;var st=vm..stage;vm.onPreviewClick({clientX:300,clientY:300,currentTarget:st},1);return 'ok';})()");
                                        await System.Threading.Tasks.Task.Delay(1200);
                                        string s24b = await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;return JSON.stringify({ps:vm.pageStamps.length,curFile:vm.curFile.split('/').pop(),saveDir:vm.saveDir});})()");
                                        await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;vm.cards={file:true,stamp:true,batch:true,text:true,output:true};vm.generateFiles(true);return 'ok';})()");
                                        await System.Threading.Tasks.Task.Delay(5000);
                                        string s24c = await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;return JSON.stringify({cards:vm.cards});})()");
                                        string outS24 = System.IO.Path.Combine(dir8, "已盖章");
                                        string outP24 = ""; int red24 = -1;
                                        if (System.IO.Directory.Exists(outS24))
                                        {
                                            var outs24 = System.IO.Directory.GetFiles(outS24, "*.pdf").OrderBy(x => x).ToList();
                                            var v2 = outs24.Where(x => x.IndexOf("s8_a", StringComparison.OrdinalIgnoreCase) >= 0 && x.IndexOf("V2.pdf", StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(x => x).LastOrDefault();
                                            outP24 = v2 ?? (outs24.Count > 0 ? outs24[outs24.Count - 1] : "");
                                        }
                                        if (!string.IsNullOrEmpty(outP24) && System.IO.File.Exists(outP24))
                                        {
                                            using (var rr = PDFQFZ.Library.PdfiumDocumentRenderer.Open(outP24))
                                            using (var bmp = rr.RenderPage(0, 144))
                                            {
                                                red24 = 0;
                                                for (int yy = 0; yy < bmp.Height; yy += 3)
                                                {
                                                    for (int xx = 0; xx < bmp.Width; xx += 3)
                                                    {
                                                        var px = bmp.GetPixel(xx, yy);
                                                        if (px.R > 150 && (px.R - px.G) > 30 && (px.R - px.B) > 30) red24++;
                                                    }
                                                }
                                            }
                                        }
                                        System.IO.File.AppendAllText(dumpPath, "\ns24a:" + s24a + "\ns24b:" + s24b + "\ns24c:" + s24c + "\ns24:" + (string.IsNullOrEmpty(outP24) ? "无输出" : (System.IO.Path.GetFileName(outP24) + " red=" + red24)));
                                    }
                                    catch (Exception ex24) { System.IO.File.AppendAllText(dumpPath, "\ns24EX:" + ex24.Message); }

                                    // s25（V2.4.0.29）：目录模式多文件章保留（对齐 WPF LoadPdf keepStampData:true）
                                    // 文件A盖章 → 切B（无章）→ 切回A（章仍在）→ 生成输出A有章
                                    try
                                    {
                                        string s25a = _bridge.OpenDirectory(dir8);
                                        await System.Threading.Tasks.Task.Delay(600);
                                        string s25b = _bridge.AddManualStamp(0.3f, 0.3f, 1);
                                        await System.Threading.Tasks.Task.Delay(300);
                                        string s25c1 = _bridge.GetPageStamps(1); // 文件A当前：应有1枚
                                        string s25c2 = _bridge.OpenPdfKeep(System.IO.Path.Combine(dir8, "s8_b.pdf")); // 切B：保留章
                                        await System.Threading.Tasks.Task.Delay(300);
                                        string s25c3 = _bridge.GetPageStamps(1); // B无章
                                        string s25c4 = _bridge.OpenPdfKeep(System.IO.Path.Combine(dir8, "s8_a.pdf")); // 切回A
                                        await System.Threading.Tasks.Task.Delay(300);
                                        string s25c5 = _bridge.GetPageStamps(1); // A章仍在
                                        System.IO.File.AppendAllText(dumpPath, "\ns25a:" + s25a + "\ns25b:" + s25b + "\ns25c1:" + s25c1 + "\ns25c2:" + s25c2 + "\ns25c3:" + s25c3 + "\ns25c4:" + s25c4 + "\ns25c5:" + s25c5);
                                    }
                                    catch (Exception ex25) { System.IO.File.AppendAllText(dumpPath, "\ns25EX:" + ex25.Message); }
                                    System.IO.File.AppendAllText(dumpPath, "\ns8k6:" + _bridge.AutoStampDir("盖章测试", "", 10, false, false, false, 0, 0));
                                    System.IO.File.AppendAllText(dumpPath, "\ns8k7:" + _bridge.GetPageStamps(1));
                                    await webView.CoreWebView2.ExecuteScriptAsync("window.__genResult=null;");
                                    System.IO.File.AppendAllText(dumpPath, "\ns8k8:" + _bridge.GenerateFiles(outK, "overlay", 150, "已盖章V", 0, 0, 1, false, "yyyyMMdd", 1, 0, 50, 20, true, 0, 1, 1, 50, 80, true, false));
                                    await WaitGen("s8kR2");
                                    System.IO.File.AppendAllText(dumpPath, "\ns8k9:" + string.Join(",", System.IO.Directory.GetFiles(outK, "*.pdf").Select(System.IO.Path.GetFileName).OrderBy(x => x)));
                                    // s28（V2.4.0.99）：图片批量水印——加载测试图片→加水印框→批量输出→校验输出文件与命名
                                    try
                                    {
                                        string s28dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PDFQFZ_E2E_97_" + System.Guid.NewGuid().ToString("N").Substring(0, 6));
                                        System.IO.Directory.CreateDirectory(s28dir);
                                        string s28src = System.IO.Path.Combine(s28dir, "风景照片.jpg");
                                        using (var bmp = new System.Drawing.Bitmap(800, 500))
                                        {
                                            using (var g = System.Drawing.Graphics.FromImage(bmp))
                                            {
                                                g.Clear(System.Drawing.Color.White);
                                                using (var b = new System.Drawing.SolidBrush(System.Drawing.Color.DodgerBlue)) g.FillRectangle(b, 50, 50, 300, 200);
                                            }
                                            bmp.Save(s28src, System.Drawing.Imaging.ImageFormat.Jpeg);
                                        }
                                        string s28json = "[\"" + s28src.Replace("\\", "\\\\") + "\"]";
                                        string s28load = _bridge.LoadImagePaths(s28json);
                                        System.IO.File.AppendAllText(dumpPath, "\ns28dbg:json=" + s28json + " exists=" + System.IO.File.Exists(s28src) + " dir=" + s28dir + " ext=" + System.IO.Path.GetExtension(s28src));
                                        string s28set = _bridge.SetCurrentImage(0);
                                        string s28add = _bridge.AddWatermarkBox(1, 0.3, 0.4, 0.4, 0.08, "{\"text\":\"E2E水印\",\"fontName\":\"微软雅黑\",\"fontScale\":0.8,\"colorArgb\":-65432,\"opacity\":100,\"bold\":false,\"italic\":false,\"underline\":false,\"strike\":false,\"letterSpacing\":0,\"lineSpacing\":0,\"align\":0,\"rotation\":0}");
                                        string s28out = _bridge.ApplyImageWatermarks(s28dir);
                                        string s28file = "";
                                        bool s28exists = false;
                                        var s28files = System.IO.Directory.GetFiles(s28dir, "*.jpg").Where(x => x.IndexOf("已加水印", System.StringComparison.Ordinal) >= 0).ToList();
                                        if (s28files.Count > 0) { s28file = System.IO.Path.GetFileName(s28files[0]); s28exists = System.IO.File.Exists(s28files[0]); }
                                        System.IO.File.AppendAllText(dumpPath, "\ns28load:" + s28load + "\ns28set:" + s28set + "\ns28add:" + s28add + "\ns28out:" + s28out + "\ns28file:" + s28file + "\ns28exists:" + s28exists);
                                        try { _bridge.ClearImages(); } catch { }
                                    }
                                    catch (Exception ex28) { System.IO.File.AppendAllText(dumpPath, "\ns28EX:" + ex28.Message); }

                                    // s29（V2.4.0.99）：水印前端方法挂载 + 添加水印框（回归：refreshPageWatermarks 未定义→水印功能全坏）
                                    try
                                    {
                                        string s29 = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){var vm=window.__vm; var out={refresh:typeof vm.refreshPageWatermarks, add:typeof vm.addWatermarkBox, imgPaths:typeof vm.imgLoadByPaths, dropPath:typeof vm.openDroppedPath, dropDir:typeof vm.openDroppedDir, imgClear:typeof vm.imgClearFrontend}; if(out.refresh==='function' && vm.pdfLoaded){ try{ vm.onWatermarkEnable(true); vm.addWatermarkBox(); }catch(e){ out.err=e.message; } window.__s29done=false; setTimeout(function(){ out.boxes=(vm.wmBoxes||[]).length; window.__s29=JSON.stringify(out); window.__s29done=true; }, 1500); return 'waiting'; } return JSON.stringify(out);})()");
                                        await System.Threading.Tasks.Task.Delay(2000);
                                        string s29r = await webView.CoreWebView2.ExecuteScriptAsync("String(window.__s29done===true ? window.__s29 : 'timeout')");
                                        System.IO.File.AppendAllText(dumpPath, "\ns29:" + s29r);
                                    }
                                    catch (Exception ex29) { System.IO.File.AppendAllText(dumpPath, "\ns29EX:" + ex29.Message); }

                                    // s30（V2.4.0.99）：前端分流链路（与拖入同函数）——单图片→图片模式；纯图文件夹→图片模式；混合文件夹→弹窗
                                    try
                                    {
                                        string s30img = "\"" + System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PDFQFZ_E2E_97_acd459", "风景照片.jpg").Replace("\\", "\\\\") + "\"";
                                        string s30dir = "\"" + System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PDFQFZ_E2E_97_acd459").Replace("\\", "\\\\") + "\"";
                                        string s30mix = "\"" + "C:\\Users\\admin\\Desktop\\__v98test_mix".Replace("\\", "\\\\") + "\"";
                                        await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){var vm=window.__vm; var out={}; vm.openDroppedPath(" + s30img + "); window.__s30done=false; setTimeout(function(){ out.pathImg={imgMode:vm.imgMode, loaded:vm.imgLoaded, queue:vm.imgQueue.length}; window.__s30=JSON.stringify(out); window.__s30done=true; }, 1500); return 'w';})()");
                                        await System.Threading.Tasks.Task.Delay(2000);
                                        string s30a = await webView.CoreWebView2.ExecuteScriptAsync("String(window.__s30done===true ? window.__s30 : 'timeout1')");
                                        await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){var vm=window.__vm; var out={}; vm.openDroppedDir(" + s30dir + "); window.__s30done=false; setTimeout(function(){ out.dirImg={imgMode:vm.imgMode, queue:vm.imgQueue.length}; window.__s30=JSON.stringify(out); window.__s30done=true; }, 1500); return 'w';})()");
                                        await System.Threading.Tasks.Task.Delay(2000);
                                        string s30b = await webView.CoreWebView2.ExecuteScriptAsync("String(window.__s30done===true ? window.__s30 : 'timeout2')");
                                        await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){var vm=window.__vm; var out={}; vm.openDroppedDir(" + s30mix + "); window.__s30done=false; setTimeout(function(){ out.mix={dlg:vm.dlgImgMixed, pdfs:vm.imgMixedInfo?vm.imgMixedInfo.pdfs:0, imgs:vm.imgMixedInfo?vm.imgMixedInfo.imgs:0}; window.__s30=JSON.stringify(out); window.__s30done=true; }, 1500); return 'w';})()");
                                        await System.Threading.Tasks.Task.Delay(2000);
                                        string s30c = await webView.CoreWebView2.ExecuteScriptAsync("String(window.__s30done===true ? window.__s30 : 'timeout3')");
                                        System.IO.File.AppendAllText(dumpPath, "\ns30a:" + s30a + "\ns30b:" + s30b + "\ns30c:" + s30c);
                                    }
                                    catch (Exception ex30) { System.IO.File.AppendAllText(dumpPath, "\ns30EX:" + ex30.Message); }

                                    // s31（V2.4.0.99）：bug 修复回归——水印开关联动展开(wmSect.main) + 图片模式渲染分支(DOM 实测)
                                    try
                                    {
                                        string s31a1 = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){var vm=window.__vm; vm.onWatermarkEnable(true); return JSON.stringify({on:true, wmSectMain:vm.wmSect.main, enabled:vm.watermarkEnabled});})()");
                                        string s31a2 = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){var vm=window.__vm; vm.onWatermarkEnable(false); return JSON.stringify({on:false, wmSectMain:vm.wmSect.main});})()");
                                        string s31a3 = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){var vm=window.__vm; vm.onWatermarkEnable(true); return JSON.stringify({on:true, wmSectMain:vm.wmSect.main});})()");
                                        string s31img = "\"" + System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PDFQFZ_E2E_97_acd459", "风景照片.jpg").Replace("\\", "\\\\") + "\"";
                                        await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){var vm=window.__vm; window.__s31done=false; vm.openDroppedPath(" + s31img + "); setTimeout(function(){ window.__s31=JSON.stringify({imgMode:vm.imgMode, imgLoaded:vm.imgLoaded, imgDispW:vm.imgDispW, imgWrap:document.querySelectorAll('.img-wrap').length, pp:document.querySelectorAll('.pp-placeholder').length}); window.__s31done=true; }, 2000); return 'w';})()");
                                        await System.Threading.Tasks.Task.Delay(2500);
                                        string s31b = await webView.CoreWebView2.ExecuteScriptAsync("String(window.__s31done===true ? window.__s31 : 'timeout')");
                                        System.IO.File.AppendAllText(dumpPath, "\ns31a1:" + s31a1 + "\ns31a2:" + s31a2 + "\ns31a3:" + s31a3 + "\ns31b:" + s31b);
                                        string s31c = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){var vm=window.__vm; return JSON.stringify({imgW:vm.imgW, imgH:vm.imgH, imgFitW:vm.imgFitW, imgScaleNum:vm.imgScaleNum, imgDispW:vm.imgDispW, stageW:(vm.$refs.stage?vm.$refs.stage.clientWidth:-1)});})()");
                                        System.IO.File.AppendAllText(dumpPath, "\ns31c:" + s31c);
                                        string s31d = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){var vm=window.__vm; var iw=vm.imgW; var fitW=Math.min(vm.imgFitW||600, iw); var manual=Math.max(10, Math.min(iw, fitW*vm.imgScaleNum)); return JSON.stringify({manual:manual, cached:vm.imgDispW, imgWNow:vm.imgW});})()");
                                        System.IO.File.AppendAllText(dumpPath, "\ns31d:" + s31d);
                                        string s31e = await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm; var el=vm.$refs.imgEl; return el?JSON.stringify({clientW:el.clientWidth, offsetW:el.offsetWidth, dispW:vm.imgDispW()}):\"no-img-el\";})()");
                                        System.IO.File.AppendAllText(dumpPath, "\ns31e:" + s31e);
                                    }
                                    catch (Exception ex31) { System.IO.File.AppendAllText(dumpPath, "\ns31EX:" + ex31.Message); }

                                    // s33（V2.4.0.370）：图片模式文字水印恒启用——模拟全新(未开文)→加载图片→加框→断言 wm-box DOM 渲染 + watermarkEnabled 状态（防 V360 删卡内开关后图片模式全新加载加框不显示的回归：后端建框成功但渲染被 watermarkEnabled 闸门拦截）
                                    try
                                    {
                                        string s33img = "\"" + System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PDFQFZ_E2E_97_acd459", "风景照片.jpg").Replace("\\", "\\\\") + "\"";
                                        // 模拟"全新未开文"：前端 data 关闭文字水印开关（不调 onWatermarkEnable，直接改状态）
                                        await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm; vm.watermarkEnabled=false; return 'reset';})()");
                                        await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){var vm=window.__vm; if(!vm){ window.__s33done=true; window.__s33=JSON.stringify({err:'no-vm'}); return 'w'; } vm.openDroppedPath(" + s33img + "); setTimeout(function(){ try{ vm.addWatermarkBox(); }catch(e){ window.__s33done=true; window.__s33=JSON.stringify({err:e.message}); return; } setTimeout(function(){ window.__s33done=true; window.__s33=JSON.stringify({imgMode:vm.imgMode, imgLoaded:vm.imgLoaded, watermarkEnabled:vm.watermarkEnabled, boxes:(vm.wmBoxes||[]).length, wmBoxDom:document.querySelectorAll('.img-wrap .wm-box').length}); }, 1800); }, 1800); return 'w';})()");
                                        await System.Threading.Tasks.Task.Delay(4500);
                                        string s33r = await webView.CoreWebView2.ExecuteScriptAsync("String(window.__s33done===true ? window.__s33 : 'timeout')");
                                        System.IO.File.AppendAllText(dumpPath, "\ns33:" + s33r);
                                    }
                                    catch (Exception ex33) { System.IO.File.AppendAllText(dumpPath, "\ns33EX:" + ex33.Message); }

                                    // s20（V2.4.0.21）：OpenFiles 多文件拖入链路（对齐 WPF LoadSourceFiles）——3 个文件→列表、第一个打开、pageCount=6
                                    try
                                    {
                                        var s20dir = System.IO.Path.GetTempPath() + "s8_dir";
                                        var s20files = new System.Collections.Generic.List<string>();
                                        for (int i = 0; i < 3 && i < System.IO.Directory.GetFiles(s20dir, "*.pdf").Length; i++)
                                            s20files.Add(System.IO.Directory.GetFiles(s20dir, "*.pdf")[i]);
                                        var s20arr = new System.Text.StringBuilder("[");
                                        for (int i = 0; i < s20files.Count; i++) { if (i > 0) s20arr.Append(','); s20arr.Append('"').Append(s20files[i].Replace("\\", "\\\\")).Append('"'); }
                                        s20arr.Append(']');
                                        string s20r = _bridge.OpenFiles(s20arr.ToString());
                                        System.IO.File.AppendAllText(dumpPath, "\ns20:" + s20r);
                                    }
                                    catch (Exception ex20) { System.IO.File.AppendAllText(dumpPath, "\ns20EX:" + ex20.Message); }

                                    // s21（V2.4.0.21b）：文件夹加载后再打开单文件——界面状态重置（curFileList 清空/dirMode=false/saveDir=单文件目录）
                                    try
                                    {
                                        string s21dir = System.IO.Path.GetTempPath() + "s8_dir";
                                        await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){var vm=window.__vm;vm.openDir('" + s21dir.Replace("\\", "\\\\") + "');return 'ok';})()");
                                        await System.Threading.Tasks.Task.Delay(2200);
                                        string s21a = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){var vm=window.__vm;return JSON.stringify({dirMode:vm.dirMode,listLen:vm.curFileList.length,saveDir:vm.saveDir});})()");
                                        await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){var vm=window.__vm;vm.openPdf('" + pdf8.Replace("\\", "\\\\") + "');return 'ok';})()");
                                        await System.Threading.Tasks.Task.Delay(1500);
                                        string s21 = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){var vm=window.__vm;return JSON.stringify({dirMode:vm.dirMode,listLen:vm.curFileList.length,curIdx:vm.curIdx,saveDir:vm.saveDir,curFile:vm.curFile,viewMode:vm.viewMode});})()");
                                        System.IO.File.AppendAllText(dumpPath, "\ns21a:" + s21a + "\ns21:" + s21);
                                    }
                                    catch (Exception ex21) { System.IO.File.AppendAllText(dumpPath, "\ns21EX:" + ex21.Message); }

                                    // s22（V2.4.0.24）：布局记忆——折叠状态保存/恢复 + leftPanelWidth 落盘 + SaveWindowState 写盘
                                    try
                                    {
                                        await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){var vm=window.__vm;vm.stampSect.disp=false;return 'ok';})()");
                                        await System.Threading.Tasks.Task.Delay(1000); // deep watcher + SetUiConfig 往返
                                        await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){var vm=window.__vm;vm.leftWidth=618;vm.saveUiConfig();return 'ok';})()");
                                        await System.Threading.Tasks.Task.Delay(1000);
                                        string s22 = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){var vm=window.__vm;return JSON.stringify({disp:vm.stampSect.disp,left:vm.leftWidth});})()");
                                        // 后端 config.ini 落盘核对（foldDisp=0 / leftPanelWidth=618）
                                        string iniPath = PDFQFZ.WPF.Services.AppConfig.IniPath;
                                        string iniText = "";
                                        if (System.IO.File.Exists(iniPath)) iniText = System.IO.File.ReadAllText(iniPath);
                                        string s22ini = "foldDisp=" + PDFQFZ.WPF.Services.AppConfig.FoldDisp
                                            + " leftPanelWidth=" + PDFQFZ.WPF.Services.AppConfig.LeftPanelWidth
                                            + " iniHasFoldDisp=" + iniText.Contains("foldDisp=0")
                                            + " iniHasLeft=" + iniText.Contains("leftPanelWidth=618");
                                        // SaveWindowState 写盘核对（窗口状态记忆）
                                        PDFQFZ.WPF.Services.AppConfig.SaveWindowState(120, 80, 1280, 900);
                                        iniText = System.IO.File.ReadAllText(PDFQFZ.WPF.Services.AppConfig.IniPath);
                                        string s22w = "winW=" + (iniText.Contains("windowWidth=1280") ? 1 : 0)
                                            + " winH=" + (iniText.Contains("windowHeight=900") ? 1 : 0)
                                            + " winL=" + (iniText.Contains("windowLeft=120") ? 1 : 0)
                                            + " winT=" + (iniText.Contains("windowTop=80") ? 1 : 0);
                                        System.IO.File.AppendAllText(dumpPath, "\ns22:" + s22 + "\ns22ini:" + s22ini + "\ns22w:" + s22w);
                                    }
                                    catch (Exception ex22) { System.IO.File.AppendAllText(dumpPath, "\ns22EX:" + ex22.Message); }

                                    // s23（V2.4.0.31）：右栏保底 min-width（V366=676px，工具条655+12余量+6px；原600/670）+ 左栏动态上限
                                    try
                                    {
                                        string s23 = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){var el=document.querySelector('.preview');var cs=getComputedStyle(el);var vm=window.__vm;return JSON.stringify({minW:cs.minWidth,left:vm.leftWidth,iw:window.innerWidth});})()");
                                        System.IO.File.AppendAllText(dumpPath, "\ns23:" + s23);
                                        // s23b（V2.4.0.29）：左栏最小宽度=显示参数行（印章尺寸输入框右缘）+48（动态测量）——先展开印章参数区再核对 leftMinW 与实测
                                        // v2.4.0.76：原"旋转处理"下拉已删除，测量目标改为 .disp-cell 的"印章尺寸"行（与前端 measureLeftMin 一致）
                                        await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;vm.isFullscreen=false;vm.cards.stamp=true;vm.stampSect.disp=true;vm.stampSect.rand=true;return 'ok';})()");
                                        await System.Threading.Tasks.Task.Delay(600);
                                        string s23b = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){var vm=window.__vm;var l=document.querySelector('.left');if(!l)return JSON.stringify({err:'no-left'});var cells=document.querySelectorAll('.left .row .disp-cell');var t=null;for(var i=0;i<cells.length;i++){var lb=cells[i].querySelector('label');if(lb&&lb.textContent==='印章尺寸'){t=cells[i];break;}}if(!t)return JSON.stringify({err:'no-cell'});var lr=l.getBoundingClientRect(),cr=t.getBoundingClientRect();var chain=[];var e=t;while(e&&e!==document.body){var r=e.getBoundingClientRect();var cs=getComputedStyle(e);var nm=(e.tagName||'').toLowerCase()+'.'+String(e.className||'').split(' ').slice(0,2).join('.');chain.push(nm+':'+Math.round(r.width)+'x'+Math.round(r.height)+':'+cs.display);e=e.parentElement;}return JSON.stringify({leftMinW:vm.leftMinW,full:vm.isFullscreen,lw:Math.round(lr.width),disp:vm.stampSect.disp,cardStamp:vm.cards.stamp,cW:Math.round(cr.width),cellRight:Math.round(cr.right),leftLeft:Math.round(lr.left),calc:Math.ceil(cr.right-lr.left+48),chain:chain});})()");
                                        System.IO.File.AppendAllText(dumpPath, "\ns23b:" + s23b);
                                    }
                                    catch (Exception ex23) { System.IO.File.AppendAllText(dumpPath, "\ns23EX:" + ex23.Message); }

                                    // s26（V2.4.0.32）：下方三区布局断言——bottom-area 在滚动条1外、系统日志无折叠/最小163/滚动条2、操作提示最小41、盖章方框56
                                    try
                                    {
                                        string s26 = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){var ls=document.querySelector('.left-scroll'),ba=document.querySelector('.bottom-area'),gc=document.querySelector('.gen-card'),oh=document.querySelector('.op-hint'),lp=document.querySelector('.log-panel'),lh=document.querySelector('.log-head'),ll=document.querySelector('.log-list');if(!ls||!ba||!gc||!oh||!lp||!lh||!ll)return JSON.stringify({err:'missing'});var lsc=getComputedStyle(ls),bac=getComputedStyle(ba),gcc=getComputedStyle(gc),ohc=getComputedStyle(oh),lpc=getComputedStyle(lp),llc=getComputedStyle(ll);var lr=ls.getBoundingClientRect(),br=ba.getBoundingClientRect();return JSON.stringify({lsFlex:lsc.flexGrow+'/'+lsc.flexShrink,lsMinH:lsc.minHeight,lsPadB:lsc.paddingBottom,baDisp:bac.display,genMinH:gcc.minHeight,genBrd:gcc.borderTopWidth,genRad:gcc.borderRadius,ohMinH:ohc.minHeight,lpFlex:lpc.flexGrow+'/'+lpc.flexShrink,lpMinH:lpc.minHeight,llOv:llc.overflowY,logClick:lh.getAttribute('@click')||'',logArr:(lh.querySelector('.arr')?'y':'n'),logShown:ll.offsetParent!==null,baBelow:br.top>=lr.bottom-1,genH:Math.round(gc.getBoundingClientRect().height),opH:Math.round(oh.getBoundingClientRect().height),logH:Math.round(lp.getBoundingClientRect().height),areaH:Math.round(br.height)});})()");
                                        System.IO.File.AppendAllText(dumpPath, "\ns26:" + s26);
                                        // s26b：1-4 全收起后系统日志扩展（flex-grow 吃剩余空间，验证情况二）
                                        await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;vm.cards.file=false;vm.cards.stamp=false;vm.cards.batch=false;vm.cards.output=false;return 'ok';})()");
                                        await System.Threading.Tasks.Task.Delay(400);
                                        string s26b = await webView.CoreWebView2.ExecuteScriptAsync(
                                            "(function(){var lp=document.querySelector('.log-panel'),ba=document.querySelector('.bottom-area');if(!lp||!ba)return JSON.stringify({err:'missing'});return JSON.stringify({logH:Math.round(lp.getBoundingClientRect().height),areaH:Math.round(ba.getBoundingClientRect().height)});})()");
                                        System.IO.File.AppendAllText(dumpPath, "\ns26b:" + s26b);
                                    }
                                    catch (Exception ex26) { System.IO.File.AppendAllText(dumpPath, "\ns26EX:" + ex26.Message); }

                                    // ---- s9 骑缝章类型完整接入（检查点9）----
                                    // 释放 s8k 目录模式渲染器句柄（否则 s8_dir 删除失败跳过本段），并清理残留
                                    _bridge.ResetDirMode();
                                    _bridge.OpenPdf(pdf8);
                                    string s8kDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "s8_dir");
                                    try { if (System.IO.Directory.Exists(s8kDir)) System.IO.Directory.Delete(s8kDir, true); } catch { }
                                    // s9a 前端 openPdf 自动分割数=页数（对齐 UpdateMaxSplitFromPdf）
                                    string s9a = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;if(!vm){return 'no-vm'}vm.openPdf('" + pdf8.Replace("\\", "\\\\") + "');return 'called'})()");
                                    await System.Threading.Tasks.Task.Delay(800);
                                    string s9a2 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;return JSON.stringify({seg:vm.segCount,page:vm.pageCount,seam:vm.seamType,mod:vm._segModified})})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns9a:" + s9a + "|" + s9a2);
                                    // s9b 5 种骑缝章类型各生成（6 页、分割数=6）；PDFQFZ_SHELL_SKIPS9=1 时只跑 qt=0（快速回归，防偶发卡死挡后续断言）
                                    var s9sizes = new System.Collections.Generic.List<string>();
                                    int s9QtMax = Environment.GetEnvironmentVariable("PDFQFZ_SHELL_SKIPS9") == "1" ? 0 : 4;
                                    for (int qt = 0; qt <= s9QtMax; qt++)
                                    {
                                        try
                                        {
                                            string sub = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "s9_out_" + qt);
                                            if (System.IO.Directory.Exists(sub)) System.IO.Directory.Delete(sub, true);
                                            System.IO.Directory.CreateDirectory(sub);
                                            await webView.CoreWebView2.ExecuteScriptAsync("window.__genResult=null;");
                                            _bridge.GenerateFiles(sub, "overlay", 150, "已盖章V", 0, 0, 1, false, "yyyyMMdd", qt, 0, 50, 6, true, 0, 1, 1, 50, 80, true, false);
                                            await WaitGen("s9b_" + qt + "R");
                                            var pdfs = System.IO.Directory.GetFiles(sub, "*.pdf");
                                            s9sizes.Add(qt + ":" + (pdfs.Length > 0 ? new System.IO.FileInfo(pdfs[0]).Length.ToString() : "无"));
                                        }
                                        catch (Exception ex9b) { System.IO.File.AppendAllText(dumpPath, "\ns9b_" + qt + "EX:" + ex9b.Message); }
                                    }
                                    System.IO.File.AppendAllText(dumpPath, "\ns9b:" + string.Join(",", s9sizes));
                                    // s9c 单页 PDF + qfzType=0（引擎 qfzPages>1 跳过骑缝章 → 输出≈仅页面章）；SKIPS9=1 时跳过（快速回归）
                                    if (Environment.GetEnvironmentVariable("PDFQFZ_SHELL_SKIPS9") != "1")
                                    try
                                    {
                                        string singleHtml = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "s9_single.html");
                                        System.IO.File.WriteAllText(singleHtml, "<!DOCTYPE html><html><head><meta charset='utf-8'><style>@page{size:A4;margin:0}body{margin:0;font-family:'Microsoft YaHei'}div{height:297mm;padding:20mm;box-sizing:border-box}span{position:absolute;left:130mm;top:100mm;font-size:14pt}</style></head><body><div>单页文档<br><span>盖章测试 单页</span></div></body></html>", new System.Text.UTF8Encoding(true));
                                        string singlePdf = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "s9_single.pdf");
                                        var psi9 = new System.Diagnostics.ProcessStartInfo(@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe")
                                        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                                        psi9.Arguments = "--headless --disable-gpu --print-to-pdf=\"" + singlePdf + "\" --no-pdf-header-footer \"file:///" + singleHtml.Replace("\\", "/") + "\"";
                                        var ep9 = System.Diagnostics.Process.Start(psi9);
                                        if (ep9 != null && !ep9.WaitForExit(20000)) { try { ep9.Kill(); } catch { } }
                                        System.IO.File.Delete(singleHtml);
                                        string s9c1 = _bridge.OpenPdf(singlePdf);
                                        string outS9 = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "s9_single_out");
                                        if (System.IO.Directory.Exists(outS9)) System.IO.Directory.Delete(outS9, true);
                                        System.IO.Directory.CreateDirectory(outS9);
                                        await webView.CoreWebView2.ExecuteScriptAsync("window.__genResult=null;");
                                        _bridge.GenerateFiles(outS9, "overlay", 150, "已盖章V", 0, 0, 1, false, "yyyyMMdd", 0, 0, 50, 1, true, 0, 1, 1, 50, 80, true, false);
                                        await WaitGen("s9cR");
                                        var s9cFiles = System.IO.Directory.GetFiles(outS9, "*.pdf");
                                        System.IO.File.AppendAllText(dumpPath, "\ns9c:" + s9c1 + "|" + (s9cFiles.Length > 0 ? new System.IO.FileInfo(s9cFiles[0]).Length.ToString() : "无"));
                                    }
                                    catch (Exception ex9c) { System.IO.File.AppendAllText(dumpPath, "\ns9cEX:" + ex9c.Message); }
                                    // s9d 前端 setSeamType 切换提示
                                    string s9d = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;if(!vm){return 'no-vm'}vm.setSeamType('2');return JSON.stringify({seam:vm.seamType,seg:vm.segCount,hint:vm.opHint})})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns9d:" + s9d);

                                                                        // ---- s10 全功能回归：F4 放大拖动平移手势 + H5 帮助文本（检查点10）----
                                    string s10ret = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var w=document.querySelector('.page-wrap');var vm=window.__vm;" +
                                        "if(!w||!vm){window.__gestureRes='no-elem';return 's10-ok';}" +
                                        "window.__gestureRes=null;var out={};" +
                                        "try{vm.viewMode='zoom';" +
                                        "vm.onStageDown({clientX:200,clientY:150,currentTarget:w},1);" +
                                        "window.dispatchEvent(new MouseEvent('mousemove',{clientX:260,clientY:150}));" +
                                        "window.dispatchEvent(new MouseEvent('mouseup',{}));" +
                                        "out.dragX=Math.round(vm.dragOfs.x);out.dragY=Math.round(vm.dragOfs.y);" +
                                        "out.before=document.querySelectorAll('.stamp-ov').length;" +
                                        "vm.onStageDown({clientX:300,clientY:300,currentTarget:w},1);" +
                                        "window.dispatchEvent(new MouseEvent('mouseup',{}));" +
                                        "out.help9=vm.helpText.indexOf('九、常见问题')>0;" +
                                        "out.methods=['onStageDown','onPreviewClick','startDrag'].every(function(m){return typeof vm[m]==='function'});" +
                                        "window.__gestureRes=JSON.stringify(out);" +
                                        "}catch(e){window.__gestureRes='ERR:'+e.message;}" +
                                        "return 's10-ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns10ret:" + s10ret);
                                    string s10g = "";
                                    for (int gi = 0; gi < 12; gi++)
                                    {
                                        await System.Threading.Tasks.Task.Delay(500);
                                        string rr = await webView.CoreWebView2.ExecuteScriptAsync("(function(){return window.__gestureRes?JSON.stringify(window.__gestureRes):''})()");
                                        if (!string.IsNullOrEmpty(rr)) { s10g = rr; break; }
                                    }
                                    System.IO.File.AppendAllText(dumpPath, "\ns10g:" + s10g);
                                    await System.Threading.Tasks.Task.Delay(1500);
                                    string s10h = await webView.CoreWebView2.ExecuteScriptAsync("(function(){return String(document.querySelectorAll('.stamp-ov').length)})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns10h:" + s10h);
                                    // s10rm 右键删除：右键空白不盖章 + 右键章删除（章数-1）
                                    string s10rmRet = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){window.__rmRes=null;" +
                                        "try{" +
                                        "window.Bridge.invoke('GetPageStamps',1).then(function(g0){" +
                                        "var before=(typeof g0==='string'?JSON.parse(g0).length:g0.length);" +
                                        "window.Bridge.invoke('AddManualStamp',0.5,0.5,1).then(function(j){" +
                                        "var r=(typeof j==='string'?JSON.parse(j):j);" +
                                        "window.Bridge.invoke('DeleteStampById',r.id).then(function(del){" +
                                        "window.Bridge.invoke('GetPageStamps',1).then(function(g1){" +
                                        "window.__rmRes={before:before,after:(typeof g1==='string'?JSON.parse(g1).length:g1.length),del:del};" +
                                        "},function(e){window.__rmRes={step:'g1-err',e:String(e)};});" +
                                        "},function(e){window.__rmRes={step:'del-err',e:String(e)};});" +
                                        "},function(e){window.__rmRes={step:'add-err',e:String(e)};});" +
                                        "},function(e){window.__rmRes={step:'g0-err',e:String(e)};});" +
                                        "}catch(e){window.__rmRes={step:'sync-err',e:String(e)};}" +
                                        "return 's10rm-ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns10rmRet:" + s10rmRet);
                                    string s10rm = "";
                                    for (int gi = 0; gi < 12; gi++)
                                    {
                                        await System.Threading.Tasks.Task.Delay(500);
                                        string rr = await webView.CoreWebView2.ExecuteScriptAsync("(function(){return window.__rmRes?window.__rmRes:''})()");
                                        if (!string.IsNullOrEmpty(rr)) { s10rm = rr; break; }
                                    }
                                    System.IO.File.AppendAllText(dumpPath, "\ns10rm:" + s10rm);

                                    // s11b 视图切换（V2.4.0.3）：zoom→125%、single→100%、double→宽度适配；每步独立执行避免异步回调异常逃逸
                                    string s11bRet = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){try{var vm=window.__vm;vm.viewMode='zoom';return 'ok'}catch(e){return 'err:'+e.message}})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns11bRet:" + s11bRet);
                                    await System.Threading.Tasks.Task.Delay(1500);
                                    string s11b1 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){try{var vm=window.__vm;return JSON.stringify({zoomText:vm.zoomText,ofs:vm.dragOfs})}catch(e){return 'err:'+e.message}})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns11b1:" + s11b1);
                                    string s11bRet2 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){try{var vm=window.__vm;vm.viewMode='single';return 'ok'}catch(e){return 'err:'+e.message}})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns11bRet2:" + s11bRet2);
                                    await System.Threading.Tasks.Task.Delay(1500);
                                    string s11b2 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){try{var vm=window.__vm;return JSON.stringify({zoomText:vm.zoomText,ofs:vm.dragOfs})}catch(e){return 'err:'+e.message}})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns11b2:" + s11b2);
                                    string s11bRet3 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){try{var vm=window.__vm;vm.viewMode='double';return 'ok'}catch(e){return 'err:'+e.message}})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns11bRet3:" + s11bRet3);
                                    await System.Threading.Tasks.Task.Delay(1500);
                                    string s11b3 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){try{var vm=window.__vm;return JSON.stringify({imgW2:vm.imgW2,imgW:vm.imgW})}catch(e){return 'err:'+e.message}})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns11b3:" + s11b3);

                                    // s12 图章去白启用按钮 + 窗口 resize 后预览重排（V2.4.0.4）
                                    string s12a = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){try{var vm=window.__vm;vm.removeWhite=false;" +
                                        "vm.removeWhite=true;vm.onWhiteEnable(true);" +
                                        "return JSON.stringify({enabled:vm.removeWhite,sectOpen:vm.stampSect.white,hint:vm.opHint})}catch(e){return 'err:'+e.message}})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns12a:" + s12a);
                                    // 切回单页（100%=适应窗口）后拉大窗口，imgW 应变大
                                    string s12r0 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){try{var vm=window.__vm;vm.viewMode='single';return 'ok'}catch(e){return 'err:'+e.message}})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns12r0:" + s12r0);
                                    await System.Threading.Tasks.Task.Delay(1500);
                                    string s12b0 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){try{var vm=window.__vm;return JSON.stringify({imgW:vm.imgW,zoom:vm.zoomText})}catch(e){return 'err:'+e.message}})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns12b0:" + s12b0);
                                    double _w0 = System.Windows.Application.Current.MainWindow.Width, _h0 = System.Windows.Application.Current.MainWindow.Height;
                                    System.Windows.Application.Current.MainWindow.Width = Math.Max(1200, _w0 + 350);
                                    System.Windows.Application.Current.MainWindow.Height = Math.Max(900, _h0 + 250);
                                    await System.Threading.Tasks.Task.Delay(1800);
                                    string s12b1 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){try{var vm=window.__vm;return JSON.stringify({imgW:vm.imgW,zoom:vm.zoomText})}catch(e){return 'err:'+e.message}})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns12b1:" + s12b1);
                                    System.Windows.Application.Current.MainWindow.Width = _w0;
                                    System.Windows.Application.Current.MainWindow.Height = _h0;

                                    // s13c V2.4.0.6（文件已加载）：⑤ 双页页码 1-2/6；⑥ 双页拖动不拖不盖章；⑦ 放大拖动钳制；⑧ 盖章边缘钳制
                                    double _w1 = System.Windows.Application.Current.MainWindow.Width, _h1 = System.Windows.Application.Current.MainWindow.Height;
                                    System.Windows.Application.Current.MainWindow.Width = 900; System.Windows.Application.Current.MainWindow.Height = 650;
                                    await System.Threading.Tasks.Task.Delay(1500);
                                    string s13c1 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var out={};try{" +
                                        "if(!vm.curSeal && vm.seals&&vm.seals.length){ vm.curSeal=vm.seals[0]; }" +
                                        "vm.setView('double'); out.mode=vm.viewMode; out.pageLabel=vm.pageLabel; out.pageCount=vm.pageCount; out.dragOfsD=JSON.stringify(vm.dragOfs);" +
                                        "var pw=document.querySelector('.preview-page .page-wrap'); out.hasPage=!!pw; var pr=pw.getBoundingClientRect();" +
                                        "window.Bridge.invoke('GetPageStamps',1).then(function(g0){" +
                                        "var before=(typeof g0==='string'?JSON.parse(g0).length:g0.length);" +
                                        "var ev={button:0,clientX:pr.left+50,clientY:pr.top+50,currentTarget:pw,preventDefault:function(){},stopPropagation:function(){}};" +
                                        "vm.onStageDown(ev,1); window.dispatchEvent(new MouseEvent('mousemove',{clientX:pr.left+220,clientY:pr.top+220})); window.dispatchEvent(new MouseEvent('mouseup',{clientX:pr.left+220,clientY:pr.top+220}));" +
                                        "window.Bridge.invoke('GetPageStamps',1).then(function(g1){" +
                                        "out.stampsAfterDrag={before:before,after:(typeof g1==='string'?JSON.parse(g1).length:g1.length)}; out.dragOfsAfterDrag=JSON.stringify(vm.dragOfs);" +
                                        "vm.setView('zoom'); window.__s13c1=JSON.stringify(out);" +
                                        "},function(e){out.err='g1:'+e;window.__s13c1=JSON.stringify(out);});" +
                                        "},function(e){out.err='g0:'+e;window.__s13c1=JSON.stringify(out);});" +
                                        "}catch(e){out.err=String(e);window.__s13c1=JSON.stringify(out);}return 'ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13c1Ret:" + s13c1);
                                    string s13c1V = "";
                                    for (int gi = 0; gi < 10; gi++)
                                    {
                                        await System.Threading.Tasks.Task.Delay(500);
                                        string rr = await webView.CoreWebView2.ExecuteScriptAsync("(function(){return window.__s13c1?window.__s13c1:''})()");
                                        if (!string.IsNullOrEmpty(rr)) { s13c1V = rr; break; }
                                    }
                                    System.IO.File.AppendAllText(dumpPath, "\ns13c1:" + s13c1V);
                                    await System.Threading.Tasks.Task.Delay(1400);
                                    string s13c2 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var out={};try{" +
                                        "var pwz=document.querySelector('.preview-page .page-wrap'); var prz=pwz.getBoundingClientRect();" +
                                        "var evz={button:0,clientX:prz.left+20,clientY:prz.top+20,currentTarget:pwz,preventDefault:function(){},stopPropagation:function(){}};" +
                                        "vm.onStageDown(evz,1); window.dispatchEvent(new MouseEvent('mousemove',{clientX:prz.left-2000,clientY:prz.top-2000})); window.dispatchEvent(new MouseEvent('mouseup',{clientX:prz.left-2000,clientY:prz.top-2000}));" +
                                        "var iw=vm.imgW||0; var stw=(vm.$refs.stage.clientWidth-36); var maxX=Math.max(0,iw-stw); var dof=vm.dragOfs;" +
                                        "out.zoomDrag={imgW:iw,stageW:stw,maxX:maxX,dx:dof.x,dy:dof.y};" +
                                        "vm.setView('single'); window.__s13c2=JSON.stringify(out);" +
                                        "}catch(e){out.err=String(e);window.__s13c2=JSON.stringify(out);}return 'ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13c2Ret:" + s13c2);
                                    string s13c2V = "";
                                    for (int gi = 0; gi < 10; gi++)
                                    {
                                        await System.Threading.Tasks.Task.Delay(500);
                                        string rr = await webView.CoreWebView2.ExecuteScriptAsync("(function(){return window.__s13c2?window.__s13c2:''})()");
                                        if (!string.IsNullOrEmpty(rr)) { s13c2V = rr; break; }
                                    }
                                    System.IO.File.AppendAllText(dumpPath, "\ns13c2:" + s13c2V);
                                    await System.Threading.Tasks.Task.Delay(1000);
                                    string s13c3 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var out={};try{" +
                                        "var pw3=document.querySelector('.preview-page .page-wrap'); var pr3=pw3.getBoundingClientRect();" +
                                        "vm.onPreviewClick({clientX:pr3.left+2,clientY:pr3.top+2,currentTarget:pw3},1);" +
                                        "window.Bridge.invoke('GetPageStamps',1).then(function(g3){" +
                                        "var arr=(typeof g3==='string'?JSON.parse(g3):g3);" +
                                        "out.stampClamp={count:arr.length,first:arr.length?{x:arr[0].x,y:arr[0].y}:null};" +
                                        "window.__s13c3=JSON.stringify(out);" +
                                        "},function(e){out.err='g3:'+e;window.__s13c3=JSON.stringify(out);});" +
                                        "}catch(e){out.err=String(e);window.__s13c3=JSON.stringify(out);}return 'ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13c3Ret:" + s13c3);
                                    string s13c3V = "";
                                    for (int gi = 0; gi < 10; gi++)
                                    {
                                        await System.Threading.Tasks.Task.Delay(500);
                                        string rr = await webView.CoreWebView2.ExecuteScriptAsync("(function(){return window.__s13c3?window.__s13c3:''})()");
                                        if (!string.IsNullOrEmpty(rr)) { s13c3V = rr; break; }
                                    }
                                    System.IO.File.AppendAllText(dumpPath, "\ns13c3:" + s13c3V);
                                    System.Windows.Application.Current.MainWindow.Width = _w1; System.Windows.Application.Current.MainWindow.Height = _h1;

                                    // s13d/s13e/s13f/s13g V2.4.0.7：放大滚轮连续阅读 / 双页垂直居中 / 双页残留125%不拖 / 打开文件不被调试页覆盖
                                    // 全部用"同步执行 + C# Delay + 独立读值"模式（setTimeout 回调异常会逃逸空结果，s11b 踩坑）
                                    double _w2 = System.Windows.Application.Current.MainWindow.Width, _h2 = System.Windows.Application.Current.MainWindow.Height;
                                    System.Windows.Application.Current.MainWindow.Width = 900; System.Windows.Application.Current.MainWindow.Height = 650;
                                    await System.Threading.Tasks.Task.Delay(1200);
                                    // s13d0：先经前端 openPdf 重开 s8（同步前后端，避免 s9c1 C# 直调 1 页文档污染后端渲染器）；
                                    // 再切放大 300%（先切视图等 watch 完成，再设 300% 避免 watch 覆盖；页面超高 maxY>0 才能验证"先滚后翻"）
                                    string pdf8js13d = pdf8.Replace("\\", "\\\\");
                                    string s13d0 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;vm.openPdf('" + pdf8js13d + "');return 'ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13d0Ret:" + s13d0);
                                    await System.Threading.Tasks.Task.Delay(1800);
                                    await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;vm.setView('zoom');return 'ok';})()");
                                    await System.Threading.Tasks.Task.Delay(800);
                                    await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;vm.zoomText='300%';vm.fitPage();return 'ok';})()");
                                    await System.Threading.Tasks.Task.Delay(1600);
                                    string s13d1 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var out={};try{" +
                                        "var st=vm.$refs.stage; out.zoomText=vm.zoomText; out.mode=vm.viewMode; out.imgW=vm.imgW;" +
                                        "st.dispatchEvent(new WheelEvent('wheel',{deltaY:100,bubbles:true,cancelable:true}));" +
                                        "out.yAfterScroll=vm.dragOfs.y; window.__s13d1=JSON.stringify(out);" +
                                        "}catch(e){out.err=String(e);window.__s13d1=JSON.stringify(out);}return 'ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13d1Ret:" + s13d1);
                                    await System.Threading.Tasks.Task.Delay(300);
                                    string s13d1V = await webView.CoreWebView2.ExecuteScriptAsync("(function(){return window.__s13d1?window.__s13d1:''})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13d1:" + s13d1V);
                                    // s13d2a：直接设 dragOfs.y=maxY（已到底）→ dispatch 一次滚轮 → 应翻页（y 回 0，curPage 异步+1）
                                    string s13d2a = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var out={};try{" +
                                        "var iw=vm.imgW||0; var ih=iw*((vm.pageH||vm.pageW||1)/(vm.pageW||1));" +
                                        "var sh=(vm.$refs.stage&&vm.$refs.stage.clientHeight)?(vm.$refs.stage.clientHeight-52):0;" +
                                        "var maxY=Math.max(0,ih-sh); vm.dragOfs={x:0,y:maxY};" +
                                        "out.maxY=maxY; out.pageBefore=Number(vm.curPage);" +
                                        "var st=vm.$refs.stage; st.dispatchEvent(new WheelEvent('wheel',{deltaY:100,bubbles:true,cancelable:true}));" +
                                        "out.yAfterWheel=vm.dragOfs.y; window.__s13d2a=JSON.stringify(out);" +
                                        "}catch(e){out.err=String(e);window.__s13d2a=JSON.stringify(out);}return 'ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13d2aRet:" + s13d2a);
                                    string s13d2aV = await webView.CoreWebView2.ExecuteScriptAsync("(function(){return window.__s13d2a?window.__s13d2a:''})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13d2a:" + s13d2aV);
                                    await System.Threading.Tasks.Task.Delay(3500);
                                    await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var out={};try{" +
                                        "out.pageAfter=vm.curPage; out.yFinal=vm.dragOfs.y;" +
                                        "out.pageChanged=Number(vm.curPage)!==Number(JSON.parse(window.__s13d2a).pageBefore); out.opHint=vm.opHint;" +
                                        "window.__s13d2=JSON.stringify(out);" +
                                        "}catch(e){out.err=String(e);window.__s13d2=JSON.stringify(out);}return 'ok';})()");
                                    string s13d2V = await webView.CoreWebView2.ExecuteScriptAsync("(function(){return window.__s13d2?window.__s13d2:''})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13d2:" + s13d2V);
                                    // s13e：双页视图上下居中（margin 52 auto + 几何居中判定）
                                    await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;vm.setView('double');return 'ok';})()");
                                    await System.Threading.Tasks.Task.Delay(2000);
                                    string s13e = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var out={};try{" +
                                        "var st=vm.$refs.stage; var pp=document.querySelector('.preview-page');" +
                                        "var cs=getComputedStyle(pp); var sr=st.getBoundingClientRect(); var pr=pp.getBoundingClientRect();" +
                                        "out.marginTop=cs.marginTop; out.marginBottom=cs.marginBottom; out.minHeight=cs.minHeight; out.stageH=Math.round(sr.height); out.pageH=Math.round(pr.height);" +
                                        "out.pageTop=Math.round(pr.top-sr.top);" +
                                        // 居中区域 = [70(52让位+18padding), stageH-18]，页面应居中于此
                                        "out.expectedTop=Math.round(70+((sr.height-70-18-pr.height)/2));" +
                                        "window.__s13e=JSON.stringify(out);" +
                                        "}catch(e){out.err=String(e);window.__s13e=JSON.stringify(out);}return 'ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13eRet:" + s13e);
                                    string s13eV = await webView.CoreWebView2.ExecuteScriptAsync("(function(){return window.__s13e?window.__s13e:''})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13e:" + s13eV);
                                    // s13f：从放大 125% 切双页，zoomText 重置 100%，拖动不移动不盖章（zoomText 在 watch 异步后读）
                                    await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;vm.setView('zoom');return 'ok';})()");
                                    await System.Threading.Tasks.Task.Delay(1600);
                                    await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;vm.setView('double');return 'ok';})()");
                                    await System.Threading.Tasks.Task.Delay(1800);
                                    string s13f2 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var out={};try{" +
                                        "out.zoomTextAfterDouble=vm.zoomText;" +
                                        "var pw=document.querySelector('.preview-page .page-wrap'); var pr=pw.getBoundingClientRect();" +
                                        "window.Bridge.invoke('GetPageStamps',Number(vm.curPage)).then(function(g0){" +
                                        "var before=(typeof g0==='string'?JSON.parse(g0).length:g0.length);" +
                                        "var ev={button:0,clientX:pr.left+50,clientY:pr.top+50,currentTarget:pw,preventDefault:function(){},stopPropagation:function(){}};" +
                                        "vm.onStageDown(ev,1); window.dispatchEvent(new MouseEvent('mousemove',{clientX:pr.left+200,clientY:pr.top+200})); window.dispatchEvent(new MouseEvent('mouseup',{clientX:pr.left+200,clientY:pr.top+200}));" +
                                        "window.Bridge.invoke('GetPageStamps',Number(vm.curPage)).then(function(g1){" +
                                        "out.stampsAfterDrag={before:before,after:(typeof g1==='string'?JSON.parse(g1).length:g1.length)}; out.dragOfsAfterDrag=JSON.stringify(vm.dragOfs);" +
                                        "window.__s13f2=JSON.stringify(out);" +
                                        "},function(e){out.err='g1:'+e;window.__s13f2=JSON.stringify(out);});" +
                                        "},function(e){out.err='g0:'+e;window.__s13f2=JSON.stringify(out);});" +
                                        "}catch(e){out.err=String(e);window.__s13f2=JSON.stringify(out);}return 'ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13f2Ret:" + s13f2);
                                    string s13fV = "";
                                    for (int gi = 0; gi < 10; gi++)
                                    {
                                        await System.Threading.Tasks.Task.Delay(500);
                                        string rr = await webView.CoreWebView2.ExecuteScriptAsync("(function(){return window.__s13f2?window.__s13f2:''})()");
                                        if (!string.IsNullOrEmpty(rr)) { s13fV = rr; break; }
                                    }
                                    System.IO.File.AppendAllText(dumpPath, "\ns13f:" + s13fV);
                                    // s13g：打开文件后不被调试页覆盖（_userFileLoaded + 渲染序号守卫）
                                    string pdf8js = pdf8.Replace("\\", "\\\\");
                                    string s13g0 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var out={};try{" +
                                        "window.Bridge.invoke('OpenPdf','" + pdf8js + "').then(function(json){" +
                                        "var r=JSON.parse(json); out.openOk=r.ok; out.userFileLoaded=vm._userFileLoaded; out.debugActive=vm.debugActive; out.curFile=vm.curFile;" +
                                        "vm.renderPage(1); vm.renderPage(3);" +
                                        "window.__s13g0=JSON.stringify(out);" +
                                        "},function(e){out.err='open:'+e;window.__s13g0=JSON.stringify(out);});" +
                                        "}catch(e){out.err=String(e);window.__s13g0=JSON.stringify(out);}return 'ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13g0Ret:" + s13g0);
                                    string s13g0V = "";
                                    for (int gi = 0; gi < 10; gi++)
                                    {
                                        await System.Threading.Tasks.Task.Delay(500);
                                        string rr = await webView.CoreWebView2.ExecuteScriptAsync("(function(){return window.__s13g0?window.__s13g0:''})()");
                                        if (!string.IsNullOrEmpty(rr)) { s13g0V = rr; break; }
                                    }
                                    System.IO.File.AppendAllText(dumpPath, "\ns13g0:" + s13g0V);
                                    await System.Threading.Tasks.Task.Delay(2200);
                                    string s13g1 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var out={};try{" +
                                        "out.pageAfterRapid=vm.curPage; out.debugActiveFinal=vm.debugActive; out.userFileFinal=vm._userFileLoaded; out.curFileFinal=vm.curFile;" +
                                        "window.__s13g=JSON.stringify(out);" +
                                        "}catch(e){out.err=String(e);window.__s13g=JSON.stringify(out);}return 'ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13g1Ret:" + s13g1);
                                    string s13gV = await webView.CoreWebView2.ExecuteScriptAsync("(function(){return window.__s13g?window.__s13g:''})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13g:" + s13gV);
                                    // s13h V2.4.0.8：① 放大滚轮方向（内容向上滚，transform 负向）；② 放大 resize 页面大小不变；
                                    // ③ 放大缩放 100 自动切单页；④ 预览页背景透明（上下露出灰色舞台）；⑤ OpenDebugPage 不替换用户文件渲染器
                                    string s13h0a = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;vm.setView('single');return 'ok';})()");
                                    await System.Threading.Tasks.Task.Delay(900);
                                    await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;vm.setView('zoom');return 'ok';})()");
                                    await System.Threading.Tasks.Task.Delay(800);
                                    await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;vm.zoomText='300%';vm.fitPage();return 'ok';})()");
                                    await System.Threading.Tasks.Task.Delay(1600);
                                    string s13h0 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var out={};try{" +
                                        "var st=vm.$refs.stage; out.yBefore=vm.dragOfs.y;" +
                                        "st.dispatchEvent(new WheelEvent('wheel',{deltaY:100,bubbles:true,cancelable:true}));" +
                                        "out.yAfter=vm.dragOfs.y;" +
                                        "window.__s13h0=JSON.stringify(out);" +
                                        "}catch(e){out.err=String(e);window.__s13h0=JSON.stringify(out);}return 'ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13h0Ret:" + s13h0);
                                    string s13h0V = await webView.CoreWebView2.ExecuteScriptAsync("(function(){return window.__s13h0?window.__s13h0:''})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13h0:" + s13h0V);
                                    // Vue nextTick 异步更新 DOM，等一拍再读 transform 验证负方向（滚轮向下→内容向上→matrix f<0）
                                    await System.Threading.Tasks.Task.Delay(900);
                                    string s13h0b = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var pw=document.querySelector('.preview-page .page-wrap');" +
                                        "return JSON.stringify({transform:pw?getComputedStyle(pw).transform:'no-page',y:window.__vm?window.__vm.dragOfs.y:null})})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13h0b:" + s13h0b);
                                    // s13h1：放大 300% 下窗口拉大 → imgW 不变
                                    string s13h1a = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;return JSON.stringify({imgW:vm.imgW,zoom:vm.zoomText})})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13h1a:" + s13h1a);
                                    double _w3 = System.Windows.Application.Current.MainWindow.Width, _h3 = System.Windows.Application.Current.MainWindow.Height;
                                    System.Windows.Application.Current.MainWindow.Width = _w3 + 300;
                                    System.Windows.Application.Current.MainWindow.Height = _h3 + 200;
                                    await System.Threading.Tasks.Task.Delay(1600);
                                    string s13h1 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;return JSON.stringify({imgW:vm.imgW,zoom:vm.zoomText})})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13h1:" + s13h1);
                                    System.Windows.Application.Current.MainWindow.Width = _w3; System.Windows.Application.Current.MainWindow.Height = _h3;
                                    // s13h2：放大 300% 缩放 100 → 自动切单页（对齐 WPF SyncViewModeAfterZoom）
                                    string s13h2a = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;vm.zoomText='100%';return 'ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13h2aRet:" + s13h2a);
                                    await System.Threading.Tasks.Task.Delay(1400);
                                    string s13h2 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;return JSON.stringify({mode:vm.viewMode,zoom:vm.zoomText})})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13h2:" + s13h2);
                                    // s13h3：单页下 preview-page 背景透明（上下露出灰色舞台）
                                    string s13h3 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var out={};try{" +
                                        "vm.setView('single');" +
                                        "var pp=document.querySelector('.preview-page'); out.bg=pp?getComputedStyle(pp).backgroundColor:'no-page';" +
                                        "window.__s13h3=JSON.stringify(out);" +
                                        "}catch(e){out.err=String(e);window.__s13h3=JSON.stringify(out);}return 'ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13h3Ret:" + s13h3);
                                    await System.Threading.Tasks.Task.Delay(900);
                                    string s13h3V = await webView.CoreWebView2.ExecuteScriptAsync("(function(){return window.__s13h3?window.__s13h3:''})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13h3:" + s13h3V);
                                    // s13h4：用户文件已打开后 OpenDebugPage 不得替换渲染器（返回 isDebug:false）
                                    string s13h4 = _bridge.OpenDebugPage();
                                    System.IO.File.AppendAllText(dumpPath, "\ns13h4:" + s13h4);
                                    string s13h4b = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;vm.renderPage(1);return 'ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13h4bRet:" + s13h4b);
                                    await System.Threading.Tasks.Task.Delay(1800);
                                    string s13h4c = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;return JSON.stringify({curPage:vm.curPage,pageCount:vm.pageCount,debugActive:vm.debugActive,userFile:vm._userFileLoaded,hint:vm.opHint})})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13h4c:" + s13h4c);
                                    // s14pre（V2.4.0.12 验证）：记录启动调试页时 img.src（修复后应带 ?v= 参数）
                                    string s14pre = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var i=document.querySelector('.preview-stage .page-img');var vm=window.__vm;return JSON.stringify({imgSrc:i?i.src:'',curPageUrl:vm.curPageUrl,imgSeq:vm._imgSeq,curFile:vm.curFile,debugActive:vm.debugActive})})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns14pre:" + s14pre);
                                    // s13i V2.4.0.9：① OpenPdfFromBytes 拖入通道替换预览区（对齐用户方案：调试页=普通内部PDF，放新文件即替换）；
                                    // ② 单页视图放大>100 自动进放大视图且保留输入值；③ 放大视图进入后左右居中；④ 拖右钳到 0（抓纸手感）
                                    string s8b64 = Convert.ToBase64String(System.IO.File.ReadAllBytes(pdf8));
                                    string s13i1 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var out={};try{" +
                                        "window.Bridge.invoke('OpenPdfFromBytes','" + s8b64 + "').then(function(json){" +
                                        "var r=JSON.parse(json); out.openOk=r.ok; out.userFile=vm._userFileLoaded; out.debugActive=vm.debugActive; out.curFile=vm.curFile; out.pageCount=vm.pageCount;" +
                                        "vm.renderPage(1);" +
                                        "window.__s13i1=JSON.stringify(out);" +
                                        "},function(e){out.err='open:'+e;window.__s13i1=JSON.stringify(out);});" +
                                        "}catch(e){out.err=String(e);window.__s13i1=JSON.stringify(out);}return 'ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13i1Ret:" + s13i1);
                                    string s13i1V = "";
                                    for (int gi = 0; gi < 10; gi++)
                                    {
                                        await System.Threading.Tasks.Task.Delay(500);
                                        string rr = await webView.CoreWebView2.ExecuteScriptAsync("(function(){return window.__s13i1?window.__s13i1:''})()");
                                        if (!string.IsNullOrEmpty(rr)) { s13i1V = rr; break; }
                                    }
                                    System.IO.File.AppendAllText(dumpPath, "\ns13i1:" + s13i1V);
                                    await System.Threading.Tasks.Task.Delay(1800);
                                    string s13i1b = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;return JSON.stringify({curPage:vm.curPage,pageCount:vm.pageCount,debugActive:vm.debugActive,hint:vm.opHint})})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13i1b:" + s13i1b);
                                    // s13i2：单页视图输入 150% → 自动进放大视图且保留 150%
                                    await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;vm.setView('single');return 'ok';})()");
                                    await System.Threading.Tasks.Task.Delay(1000);
                                    await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;vm.zoomText='150%';return 'ok';})()");
                                    await System.Threading.Tasks.Task.Delay(1400);
                                    string s13i2 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;return JSON.stringify({mode:vm.viewMode,zoom:vm.zoomText,dragOfs:vm.dragOfs})})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13i2:" + s13i2);
                                    // s13i3：放大视图进入后左右居中（preview-page 中心 ≈ stage 中心）
                                    await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;vm.setView('zoom');return 'ok';})()");
                                    await System.Threading.Tasks.Task.Delay(1200);
                                    string s13i3 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var out={};try{" +
                                        "var pp=document.querySelector('.preview-page'); var st=vm.$refs.stage;" +
                                        "var pr=pp.getBoundingClientRect(); var sr=st.getBoundingClientRect();" +
                                        "out.pageCenter=pr.left+pr.width/2; out.stageCenter=sr.left+sr.width/2; out.diff=Math.round((pr.left+pr.width/2)-(sr.left+sr.width/2));" +
                                        "out.margin=pp?getComputedStyle(pp).margin:'-';" +
                                        "window.__s13i3=JSON.stringify(out);" +
                                        "}catch(e){out.err=String(e);window.__s13i3=JSON.stringify(out);}return 'ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13i3Ret:" + s13i3);
                                    string s13i3V = await webView.CoreWebView2.ExecuteScriptAsync("(function(){return window.__s13i3?window.__s13i3:''})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13i3:" + s13i3V);
                                    // s13i4：放大视图拖右（抓纸）钳到 0
                                    string s13i4 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var out={};try{" +
                                        "var pw4=document.querySelector('.preview-page .page-wrap'); var pr4=pw4.getBoundingClientRect();" +
                                        "var ev4={button:0,clientX:pr4.left+20,clientY:pr4.top+20,currentTarget:pw4,preventDefault:function(){},stopPropagation:function(){}};" +
                                        "vm.dragOfs={x:40,y:40};" +
                                        "vm.onStageDown(ev4,1); window.dispatchEvent(new MouseEvent('mousemove',{clientX:pr4.left+300,clientY:pr4.top+300})); window.dispatchEvent(new MouseEvent('mouseup',{clientX:pr4.left+300,clientY:pr4.top+300}));" +
                                        "out.dragRight={x:vm.dragOfs.x,y:vm.dragOfs.y};" +
                                        "window.__s13i4=JSON.stringify(out);" +
                                        "}catch(e){out.err=String(e);window.__s13i4=JSON.stringify(out);}return 'ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13i4Ret:" + s13i4);
                                    string s13i4V = await webView.CoreWebView2.ExecuteScriptAsync("(function(){return window.__s13i4?window.__s13i4:''})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13i4:" + s13i4V);
                                    // s13i5：模拟真实拖放事件（构造 DataTransfer+File 走完整 onDropPdf 链：arrayBuffer→btoa→OpenPdfFromBytes→覆盖）
                                    // B 方案：真实文件取 pdf8（环境变量/桌面自动发现），不再依赖固定 29 页测试文件
                                    string realPdf = pdf8;
                                    string realB64 = Convert.ToBase64String(System.IO.File.ReadAllBytes(realPdf));
                                    string s13i5 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var out={};try{" +
                                        "var st=vm.$refs.stage;" +
                                        "var bin=atob('" + realB64 + "'); var u8=new Uint8Array(bin.length); for(var i=0;i<bin.length;i++){u8[i]=bin.charCodeAt(i);}" +
                                        "var f=new File([u8],'" + pdf8Name + "',{type:'application/pdf'});" +
                                        "var dt=new DataTransfer(); dt.items.add(f);" +
                                        "var ev=new DragEvent('drop',{dataTransfer:dt,bubbles:true,cancelable:true});" +
                                        "st.dispatchEvent(ev);" +
                                        "out.dispatched=true;" +
                                        "window.__s13i5=JSON.stringify(out);" +
                                        "}catch(e){out.err=String(e);window.__s13i5=JSON.stringify(out);}return 'ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13i5Ret:" + s13i5);
                                    await System.Threading.Tasks.Task.Delay(2500);
                                    string s13i5b = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;return JSON.stringify({curFile:vm.curFile,pageCount:vm.pageCount,debugActive:vm.debugActive,userFile:vm._userFileLoaded,curPage:vm.curPage,hint:vm.opHint})})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13i5b:" + s13i5b);
                                    // s13i6：选择文件通道全链（前端 invoke('OpenPdf', 真实文件路径) → RenderPage → img 真实加载验证）
                                    string s13i6 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "window.Bridge.invoke('OpenPdf','" + realPdf.Replace("\\", "\\\\") + "').then(function(json){var vm=window.__vm;var r=JSON.parse(json);vm._userFileLoaded=true;vm.pageCount=r.pageCount;vm.curFile='" + pdf8Name + "';vm.pdfLoaded=true;vm.debugActive=false;vm.renderPage(1);return 'invoked:'+json;})");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13i6:" + s13i6);
                                    await System.Threading.Tasks.Task.Delay(2500);
                                    string s13i6b = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var i=document.querySelector('.preview-stage .page-img');var rect=i?i.getBoundingClientRect():null;return JSON.stringify({curFile:vm.curFile,pageCount:vm.pageCount,debugActive:vm.debugActive,curPage:vm.curPage,url:vm.curPageUrl,imgFound:!!i,imgComplete:i?i.complete:false,naturalW:i?i.naturalWidth:0,imgW:vm.imgW,rectW:rect?Math.round(rect.width):0,rectH:rect?Math.round(rect.height):0})})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13i6b:" + s13i6b);
                                    // s13i7：验证 dragover 监听真实生效（带 dataTransfer 模拟真实拖放）
                                    // s13i7a 测 #app 根（@dragover.prevent 绑定）；s13i7b 测 stage（预览区，真实拖放落点）
                                    string s13i7a = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var out={};try{" +
                                        "vm.dragOver=false;var el=document.getElementById('app');" +
                                        "var dt=new DataTransfer();" +
                                        "var ev=new DragEvent('dragover',{dataTransfer:dt,bubbles:true,cancelable:true});" +
                                        "el.dispatchEvent(ev);" +
                                        "out.appDragOver=vm.dragOver;out.appPrevented=ev.defaultPrevented;" +
                                        "vm.dragOver=false;var st=vm.$refs.stage;" +
                                        "var ev2=new DragEvent('dragover',{dataTransfer:dt,bubbles:true,cancelable:true});" +
                                        "st.dispatchEvent(ev2);" +
                                        "out.stageDragOver=vm.dragOver;out.stagePrevented=ev2.defaultPrevented;" +
                                        "window.__s13i7=JSON.stringify(out);" +
                                        "}catch(e){out.err=String(e);window.__s13i7=JSON.stringify(out);}return 'ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13i7Ret:" + s13i7a);
                                    string s13i7v = await webView.CoreWebView2.ExecuteScriptAsync("(function(){return window.__s13i7?window.__s13i7:''})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13i7:" + s13i7v);
                                    // s13i8：真实文件（桌面 投标文件测试.pdf）走选择文件通道（OpenPdf 路径）→ 渲染 → img 真实加载
                                    string s13i8 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "window.Bridge.invoke('OpenPdf','" + realPdf.Replace("\\", "\\\\") + "').then(function(json){var vm=window.__vm;var r=JSON.parse(json);vm._userFileLoaded=true;vm.pageCount=r.pageCount;vm.curFile='" + pdf8Name + "';vm.pdfLoaded=true;vm.debugActive=false;vm.renderPage(1);return 'invoked:'+json;})");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13i8:" + s13i8);
                                    await System.Threading.Tasks.Task.Delay(3000);
                                    string s13i8b = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var i=document.querySelector('.preview-stage .page-img');return JSON.stringify({curFile:vm.curFile,pageCount:vm.pageCount,debugActive:vm.debugActive,curPage:vm.curPage,url:vm.curPageUrl,imgFound:!!i,imgComplete:i?i.complete:false,naturalW:i?i.naturalWidth:0})})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13i8b:" + s13i8b);
                                    // s13diag：诊断 document 级拖放监听是否注册生效
                                    string s13diag = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var out={};try{" +
                                        "out.bindFn=typeof vm._bindDocDrag;" +
                                        "out.docDragBound=vm._docDragBound;" +
                                        "out.hasDocDrop=(typeof vm._docDrop==='function');" +
                                        "out.hasDocOver=(typeof vm._docDragOver==='function');" +
                                        "out.dropBusy=vm._dropBusy;" +
                                        "var dt=new DataTransfer();" +
                                        "var ev=new DragEvent('drop',{dataTransfer:dt,bubbles:true,cancelable:true});" +
                                        "var fired=false; var t=function(){fired=true;};" +
                                        "document.addEventListener('drop',t); document.dispatchEvent(ev); document.removeEventListener('drop',t);" +
                                        "out.docDispatchFired=fired;" +
                                        "vm.dragOver=false; var ev2=new DragEvent('dragover',{dataTransfer:dt,bubbles:true,cancelable:true});" +
                                        "var t2=function(){fired=true;}; document.addEventListener('dragover',t2); document.dispatchEvent(ev2); document.removeEventListener('dragover',t2);" +
                                        "out.docOverFired=fired; out.dragOverAfter=vm.dragOver;" +
                                        "window.__s13diag=JSON.stringify(out);" +
                                        "}catch(e){out.err=String(e);window.__s13diag=JSON.stringify(out);}return 'ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13diagRet:" + (s13diag != null ? s13diag : "null"));
                                    string s13diagv = await webView.CoreWebView2.ExecuteScriptAsync("(function(){return window.__s13diag?window.__s13diag:''})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13diag:" + s13diagv);
                                    // s13r：真实文件（桌面 投标文件测试.pdf）模拟拖放（DataTransfer+File 真实字节 → onDropPdf 完整链 → 覆盖）
                                    string s13r = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var out={};try{" +
                                        "var st=vm.$refs.stage;" +
                                        "var bin=atob('" + realB64 + "'); var u8=new Uint8Array(bin.length); for(var i=0;i<bin.length;i++){u8[i]=bin.charCodeAt(i);}" +
                                        "var f=new File([u8],'" + pdf8Name + "',{type:'application/pdf'});" +
                                        "var dt=new DataTransfer(); dt.items.add(f);" +
                                        "var ev=new DragEvent('drop',{dataTransfer:dt,bubbles:true,cancelable:true});" +
                                        "st.dispatchEvent(ev);" +
                                        "out.dispatched=true;" +
                                        "window.__s13r=JSON.stringify(out);" +
                                        "}catch(e){out.err=String(e);window.__s13r=JSON.stringify(out);}return 'ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13rRet:" + (s13r != null ? s13r.Substring(0, Math.Min(60, s13r.Length)) : "null"));
                                    await System.Threading.Tasks.Task.Delay(3500);
                                    string s13rb = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var i=document.querySelector('.preview-stage .page-img');return JSON.stringify({curFile:vm.curFile,pageCount:vm.pageCount,debugActive:vm.debugActive,userFile:vm._userFileLoaded,curPage:vm.curPage,url:vm.curPageUrl,imgFound:!!i,imgComplete:i?i.complete:false,naturalW:i?i.naturalWidth:0,hint:vm.opHint})})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns13rb:" + s13rb);
                                    // s14：验证「导入后第一页显示调试页」根因假设——https://cache/page_0.png 是否被 WebView2 HTTP 缓存
                                    // s14a：磁盘上 page_0.png 的实际 SHA1（当前状态=用户PDF渲染后，应已是用户PDF第一页）
                                    // s14b：前端 fetch 同一 URL 拿到的内容 SHA1——若命中了 HTTP 缓存，会是旧（调试页）内容
                                    string s14cache = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "Temp") ;
                                    string s14dir = "";
                                    try { s14dir = System.IO.Directory.GetDirectories(System.IO.Path.GetTempPath(), "pdfqfz_web_preview_*").OrderByDescending(d => new System.IO.DirectoryInfo(d).LastWriteTime).FirstOrDefault() ?? ""; } catch { }
                                    string s14a = "";
                                    try { string pf = System.IO.Path.Combine(s14dir, "page_0.png"); if (System.IO.File.Exists(pf)) s14a = BitConverter.ToString(System.Security.Cryptography.SHA1.Create().ComputeHash(System.IO.File.ReadAllBytes(pf))).Replace("-", ""); } catch { }
                                    System.IO.File.AppendAllText(dumpPath, "\ns14a_disk:" + s14a);
                                    string s14b = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "fetch('https://cache/page_0.png').then(function(r){return r.arrayBuffer();}).then(function(b){return crypto.subtle.digest('SHA-1',b);}).then(function(h){var a=new Uint8Array(h);var s='';for(var i=0;i<a.length;i++){s+=a[i].toString(16).padStart(2,'0');}window.__s14b=s;return s;}).catch(function(e){window.__s14b='ERR:'+e.message;return window.__s14b;})");
                                    await System.Threading.Tasks.Task.Delay(1500);
                                    string s14bv = await webView.CoreWebView2.ExecuteScriptAsync("(function(){return window.__s14b?window.__s14b:''})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns14b_fetch:" + s14bv);
                                    string s14bNorm = (s14bv ?? "").Trim().Trim('"').ToUpperInvariant();
                                    System.IO.File.AppendAllText(dumpPath, "\ns14match:" + (s14a.Length > 0 && s14bNorm == s14a ? "MATCH(后端与fetch一致,无缓存问题)" : "DIFF(内容不一致,需排查)"));
                                    // s14c（V2.4.0.12 关键验证）：再次 renderPage(1) → img.src 应带递增 ?v= 参数
                                    // （证明每次渲染 URL 唯一 → Vue 必更新 :src → 浏览器必重新加载最新渲染图）
                                    string s14c = await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;vm.renderPage(1);return 'ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns14cRet:" + s14c);
                                    await System.Threading.Tasks.Task.Delay(2200);
                                    string s14cv = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var i=document.querySelector('.preview-stage .page-img');var vm=window.__vm;return JSON.stringify({imgSrc:i?i.src:'',curPageUrl:vm.curPageUrl,imgSeq:vm._imgSeq,curFile:vm.curFile,pageCount:vm.pageCount,debugActive:vm.debugActive,imgComplete:i?i.complete:false})})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns14c:" + s14cv);
                                    // s15（V2.4.0.13 三项修复验证）
                                    // s15a：范围盖章确认后跳转结束页并渲染（applyRange 调 renderPage(t)）
                                    string s15a = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;vm.rangeFromN=3;vm.rangeToN=vm.pageCount;vm.applyRange();return 'ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns15aRet:" + s15a);
                                    await System.Threading.Tasks.Task.Delay(2200);
                                    string s15av = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var i=document.querySelector('.preview-stage .page-img');return JSON.stringify({curPage:vm.curPage,rangeActive:vm.rangeActive,from:vm.rangeFromN,to:vm.rangeToN,url:vm.curPageUrl,imgFound:!!i,imgComplete:i?i.complete:false,imgSeq:vm._imgSeq})})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns15a:" + s15av);
                                    // s15b：双页视图点全屏 → nextTick 后 fitPage 生效（imgW2 随预览区变宽增大）
                                    string s15b0 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;vm.setView('double');return JSON.stringify({view:vm.viewMode,debugActive:vm.debugActive});})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns15b0:" + s15b0);
                                    await System.Threading.Tasks.Task.Delay(1800);
                                    string s15b1 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var before=vm.imgW2;vm.toggleFullscreen();return JSON.stringify({before:before,isFullscreen:vm.isFullscreen});})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns15b1:" + s15b1);
                                    await System.Threading.Tasks.Task.Delay(900);
                                    string s15b2 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;return JSON.stringify({after:vm.imgW2,isFullscreen:vm.isFullscreen,imgFound:!!document.querySelector('.preview-stage .page-img')});})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns15b2:" + s15b2);
                                    // s15c：保存目录/印章文件选择按钮文字显示全（无「选择…」省略号）；V2.4.0.17：源PDF按钮改到第二行 .src-btn-row，两处一起查
                                    string s15c = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var btns=Array.prototype.slice.call(document.querySelectorAll('.fake-file-row .el-button,.src-btn-row .el-button'));var ts=btns.map(function(b){return b.textContent.trim();});return JSON.stringify({btns:ts,hasEllipsis:ts.some(function(t){return t.indexOf('…')>=0;})});})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns15c:" + s15c);
                                    // s16（V2.4.0.14 按文字盖章区修复验证）
                                    // s16a：需要盖章的文字为纯输入框（无下拉、无 el-select）
                                    string s16a = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var labels=document.querySelectorAll('.card label');var row=null;for(var i=0;i<labels.length;i++){if(labels[i].textContent.trim()==='需要盖章的文字'){row=labels[i].closest('.row');break;}}var inp=row?row.querySelector('input'):null;var sel=row?row.querySelector('.el-select'):null;return JSON.stringify({found:!!row,inputFound:!!inp,hasElSelect:!!sel,inputClass:inp?(inp.className||''):''});})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns16a:" + s16a);
                                    // s16b：输入的文字留住（失焦后不清空）
                                    string s16b = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var labels=document.querySelectorAll('.card label');var inp=null;for(var i=0;i<labels.length;i++){if(labels[i].textContent.trim()==='需要盖章的文字'){inp=labels[i].closest('.row').querySelector('input');break;}}vm.searchText='测试盖章文字';if(inp){inp.value='测试盖章文字';inp.dispatchEvent(new Event('input',{bubbles:true}));}return JSON.stringify({searchText:vm.searchText,inpVal:inp?inp.value:''});})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns16b:" + s16b);
                                    // s16c：匹配模式为选项卡（seg-item），点「任一关键词」切换 matchMode='any'
                                    string s16c = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var rows=document.querySelectorAll('.card .row');var found=null;for(var i=0;i<rows.length;i++){var lab=rows[i].querySelector('label');if(lab&&lab.textContent.trim()==='匹配模式'){found=rows[i];break;}}var segs=[];if(found){var items=found.querySelectorAll('.seg-item');for(var j=0;j<items.length;j++){segs.push(items[j].textContent.trim());}if(items.length>=2){items[1].click();}}return JSON.stringify({segs:segs,matchMode:vm.matchMode,hasSelect:found?!!found.querySelector('.el-select'):false});})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns16c:" + s16c);
                                    // s16d：附近关键词输入框左对齐 class
                                    string s16d = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var kw=document.querySelector('.kw-input input');return JSON.stringify({kwFound:!!kw});})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns16d:" + s16d);
                                    // s17（V2.4.0.15 八项修复验证）
                                    // s17a：指定范围页盖章按钮 type=primary（蓝底白字）
                                    string s17a = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var btns=Array.prototype.slice.call(document.querySelectorAll('.el-button'));var r=null;for(var i=0;i<btns.length;i++){if(btns[i].textContent.trim()==='指定范围页盖章'){r=btns[i];break;}}return JSON.stringify({found:!!r,isPrimary:r?(r.className.indexOf('el-button--primary')>=0):false});})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns17a:" + s17a);
                                    // s17b：放大视图点击盖章（无拖动=点击 → onPreviewClick；原实现 dragEnabled 时直接 return 点不了章）
                                    string s17b = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;if(vm.isFullscreen){vm.toggleFullscreen();}vm.setView('zoom');var el=document.querySelector('.page-wrap');var before=vm.pageStamps.length;var rect=el.getBoundingClientRect();var cx=rect.left+rect.width/2,cy=rect.top+rect.height/2;el.dispatchEvent(new MouseEvent('mousedown',{clientX:cx,clientY:cy,bubbles:true,cancelable:true,button:0}));window.dispatchEvent(new MouseEvent('mouseup',{clientX:cx,clientY:cy,bubbles:true,cancelable:true,button:0}));return JSON.stringify({view:vm.viewMode,before:before});})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns17b:" + s17b);
                                    await System.Threading.Tasks.Task.Delay(1800);
                                    string s17b2 = await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;return JSON.stringify({after:vm.pageStamps.length,view:vm.viewMode});})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns17b2:" + s17b2);
                                    // s17c：seamType 当前值（默认已改 '1'；config 覆盖时如实输出）
                                    string s17c = await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;return JSON.stringify({seamType:vm.seamType});})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns17c:" + s17c);
                                    // s17d：拖入真实文件（模拟 WebView2 File.path）→ 保存目录自动填充
                                    string s17d = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var st=vm.$refs.stage;var bin=atob('" + realB64 + "');var u8=new Uint8Array(bin.length);for(var i=0;i<bin.length;i++){u8[i]=bin.charCodeAt(i);}var f=new File([u8],'" + pdf8Name + "',{type:'application/pdf'});try{f.path='" + pdf8.Replace("\\", "/") + "';}catch(e){}var dt=new DataTransfer();dt.items.add(f);st.dispatchEvent(new DragEvent('drop',{dataTransfer:dt,bubbles:true,cancelable:true}));return 'ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns17dRet:" + s17d);
                                    await System.Threading.Tasks.Task.Delay(3500);
                                    string s17d2 = await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;return JSON.stringify({saveDir:vm.saveDir,curFile:vm.curFile});})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns17d2:" + s17d2);
                                    // s17d3：壳层 file-dropped 通道（openPdf 有真实路径）→ 保存目录自动填充为源文件目录
                                    string s17d3 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;vm.openPdf('" + pdf8.Replace("\\", "\\\\") + "');return 'ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns17d3Ret:" + s17d3);
                                    await System.Threading.Tasks.Task.Delay(3500);
                                    string s17d3b = await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;return JSON.stringify({saveDir:vm.saveDir,curFile:vm.curFile,dirLocked:vm.dirLocked});})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns17d3b:" + s17d3b);
                                    // s17e：后端拖入临时目录内文件名应为原始文件名（投标文件测试.pdf）
                                    string s17e = "";
                                    try {
                                        var s17dir = System.IO.Directory.GetDirectories(System.IO.Path.GetTempPath(), "pdfqfz_drop_*").OrderByDescending(d => new System.IO.DirectoryInfo(d).LastWriteTime).FirstOrDefault() ?? "";
                                        var s17files = System.IO.Directory.Exists(s17dir) ? string.Join(",", System.IO.Directory.GetFiles(s17dir).Select(System.IO.Path.GetFileName)) : "";
                                        s17e = System.IO.Path.GetFileName(s17dir) + "|" + s17files;
                                    } catch (Exception ex) { s17e = "ERR:" + ex.Message; }
                                    System.IO.File.AppendAllText(dumpPath, "\ns17e:" + s17e);
                                    // s17f：无章生成 → needConfirm 提示（ExecuteScriptAsync 不等待 Promise，改存全局后独立读）
                                    string s17f0 = await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;var p=vm.pageStamps.slice();p.forEach(function(s){vm.removeStamp(s.id);});return 'ok:'+p.length;})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns17f0:" + s17f0);
                                    await System.Threading.Tasks.Task.Delay(900);
                                    string s17f1 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;window.Bridge.invoke('GenerateFiles',vm.saveDir||'',vm.outputMode,parseInt(vm.dpi)||150,vm.nameMark,vm.namePos==='before'?1:0,vm.seqType==='upper'?1:(vm.seqType==='lower'?2:0),parseInt(vm.seqPad)||1,!!vm.useTs,vm.fmtToBack(vm.tsFormat),parseInt(vm.seamType)||0,['上','下','左','右'].indexOf(vm.sealPos),parseInt(vm.posVal)||50,Math.max(1,parseInt(vm.segCount)||20),false) // V368：文件夹不锁（可手动） // V367：手动0过渡值按1.then(function(j){window.__s17f=j;},function(e){window.__s17f='ERR:'+e.message;});return 'start';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns17f1:" + s17f1);
                                    await System.Threading.Tasks.Task.Delay(2000);
                                    string s17f = await webView.CoreWebView2.ExecuteScriptAsync("(function(){return window.__s17f?window.__s17f:'(empty)';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns17f:" + s17f);
                                    // s17g：AutoStamp 无随机偏移（存全局读结果；有匹配时检查章 offsetX/offsetY 全 0）
                                    string s17g0 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;window.Bridge.invoke('AutoStamp','项目','',10,true,false,false,0,0).then(function(j){var r={};try{r=JSON.parse(j);}catch(e){window.__s17g='PARSE-ERR:'+j;return;}if(r&&r.ok){vm.refreshPageStamps();}window.__s17g=JSON.stringify({ok:r.ok,total:r.total||0,count:r.count||0,error:r.error||''});},function(e){window.__s17g='ERR:'+e.message;});return 'start';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns17g0:" + s17g0);
                                    await System.Threading.Tasks.Task.Delay(2200);
                                    string s17g = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var offs=vm.pageStamps.map(function(s){return s.offsetX+','+s.offsetY;});return JSON.stringify({stampCount:vm.pageStamps.length,offsets:offs.slice(0,10),autoResult:window.__s17g||'(empty)'});})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns17g:" + s17g);
                                    // s17g2（V2.4.0.17）：章显示位置对齐 WPF PositionPreviewOverlay——stampStyle 返回的 left=(显示宽-章宽)×s.x、top=(显示高-章高)×s.y
                                    // （原实现 left=s.x*100% 偏差=s.x×章宽 → "按文字盖章每次对不准、位置相同"的显示层根因）
                                    string s17g2 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var s=vm.pageStamps[0];if(!s){return 'NO-STAMP';}var imgW=vm.imgW||0;var pts=vm.pagePts||595;var scale=imgW/pts;var w=s.sizeMm*72/25.4*scale;var h=w*(s.imgH/s.imgW);var dispH=imgW*((vm.pageH||vm.pageW||1)/(vm.pageW||1));var left=(s.x||0)*(imgW-w);var top=(s.y||0)*(dispH-h);var st=vm.stampStyle(s,imgW);return JSON.stringify({sx:s.x,sy:s.y,imgW:imgW,w:Math.round(w*10)/10,h:Math.round(h*10)/10,calcLeft:Math.round(left*10)/10,calcTop:Math.round(top*10)/10,styleLeft:st.left,styleTop:st.top,matchLeft:Math.abs(parseFloat(st.left)-left)<0.5,matchTop:Math.abs(parseFloat(st.top)-top)<0.5});})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns17g2:" + s17g2);
                                    // s17g3（V2.4.0.18）：手动盖章（centerRatio=true）显示=章中心对齐点击位置 left=s.x×dispW-w/2（对齐 WPF 2498 行）
                                    string s17g3a = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;window.Bridge.invoke('AddManualStamp',0.5,0.5,1).then(function(){vm.refreshPageStamps();window.__s17g3='done';},function(){window.__s17g3='err';});return 'start';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns17g3a:" + s17g3a);
                                    await System.Threading.Tasks.Task.Delay(1200);
                                    string s17g3 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var arr=vm.pageStamps.filter(function(s){return s.centerRatio;});var s=arr[arr.length-1];if(!s){return 'NO-MANUAL';}var imgW=vm.imgW||0;var pts=vm.pagePts||595;var scale=imgW/pts;var w=s.sizeMm*72/25.4*scale;var h=w*(s.imgH/s.imgW);var dispH=imgW*((vm.pageH||vm.pageW||1)/(vm.pageW||1));var left=s.x*imgW-w/2;var top=s.y*dispH-h/2;var st=vm.stampStyle(s,imgW);return JSON.stringify({centerRatio:s.centerRatio,sx:s.x,sy:s.y,calcLeft:Math.round(left*10)/10,calcTop:Math.round(top*10)/10,styleLeft:st.left,styleTop:st.top,matchLeft:Math.abs(parseFloat(st.left)-left)<0.5,matchTop:Math.abs(parseFloat(st.top)-top)<0.5});})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns17g3:" + s17g3);
                                    // s17g4（章位置 DOM 层实测——不再只验证函数返回值）：读真实 DOM 渲染坐标，
                                    // 计算最后一个章（手动章 centerRatio=true, 0.5,0.5）的中心 vs 页面中心偏差 dx/dy
                                    // 偏差≈0=显示层正确；偏差≠0 且固定=显示层确实错（按偏差定位 left/top/transform/基准）
                                    string s17g4 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var wrap=document.querySelector('.page-wrap');var ovs=document.querySelectorAll('.stamp-ov');if(!wrap||!ovs.length){return 'NO-DOM';}var sr=ovs[ovs.length-1].getBoundingClientRect();var wr=wrap.getBoundingClientRect();var pcx=wr.left+wr.width/2,pcy=wr.top+wr.height/2;var scx=sr.left+sr.width/2,scy=sr.top+sr.height/2;return JSON.stringify({pageW:vm.pageW,pageH:vm.pageH,pagePts:vm.pagePts,imgW:vm.imgW,wrapRect:{l:Math.round(wr.left),t:Math.round(wr.top),w:Math.round(wr.width),h:Math.round(wr.height)},stampRect:{l:Math.round(sr.left),t:Math.round(sr.top),w:Math.round(sr.width),h:Math.round(sr.height)},pageCenterX:Math.round(pcx),pageCenterY:Math.round(pcy),stampCenterX:Math.round(scx),stampCenterY:Math.round(scy),dx:Math.round((scx-pcx)*10)/10,dy:Math.round((scy-pcy)*10)/10,styleLeft:ovs[ovs.length-1].style.left,styleTop:ovs[ovs.length-1].style.top,transform:ovs[ovs.length-1].style.transform});})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns17g4:" + s17g4);
                                    // s18（V2.4.0.16 八项修复验证）
                                    // s18a：中心偏移记忆不自动启用（loadCenterOffsetForSearch 只恢复 off 数值、不动 offsetEnabled）
                                    string s18a0 = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;vm.searchText='项目';vm.offsetEnabled=true;vm.loadCenterOffsetForSearch();return 'ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns18a0:" + s18a0);
                                    await System.Threading.Tasks.Task.Delay(800);
                                    string s18a = await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;return JSON.stringify({offsetEnabled:vm.offsetEnabled,offH:vm.off.h,offV:vm.off.v});})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns18a:" + s18a);
                                    // s18b：openPdf 后 curFile=完整路径 + viewMode=single
                                    string s18b0 = await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;vm.viewMode='double';vm.openPdf('" + pdf8.Replace("\\", "\\\\") + "');return 'ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns18b0:" + s18b0);
                                    await System.Threading.Tasks.Task.Delay(3200);
                                    string s18b = await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;return JSON.stringify({viewMode:vm.viewMode,curFile:vm.curFile,fullPath:(vm.curFile||'').indexOf('C:/Users/admin/Desktop')===0});})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns18b:" + s18b);
                                    // s18bd：用户文件（投标文件测试.pdf）DOM 层章位置实测——重新盖中心章读真实渲染坐标
                                    // （对照 s17g4 的 s8_text.pdf：若 A4 偏差≈0 而用户文件偏差≠0 → 文件基准问题；都偏差 → 显示层问题）
                                    string s18bd0 = await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;window.Bridge.invoke('AddManualStamp',0.5,0.5,1).then(function(){vm.refreshPageStamps();window.__s18bd='done';},function(){window.__s18bd='err';});return 'start';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns18bd0:" + s18bd0);
                                    await System.Threading.Tasks.Task.Delay(1200);
                                    string s18bd = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var wrap=document.querySelector('.page-wrap');var ovs=document.querySelectorAll('.stamp-ov');if(!wrap||!ovs.length){return 'NO-DOM';}var sr=ovs[ovs.length-1].getBoundingClientRect();var wr=wrap.getBoundingClientRect();var pcx=wr.left+wr.width/2,pcy=wr.top+wr.height/2;var scx=sr.left+sr.width/2,scy=sr.top+sr.height/2;return JSON.stringify({pageW:vm.pageW,pageH:vm.pageH,pagePts:vm.pagePts,imgW:vm.imgW,wrapRect:{l:Math.round(wr.left),t:Math.round(wr.top),w:Math.round(wr.width),h:Math.round(wr.height)},stampRect:{l:Math.round(sr.left),t:Math.round(sr.top),w:Math.round(sr.width),h:Math.round(sr.height)},pageCenterX:Math.round(pcx),pageCenterY:Math.round(pcy),stampCenterX:Math.round(scx),stampCenterY:Math.round(scy),dx:Math.round((scx-pcx)*10)/10,dy:Math.round((scy-pcy)*10)/10,styleLeft:ovs[ovs.length-1].style.left,styleTop:ovs[ovs.length-1].style.top});})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns18bd:" + s18bd);
                                    // s18c：openDir 完整路径 + saveDir=目录\已盖章 + 工具栏下拉存在（先解锁输出目录以验证自动跳转逻辑）
                                    string s18c0 = await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;vm.dirLocked=false;vm.openDir('C:/Users/admin/Desktop');return 'ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns18c0:" + s18c0);
                                    await System.Threading.Tasks.Task.Delay(3200);
                                    string s18c = await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;var sel=document.querySelector('.float-bar .el-select');return JSON.stringify({dirMode:vm.dirMode,list0:(vm.curFileList[0]||''),listFull:(vm.curFileList[0]||'').indexOf('/')>=0,saveDir:vm.saveDir,curFile:vm.curFile,hasSelect:!!sel,viewMode:vm.viewMode});})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns18c:" + s18c);
                                    // s18c-diag：openDir 未生效诊断（opHint + C# 直调 OpenDirectory）
                                    string s18cd = await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;return JSON.stringify({hint:vm.opHint,dirMode:vm.dirMode});})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns18cd:" + s18cd);
                                    string s18e = "";
                                    try { s18e = _bridge.OpenDirectory("C:/Users/admin/Desktop"); } catch (Exception ex) { s18e = "ERR:" + ex.Message; }
                                    System.IO.File.AppendAllText(dumpPath, "\ns18e:" + s18e);
                                    // s18c2：下拉切换文件（curIdx=1 → switchDirFile → openPdf 完整路径）
                                    string s18c2a = await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;if(vm.curFileList.length>1){vm.curIdx=1;vm.switchDirFile();return 'switched';}return 'singleFile';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns18c2a:" + s18c2a);
                                    await System.Threading.Tasks.Task.Delay(3000);
                                    string s18c2 = await webView.CoreWebView2.ExecuteScriptAsync("(function(){var vm=window.__vm;return JSON.stringify({curIdx:vm.curIdx,curFile:vm.curFile,matchesList:(vm.curFile===vm.curFileList[vm.curIdx])});})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns18c2:" + s18c2);
                                    // s19：工具栏目录文件下拉"点了盖章/悬停不变色"——实测 popper 渲染位置/z-index/pointer-events + 点击选项是否切换而非盖章
                                    string s19a = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var sel=document.querySelector('.float-bar .el-select');if(!sel){window.__s19={err:'NO-SELECT'};return JSON.stringify(window.__s19);}var w=sel.querySelector('.el-select__wrapper')||sel;var ev=new MouseEvent('click',{bubbles:true,cancelable:true,view:window});w.dispatchEvent(ev);window.__s19='open';return 'ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns19a:" + s19a);
                                    await System.Threading.Tasks.Task.Delay(1000);
                                    string s19b = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;var dd=document.querySelector('.el-select-dropdown');if(!dd){window.__s19={err:'NO-DROPDOWN'};return JSON.stringify(window.__s19);}var cs=getComputedStyle(dd);var opts=dd.querySelectorAll('.el-select-dropdown__item');var info={dropParent:(dd.parentElement?('.'+(dd.parentElement.className||'')):'body'),zIndex:cs.zIndex,pe:cs.pointerEvents,display:cs.display,visibility:cs.visibility,opts:opts.length,stampCount0:vm.pageStamps.length};if(opts.length>2){opts[2].click();}window.__s19=info;return 'ok';})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns19b:" + s19b);
                                    await System.Threading.Tasks.Task.Delay(1500);
                                    string s19c = await webView.CoreWebView2.ExecuteScriptAsync(
                                        "(function(){var vm=window.__vm;if(window.__s19&&window.__s19.err){return JSON.stringify(window.__s19);}window.__s19.curIdx=vm.curIdx;window.__s19.stampCount1=vm.pageStamps.length;window.__s19.curFile=(vm.curFile||'').split('/').pop();return JSON.stringify(window.__s19);})()");
                                    System.IO.File.AppendAllText(dumpPath, "\ns19c:" + s19c);
                                    // s18d：重复拖入同名文件不报"正由另一进程使用"（C# 直调 OpenPdfFromBytes 两次）
                                    string s18d = "";
                                    try { string pdf8b = System.Environment.GetEnvironmentVariable("PDFQFZ_SHELL_PDF8") ?? ""; string pdf8bName = string.IsNullOrEmpty(pdf8b) ? "s8_text.pdf" : Path.GetFileName(pdf8b); string b64d = Convert.ToBase64String(System.IO.File.ReadAllBytes(string.IsNullOrEmpty(pdf8b) ? (System.IO.Path.GetTempPath() + "s8_text.pdf") : pdf8b)); string r1 = _bridge.OpenPdfFromBytes(b64d, pdf8bName); string r2 = _bridge.OpenPdfFromBytes(b64d, pdf8bName); s18d = r1 + "|" + r2; } catch (Exception ex) { s18d = "ERR:" + ex.Message; }
                                    System.IO.File.AppendAllText(dumpPath, "\ns18d:" + s18d);
                                    System.Windows.Application.Current.MainWindow.Width = _w2; System.Windows.Application.Current.MainWindow.Height = _h2;

                                    System.IO.File.AppendAllText(dumpPath, "\ns8final:" + string.Join("|", Directory.GetFiles(out8, "*.pdf").Select(Path.GetFileName)));
                                    // 等待 E2E 异步（视图切换渲染等）落定后再退出
                                    await System.Threading.Tasks.Task.Delay(2500);
                                    // 用正常关闭窗口替代 Environment.Exit：Exit 会跳过 WebView2 Dispose，GC 终结器回收已销毁的
                                    // COM 接口导致 InvalidCastException（"已停止工作"弹窗）。正常关闭走 Dispose 干净退出。
                                    System.Windows.Application.Current.Dispatcher.Invoke(
                                        () => System.Windows.Application.Current.MainWindow.Close());
                                    return;
                                }
                                catch (Exception ex)
                                {
                                    // 失败场景解耦（B 方案）：任何异常也写 s8final 完成标记（可含 s8ERR 详情），
                                    // 避免脚本等不到完成标记 600s 超时；断言按 dump 内容判定，不依赖固定文件。
                                    System.IO.File.AppendAllText(dumpPath, "\ns8ERR:" + ex.Message);
                                    System.IO.File.AppendAllText(dumpPath, "\ns8final:" + string.Join("|", Directory.GetFiles(out8, "*.pdf").Select(Path.GetFileName)));
                                }
                            }
                        }
                        catch (Exception ex) { System.IO.File.AppendAllText(dumpPath, "\nERR:" + ex.Message); }
                    };
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("WebView2 初始化失败：\n" + ex.Message, "文件批量盖章与水印工具", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // V2.4.0.11：壳层窗口级拖放兜底——防止外部拖放被系统回退到"用关联程序打开"（用户实测拖入 PDF 变 Adobe 打开）。
        // 页面内 document 级拖放监听（前端 _bindDocDrag）保证 WebView2 区域接受拖放；此处窗口级兜底：
        // ① DragOver 一律标记 Copy + Handled，阻止系统默认行为；② 收到文件 Drop 时把路径推给前端走 openPdf（路径通道）。
        private void Win_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
            e.Handled = true;
        }

        private void Win_Drop(object sender, System.Windows.DragEventArgs e)
        {
            e.Handled = true;
            try
            {
                // V2.4.0.33：真实 OS 拖放诊断——记录壳层是否收到文件 Drop（前端拖放路径排查用）
                try
                {
                    string _dbg = "壳层Win_Drop触发: FileDrop=" + (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) ? "有" : "无");
                    if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
                    {
                        var _fs = e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[];
                        _dbg += " count=" + (_fs == null ? 0 : _fs.Length) + " first=" + (_fs != null && _fs.Length > 0 ? _fs[0] : "");
                    }
                    if (_bridge != null) _bridge.DiagLog(_dbg);
                }
                catch { }
                if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
                {
                    var files = e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[];
                    if (files == null || files.Length == 0 || _bridge == null) return;
                    string p = files[0];
                    if (string.IsNullOrWhiteSpace(p)) return;
                    bool isDir = System.IO.Directory.Exists(p);
                    if (!isDir && !System.IO.File.Exists(p)) return;
                    if (webView != null && webView.CoreWebView2 != null)
                    {
                        // V2.4.0.16：文件夹走 file-dropped-dir（前端 openDir），文件走 file-dropped（openPdf）
                        string kind = isDir ? "file-dropped-dir" : "file-dropped";
                        try { if (_bridge != null) _bridge.DiagLog("壳层Win_Drop→PostWebMessage " + kind + " " + p); } catch { }
                        webView.CoreWebView2.PostWebMessageAsJson(
                            "{\"kind\":\"" + kind + "\",\"payload\":{\"path\":\"" +
                            p.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}}");
                    }
                }
            }
            catch { }
        }

        private static string LocateWebRoot()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            // 开发形态优先：源码侧 WebUI\prototype（bin\Debug 向上三级），源码永远最新
            string dev = Path.GetFullPath(Path.Combine(exeDir, @"..\..\..\WebUI\prototype"));
            if (File.Exists(Path.Combine(dev, "index.html")) && Directory.Exists(Path.Combine(dev, "lib")))
                return dev;
            // 交付形态：EXE 同目录 WebUI\prototype，需版本标记一致才直接使用（一致=同版本，保护用户手动修改）
            string rt = PDFQFZ.WPF.Services.AppConfig.RuntimeDir;
            string p1 = Path.Combine(rt, "WebUI", "prototype");
            string curVer = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            string verFile = Path.Combine(rt, "WebUI", "webui.version.txt");
            bool fresh = false;
            try
            {
                fresh = File.Exists(Path.Combine(p1, "index.html")) && Directory.Exists(Path.Combine(p1, "lib"))
                    && File.Exists(verFile) && File.ReadAllText(verFile).Trim() == curVer;
            }
            catch { fresh = false; }
            try
            {
                if (PDFQFZ.WPF.Services.AppConfig.LoadStampEntries().Count == 0)
                {
                    string gz = Path.Combine(PDFQFZ.WPF.Services.AppConfig.StampLibraryDir, "公章.png");
                    if (!File.Exists(gz))
                    {
                        // 交付目录根下用户手动放置的公章.png（单 EXE 形态），录入印章库后注册
                        string rootGz = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "公章.png");
                        if (File.Exists(rootGz))
                        {
                            try { Directory.CreateDirectory(Path.GetDirectoryName(gz)); File.Copy(rootGz, gz, true); } catch { }
                        }
                    }
                    if (File.Exists(gz))
                        PDFQFZ.WPF.Services.AppConfig.AppendStampEntry("测试公章", gz); // v2.4.0.77：内置示范章显示名"测试公章"（原"公章"）；可删除（删除后印章库目录存在时不重新提取）
                }
            }
            catch
            {
            }
            // 总是覆盖 WebUI（开发调试用，不用手动删）
            // if (fresh)
            //     return p1;
            // 单 EXE 形态：嵌入资源提取（总是覆盖，提取后写版本标记）
            if (ExtractEmbeddedWebRoot(p1, verFile, curVer))
                return p1;
            return null;
        }

        /// <summary>阶段 10 单 EXE：把嵌入的 WebUI\prototype 资源提取到指定目录。版本标记一致时跳过（保护用户手动修改）；缺失或版本不一致时覆盖更新并写版本标记。</summary>
        private static bool ExtractEmbeddedWebRoot(string targetDir, string verFile, string curVer)
        {
            try
            {
                bool needRefresh = true; // V2.4.0.163：调试版本总是覆盖 WebUI，不用手动删运行组件
                var map = new Dictionary<string, string>
                {
                    { "PDFQFZ.WebShell.webui.index.html", "index.html" },
                    { "PDFQFZ.WebShell.webui.bridge.js", "bridge.js" },
                    { "PDFQFZ.WebShell.webui.lib.vue.global.prod.js", @"lib\vue.global.prod.js" },
                    { "PDFQFZ.WebShell.webui.lib.element-plus.min.js", @"lib\element-plus.min.js" },
                    { "PDFQFZ.WebShell.webui.lib.element-plus.css", @"lib\element-plus.css" },
                    { "PDFQFZ.WebShell.webui.lib.icons.iife.min.js", @"lib\icons.iife.min.js" },
                    { "PDFQFZ.WebShell.webui.modules.stampActions.js", @"modules\stampActions.js" },
                    { "PDFQFZ.WebShell.webui.modules.sealLib.js", @"modules\sealLib.js" },
                    { "PDFQFZ.WebShell.webui.modules.batchActions.js", @"modules\batchActions.js" },
                    { "PDFQFZ.WebShell.webui.modules.uiLayout.js", @"modules\uiLayout.js" },
                    { "PDFQFZ.WebShell.webui.modules.configPersist.js", @"modules\configPersist.js" },
                    { "PDFQFZ.WebShell.webui.modules.watermark.js", @"modules\watermark.js" },
                    { "PDFQFZ.WebShell.webui.modules.imageWatermark.js", @"modules\imageWatermark.js" },
                    { "PDFQFZ.WebShell.webui.modules.badge.js", @"modules\badge.js" },
                    { "PDFQFZ.WebShell.webui.modules.theme.js", @"modules\theme.js" },
                    { "PDFQFZ.WebShell.webui.vendor.localforage.min.js", @"vendor\localforage.min.js" }
                };
                foreach (var kv in map)
                {
                    string outPath = Path.Combine(targetDir, kv.Value);
                    using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(kv.Key))
                    {
                        if (s == null)
                            return false;
                        string dir = Path.GetDirectoryName(outPath);
                        if (!string.IsNullOrEmpty(dir))
                            Directory.CreateDirectory(dir);
                        using (FileStream fs = new FileStream(outPath, FileMode.Create))
                            s.CopyTo(fs);
                    }
                }
                try
                {
                    string wd = Path.GetDirectoryName(verFile);
                    if (!string.IsNullOrEmpty(wd))
                        Directory.CreateDirectory(wd);
                    File.WriteAllText(verFile, curVer);
                }
                catch { }
                return File.Exists(Path.Combine(targetDir, "index.html")) && Directory.Exists(Path.Combine(targetDir, "lib"));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>V2.4.0.24：恢复上次窗口位置/大小（对齐 WPF LoadConfigToUi：WindowWidth/Height>0 恢复尺寸；
        /// WindowLeft/Top>-1 恢复位置并改 Manual 定位；FitWindowToScreen 防小屏/旧配置越界）。</summary>
        private void RestoreWindowState()
        {
            try
            {
                if (PDFQFZ.WPF.Services.AppConfig.WindowWidth > 0) Width = PDFQFZ.WPF.Services.AppConfig.WindowWidth;
                if (PDFQFZ.WPF.Services.AppConfig.WindowHeight > 0) Height = PDFQFZ.WPF.Services.AppConfig.WindowHeight;
                if (PDFQFZ.WPF.Services.AppConfig.WindowLeft > -1 && PDFQFZ.WPF.Services.AppConfig.WindowTop > -1)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = PDFQFZ.WPF.Services.AppConfig.WindowLeft;
                    Top = PDFQFZ.WPF.Services.AppConfig.WindowTop;
                }
                FitWindowToScreen();
            }
            catch { }
        }

        /// <summary>V2.4.0.24：窗口自适应（对齐 WPF FitWindowToScreen）——尺寸超过所在屏幕工作区→压缩到屏幕内（不低于最小尺寸）；
        /// 位置整体在屏幕外→回到屏幕中央（CenterScreen 需在 Show 前设置才生效，此处启动早期调用）。</summary>
        private void FitWindowToScreen()
        {
            try
            {
                double scaleX = 1.0, scaleY = 1.0;
                try
                {
                    var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
                    scaleX = dpi.DpiScaleX;
                    scaleY = dpi.DpiScaleY;
                }
                catch { }
                int centerX = (int)((Left + Width / 2.0) * scaleX);
                int centerY = (int)((Top + Height / 2.0) * scaleY);
                var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(centerX, centerY));
                var workArea = screen.WorkingArea;
                double availLeft = workArea.Left / scaleX;
                double availTop = workArea.Top / scaleY;
                double availWidth = workArea.Width / scaleX;
                double availHeight = workArea.Height / scaleY;
                const double margin = 20.0;
                if (Width > availWidth - margin) Width = Math.Max(MinWidth, availWidth - margin);
                if (Height > availHeight - margin) Height = Math.Max(MinHeight, availHeight - margin);
                bool offscreen = Left < availLeft - 40 || Top < availTop - 40 ||
                                 Left > availLeft + availWidth - 40 || Top > availTop + availHeight - 40;
                if (offscreen) WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            catch { }
        }

        // ============ V2.4.0.21：Win32 OLE 拖放回调 ============
        // OLE IDropTarget.Drop 解析出真实系统路径数组后回调（壳层线程）：
        //   单文件 → file-dropped（openPdf）；单目录 → file-dropped-dir（openDir）；多文件 → file-dropped-multi（openFiles）
        private void OnOleDrop(string[] paths)
        {
            if (paths == null || paths.Length == 0) return;
            try
            {
                string json;
                if (paths.Length == 1)
                {
                    string p = paths[0];
                    string kind = Directory.Exists(p) ? "file-dropped-dir" : "file-dropped";
                    json = "{\"kind\":\"" + kind + "\",\"payload\":{\"path\":" + _bridge.EscapeJson(p) + "}}";
                }
                else
                {
                    var arr = new System.Text.StringBuilder("[");
                    for (int i = 0; i < paths.Length; i++)
                    {
                        if (i > 0) arr.Append(',');
                        arr.Append(_bridge.EscapeJson(paths[i]));
                    }
                    arr.Append(']');
                    json = "{\"kind\":\"file-dropped-multi\",\"payload\":" + arr.ToString() + "}";
                }
                webView.Dispatcher.Invoke(() =>
                {
                    try { webView.CoreWebView2.PostWebMessageAsJson(json); } catch { }
                });
            }
            catch { }
        }
    }
}
