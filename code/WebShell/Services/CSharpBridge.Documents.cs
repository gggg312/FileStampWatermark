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
    /// V2.4.0.82：桥按业务拆 partial（本文件：阶段 4 加载/渲染/目录）。
    /// </summary>
    public partial class CSharpBridge
    {
        /// <summary>MainWindow 初始化时注入渲染缓存目录 + 虚拟域名前缀（须先 SetVirtualHostNameToFolderMapping）。</summary>
        public void SetRenderCache(string dir, string urlPrefix)
        {
            _renderCacheDir = dir;
            _cacheUrlPrefix = urlPrefix;
        }
        /// <summary>弹文件对话框选 PDF（UI 线程调用）。取消返回空串。</summary>
        public string PickPdf()
        {
            try
            {
                using (var dlg = new System.Windows.Forms.OpenFileDialog())
                {
                    dlg.Filter = "PDF 文件 (*.pdf)|*.pdf|图片文件 (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|所有文件 (*.*)|*.*";
                    dlg.Title = "选择要盖章的 PDF 文件";
                    dlg.CheckFileExists = true;
                    return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dlg.FileName : "";
                }
            }
            catch (Exception ex) { return "err:" + ex.Message; }
        }
        /// <summary>打开 PDF（内部先关闭上一个）。返回 {ok,pageCount,error}。</summary>
        public string OpenPdf(string path, bool keepStampData = false)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return Json(new Dictionary<string, object> { ["ok"] = false, ["error"] = "文件不存在：" + (path ?? "") });
                var renderer = PDFQFZ.Library.PdfiumDocumentRenderer.Open(path);
                lock (renderSync)
                {
                    if (_renderer != null) { _renderer.Dispose(); _renderer = null; }
                    _renderer = renderer;
                }
                // 清空上次预览缓存，避免残留图；V2.4.0.80：同步清页渲染缓存（换文档/同文档重开均失效）
                lock (_renderPageCacheLock) { _renderPageCache.Clear(); }
            lock (_renderGridCacheLock) { _renderGridCache.Clear(); } // V1.0.0.50：同步清网格缩略图缓存
                try
                {
                    if (!string.IsNullOrEmpty(_renderCacheDir) && Directory.Exists(_renderCacheDir))
                        foreach (var f in Directory.GetFiles(_renderCacheDir, "page_*.png"))
                            File.Delete(f);
                }
                catch { }
                int count = renderer.PageCount;
                // 加载用户真实文件：退出调试页模式
                _isDebugPage = false;
                _userFileActive = true;
                // 阶段6：keepStampData 语义——目录模式内切换当前文件（OpenPdfKeep=true）保留各文件章（按 DocumentPath 独立存储）；
                // 非切换打开/重新导入（默认 false）无论是否同文档一律清空（V2.4.0.86 修复：重新拖入同一 PDF 章残留——
                // 原实现仅路径不同才进清章分支，同文档重开时 keepStampData=false 被忽略，后端章未清、refreshPageStamps 拉回旧章）
                if (!keepStampData)
                {
                    _stampPlacements.Clear();
                    _watermarks.ClearAll(); // V2.4.0.96：换文档清空全部水印框
                    _stampDocPath = path;
                    _imgMode = false; _imgDocPath = ""; // V2.4.0.97：加载 PDF 时退出图片模式
                }
                else if (!string.Equals(_stampDocPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    _stampDocPath = path;
                    _imgMode = false; _imgDocPath = ""; // V2.4.0.97：加载 PDF 时退出图片模式
                }
                return "{\"ok\":true,\"pageCount\":" + count + "}";
            }
            catch (Exception ex)
            {
                return "{\"ok\":false,\"error\":" + Json("打开失败：" + ex.Message) + "}";
            }
        }
        /// <summary>V2.4.0.29：目录模式内切换当前文件——保留各文件已放置的印章（对齐 WPF LoadPdf(keepStampData:true)）。</summary>
        public string OpenPdfKeep(string path)
        {
            return OpenPdf(path, true);
        }
        /// <summary>调试页（对齐 WPF ShowBlankDebugPage）：从嵌入资源提取内置 A4 调试页到 TEMP，打开为当前预览文档。
        /// 返回 OpenPdf 同格式 JSON + isDebug:true；可手动盖章调试渲染参数，范围/文字/生成被拦截（见对应方法）。</summary>
        public string OpenDebugPage()
        {
            try
            {
                // V2.4.0.8：用户已打开真实文件（含拖入）时，调试页不得再替换渲染器——
                // 启动异步 OpenDebugPage 与用户拖入/选择文件竞态时，后端渲染器若被换成调试页，
                // 前端守卫只能防住状态覆盖、防不住渲染器被换（会显示调试页第一页）。已加载则直接跳过。
                if (_userFileActive)
                {
                    lock (renderSync)
                    {
                        int pc = (_renderer != null) ? _renderer.PageCount : 1;
                        return "{\"ok\":true,\"pageCount\":" + pc + ",\"isDebug\":false}";
                    }
                }
                string dir = Path.Combine(Path.GetTempPath(), "PDFQFZ_" + _procTag);
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "debug_page.pdf");
                if (!File.Exists(path))
                {
                    using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("PDFQFZ.WebShell.assets.debug_page.pdf"))
                    {
                        if (stream == null) return "{\"ok\":false,\"error\":\"内置调试页缺失\"}";
                        using (var fs = new FileStream(path, FileMode.Create)) stream.CopyTo(fs);
                    }
                }
                var renderer = PDFQFZ.Library.PdfiumDocumentRenderer.Open(path);
                lock (renderSync)
                {
                    if (_renderer != null) { _renderer.Dispose(); _renderer = null; }
                    _renderer = renderer;
                }
                // V2.4.0.80：换调试页同步清页渲染缓存
                lock (_renderPageCacheLock) { _renderPageCache.Clear(); }
            lock (_renderGridCacheLock) { _renderGridCache.Clear(); } // V1.0.0.50：同步清网格缩略图缓存
                try
                {
                    if (!string.IsNullOrEmpty(_renderCacheDir) && Directory.Exists(_renderCacheDir))
                        foreach (var f in Directory.GetFiles(_renderCacheDir, "page_*.png")) File.Delete(f);
                }
                catch { }
                _isDebugPage = true;
                _userFileActive = false;
                _stampPlacements.Clear();
                _watermarks.ClearAll(); // V2.4.0.96：切调试页清空水印框
                _stampDocPath = path;
                    _imgMode = false; _imgDocPath = ""; // V2.4.0.97：加载 PDF 时退出图片模式
                int count = renderer.PageCount;
                return "{\"ok\":true,\"pageCount\":" + count + ",\"isDebug\":true}";
            }
            catch (Exception ex)
            {
                return "{\"ok\":false,\"error\":" + Json("调试页打开失败：" + ex.Message) + "}";
            }
        }
        /// <summary>渲染指定页到缓存 PNG，返回 {ok,url,w,h,error}（w/h 为 144dpi 像素）。</summary>
        public string RenderPage(int pageIndex)
        {
            try
            {
                PDFQFZ.Library.IPdfDocumentRenderer r;
                lock (renderSync) { r = _renderer; }
                if (r == null) return "{\"ok\":false,\"error\":\"未打开PDF\"}";
                // V2.4.0.80：页缓存命中（对照 WPF PageCache<T>）——同一文档同页翻回直接复用 PNG，免 PDFium 重渲染
                string cacheKey = (_stampDocPath ?? "") + "|" + pageIndex;
                string cachedJson;
                lock (_renderPageCacheLock)
                {
                    if (_renderPageCache.TryGetValue(cacheKey, out cachedJson))
                    {
                        string cf = Path.Combine(_renderCacheDir, "page_" + pageIndex + ".png");
                        if (File.Exists(cf)) return cachedJson;
                        _renderPageCache.Remove(cacheKey);
                    }
                }
                using (var bmp = r.RenderPage(pageIndex, PreviewDpi))
                {
                    using (var ms = new MemoryStream())
                    {
                        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        string name = "page_" + pageIndex + ".png";
                        string full = Path.Combine(_renderCacheDir, name);
                        File.WriteAllBytes(full, ms.ToArray());
                        string json = "{\"ok\":true,\"url\":" + Json(_cacheUrlPrefix + "/" + name) + ",\"w\":" + bmp.Width + ",\"h\":" + bmp.Height +
                            ",\"ptW\":" + (bmp.Width / 2) + ",\"ptH\":" + (bmp.Height / 2) + "}"; // V2.4.0.17：ptW/ptH=页面真实 pt（PreviewDpi=144=2×72 → 渲染px/2=页面pt），前端显示/印章换算用 pt 基准，对齐 WPF displayWidth 语义
                        lock (_renderPageCacheLock) { _renderPageCache[cacheKey] = json; }
                        _lastRenderedPage = pageIndex + 1; // V2.4.0.96：记录最近渲染页（方案保存/应用锚点）
                        return json;
                    }
                }
            }
            catch (Exception ex)
            {
                return "{\"ok\":false,\"error\":" + Json("渲染失败：" + ex.Message) + "}";
            }
        }
        /// <summary>V1.0.0.50：网格视图缩略图渲染（默认 72dpi 小图，避免 grid 一次 8 张 144dpi 大图并发解码导致渲染白屏）。
        /// 独立缓存（文档|g|页序|dpi），换文档时与页缓存一并清空；ptW/ptH 仍为页面真实 pt（px×72/dpi）。</summary>
        public string RenderGridPage(int pageIndex, int dpi)
        {
            try
            {
                PDFQFZ.Library.IPdfDocumentRenderer r;
                lock (renderSync) { r = _renderer; }
                if (r == null) return "{\"ok\":false,\"error\":\"未打开PDF\"}";
                if (dpi < 36) dpi = 72; else if (dpi > 144) dpi = 144;
                string cacheKey = (_stampDocPath ?? "") + "|g|" + pageIndex + "|" + dpi;
                string cachedJson;
                lock (_renderGridCacheLock)
                {
                    if (_renderGridCache.TryGetValue(cacheKey, out cachedJson))
                    {
                        string cf = Path.Combine(_renderCacheDir, "page_grid_" + pageIndex + ".png");
                        if (File.Exists(cf)) return cachedJson;
                        _renderGridCache.Remove(cacheKey);
                    }
                }
                using (var bmp = r.RenderPage(pageIndex, dpi))
                {
                    using (var ms = new MemoryStream())
                    {
                        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        string name = "page_grid_" + pageIndex + ".png";
                        string full = Path.Combine(_renderCacheDir, name);
                        File.WriteAllBytes(full, ms.ToArray());
                        string json = "{\"ok\":true,\"url\":" + Json(_cacheUrlPrefix + "/" + name) + ",\"w\":" + bmp.Width + ",\"h\":" + bmp.Height +
                            ",\"ptW\":" + (bmp.Width * 72 / dpi) + ",\"ptH\":" + (bmp.Height * 72 / dpi) + "}"; // V1.0.0.50：ptW/ptH=页面真实 pt（px×72/dpi，dpi=72 时 px 即 pt）
                        lock (_renderGridCacheLock) { _renderGridCache[cacheKey] = json; }
                        return json;
                    }
                }
            }
            catch (Exception ex)
            {
                return "{\"ok\":false,\"error\":" + Json("渲染失败：" + ex.Message) + "}";
            }
        }
        /// <summary>V1.0.0.61：网格缩略图批量渲染——一次 invoke 渲染 count 页（前端默认 32），内部复用 RenderGridPage 的缓存与渲染逻辑。
        /// 将 WebView2 往返从每页 1 次降到每批 1 次（千页懒加载提速根因，滚动连续加载不露底）；
        /// 返回 {"ok":true,"items":[{page,ok,url,w,h,ptW,ptH} | {page,ok:false,error}]}——单页失败不影响同批其余页，前端对失败页走单页重试。</summary>
        public string RenderGridPageBatch(int startIndex, int count, int dpi)
        {
            try
            {
                int total = 0;
                lock (renderSync) { if (_renderer != null) { try { total = _renderer.PageCount; } catch { total = 0; } } }
                if (total <= 0) return "{\"ok\":false,\"error\":\"未打开PDF\"}";
                if (dpi < 36) dpi = 72; else if (dpi > 144) dpi = 144;
                int s = Math.Max(0, startIndex);
                int e = Math.Min(total, s + Math.Max(1, count));
                if (s >= e) return "{\"ok\":false,\"error\":\"页码超出范围\"}";
                var items = new List<string>();
                for (int i = s; i < e; i++)
                {
                    string one = RenderGridPage(i, dpi); // 复用单页缓存/渲染（{"ok":true,"url":..,"w":..,"h":..,"ptW":..,"ptH":..} 或 {"ok":false,"error":..}）
                    if (one.Length >= 1 && one[0] == '{') one = one.Substring(1); // 去掉外层 {，拼入 page 字段（JSON 保持合法）
                    items.Add("{\"page\":" + (i + 1) + "," + one);
                }
                return "{\"ok\":true,\"items\":[" + string.Join(",", items) + "]}";
            }
            catch (Exception ex)
            {
                return "{\"ok\":false,\"error\":" + Json("批量渲染失败：" + ex.Message) + "}";
            }
        }
        /// <summary>拖入的文件：base64 → 临时文件 → 打开（WebView2 拖入拿不到路径，只能读内容）。
        /// V2.4.0.15：新增 fileName 参数——按原名保存（进程隔离目录内），使输出文件命名基于原始文件名
        /// （修复拖入生成的文件名变成 pdfqfz_drop_xxx_已盖章V2 的问题）。返回 OpenPdf 同格式 JSON。</summary>
        public string OpenPdfFromBytes(string base64, string fileName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(base64)) return "{\"ok\":false,\"error\":\"空数据\"}";
                byte[] bytes = Convert.FromBase64String(base64);
                string safeName = string.IsNullOrWhiteSpace(fileName) ? "drop.pdf" : Path.GetFileName(fileName.Replace('\\', '/'));
                if (!safeName.ToLowerInvariant().EndsWith(".pdf")) safeName += ".pdf";
                string dir = Path.Combine(Path.GetTempPath(), "pdfqfz_drop_" + _procTag);
                Directory.CreateDirectory(dir);
                string tmp = Path.Combine(dir, safeName);
                // V2.4.0.16：写文件前先释放旧渲染器（pdfium 持有同名临时文件句柄）——
                // 重复拖入同一文件时避免"正由另一进程使用"；_stampDocPath 清空使 OpenPdf 按"换文档"清章
                lock (renderSync)
                {
                    if (_renderer != null) { _renderer.Dispose(); _renderer = null; }
                }
                _stampDocPath = "";
                File.WriteAllBytes(tmp, bytes);
                return OpenPdf(tmp);
            }
            catch (Exception ex)
            {
                return "{\"ok\":false,\"error\":" + Json("拖入文件解析失败：" + ex.Message) + "}";
            }
        }
        /// <summary>关闭当前 PDF 并释放资源。</summary>
        public string ClosePdf()
        {
            lock (renderSync)
            {
                if (_renderer != null) { _renderer.Dispose(); _renderer = null; }
            }
            // V2.4.0.80：关闭文档同步清页渲染缓存
            lock (_renderPageCacheLock) { _renderPageCache.Clear(); }
            lock (_renderGridCacheLock) { _renderGridCache.Clear(); } // V1.0.0.50：同步清网格缩略图缓存
            return "{\"ok\":true}";
        }
        /// <summary>选择源文件夹（FolderBrowserDialog，对齐 WPF ChooseSourceDirectory）；取消返回 {"ok":false,"cancel":true}。</summary>
        public string PickSourceDir()
        {
            try
            {
                using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dlg.Description = "选择要盖章的文件夹";
                    dlg.ShowNewFolderButton = false;
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return "{\"ok\":false,\"cancel\":true}";
                    string dirRet = OpenDirectory(dlg.SelectedPath);
                    if (dirRet.StartsWith("{\"ok\":true")) return dirRet.Substring(0, dirRet.Length - 1) + ",\"path\":" + Json(dlg.SelectedPath) + "}";
                    return dirRet;
                }
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }
        /// <summary>打开文件夹：递归枚举 PDF（排除输出目录）→ 加载第一个预览（对齐 WPF LoadDirectory）。
        /// 返回文件列表与当前文件页数；切换文件走 OpenPdf。</summary>
        public string OpenDirectory(string dir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return "{\"ok\":false,\"error\":\"文件夹不存在\"}";
                // V2.4.0.351：只扫根目录（用户确认：拖入哪个文件夹就只看该文件夹根目录，不看子目录——彻底无穿透机制）。
                // 不再递归子目录：子目录（含历史输出目录"已盖章"/"已处理"）天然不进入列表，无需跳过名单、不存在自包含。
                var files = new List<string>();
                try
                {
                    foreach (var f in Directory.GetFiles(dir, "*.pdf"))
                        files.Add(f);
                }
                catch { }
                files.Sort(StringComparer.OrdinalIgnoreCase);
                if (files.Count == 0) return "{\"ok\":false,\"error\":\"该文件夹下没有 PDF 文件\"}";

                _dirMode = true;
                _dirSource = dir;
                _dirFiles = files;

                // V2.4.0.55：新会话语义——重新导入文件夹（即使与上次同一路径）强制清空预览章，
                // 避免 OpenPdf 阶段6"同文档重开保留章"导致手动章/范围页章残留
                _stampPlacements.Clear();
                _watermarks.ClearAll(); // V2.4.0.96：重新导入文件夹=新会话，清空水印框
                _stampDocPath = null;
                // 加载第一个文件预览（复用 OpenPdf 的渲染/清章逻辑；OpenPdf 保留 _dirMode）
                string first = files[0];
                string r = OpenPdf(first);
                var robj = ParseJsonObject(r);
                if (robj == null || !(robj.TryGetValue("ok", out object okv) && okv is bool bok && bok))
                    return "{\"ok\":false,\"error\":\"无法打开第一个文件\"}";
                var d = new Dictionary<string, object>
                {
                    ["ok"] = true,
                    // V2.4.0.16：files/current 返回完整路径——前端取目录设保存目录、切换文件 openPdf 均需完整路径
                    // （原只返回文件名导致 applyDir 的 saveDir 取目录为空、switchDirFile 打开失败）
                    ["files"] = files.ToList(),
                    ["count"] = files.Count,
                    ["current"] = first,
                    ["pageCount"] = robj.TryGetValue("pageCount", out object pv) ? pv : 0,
                    // V2.4.0.351：返回用户拖入的目录本身（去尾部分隔符）——前端以此锚定源目录与默认输出目录，
                    // 即使根目录无 PDF 也以拖入文件夹为准，路径不穿透到子目录
                    ["source"] = dir.TrimEnd('\\', '/')
                };
                return Json(d);
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }
        /// <summary>V2.4.0.21：打开多个文件（Win32 OLE 拖放多文件，对齐 WPF LoadSourceFiles(files)）。
        /// 非目录模式（dirMode=false）：文件列表可切换（工具栏下拉），saveDir 走文件所在目录。参数为路径数组 JSON。</summary>
        public string OpenFiles(string json)
        {
            try
            {
                var paths = ParseStringArray(json);
                var files = paths
                    .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p)
                        && p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (files.Count == 0) return "{\"ok\":false,\"error\":\"没有可打开的 PDF 文件\"}";
                // 多文件 = 文件列表模式（非目录模式：saveDir 由前端按当前文件所在目录设置，对齐 WPF LoadSourceFiles）
                _dirMode = false;
                _dirSource = null;
                _dirFiles = files;
                string first = files[0];
                string r = OpenPdf(first);
                var robj = ParseJsonObject(r);
                if (robj == null || !(robj.TryGetValue("ok", out object okv) && okv is bool bok && bok))
                    return "{\"ok\":false,\"error\":\"无法打开第一个文件\"}";
                var d = new Dictionary<string, object>
                {
                    ["ok"] = true,
                    ["files"] = files.ToList(),
                    ["count"] = files.Count,
                    ["current"] = first,
                    ["pageCount"] = robj.TryGetValue("pageCount", out object pv) ? pv : 0
                };
                return Json(d);
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }
        /// <summary>解析 JSON 字符串数组（["a","b"]；路径含中文原样）。</summary>
        private static List<string> ParseStringArray(string json)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(json)) return list;
            string s = json.Trim();
            if (!s.StartsWith("[")) return list;
            int pos = 1;
            while (pos < s.Length)
            {
                int q = s.IndexOf('"', pos);
                if (q < 0) break;
                int qe = s.IndexOf('"', q + 1);
                if (qe < 0) break;
                list.Add(s.Substring(q + 1, qe - q - 1));
                pos = qe + 1;
            }
            return list;
        }
        /// <summary>退出目录模式（单选文件后调用，对齐 WPF LoadSourceFiles）。</summary>
        public string ResetDirMode()
        {
            _dirMode = false;
            _dirSource = null;
            _dirFiles = null;
            return "{\"ok\":true}";
        }
    }
}
