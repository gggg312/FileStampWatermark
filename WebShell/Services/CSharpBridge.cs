using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Drawing;
using PDFQFZ.Library;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using iTextSharp.text.pdf;
using PDFQFZ.WPF.Services;

namespace PDFQFZ.WebShell.Services
{
    /// <summary>
    /// C# → JS 桥对象（AddHostObjectToScript 注入，JS 端以 window.CSharpBridge 访问）。
    /// 方法返回简单类型（string/int/bool），JS 端调用得到 Promise。
    /// 阶段 3：配置对接——AppConfig（config.ini）读写经此暴露给前端。
    /// </summary>
    [ComVisible(true)]
    public partial class CSharpBridge
    {
        /// <summary>写运行日志到统一文件 app_log.log（EXE 同目录，保留最近两次运行）。V1.0.0.14：由 wm_debug.log 迁入 AppLog。</summary>
        public static void WriteLog(string msg) {
            AppLog.Write(msg);
        }
        /// <summary>V1.0.0.14：前端系统日志区落盘/JS 全局错误等逐行写入统一 app_log.log（供用户复现后反馈排查）。</summary>
        public void LogLine(string msg) {
            AppLog.Write(msg);
        }
        /// <summary>C# 主动推事件给 JS 的出口（由 MainWindow 注入 CoreWebView2.PostWebMessageAsJson）。</summary>
        private readonly Action<string> _postEvent;

        /// <summary>调试页标记（对齐 WPF _debugPageActive）：未加载用户文件时预览区显示内置调试页，可手动盖章，范围/文字/生成被拦截。</summary>
        private bool _isDebugPage;

        /// <summary>用户文件已打开标记（V2.4.0.8）：防止启动异步 OpenDebugPage 竞态把已打开的用户文件渲染器替换成调试页。</summary>
        private bool _userFileActive;
        // ===== 打点结束 =====

        public CSharpBridge(Action<string> postEvent)
        {
            _postEvent = postEvent;
        }

        // ---------- 基础 ----------




        // ---------- 阶段 3：配置 ----------






        // ---------- 阶段 4：PDF 渲染预览 ----------

        private static readonly object renderSync = new object();
        // V2.4.0.81：生成/批量放置互斥（批次2-③）——后台 Task 与 UI 操作并发保护；Interlocked 无阻塞、幂等
        private int _batchBusy;
        private bool TryEnterBatch() { return System.Threading.Interlocked.CompareExchange(ref _batchBusy, 1, 0) == 0; }
        private void ExitBatch() { System.Threading.Interlocked.Exchange(ref _batchBusy, 0); }
        private PDFQFZ.Library.IPdfDocumentRenderer _renderer;
        private string _renderCacheDir;
        private string _cacheUrlPrefix;
        private const int PreviewDpi = 144; // 与 WPF 版 RenderDpi 常量一致
        // V2.4.0.80：页渲染缓存（对照 WPF PageCache<T>）——key=文档路径|页序，value=RenderPage 完整 JSON；
        // 命中直接复用 PNG 路径，免 PDFium 重渲染（翻回旧页卡顿主因）。换文档（OpenPdf/OpenDebugPage/ClosePdf）时清空。
        private readonly object _renderPageCacheLock = new object();
        private readonly Dictionary<string, string> _renderPageCache = new Dictionary<string, string>();
        // V2.4.0.9：进程隔离标签——%TEMP% 共享路径（调试页/拖入临时文件）加 pid 后缀，
        // 旧版本残留进程与新版本共跑时不再互相锁文件/覆盖文件（调试页提取、拖入解析均受影响）
        private static readonly string _procTag = System.Diagnostics.Process.GetCurrentProcess().Id.ToString();










        // ---------- 序列化辅助 ----------

        private static string Json(object obj)
        {
            return new JavaScriptSerializer().Serialize(obj);
        }

        /// <summary>V2.4.0.21：字符串转 JSON 字符串字面量（含引号转义），供壳层拼事件 JSON 用。</summary>
        public string EscapeJson(string s)
        {
            return Json(s ?? "");
        }

        private static int GetInt(Dictionary<string, object> d, string key, int def)
        {
            if (!d.TryGetValue(key, out object v) || v == null) return def;
            if (v is int i) return i;
            return int.TryParse(v.ToString(), out int r) ? r : def;
        }
        // ---------- 阶段 6：手动盖章 ----------

        private static readonly Random StampRandomGenerator = new Random();
        private readonly PDFQFZ.Library.StampPlacementCollection _stampPlacements = new PDFQFZ.Library.StampPlacementCollection();
        private string _stampDocPath = "";
        // V2.4.0.96：文字水印框集合（按 文档路径+页 隔离，与章集合同生命周期；文档切换清空见 Documents.cs）
        private readonly PDFQFZ.Library.WatermarkBoxCollection _watermarks = new PDFQFZ.Library.WatermarkBoxCollection();
        // V2.4.0.96：最近渲染页（RenderPage 时更新；水印方案保存/应用以当前渲染页为锚）
        private int _lastRenderedPage = 1;
        // V2.4.0.97：图片模式（第二步：图片批量水印）——拖入图片/文件夹自动进入，不做专门模式 Tab。
        // 图片水印框复用 _watermarks：DocumentPath=图片完整路径、Page=1（_watermarkDocPath() 切换上下文）。
        private bool _imgMode = false;
        private string _imgDocPath = "";                 // 当前预览图片完整路径（水印框存取上下文）
        private readonly List<string> _imgQueue = new List<string>(); // 图片队列（完整路径）
        private int _imgCurrentIndex = -1;

