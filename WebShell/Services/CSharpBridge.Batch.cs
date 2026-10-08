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
    /// V2.4.0.83：桥按业务拆 partial（本文件：阶段 6 批量预览/批量生成）。
    /// </summary>
    public partial class CSharpBridge
    {
        /// <summary>选输出目录（FolderBrowserDialog）。返回路径或 "cancel"。</summary>
        public string PickOutputDir()
        {
            try
            {
                using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dlg.Description = "选择输出目录";
                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        AppConfig.OutputDir = dlg.SelectedPath;
                        return dlg.SelectedPath;
                    }
                }
                return "cancel";
            }
            catch (Exception ex) { return "err:" + ex.Message; }
        }
        /// <summary>V2.4.0.405：打开文件/文件夹（输出目录日志点击）。返回 "ok" 或 "err:..."。</summary>
        public string OpenFolder(string path)
        {
            try
            {
                // V2.4.0.409：规范化路径——TrimEnd 尾反斜杠（explorer.exe "C:\a\b\" 的 " 会被解析为转义引号，
                // 导致打开错误位置（默认"文档"）；Trim 去空白防前端传参含空格
                path = (path ?? "").Trim().TrimEnd('\\', '/');
                // v1.0.0.2：真根因=前端路径为正斜杠（C:/...），explorer 对"正斜杠+中文"路径解析失败会回退打开"文档"；
                // 统一转为 Windows 反斜杠格式后再打开（Directory.Exists 两者都认，explorer 只认反斜杠）
                path = path.Replace('/', '\\');
                // 诊断：记录点击路径到统一 app_log.log（定位"输出目录打开文档"问题）
                try { AppLog.Write("[OpenFolder] path=" + path + " isDir=" + Directory.Exists(path) + " isFile=" + File.Exists(path)); } catch { }
                if (string.IsNullOrWhiteSpace(path)) return "err:路径为空";
                if (Directory.Exists(path))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
                    return "ok";
                }
                if (File.Exists(path))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
                    return "ok";
                }
                return "err:路径不存在";
            }
            catch (Exception ex) { return "err:" + ex.Message; }
        }

        /// <summary>V2.4.0.61：批量放置印章（所有文件）——一次按批量参数把页面章放入文件夹内全部文件（batchId=-1），
        /// 只做内存预览不写文件。返回 {ok,results:[{file,ok,count,skippedCount,error}]}。</summary>
        public string BatchPreviewAll(int rangeMode, int rangeStart, int rangeEnd, int xPct, int yPct, bool clampEnabled, string outDir)
        {
            try
            {
                if (_dirFiles == null || _dirFiles.Count == 0) return "{\"ok\":false,\"error\":\"请先选择文件夹\"}";
                if (_renderer == null) return "{\"ok\":false,\"error\":\"未打开 PDF\"}";
                string stampPath = CurrentStampPath();
                if (string.IsNullOrWhiteSpace(stampPath) || !File.Exists(stampPath))
                    return "{\"ok\":false,\"error\":\"请先选择印章\"}";
                string outDirT = (outDir ?? "").Trim();
                if (outDirT.Length == 0)
                {
                    string srcDir2 = !string.IsNullOrWhiteSpace(_dirSource) ? _dirSource : null;
                    if (string.IsNullOrWhiteSpace(srcDir2) || !Directory.Exists(srcDir2))
                        return "{\"ok\":false,\"error\":\"请先在【源文件】选择文件夹\"}";
                    outDirT = Path.Combine(srcDir2, "已处理");
                }
                var sp = AppConfig.LoadStampParams(CurrentStampName() ?? "");
                var prepOptions = new StampOptions
                {
                    StampImagePath = stampPath,
                    OutputPath = outDirT,
                    QfzType = 0,
                    RemoveWhite = sp.RemoveWhite,
                    Opacity = sp.Opacity,
                    Rotation = sp.Rotation,
                    OriginalRotationCrop = true, // v2.4.0.76: 固定切边
                    SizeMm = sp.Size,
                    WzType = 0,
                    WzPercent = 0,
                    MaxSplit = 1,
                    Placements = new StampPlacementCollection(),
                    TextureEnabled = sp.TextureQuality,
                    TextureSeed = NewTextureSeed(),
                    TextureBrightness = sp.TextureBrightness, TextureBlob = sp.TextureBlob,
                    TextureGradient = sp.TextureGradient, TextureWhite = sp.TextureWhite,
                    TextureSpot = sp.TextureSpot, TextureRadial = sp.TextureRadial, TextureCast = sp.TextureCast,
                    TextureKb = NewTextureK(), TextureKblob = NewTextureK(), TextureKgrad = NewTextureK(),
                    TextureKwhite = NewTextureK(), TextureKspot = NewTextureK(),
                    TextureKradial = NewTextureK(), TextureKcast = NewTextureK()
                };
                if (!StampEngine.PrepareStampResources(prepOptions, out Bitmap seamImage, out float xzbl, _ => { }))
                    return "{\"ok\":false,\"error\":\"印章准备失败，请检查印章图片\"}";
                try
                {
                    float sfbl = (100f * sp.Size * xzbl * 72) / (25.4f * seamImage.Width);
                    float imgWpt = seamImage.Width * sfbl / 100f;
                    float imgHpt = seamImage.Height * sfbl / 100f;
                    // V2.4.0.81：生成互斥——防与 GenerateFiles/重复点击并发（批次2-③）；占用失败释放 seamImage
                    if (!TryEnterBatch()) { try { seamImage.Dispose(); } catch { } return "{\"ok\":false,\"error\":\"正在处理中，请稍候\"}"; }
                    // V2.4.0.63: this placement allocates one unique negative batch id; multi-batch coexists without clearing old batches
                    int previewBatchId = _stampPlacements.CreatePreviewBatchId();
                    // V2.4.0.81：快照文件列表 + 参数局部化（Task 内只读；不再 OpenPdfKeep 换渲染器，零 _renderer 竞态）
                    var filesSnap = new List<string>(_dirFiles);
                    int rangeModeL = rangeMode, rangeStartL = Math.Max(1, rangeStart), rangeEndL = Math.Max(1, rangeEnd);
                    bool clampL = clampEnabled; float xL = xPct, yL = yPct;
                    Task.Run(() =>
                    {
                        try
                        {
                            var results = new List<object>();
                            foreach (var f in filesSnap)
                            {
                                string name = Path.GetFileName(f);
                                PdfReader reader = null;
                                try
                                {
                                    try { reader = new PdfReader(f); }
                                    catch
                                    {
                                        results.Add(new Dictionary<string, object> { ["file"] = name, ["ok"] = false, ["error"] = "无法读取文件（加密或损坏）" });
                                        continue;
                                    }
                                    int pageCount = reader.NumberOfPages;
                                    var pages = BatchRangeCalculator.ComputePages(rangeModeL, rangeStartL, rangeEndL, pageCount);
                                    int placed = 0, skipped = 0;
                                    foreach (int page in pages)
                                    {
                                        var psize = reader.GetPageSize(page);
                                        int rot = reader.GetPageRotation(page);
                                        float pw = (rot == 90 || rot == 270) ? psize.Height : psize.Width;
                                        float ph = (rot == 90 || rot == 270) ? psize.Width : psize.Height;
                                        var c = BatchRangeCalculator.ClampCenter(xL, yL, clampL, imgWpt, imgHpt, pw, ph);
                                        if (c == null) { skipped++; continue; }
                                        // V2.4.0.81：显式 docPath——不依赖/不修改 _stampDocPath（原 OpenPdfKeep 换渲染器 31 次已移除）
                                        AddPlacementCore(f, c.Value.X, c.Value.Y, page, previewBatchId, "batch");
                                        placed++;
                                    }
                                    results.Add(new Dictionary<string, object> { ["file"] = name, ["ok"] = true, ["count"] = placed, ["skippedCount"] = skipped });
                                }
                                finally { if (reader != null) reader.Close(); }
                            }
                            _postEvent("{\"kind\":\"batch-preview-done\",\"payload\":" + Json(new Dictionary<string, object> { ["ok"] = true, ["results"] = results }) + "}");
                        }
                        catch (Exception ex)
                        {
                            _postEvent("{\"kind\":\"batch-preview-done\",\"payload\":{\"ok\":false,\"error\":" + Json(ex.Message) + "}}");
                        }
                        finally { if (seamImage != null) { try { seamImage.Dispose(); } catch { } } ExitBatch(); }
                    });
                    return "{\"ok\":true,\"started\":true}";
                }
                catch (Exception ex)
                {
                    ExitBatch();
                    return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}";
                }
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }
        /// <summary>V2.4.0.63：撤销放置——LIFO 撤销最近一批批量放置章（batchId 最小的负值批次，即最新批次）。
        /// 返回 {ok, removed, remaining}；remaining=0 表示已无批量章可撤销。</summary>
        public string RemoveBatchPreviewAll()
        {
            try
            {
                int removed = _stampPlacements.RemoveLatestPreviewBatch(out int remaining);
                return "{\"ok\":true,\"removed\":" + removed + ",\"remaining\":" + remaining + "}";
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }
        /// <summary>V2.4.0.63：删除指定批量放置批次（batchId&lt;0）在所有文件上的章（右键-删除整个批次）。</summary>
        public string RemovePreviewBatch(int batchId)
        {
            try
            {
                int removed = _stampPlacements.RemovePreviewBatch(batchId);
                return "{\"ok\":true,\"removed\":" + removed + "}";
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }
        /// <summary>V2.4.0.61：批量章右键"仅删除当前页"——删除当前文件指定页的 batchId=-1 章。</summary>
        /// <summary>V2.4.0.63：批量章右键-仅删除当前页——删除当前文件指定页上指定批次的批量章。</summary>
        public string DeletePreviewBatchOnPage(int batchId, int page)
        {
            try
            {
                if (_stampDocPath == null || !File.Exists(_stampDocPath))
                    return "{\"ok\":false,\"error\":\"请先加载 PDF 文件\"}";
                int removed = _stampPlacements.RemovePreviewBatchOnPage(_stampDocPath, page, batchId);
                return "{\"ok\":true,\"removed\":" + removed + "}";
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }
        public string BatchPreview(string filePath, int rangeMode, int rangeStart, int rangeEnd,
            int xPct, int yPct, bool clampEnabled, string outDir)
        {
            try
            {
                if (_renderer == null) return "{\"ok\":false,\"error\":\"未打开 PDF\"}";
                string stampPath = CurrentStampPath();
                if (string.IsNullOrWhiteSpace(stampPath) || !File.Exists(stampPath))
                    return "{\"ok\":false,\"error\":\"请先选择印章\"}";
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                    return "{\"ok\":false,\"error\":\"文件不存在\"}";
                // 打开目标文件（保留已有章），同步完成
                string open = OpenPdfKeep(filePath);
                var or = ParseJsonObject(open);
                bool openOk = or != null && or.TryGetValue("ok", out var ov) && ov is bool bok && bok;
                if (!openOk) return open;
                // V2.4.0.58：重算前先清空该文件的旧批量预览章（batchId=-1 约定），再添加新章——
                // 避免参数变化后新旧章叠加；手动章(0)/范围页章(>0)不受影响
                _stampPlacements.RemoveBatchPreview(_stampDocPath);
                // V2.4.0.55：OutputPath 必须非空（PrepareStampResources 会 CreateDirectory）——
                // 空则兜底 源/已盖章（与批量生成一致）；前端主防线是调用前校验并传 saveDir
                string outDirT = (outDir ?? "").Trim();
                if (outDirT.Length == 0)
                {
                    string srcDir2 = _dirMode && !string.IsNullOrWhiteSpace(_dirSource) ? _dirSource : null;
                    if (string.IsNullOrWhiteSpace(srcDir2) || !Directory.Exists(srcDir2))
                        return "{\"ok\":false,\"error\":\"请先在【源文件】选择文件夹\"}";
                    outDirT = Path.Combine(srcDir2, "已处理");
                }
                var sp = AppConfig.LoadStampParams(CurrentStampName() ?? "");
                var prepOptions = new StampOptions
                {
                    StampImagePath = stampPath,
                    OutputPath = outDirT,
                    QfzType = 0,
                    RemoveWhite = sp.RemoveWhite,
                    Opacity = sp.Opacity,
                    Rotation = sp.Rotation,
                    OriginalRotationCrop = true, // v2.4.0.76: 固定切边
                    SizeMm = sp.Size,
                    WzType = 0,
                    WzPercent = 0,
                    MaxSplit = 1,
                    Placements = new StampPlacementCollection(),
                    TextureEnabled = sp.TextureQuality,
                    TextureSeed = NewTextureSeed(),
                    TextureBrightness = sp.TextureBrightness, TextureBlob = sp.TextureBlob,
                    TextureGradient = sp.TextureGradient, TextureWhite = sp.TextureWhite,
                    TextureSpot = sp.TextureSpot, TextureRadial = sp.TextureRadial, TextureCast = sp.TextureCast,
                    TextureKb = NewTextureK(), TextureKblob = NewTextureK(), TextureKgrad = NewTextureK(),
                    TextureKwhite = NewTextureK(), TextureKspot = NewTextureK(),
                    TextureKradial = NewTextureK(), TextureKcast = NewTextureK()
                };
                if (!StampEngine.PrepareStampResources(prepOptions, out Bitmap seamImage, out float xzbl, _ => { }))
                    return "{\"ok\":false,\"error\":\"印章准备失败，请检查印章图片\"}";
                try
                {
                    float sfbl = (100f * sp.Size * xzbl * 72) / (25.4f * seamImage.Width);
                    float imgWpt = seamImage.Width * sfbl / 100f;
                    float imgHpt = seamImage.Height * sfbl / 100f;
                    int pageCount = 0, placed = 0, skipped = 0;
                    PdfReader reader = null;
                    try
                    {
                        try { reader = new PdfReader(filePath); }
                        catch { return "{\"ok\":false,\"error\":\"无法读取文件（加密或损坏）\"}"; }
                        pageCount = reader.NumberOfPages;
                        var pages = BatchRangeCalculator.ComputePages(rangeMode, rangeStart, rangeEnd, pageCount);
                        // V2.4.0.66：一次重算统一一个批次号（原循环内逐页 CreatePreviewBatchId 导致"撤销退化为逐个撤销"）
                        int previewBatchId = _stampPlacements.CreatePreviewBatchId();
                        foreach (int page in pages)
                        {
                            var psize = reader.GetPageSize(page);
                            int rot = reader.GetPageRotation(page);
                            float pw = (rot == 90 || rot == 270) ? psize.Height : psize.Width;
                            float ph = (rot == 90 || rot == 270) ? psize.Width : psize.Height;
                            var c = BatchRangeCalculator.ClampCenter(xPct, yPct, clampEnabled, imgWpt, imgHpt, pw, ph);
                            if (c == null) { skipped++; continue; }
                            AddPlacement(c.Value.X, c.Value.Y, page, previewBatchId, "batch");
                            placed++;
                        }
                    }
                    finally { if (reader != null) reader.Close(); }
                    return Json(new Dictionary<string, object>
                    {
                        ["ok"] = true,
                        ["pageCount"] = pageCount,
                        ["count"] = placed,
                        ["skippedCount"] = skipped
                    });
                }
                finally { if (seamImage != null) seamImage.Dispose(); }
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }
        /// <summary>批量核心：逐文件处理（读页数→范围交集→百分比钳制→独立 placements→PDFWatermark+压缩→进度事件）→ 汇总。
        /// outFormat（V1.0.0.46）：pdf=保持原格式；jpg/png=合并产物转图片（每 PDF 建同名文件夹放 p0001.* 系列）。</summary>
        private Dictionary<string, object> RunBatchFiles(List<string> files, string outDirT,
            int rangeMode, int rangeStart, int rangeEnd, int xPct, int yPct, bool clampEnabled,
            int qfzType, int wzType, int wzPercent, int maxSplit, int dpi, int djType, string outFormat,
            OutputNamingOptions naming, Bitmap seamImage, float xzbl, AppConfig.StampParams sp, string stampPath,
            List<StampPlacement> previewSnap, List<WatermarkBox> wmSnap = null)
        {
            var success = new List<string>();
            var skipped = new List<Dictionary<string, string>>();
            var failed = new List<Dictionary<string, string>>();
            int total = files.Count, idx = 0, ruleCount = 0;
            var fileStats = new List<object>();
            foreach (var file in files)
            {
                idx++;
                string name = Path.GetFileName(file);
                string outputPath = OutputFileNamingPolicy.GetNextOutputPath(outDirT, file, naming);
                PdfReader reader = null;
                try
                {
                    try { reader = new PdfReader(file); }
                    catch { skipped.Add(MkSkip(name, "加密或无法打开", file)); continue; }
                    int pageCount = reader.NumberOfPages;
                    int ms = maxSplit <= 0 ? pageCount : Math.Max(1, maxSplit);
                    var col = new StampPlacementCollection();
                    // V2.4.0.67：所见即所得——文件夹模式不再独立计算规则章，输出=预览放置的全部章
                    // （含批量放置章 batchId<0 多批次；撤销后 placements 无该批 → 输出即无章）。
                    // 路径用 Path.GetFullPath 规范化后匹配（大小写/分隔符统一，吸取 V54 输出目录排除教训）
                    var fsManual = 0; var fsRange = 0; var fsText = 0; var fsBatch = 0;
                    if (previewSnap != null && previewSnap.Count > 0)
                    {
                        string fileFull = Path.GetFullPath(file);
                        foreach (var p in previewSnap)
                        {
                            if (!string.IsNullOrWhiteSpace(p.DocumentPath) &&
                                string.Equals(Path.GetFullPath(p.DocumentPath), fileFull, StringComparison.OrdinalIgnoreCase))
                            {
                                col.AddExisting(p);
                                if (p.BatchId < 0) fsBatch++;
                                else if (!p.CenterRatio) fsText++;
                                else if (p.BatchId > 0) fsRange++;
                                else fsManual++;
                            }
                        }
                    }
                    ruleCount += fsBatch; // V2.4.0.67：批量章数=放置的批量放置章（batchId<0，多批次累加）
                    string outDisplay = Path.GetFileName(outputPath); // V1.0.0.46：图片格式时改显示同名文件夹名
                    var options = new StampOptions
                    {
                        StampImagePath = stampPath, OutputPath = outDirT, QfzType = qfzType,
                        RemoveWhite = sp.RemoveWhite, Opacity = sp.Opacity, Rotation = sp.Rotation,
                        OriginalRotationCrop = true, SizeMm = sp.Size, // v2.4.0.76: 固定切边
                        WzType = wzType, WzPercent = wzPercent, MaxSplit = ms, Placements = col,
                        Watermarks = wmSnap, // V2.4.0.96：文件夹模式文字水印（PDFWatermark 内按 file 过滤）
                        TextureEnabled = sp.TextureQuality,
                        TextureSeed = NewTextureSeed(),
                        TextureBrightness = sp.TextureBrightness, TextureBlob = sp.TextureBlob,
                        TextureGradient = sp.TextureGradient, TextureWhite = sp.TextureWhite,
                        TextureSpot = sp.TextureSpot, TextureRadial = sp.TextureRadial, TextureCast = sp.TextureCast,
                        TextureKb = NewTextureK(), TextureKblob = NewTextureK(), TextureKgrad = NewTextureK(),
                        TextureKwhite = NewTextureK(), TextureKspot = NewTextureK(),
                        TextureKradial = NewTextureK(), TextureKcast = NewTextureK()
                    };
                    string output = outputPath;
                    string lastErr = "盖章失败";
                    _postEvent(BatchProgress(idx, total, name, 0, pageCount));
                    bool ok = StampEngine.PDFWatermark(options, seamImage, xzbl, file, output, file,
                        msg => { lastErr = msg; },
                        (d, t) => _postEvent(BatchProgress(idx, total, name, d, t)),
                        active => { if (active) _postEvent("{\"kind\":\"generate-progress\",\"payload\":\"" + "文件生成中：第 " + idx + "/" + total + " 个文件，请勿关闭软件" + "\"}"); }); // V1.0.0.79：去掉文本省略号（动态点点由前端动画），文件夹模式带文件序号
                    // V1.0.0.47：输出格式多选分流——勾选 pdf 时输出 PDF（合并栅格化/叠加可编辑）；勾选 jpg/png 时转图片（每格式同名文件夹 xxx_JPG/xxx_PNG，4 位页码 p0001）；可同时输出
                    int imgCount = 0; var imgFormats = new List<string>();
                    if (ok)
                    {
                        var fmts = StampEngine.ParseOutFormats(outFormat);
                        bool wantPdf = fmts.Contains("pdf");
                        if (wantPdf && djType == 1) StampEngine.PDFToiPDF(output, dpi);
                        foreach (var fmt in fmts)
                        {
                            if (fmt == "pdf") continue;
                            var imgs = StampEngine.PDFToImages(output, outDirT, Path.GetFileNameWithoutExtension(output), dpi, fmt,
                                (d, t) => _postEvent("{\"kind\":\"generate-progress\",\"payload\":\"" + string.Format("正在输出图片：已完成 {0}/{1} 张（{2}%），文件：《{3}》", d, t, t > 0 ? (int)Math.Round(100.0 * d / t) : 0, name) + "\"}")); // V1.0.0.78：图片输出逐张百分比（文件夹批量入口）+ 文件名《》包裹
                            if (imgs.Count > 0) { imgCount += imgs.Count; imgFormats.Add(fmt.ToUpperInvariant()); }
                        }
                        if (imgCount > 0 && !wantPdf) { try { File.Delete(output); } catch { } outDisplay = Path.GetFileNameWithoutExtension(output); }
                    }
                    fileStats.Add(new Dictionary<string, object> {
                        ["index"] = idx, ["name"] = name, ["output"] = outDisplay,
                        ["manual"] = fsManual, ["range"] = fsRange, ["text"] = fsText, ["batch"] = fsBatch,
                        ["imgCount"] = imgCount, ["imgFormats"] = string.Join(",", imgFormats)
                    });
                    if (ok) success.Add(name); else failed.Add(MkSkip(name, lastErr, file));
                }
                catch (Exception ex) { failed.Add(MkSkip(name, ex.Message, file)); }
                finally { if (reader != null) reader.Close(); }
            }
            var result = new Dictionary<string, object>
            {
                ["ok"] = failed.Count == 0,
                ["total"] = total,
                ["success"] = success,
                ["skipped"] = skipped,
                ["failed"] = failed,
                ["ruleCount"] = ruleCount,
                ["fileStats"] = fileStats
            };
            return result;
        }
        private static Dictionary<string, string> MkSkip(string name, string reason, string path)
        {
            return new Dictionary<string, string> { ["name"] = name, ["reason"] = reason, ["path"] = path };
        }
        private string BatchProgress(int index, int total, string name, int done, int totalPage)
        {
            var o = new Dictionary<string, object>
            {
                ["index"] = index, ["total"] = total, ["file"] = name,
                ["done"] = done, ["totalPage"] = totalPage
            };
            return "{\"kind\":\"batch-progress\",\"payload\":" + Json(o) + "}";
        }
        /// <summary>V2.4.0.59：统计印章类型（手动 batchId=0+中心比例 / 范围 batchId>0+中心比例 / 文字 batchId>0+左上比例 / 批量预览 batchId=-1）。</summary>
        private Dictionary<string, int> BuildStampStats(IEnumerable<PDFQFZ.Library.StampPlacement> snap)
        {
            var st = new Dictionary<string, int> { ["manual"] = 0, ["range"] = 0, ["text"] = 0, ["batch"] = 0 };
            if (snap == null) return st;
            foreach (var p in snap)
            {
                if (p.BatchId < 0) st["batch"]++;
                else if (!p.CenterRatio) st["text"]++;
                else if (p.BatchId > 0) st["range"]++;
                else st["manual"]++;
            }
            return st;
        }
        /// <summary>V2.4.0.59：路径是否在目标目录或其子目录内（Path.GetFullPath 规范化，OrdinalIgnoreCase）——批量扫描排除输出目录。</summary>
        private static bool IsPathInside(string path, string dir)
        {
            string p = Path.GetFullPath(path).TrimEnd('\\', '/');
            string d = Path.GetFullPath(dir).TrimEnd('\\', '/');
            if (string.Equals(p, d, StringComparison.OrdinalIgnoreCase)) return true;
            return p.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || p.StartsWith(d + '/', StringComparison.OrdinalIgnoreCase);
        }
        private OutputNamingOptions BuildBatchNaming()
        {
            return new OutputNamingOptions
            {
                Mark = AppConfig.OutputNameMark ?? "已处理V",
                BeforeName = AppConfig.OutputNamePos == 1,
                SeqType = AppConfig.OutputNameSeqType < 0 ? 0 : (AppConfig.OutputNameSeqType > 2 ? 2 : AppConfig.OutputNameSeqType),
                Pad = AppConfig.OutputNamePad < 1 ? 1 : (AppConfig.OutputNamePad > 3 ? 3 : AppConfig.OutputNamePad),
                UseTimestamp = AppConfig.OutputNameTs,
                TsFormat = string.IsNullOrWhiteSpace(AppConfig.OutputNameTsFormat) ? "yyyyMMdd" : AppConfig.OutputNameTsFormat
            };
        }
        /// <summary>生成文件（对齐 WPF RunStampBatchAsync + StampBatchWorker）：UI 线程校验/快照，
        /// Task.Run 后台批处理，完成后推送 generate-done 事件。mode=merge|overlay；qfzType 0加盖/1不加/2单页/3双页/4随意。
        /// format（V1.0.0.46 需求3）：pdf/jpg/png 输出格式；jpg/png 时合并/叠加产物均转图片（每 PDF 同名文件夹 p0001.* 系列）。</summary>
        public string GenerateFiles(string outDir, string mode, int dpi, string mark, int pos, int seqType, int pad,
            bool ts, string tsFormat, int qfzType, int wzType, int wzPercent, int maxSplit, bool forceConfirm,
            int batchRange, int batchStart, int batchEnd, int xPct, int yPct, bool clampEnabled, bool batchForce,
            string format = "pdf")
        {
            try
            {
                if (_renderer == null) { WriteLog("[GEN-FAIL] 生成失败：渲染器未就绪（请先加载 PDF 文件）"); return "{\"ok\":false,\"error\":\"请先加载 PDF 文件\"}"; }
                if (_isDebugPage) { WriteLog("[GEN-FAIL] 生成失败：调试页状态（请先加载 PDF 文件）"); return "{\"ok\":false,\"error\":\"请先加载 PDF 文件\"}"; }
                if (string.IsNullOrWhiteSpace(_stampDocPath) || !System.IO.File.Exists(_stampDocPath))
                { WriteLog("[GEN-FAIL] 生成失败：未加载有效 PDF（docPath=" + (_stampDocPath ?? "") + "）"); return "{\"ok\":false,\"error\":\"请先加载 PDF 文件\"}"; }
                string outDirT = (outDir ?? "").Trim();
                if (outDirT.Length == 0) { WriteLog("[GEN-FAIL] 生成失败：保存目录为空"); return "{\"ok\":false,\"error\":\"请先选择保存目录\"}"; }
                try { Directory.CreateDirectory(outDirT); }
                catch { WriteLog("[GEN-FAIL] 生成失败：无法创建输出目录（" + outDirT + "）"); return "{\"ok\":false,\"error\":\"无法创建输出目录\"}"; }

                // V1.0.0.75：骑缝章只与前端开关（seamType）相关——水印开关/水印框不干预 qfzType；
                // 纯水印任务由前端传 qfzType=1（下方 L493 无章时不校验印章）保证，此处不再强制改写
                // （V392 原「水印模式强制不加骑缝章」与 V54 水印开关常驻冲突，已由用户澄清取消）
                bool wantsSeam = qfzType != 1;
                bool hasStamps = _stampPlacements.Count > 0;
                // V2.4.0.69：所见即所得，取消"无章确认"流程——无章也直接生成（输出=预览放置的章，无章即无章副本/仅骑缝章）。
                // 此前 needConfirm 走 ElMessageBox.confirm 在 WebView2 下挂起不渲染（V2.4.0.60 已知问题复发），流程卡死；
                // 用户确认"预览所见即所得，取消无章确认"。forceConfirm 参数保留（前端仍传，兼容签名）。
                bool needStamp = wantsSeam || hasStamps;
                string stampPath = CurrentStampPath();
                if (needStamp && (string.IsNullOrWhiteSpace(stampPath) || !System.IO.File.Exists(stampPath)))
                { WriteLog("[GEN-FAIL] 生成失败：印章图片无效（name=" + (CurrentStampName() ?? "") + " path=" + (stampPath ?? "") + "），请重新选择印章；若配置文件编码损坏请重新导入印章"); return "{\"ok\":false,\"error\":\"请先选择印章图片\"}"; }

                var spG = AppConfig.LoadStampParams(CurrentStampName());
                var options = new StampOptions
                {
                    StampImagePath = stampPath,
                    OutputPath = outDirT,
                    QfzType = qfzType,
                    RemoveWhite = spG.RemoveWhite,
                    Opacity = spG.Opacity,
                    Rotation = spG.Rotation,
                    OriginalRotationCrop = true, // v2.4.0.76: 固定切边
                    SizeMm = spG.Size,
                    WzType = wzType,
                    WzPercent = wzPercent,
                    MaxSplit = Math.Max(0, maxSplit), // V390：保留 0=自动语义，由 StampEngine 按该文件页数解析（此前 Math.Max(1,0)=1 → 骑缝章 tmp=0 除零）
                    Placements = _stampPlacements,
                    Watermarks = _watermarkEnabled ? _watermarks.Snapshot() : null, // V2.4.0.96：文字水印（开关关闭不输出；PDFWatermark 按 sourcepath 过滤）
                    TextureEnabled = spG.TextureQuality,
                    TextureSeed = NewTextureSeed(),
                    TextureBrightness = spG.TextureBrightness, TextureBlob = spG.TextureBlob,
                    TextureGradient = spG.TextureGradient, TextureWhite = spG.TextureWhite,
                    TextureSpot = spG.TextureSpot, TextureRadial = spG.TextureRadial, TextureCast = spG.TextureCast,
                    TextureKb = NewTextureK(), TextureKblob = NewTextureK(), TextureKgrad = NewTextureK(),
                    TextureKwhite = NewTextureK(), TextureKspot = NewTextureK(),
                    TextureKradial = NewTextureK(), TextureKcast = NewTextureK()
                };
                if (!StampEngine.PrepareStampResources(options, out Bitmap seamImage, out float xzbl, _ => { }))
                    return "{\"ok\":false,\"error\":\"印章准备失败，请检查印章图片\"}";

                int djType = string.Equals(mode, "overlay", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                if (dpi < 72) dpi = 72; if (dpi > 600) dpi = 600;
                var naming = new OutputNamingOptions
                {
                    Mark = mark ?? "",
                    BeforeName = pos == 1,
                    SeqType = seqType < 0 ? 0 : (seqType > 2 ? 2 : seqType),
                    Pad = pad < 1 ? 1 : (pad > 3 ? 3 : pad),
                    UseTimestamp = ts,
                    TsFormat = string.IsNullOrWhiteSpace(tsFormat) ? "yyyyMMdd" : tsFormat
                };
                // V2.4.0.59：文件夹模式统一到"盖章并生成文件"——按批量规则逐文件处理（规则章 + 合并已放置章），
                // 进度/汇总走 batch-progress/batch-done 事件（前端写系统日志）；单文件走下方 StampBatchWorker
                // V2.4.0.68：文件夹模式异步化——原同步 RunBatchFiles 阻塞 UI 线程，大文件夹下
                // batch-progress/batch-done 事件（Dispatcher.Invoke 排队）与 invoke resolve 全被堵，
                // 前端按钮一直 loading、日志不更新、文件迟迟不产出。改 Task.Run 与单文件模式一致。
                if (_dirMode && !string.IsNullOrWhiteSpace(_dirSource) && Directory.Exists(_dirSource))
                {
                    try
                    {
                        // V2.4.0.81：生成互斥——防重复点击/与批量放置并发（批次2-③）
                        if (!TryEnterBatch()) return "{\"ok\":false,\"error\":\"正在处理中，请稍候\"}";
                        string outDirB = outDirT.Length == 0 ? Path.Combine(_dirSource.TrimEnd('\\', '/'), "已处理") : outDirT;
                        try { Directory.CreateDirectory(outDirB); }
                        catch { ExitBatch(); return "{\"ok\":false,\"error\":\"无法创建输出目录\"}"; }
                        // V2.4.0.396: 生成侧与加载侧对齐——只扫根目录不穿透子目录(V2.4.0.351 用户规范: 拖入哪个文件夹就只看该文件夹根目录)
                        var batchFiles = Directory.GetFiles(_dirSource, "*.pdf", SearchOption.TopDirectoryOnly)
                            .Where(f => !IsPathInside(f, outDirB))
                            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
                        if (batchFiles.Count == 0) { ExitBatch(); return "{\"ok\":false,\"error\":\"源文件夹中没有 PDF 文件\"}"; }
                        var spB = AppConfig.LoadStampParams(CurrentStampName() ?? "");
                        int djB = string.Equals(mode, "overlay", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                        int dpiB = dpi < 72 ? 72 : (dpi > 600 ? 600 : dpi);
                        var previewSnap = _stampPlacements.Snapshot(); // UI 线程快照，Task 内只读快照不碰共享集合
                        var wmSnap = _watermarkEnabled ? _watermarks.Snapshot() : null; // V2.4.0.96：文字水印框快照（文件夹模式逐文件按 sourcepath 过滤）
                        var filesB = batchFiles; var outDirB2 = outDirB; var namingB = naming;
                        Task.Run(() =>
                        {
                            try
                            {
                                var res = RunBatchFiles(filesB, outDirB2,
                                    batchRange, Math.Max(1, batchStart), Math.Max(1, batchEnd),
                                    xPct, yPct, clampEnabled,
                                    qfzType, wzType, wzPercent, maxSplit, dpiB, djB, format, namingB,
                                    seamImage, xzbl, spB, stampPath, previewSnap, wmSnap);
                                var statsB = BuildStampStats(_stampPlacements.Snapshot());
                                statsB["batch"] = (res.TryGetValue("ruleCount", out object rc) && rc is int ri) ? ri : 0;
                                res["stats"] = statsB;
                                res["outDir"] = outDirB2; // V2.4.0.407：输出目录供前端日志可点击
                                _postEvent("{\"kind\":\"batch-done\",\"payload\":" + Json(res) + "}");
                            }
                            catch (Exception ex)
                            {
                                _postEvent("{\"kind\":\"batch-done\",\"payload\":{\"success\":[],\"skipped\":[],\"failed\":[{\"name\":\"\",\"reason\":" + Json(ex.Message) + "}]}}");
                            }
                            finally { if (seamImage != null) { try { seamImage.Dispose(); } catch { } } ExitBatch(); }
                        });
                        return "{\"ok\":true,\"started\":true}";
                    }
                    catch (Exception ex) { ExitBatch(); return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
                }

                var request = new StampBatchRequest
                {
                    Options = options,
                    Source = _stampDocPath,
                    OutDir = outDirT,
                    DjType = djType,
                    QualityDpi = dpi,
                    OutFormat = format, // V1.0.0.46：输出格式贯通（StampBatchWorker 内分流）
                    NamingOptions = naming,
                    DirMode = false
                };
                string srcName = _dirMode && _dirSource != null ? Path.GetFileName(_dirSource.TrimEnd('\\', '/')) : Path.GetFileName(_stampDocPath);
                // 生成前预计算本次输出路径（完成后 summary 用同一路径；生成后计算会递进到下一版本号）
                string expectedOutput = OutputFileNamingPolicy.GetNextOutputPath(outDirT, _dirMode && _dirSource != null ? _dirSource : _stampDocPath, naming);

                // V2.4.0.81：生成互斥——防重复点击/与批量放置并发（批次2-③）；seamImage 已分配，占用失败需释放
                if (!TryEnterBatch()) { try { seamImage.Dispose(); } catch { } return "{\"ok\":false,\"error\":\"正在处理中，请稍候\"}"; }
                // 后台批处理（零控件引用，与 WPF StampBatchWorker 相同）；完成后推送事件
                Task.Run(() =>
                {
                    var logs = new List<string>();
                    bool anyFail = false;
                    try
                    {
                        bool hasFailures = StampBatchWorker.Run(request, seamImage, xzbl,
                            (m, e) => { logs.Add((e ? "[失败] " : "") + m); _postEvent("{\"kind\":\"generate-log\",\"payload\":" + Json(new Dictionary<string, object> { ["msg"] = m, ["err"] = e }) + "}"); }, /* V1.0.0.83：单文件日志实时推送（原只收集 logs 等 generate-done 一次性发——「正在处理第 1/1 个文件」生成完毕才出现）；generate-done 的 logs 仍带作为兜底，前端去重 */
                            p => { /* 进度事件单独推送 */ _postEvent("{\"kind\":\"generate-progress\",\"payload\":" + Json(p) + "}"); },
                            null); // V1.0.0.78：onSaving 已弃用——写盘提示文案改由 StampBatchWorker 内部经 onProgress 推送（单文件/文件夹分别带不带文件序号）
                        anyFail = hasFailures;
                    }
                    catch (Exception ex)
                    {
                        anyFail = true;
                        logs.Add("[失败] 处理过程中发生错误：" + ex.Message);
                    }
                    finally
                    {
                        if (seamImage != null) seamImage.Dispose();
                        ExitBatch(); // V2.4.0.81：Task 完成释放生成互斥
                    }
                    var result = new Dictionary<string, object>
                    {
                        ["ok"] = !anyFail,
                        ["source"] = srcName,
                        ["logs"] = logs,
                        ["stats"] = BuildStampStats(_stampPlacements.Snapshot()),
                        ["summary"] = anyFail ? "部分或全部文件生成失败，详见日志"
                            : OutputFileNamingPolicy.BuildSuccessMessage(srcName, expectedOutput),
                        ["outDir"] = outDirT // V2.4.0.407：输出目录供前端日志可点击（此前缺失导致打开文档目录）
                    };
                    _postEvent("{\"kind\":\"generate-done\",\"payload\":" + Json(result) + "}");
                });
                return "{\"ok\":true,\"started\":true}";
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }
    }
}
