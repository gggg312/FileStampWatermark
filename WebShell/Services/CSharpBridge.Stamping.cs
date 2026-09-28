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
    /// V2.4.0.83：桥按业务拆 partial（本文件：阶段 5 盖章/预览/放置/自动盖章）。
    /// </summary>
    public partial class CSharpBridge
    {
        /// <summary>随机位移（mm）：横/纵各自 -max~+max 均匀随机（对齐 WPF GetRandomOffset）。</summary>
        private static void GetRandomOffset(bool randomOn, float maxXMm, float maxYMm, out float dxMm, out float dyMm)
        {
            dxMm = 0f; dyMm = 0f;
            if (!randomOn || (maxXMm <= 0f && maxYMm <= 0f)) return;
            lock (StampRandomGenerator)
            {
                if (maxXMm > 0f) dxMm = (float)((StampRandomGenerator.NextDouble() * 2.0 - 1.0) * maxXMm);
                if (maxYMm > 0f) dyMm = (float)((StampRandomGenerator.NextDouble() * 2.0 - 1.0) * maxYMm);
            }
        }
        /// <summary>随机旋转有效角度：base + random(-range, +range)（对齐 WPF GetEffectiveRotation）。</summary>
        private static int GetEffectiveRotation(int baseRotation, bool randomOn, int range)
        {
            if (randomOn && range > 0)
            {
                lock (StampRandomGenerator) { return baseRotation + StampRandomGenerator.Next(-range, range + 1); }
            }
            return baseRotation;
        }
        private string CurrentStampName()
        {
            return AppConfig.LastSelectedStampNames != null && AppConfig.LastSelectedStampNames.Count > 0
                ? AppConfig.LastSelectedStampNames[0] : null;
        }
        private string CurrentStampPath()
        {
            string name = CurrentStampName();
            if (string.IsNullOrWhiteSpace(name)) return null;
            var e = AppConfig.LoadStampEntries()
                .FirstOrDefault(x => string.Equals(x.DisplayName, name.Trim(), StringComparison.OrdinalIgnoreCase));
            return e != null ? e.Path : null;
        }
        /// <summary>手动盖章：xRatio/yRatio 中心比例(0~1)，page 从 1 起。返回 {ok,id,url,imgW,imgH,sizeMm,x,y,rotation,randomRotation,offsetX,offsetY}。</summary>
        public string AddManualStamp(float xRatio, float yRatio, int page)
        {
            try
            {
                if (_renderer == null) return "{\"ok\":false,\"error\":\"未打开 PDF\"}";
                string stampPath = CurrentStampPath();
                if (string.IsNullOrWhiteSpace(stampPath) || !File.Exists(stampPath))
                    return "{\"ok\":false,\"error\":\"请先选择印章\"}";
                var placement = AddPlacement(Math.Max(0f, Math.Min(1f, xRatio)), Math.Max(0f, Math.Min(1f, yRatio)), page, 0, "manual");
                return Json(RenderStampToCache(placement));
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }
        /// <summary>指定范围页盖章：点击结束页触发，自动在 [startPage..endPage] 每页同一位置盖章（每页独立随机位移/旋转/纹理种子，对齐 WPF）。返回 {ok,count,batchId}。</summary>
        public string AddRangeStamps(float xRatio, float yRatio, int startPage, int endPage)
        {
            try
            {
                if (_renderer == null) return "{\"ok\":false,\"error\":\"未打开 PDF\"}";
                if (_isDebugPage) return "{\"ok\":false,\"error\":\"请先加载 PDF 文件\"}";
                string stampPath = CurrentStampPath();
                if (string.IsNullOrWhiteSpace(stampPath) || !File.Exists(stampPath))
                    return "{\"ok\":false,\"error\":\"请先选择印章\"}";
                int s = Math.Max(1, (int)startPage), e = Math.Max(s, (int)endPage);
                if (s < 1 || e > _renderer.PageCount) return "{\"ok\":false,\"error\":\"页码超出范围\"}";
                int batchId = _stampPlacements.CreateBatchId();
                int count = 0;
                for (int page = s; page <= e; page++)
                {
                    AddPlacement(Math.Max(0f, Math.Min(1f, xRatio)), Math.Max(0f, Math.Min(1f, yRatio)), page, batchId, "range");
                    count++;
                }
                return "{\"ok\":true,\"count\":" + count + ",\"batchId\":" + batchId + "}";
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }
        /// <summary>放置一枚印章（手动 batchId=0 / 范围批次统一 batchId）。参数取当前章记忆；随机值放置时生成并固定。</summary>
        private PDFQFZ.Library.StampPlacement AddPlacement(float xRatio, float yRatio, int page, int batchId, string type = "manual", string keyword = null)
        {
            return AddPlacementCore(_stampDocPath, xRatio, yRatio, page, batchId, type, keyword);
        }
        /// <summary>V2.4.0.81：放置核心（显式文档路径）——批量放置 Task.Run 内按文件逐枚放置，
        /// 不依赖/不修改 _stampDocPath 与 _renderer（零共享写入，消除批量放置与翻页渲染的竞态）。</summary>
        private PDFQFZ.Library.StampPlacement AddPlacementCore(string docPath, float xRatio, float yRatio, int page, int batchId, string type = "manual", string keyword = null)
        {
            string name = CurrentStampName();
            var sp = AppConfig.LoadStampParams(name ?? "");
            string stampPath = CurrentStampPath();
            GetRandomOffset(sp.RandomParams, sp.RandomOffsetXMm, sp.RandomOffsetYMm, out float dxMm, out float dyMm);
            int rotation = GetEffectiveRotation(sp.Rotation, sp.RandomParams, sp.RandomRange);
            bool randomRotation = sp.RandomParams && sp.RandomRange > 0;
            var pl = _stampPlacements.Add(docPath, page,
                Math.Max(0f, Math.Min(1f, xRatio)), Math.Max(0f, Math.Min(1f, yRatio)),
                stampPath, sp.Size, sp.Opacity, rotation, sp.Tolerance, sp.RemoveWhite,
                sp.RotationHandle == 0, randomRotation: randomRotation, batchId: batchId, centerRatio: true,
                offsetXmm: dxMm, offsetYmm: dyMm,
                textureEnabled: sp.TextureQuality, textureSeed: NewTextureSeed(),
                textureKb: NewTextureK(), textureKblob: NewTextureK(), textureKgrad: NewTextureK(),
                textureKwhite: NewTextureK(), textureKspot: NewTextureK(),
                textureKradial: NewTextureK(), textureKcast: NewTextureK(),
                textureBrightness: sp.TextureBrightness, textureBlob: sp.TextureBlob,
                textureGradient: sp.TextureGradient, textureWhite: sp.TextureWhite,
                textureSpot: sp.TextureSpot, textureRadial: sp.TextureRadial,
                textureCast: sp.TextureCast, type: type, keyword: keyword);
            return pl;
        }
        /// <summary>取某页全部印章（渲染为缓存 PNG，参数/种子已固化于放置时；供前端叠加刷新）。</summary>
        public string GetPageStamps(int page)
        {
            try
            {
                var list = new List<object>();
                foreach (var p in _stampPlacements.ForPage(_stampDocPath, page))
                {
                    list.Add(RenderStampToCache(p));
                }
                return Json(list);
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }
        /// <summary>把一枚印章渲染为缓存 PNG（对齐 WPF CreatePlacementBitmap：去白/透明度/纹理/旋转），返回叠加所需对象。</summary>
        private Dictionary<string, object> RenderStampToCache(PDFQFZ.Library.StampPlacement placement)
        {
            string fileName = "stamp_" + placement.Id.ToString() + ".png";
            string outPath = Path.Combine(_renderCacheDir, fileName);
            int w = 1, h = 1;
            using (var bmp = PDFQFZ.WPF.Services.StampEngine.CreatePlacementBitmap(placement))
            {
                w = bmp.Width; h = bmp.Height;
                bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
            }
            return new Dictionary<string, object>
            {
                ["ok"] = true,
                ["id"] = placement.Id,
                ["url"] = _cacheUrlPrefix + "/" + fileName,
                ["imgW"] = w,
                ["imgH"] = h,
                ["sizeMm"] = placement.SizeMm,
                ["x"] = placement.X,
                ["y"] = placement.Y,
                ["rotation"] = placement.Rotation,
                ["randomRotation"] = placement.RandomRotation,
                ["offsetX"] = placement.OffsetXmm,
                ["offsetY"] = placement.OffsetYmm,
                ["centerRatio"] = placement.CenterRatio, // V2.4.0.18：前端显示按此区分——true=手动/范围（章中心比例）、false=按文字（左上区间比例），对齐 WPF PositionPreviewOverlay
                ["batchId"] = placement.BatchId,
                // V2.4.0.88：章类型数据模型——type 权威区分章来源；keyword 按文字章关键词（其余 null）；page 所在页码（前端右键弹窗按 type 分支）
                ["type"] = placement.Type,
                ["keyword"] = placement.Keyword,
                ["page"] = placement.Page
            };
        }
        /// <summary>删除一枚印章（按 id）。返回 "ok" 或 "err:..."。</summary>
        public string DeleteStampById(int id)
        {
            try
            {
                if (_stampPlacements.Remove(id))
                {
                    string f = Path.Combine(_renderCacheDir, "stamp_" + id.ToString() + ".png");
                    try { if (File.Exists(f)) File.Delete(f); } catch { }
                    return "ok";
                }
                return "err:印章不存在";
            }
            catch (Exception ex) { return "err:" + ex.Message; }
        }
        /// <summary>v2.4.0.50：拖拽移动已盖章——按 id 更新页面内比例坐标（钳制 0-1）。返回 {"ok":true} 或 {"ok":false,"error":...}。</summary>
        public string MoveStamp(int id, float xRatio, float yRatio)
        {
            try
            {
                if (_renderer == null) return "{\"ok\":false,\"error\":\"未打开 PDF\"}";
                float x = Math.Max(0f, Math.Min(1f, xRatio));
                float y = Math.Max(0f, Math.Min(1f, yRatio));
                if (_stampPlacements.UpdatePosition(id, x, y))
                {
                    return "{\"ok\":true}";
                }
                return "{\"ok\":false,\"error\":\"印章不存在\"}";
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }
        /// <summary>v2.4.0.51：骑缝章预览——生成基准章图（对齐生成管线：去白/不透明度/旋转）+ 返回全部骑缝章页切片布局（纯计算，无每页图）。
        /// 参数与 GenerateFiles 同源（qfzType/wzType/wzPercent/maxSplit；wzType：0下/1上/2左/3右）；
        /// pageWPt/pageHPt 为前端当前渲染页 pt 尺寸（统一用于各页比例，与预览显示自洽）。
        /// 返回 {ok,baseUrl,baseW,baseH,layout:[{page,x,y,w,h,srcX,srcY,srcW,srcH,rotated}]}，坐标为 0-1 比例。</summary>
        public string GetSeamPreview(int pageCount, int qfzType, int wzType, int wzPercent, int maxSplit, int pageWPt, int pageHPt)
        {
            try
            {
                if (_renderer == null) return "{\"ok\":false,\"error\":\"未打开 PDF\"}";
                if (qfzType == 1) return "{\"ok\":true,\"layout\":[]}";
                string stampPath = CurrentStampPath();
                if (string.IsNullOrWhiteSpace(stampPath) || !File.Exists(stampPath))
                    return "{\"ok\":false,\"error\":\"请先选择印章图片\"}";

                var sp = AppConfig.LoadStampParams(CurrentStampName() ?? "");
                var options = new StampOptions
                {
                    StampImagePath = stampPath,
                    OutputPath = _renderCacheDir,
                    QfzType = qfzType,
                    RemoveWhite = sp.RemoveWhite,
                    Opacity = sp.Opacity,
                    Rotation = sp.Rotation,
                    OriginalRotationCrop = true, // v2.4.0.76: 固定切边
                    SizeMm = sp.Size,
                    Placements = _stampPlacements,
                    TextureEnabled = sp.TextureQuality,
                    TextureSeed = NewTextureSeed(),
                    TextureBrightness = sp.TextureBrightness, TextureBlob = sp.TextureBlob,
                    TextureGradient = sp.TextureGradient, TextureWhite = sp.TextureWhite,
                    TextureSpot = sp.TextureSpot, TextureRadial = sp.TextureRadial, TextureCast = sp.TextureCast,
                    TextureKb = NewTextureK(), TextureKblob = NewTextureK(), TextureKgrad = NewTextureK(),
                    TextureKwhite = NewTextureK(), TextureKspot = NewTextureK(),
                    TextureKradial = NewTextureK(), TextureKcast = NewTextureK()
                };
                if (!StampEngine.PrepareStampResources(options, out Bitmap seamImage, out float xzbl, _ => { }))
                    return "{\"ok\":false,\"error\":\"印章准备失败，请检查印章图片\"}";

                string baseName = "seam_base.png";
                string outPath = Path.Combine(_renderCacheDir, baseName);
                int baseW = seamImage.Width, baseH = seamImage.Height;
                using (seamImage)
                {
                    seamImage.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
                }

                var stampedPages = qfzType == 4 ? _stampPlacements.DistinctPages(_stampDocPath).ToList() : null;
                float pW = pageWPt > 0 ? pageWPt : 595f;
                float pH = pageHPt > 0 ? pageHPt : 842f;
                // V1.0.0.32：预览端逐页读取尺寸/旋转（对齐生成端 StampEngine：GetPageSize 原始 + GetPageRotation），
                // 横向页（rotation 90/270）骑缝章方向按"横页竖起来盖"映射，保证预览与输出一致
                var pageSizes = new (float W, float H)[Math.Max(1, pageCount) + 1];
                var pageRots = new int[Math.Max(1, pageCount) + 1];
                try
                {
                    using (var reader = new iTextSharp.text.pdf.PdfReader(_stampDocPath))
                    {
                        int n = Math.Min(pageCount, reader.NumberOfPages);
                        for (int pi = 1; pi <= n; pi++)
                        {
                            var ps = reader.GetPageSize(pi);
                            pageSizes[pi] = ((float)ps.Width, (float)ps.Height);
                            pageRots[pi] = reader.GetPageRotation(pi);
                        }
                    }
                }
                catch { }
                var layout = PDFQFZ.Library.SeamLayoutCalculator.Compute(
                    Math.Max(1, pageCount), qfzType, wzType, wzPercent, (maxSplit <= 0 ? Math.Max(1, pageCount) : Math.Max(1, maxSplit)),
                    sp.Size, baseW, baseH, xzbl, stampedPages,
                    p => (p >= 1 && p <= pageCount && pageSizes[p].W > 0) ? pageSizes[p] : (pW, pH),
                    p => (p >= 1 && p <= pageCount) ? pageRots[p] : 0);

                return Json(new Dictionary<string, object>
                {
                    ["ok"] = true,
                    ["baseUrl"] = _cacheUrlPrefix + "/" + baseName,
                    ["baseW"] = baseW,
                    ["baseH"] = baseH,
                    ["layout"] = layout.Select(l => new Dictionary<string, object>
                    {
                        ["page"] = l.Page,
                        ["x"] = l.X, ["y"] = l.Y, ["w"] = l.W, ["h"] = l.H,
                        ["srcX"] = l.SrcX, ["srcY"] = l.SrcY, ["srcW"] = l.SrcW, ["srcH"] = l.SrcH,
                        ["rotated"] = l.Rotated
                    }).ToList()
                });
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }
        /// <summary>按文字盖章：搜索→上下文过滤→定位放置（对齐 WPF ApplyAutoPlaceToFile）。</summary>
        public string AutoStamp(string keyword, string contextKeywords, int contextRange, bool requireAll, bool excludeSpaces, bool coEnabled, float coX, float coY)
        {
            try
            {
                if (_isDebugPage || _stampDocPath == null) return "{\"ok\":false,\"error\":\"请先加载 PDF 文件再放置印章\"}";
                return AutoStampCore(_stampDocPath, keyword, contextKeywords, contextRange, requireAll, excludeSpaces, coEnabled, coX, coY);
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }
        /// <summary>目录模式：对全部源 PDF 逐个按文字盖章（对齐 WPF"全部文件"询问分支）。
        /// 每个文件独立批次，结果汇总返回；预览区只刷新当前文档（桥内放置集合按文档路径区分）。</summary>
        public string AutoStampDir(string keyword, string contextKeywords, int contextRange, bool requireAll, bool excludeSpaces, bool coEnabled, float coX, float coY)
        {
            try
            {
                if (_isDebugPage || _dirFiles == null || _dirFiles.Count == 0) return "{\"ok\":false,\"error\":\"请先选择文件夹\"}";
                var results = new List<object>();
                int totalCount = 0;
                foreach (var f in _dirFiles)
                {
                    var r = AutoStampCore(f, keyword, contextKeywords, contextRange, requireAll, excludeSpaces, coEnabled, coX, coY);
                    // 目录模式不做逐文件失败中断：无文字层/未找到记录为 error，继续下一个
                    var obj = ParseJsonObject(r);
                    int cnt = 0;
                    if (obj != null && obj.TryGetValue("ok", out object okv) && okv is bool bok && bok)
                    {
                        if (obj.TryGetValue("count", out object cv)) { int.TryParse(Convert.ToString(cv), out cnt); }
                        totalCount += cnt;
                    }
                    results.Add(new Dictionary<string, object>
                    {
                        ["file"] = Path.GetFileName(f),
                        ["ok"] = obj != null && obj.TryGetValue("ok", out object okv2) && okv2 is bool bok2 && bok2,
                        ["count"] = cnt,
                        ["error"] = obj != null && obj.TryGetValue("error", out object ev) ? Convert.ToString(ev) : ""
                    });
                }
                var d = new Dictionary<string, object> { ["ok"] = true, ["total"] = totalCount, ["results"] = results };
                return Json(d);
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }
        /// <summary>AutoStamp 核心（按指定文档执行；目录模式逐文件复用）。</summary>
        private string AutoStampCore(string docPath, string keyword, string contextKeywords, int contextRange, bool requireAll, bool excludeSpaces, bool coEnabled, float coX, float coY)
        {
            try
            {
                if (_renderer == null) return "{\"ok\":false,\"error\":\"请先加载 PDF 文件再放置印章\"}";
                string stampPath = CurrentStampPath();
                if (string.IsNullOrWhiteSpace(stampPath) || !File.Exists(stampPath))
                    return "{\"ok\":false,\"error\":\"请先选择印章图片\"}";
                string kw = (keyword ?? "").Trim();
                if (kw.Length == 0) return "{\"ok\":false,\"error\":\"请输入要识别的盖章文字\"}";

                using (var searcher = new PDFQFZ.Library.PdfTextSearcher(docPath))
                {
                    if (!searcher.HasAnyText())
                        return "{\"ok\":false,\"error\":\"图片型 PDF 无文字层，无法按文字盖章\"}";
                    var allMatches = searcher.FindAll(kw);
                    if (allMatches == null || allMatches.Count == 0)
                        return "{\"ok\":false,\"error\":\"没找到您指定的盖章文字\"}";

                    // 上下文过滤（关键词为空则不过滤）
                    string[] kws = ParseContextKeywords(contextKeywords);
                    List<PDFQFZ.Library.PdfTextMatch> matches;
                    if (kws.Length == 0) { matches = allMatches; }
                    else
                    {
                        using (var s2 = new PDFQFZ.Library.PdfTextSearcher(docPath))
                        {
                            matches = s2.FindAll(kw, kws, Math.Max(1, contextRange), requireAll, excludeSpaces);
                        }
                    }
                    if (matches == null || matches.Count == 0)
                        return "{\"ok\":false,\"error\":\"没找到匹配上下文关键词的盖章位置\"}";

                    // 批次：同关键词同文档已有批次 → 追加（位置接近跳过）；否则新批次（对齐 WPF）
                    var existing = _autoStampOps.LastOrDefault(o =>
                        string.Equals(o.FilePath, docPath, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(o.Keyword, kw, StringComparison.Ordinal));
                    int batchId;
                    bool append = existing != null;
                    if (append) { batchId = existing.BatchId; }
                    else { batchId = _stampPlacements.CreateBatchId(); }

                    // V2.4.0.17：中心偏移（随搜索文字记忆；本次参数优先，与 WPF 传参一致）
                    float coBaseX = coEnabled ? coX : 0f;
                    float coBaseY = coEnabled ? coY : 0f;

                    int addedCount = 0;
                    var addedPages = new List<int>();
                    foreach (var match in matches)
                    {
                        var sp = AppConfig.LoadStampParams(CurrentStampName() ?? "");
                        // V2.4.0.17：对齐 WPF ApplyAutoPlaceToFile 的 ratioW/ratioH（overlaySize.Width/previewW、Height/previewH）——
                        // 印章尺寸占页面宽/高比例用 match.PageWidth/PageHeight（每页实际 pt），不再依赖前端 pagePts 传参
                        // （前端 pagePts 曾用渲染缓存 r.w/2 近似，dpi 非 144 时会算错 → 印章比例错 → 位置固定偏移）
                        float ratioW = (float)(sp.Size * 72.0 / 25.4 / Math.Max(1.0, match.PageWidth));
                        float ratioH = (float)(sp.Size * 72.0 / 25.4 / Math.Max(1.0, match.PageHeight));
                        var pos = PDFQFZ.Library.AutoStampPositionCalculator.Calculate(
                            match.CenterX, match.CenterY, match.PageWidth, match.PageHeight, ratioW, ratioH);
                        if (append)
                        {
                            bool alreadyPlaced = _stampPlacements.ForPage(docPath, match.PageIndex + 1)
                                .Any(pl => pl.BatchId == batchId
                                    && Math.Abs(pl.X - pos.Px) < 0.03
                                    && Math.Abs(pl.Y - pos.Py) < 0.03);
                            if (alreadyPlaced) continue;
                        }
                        // V2.4.0.72：按文字盖章随机语义对齐 WPF（git 5d8e7ef~1 ApplyAutoPlaceToFile 证据）——
                        // 随机角度位移开关开启时，按文字盖章同样应用随机位移/随机旋转（放置时一次 roll 固化，预览/输出同一数据）。
                        // 中心偏移记忆（coX/coY）继续叠加在随机位移之上，与 AddPlacement 同款随机调用。
                        GetRandomOffset(sp.RandomParams, sp.RandomOffsetXMm, sp.RandomOffsetYMm, out float dxMm, out float dyMm);
                        int rotation = GetEffectiveRotation(sp.Rotation, sp.RandomParams, sp.RandomRange);
                        bool randomRotation = sp.RandomParams && sp.RandomRange > 0;
                        _stampPlacements.Add(docPath, match.PageIndex + 1, pos.Px, pos.Py, stampPath,
                            sp.Size, sp.Opacity, rotation,
                            sp.Tolerance, sp.RemoveWhite, sp.RotationHandle == 0,
                            randomRotation: randomRotation,
                            batchId: batchId, centerRatio: false,
                            offsetXmm: coBaseX + dxMm, offsetYmm: coBaseY + dyMm,
                            textureEnabled: sp.TextureQuality, textureSeed: NewTextureSeed(),
                            textureKb: NewTextureK(), textureKblob: NewTextureK(), textureKgrad: NewTextureK(),
                            textureKwhite: NewTextureK(), textureKspot: NewTextureK(),
                            textureKradial: NewTextureK(), textureKcast: NewTextureK(),
                            textureBrightness: sp.TextureBrightness, textureBlob: sp.TextureBlob,
                            textureGradient: sp.TextureGradient, textureWhite: sp.TextureWhite,
                            textureSpot: sp.TextureSpot, textureRadial: sp.TextureRadial,
                            textureCast: sp.TextureCast, type: "text", keyword: kw);
                        addedCount++;
                        addedPages.Add(match.PageIndex + 1);
                    }

                    if (!append)
                    {
                        _autoStampOps.Add(new AutoStampOp { Keyword = kw, BatchId = batchId, FilePath = docPath });
                    }
                    // 历史与中心偏移记忆（对齐 WPF：找到并完成盖章才记录）
                    AppConfig.RecordAutoStampKeyword(kw);
                    AppConfig.SetCenterOffsetForKeyword(kw, coEnabled, coX, coY);

                    var pages = addedPages.Distinct().OrderBy(x => x).ToList();
                    var d = new Dictionary<string, object>
                    {
                        ["ok"] = true, ["count"] = addedCount, ["total"] = matches.Count,
                        ["batchId"] = batchId, ["append"] = append,
                        ["pages"] = pages, ["contextFiltered"] = kws.Length > 0,
                    };
                    return Json(d);
                }
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }
        /// <summary>解析"附近关键词"：逗号/分号/顿号/空格分隔，去空白去重（对齐 WPF ParseContextKeywords）。</summary>
        private static string[] ParseContextKeywords(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return new string[0];
            return input
                .Split(new[] { ',', ';', '，', '；', '、', ' ', '	' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .Distinct()
                .ToArray();
        }
        /// <summary>撤销放置：撤销当前文档最近一次"按文字放置"（对齐 WPF OnUndoAutoClick）。</summary>
        public string UndoAutoStamp()
        {
            try
            {
                int idx = -1;
                for (int i = _autoStampOps.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(_autoStampOps[i].FilePath, _stampDocPath, StringComparison.OrdinalIgnoreCase))
                    { idx = i; break; }
                }
                if (idx < 0) return "{\"ok\":true,\"removed\":0,\"note\":\"无按文字批次\"}";
                var op = _autoStampOps[idx];
                _autoStampOps.RemoveAt(idx);
                int removed = _stampPlacements.RemoveBatch(_stampDocPath, op.BatchId);
                return "{\"ok\":true,\"removed\":" + removed + ",\"keyword\":" + Json(op.Keyword) + "}";
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }
        /// <summary>按文字历史（最近 10 条）。</summary>
        public string GetAutoStampHistory()
        {
            try { return Json(AppConfig.LoadAutoStampHistory()); }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }
        /// <summary>删除一条按文字搜索历史（V2.4.0.88 恢复——V79 误删该桥方法，AppConfig 实现一直在）。返回 "ok"。</summary>
        public string RemoveAutoStampKeyword(string keyword)
        {
            try { AppConfig.RemoveAutoStampKeyword(keyword ?? ""); return "ok"; }
            catch (Exception ex) { return "err:" + ex.Message; }
        }
        /// <summary>取某关键词的中心偏移记忆。返回 {enabled,x,y}。</summary>
        public string GetCenterOffsetForKeyword(string keyword)
        {
            try
            {
                AppConfig.GetCenterOffsetForKeyword(keyword ?? "", out bool enabled, out float x, out float y);
                return "{\"enabled\":" + (enabled ? "true" : "false") + ",\"x\":" + x + ",\"y\":" + y + "}";
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }
    }
}