        private static int NewTextureSeed()
        {
            lock (StampRandomGenerator) { return StampRandomGenerator.Next(1, 1000000); }
        }

        /// <summary>盖章渲染强度系数：每枚章盖章时随机 0.85~1.15（对齐 WPF NewTextureK）。</summary>
        private static float NewTextureK()
        {
            lock (StampRandomGenerator) { return (float)(0.85 + 0.30 * StampRandomGenerator.NextDouble()); }
        }
















        // ---------- 阶段 8：输出与命名 ----------


        // ===================== V2.4.0.53 文件夹批量处理（简化版 V1） =====================













        // ---------- 阶段 7：按文字盖章 ----------

        private class AutoStampOp
        {
            public string Keyword; public int BatchId; public string FilePath;
        }
        private readonly List<AutoStampOp> _autoStampOps = new List<AutoStampOp>();
        // 目录模式状态（对齐 WPF LoadDirectory）：_dirMode=true 时生成处理整个目录
        private bool _dirMode = false;
        private string _dirSource = null;
        private List<string> _dirFiles = null;









        /// <summary>简单 JSON 对象解析（Dictionary；仅一层，值含 string/bool/number/数组原始字符串）。</summary>
        private static Dictionary<string, object> ParseJsonObject(string json)
        {
            try
            {
                var d = new Dictionary<string, object>();
                string s = (json ?? "").Trim();
                if (!s.StartsWith("{")) return null;
                // 手工逐键解析（避免引 System.Web.Extensions）
                int pos = 1;
                while (pos < s.Length)
                {
                    while (pos < s.Length && (s[pos] == ' ' || s[pos] == ',')) pos++;
                    if (pos >= s.Length || s[pos] == '}') break;
                    if (s[pos] != '"') return null;
                    int ke = s.IndexOf('"', pos + 1);
                    if (ke < 0) return null;
                    string key = s.Substring(pos + 1, ke - pos - 1);
                    pos = ke + 1;
                    while (pos < s.Length && (s[pos] == ' ' || s[pos] == ':')) pos++;
                    // 值：引号字符串 / 布尔 / 数字 / 数组 / 对象（原样取到匹配结束）
                    string val; bool isQuoted = false;
                    if (pos < s.Length && s[pos] == '"')
                    {
                        // V281: 逐字符扫描字符串，处理 JSON 转义（\n \t \" \\ 等），还原真实字符
                        pos++;
                        val = "";
                        while (pos < s.Length)
                        {
                            char ch = s[pos];
                            if (ch == '"') { pos++; break; }
                            if (ch == '\\' && pos + 1 < s.Length)
                            {
                                char nx = s[pos + 1];
                                switch (nx)
                                {
                                    case 'n': val += "\n"; break;
                                    case 't': val += "\t"; break;
                                    case 'r': val += "\r"; break;
                                    case '"': val += "\""; break;
                                    case '\\': val += "\\"; break;
                                    case '/': val += "/"; break;
                                    default: val += nx; break;
                                }
                                pos += 2;
                            }
                            else { val += ch; pos++; }
                        }
                        isQuoted = true;
                    }
                    else
                    {
                        int start = pos;
                        int depth = 0;
                        while (pos < s.Length)
                        {
                            char ch = s[pos];
                            if (ch == '{' || ch == '[') depth++;
                            else if (ch == '}' || ch == ']') { if (depth == 0) break; depth--; }
                            else if ((ch == ',' || ch == '}') && depth == 0) break;
                            pos++;
                        }
                        val = s.Substring(start, pos - start).Trim();
                    }
                    object objVal = val;
                    if (string.Equals(val, "true", StringComparison.OrdinalIgnoreCase)) objVal = true;
                    else if (string.Equals(val, "false", StringComparison.OrdinalIgnoreCase)) objVal = false;
                    else if (!isQuoted && val.Length > 0 && val[0] != '[' && val[0] != '{' && double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double num))
                        objVal = num;
                    d[key] = objVal;
                }
                return d;
            }
            catch { return null; }
        }





        /// <summary>删除整个批量批次（指定范围页盖章）。返回 "ok" 或 "err:..."。</summary>
        public string DeleteStampBatch(int batchId)
        {
            try
            {
                return _stampPlacements.RemoveBatch(_stampDocPath, batchId) > 0 ? "ok" : "err:批次不存在";
            }
            catch (Exception ex) { return "err:" + ex.Message; }
        }

        /// <summary>删除某批次在当前页的全部印章。返回 "ok" 或 "err:..."。</summary>
        public string DeleteStampBatchOnPage(int batchId, int page)
        {
            try
            {
                return _stampPlacements.RemoveBatchOnPage(_stampDocPath, page, batchId) > 0 ? "ok" : "err:未找到";
            }
            catch (Exception ex) { return "err:" + ex.Message; }
        }



        // ---------- 阶段 5：印章库 ----------










        private static bool GetBool(Dictionary<string, object> d, string key, bool def)
        {
            if (!d.TryGetValue(key, out object v) || v == null) return def;
            if (v is bool b) return b;
            return v.ToString() == "1" || string.Equals(v.ToString(), "true", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetStr(Dictionary<string, object> d, string key, string def)
        {
            if (!d.TryGetValue(key, out object v) || v == null) return def ?? "";
            return v.ToString();
        }
    }
}
