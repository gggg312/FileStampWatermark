using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;
using PDFQFZ.Library;

namespace PDFQFZ.WebShell.Services
{
    /// <summary>
    /// C# → JS 桥对象（AddHostObjectToScript 注入）。水印功能桥（V2.4.0.96 新增）。
    /// 语义：水印框按 当前文档路径+页 存储（_watermarks，与 _stampPlacements 同生命周期）；
    /// 方案存储走 config.ini [WatermarkSchemes]（WatermarkSchemeStore）。
    /// </summary>
    public partial class CSharpBridge
    {
        /// <summary>新增水印框（当前文档当前页；几何 + 全部文字参数）。返回 {ok, box:{...}}。
        /// V2.4.0.97：图片模式（_imgMode）下以当前图片为文档上下文（DocumentPath=图片路径、Page=1）。</summary>
        public string AddWatermarkBox(int page, double x, double y, double w, double h, string jsonParams)
        {
            try
            {
                string docPath = _watermarkDocPath();
                // V369 诊断：图片批次状态（定位图片模式加框被拒/落错上下文）
                try { AppLog.Write("[WM-ADD-DIAG] page=" + page + " _imgMode=" + _imgMode + " _imgDocPath='" + (_imgDocPath ?? "") + "' _isImgBatch=" + _isImgBatch() + " _isPdfBatch=" + _isPdfBatch() + " docPath='" + (docPath ?? "") + "'"); } catch {}
                if (!_isImgBatch() && !_isPdfBatch() && (string.IsNullOrWhiteSpace(docPath) || !System.IO.File.Exists(docPath)))
                    return "{\"ok\":false,\"error\":\"请先加载 PDF 文件\"}";
                var box = new WatermarkBox
                {
                    DocumentPath = docPath,
                    Page = page,  // 0=所有页，>=1=指定页
                    X = Clamp01((float)x), Y = Clamp01((float)y),
                    W = Clamp01((float)w), H = Clamp01((float)h),
                    Rotation = ClampRot((float)(JsonDouble(jsonParams, "rotation") ?? 0)),
                    Text = JsonStr(jsonParams, "text") ?? "",
                    FontName = JsonStr(jsonParams, "fontName") ?? "微软雅黑",
                    ColorArgb = JsonInt(jsonParams, "colorArgb") ?? unchecked((int)0xFF1F2329),
                    Opacity = ClampInt(JsonInt(jsonParams, "opacity") ?? 100, 0, 100),
                    Bold = JsonBool(jsonParams, "bold"), Italic = JsonBool(jsonParams, "italic"),
                    Underline = JsonBool(jsonParams, "underline"), Strike = JsonBool(jsonParams, "strike"),
                    LetterSpacing = ClampInt(JsonInt(jsonParams, "letterSpacing") ?? 0, -150, 300),
                    LineSpacing = ClampInt(JsonInt(jsonParams, "lineSpacing") ?? 0, -150, 300),
                    Align = ClampInt(JsonInt(jsonParams, "align") ?? 0, 0, 2),
                    FontScale = (float)(JsonDouble(jsonParams, "fontScale") ?? 0.8),
                    H0 = (float)(JsonDouble(jsonParams, "h0") ?? 0),
                    FsToS = Math.Max(0f, (float)(JsonDouble(jsonParams, "fsToS") ?? 0))
                };
                if (box.W <= 0.01f || box.H <= 0.01f) return "{\"ok\":false,\"error\":\"水印框尺寸无效\"}";
                // V339: 解析快照换行（粘贴/方案新建走 AddWatermarkBox，wrapLines 必须在注册时同步写入后端）
                var _d = ParseParams(jsonParams);
                if (_d.TryGetValue("wrapLines", out object _wlv))
                {
                    var _list = new System.Collections.Generic.List<string>();
                    if (_wlv is string _wlStr)
                    {
                        string s = _wlStr.Trim();
                        if (s.StartsWith("["))
                        {
                            int pos = 1;
                            while (pos < s.Length)
                            {
                                while (pos < s.Length && (s[pos] == ' ' || s[pos] == ',')) pos++;
                                if (pos >= s.Length || s[pos] == ']') break;
                                if (s[pos] == '"')
                                {
                                    pos++;
                                    string val = "";
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
                                                default: val += nx; break;
                                            }
                                            pos += 2;
                                        }
                                        else { val += ch; pos++; }
                                    }
                                    _list.Add(val);
                                }
                                else { pos++; }
                            }
                        }
                    }
                    else if (_wlv is System.Collections.IEnumerable _wlEnum)
                    {
                        foreach (var item in _wlEnum) _list.Add(item?.ToString() ?? "");
                    }
                    box.WrapLines = _list;
                    try { AppLog.Write("[WM-ADD] AddWatermarkBox id=" + box.Id + " wrapLines count=" + (_list == null ? 0 : _list.Count) + " first=" + (_list != null && _list.Count > 0 ? _list[0].Substring(0, Math.Min(20, _list[0].Length)) : "")); } catch {}
                }
                _watermarks.Add(box);
                return "{\"ok\":true,\"box\":" + Json(WatermarkSchemeStore.BoxToDict(box)) + "}";
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }

        /// <summary>当前文档指定页的全部水印框（前端预览渲染用；图片模式下为当前图片，页恒 1）。</summary>
        public string GetPageWatermarks(int page)
        {
            try
            {
                int p = page; // 允许 page=0（全局水印）
                var list = _watermarks.ForDocument(_watermarkDocPath())
                    .Where(b => b.Page == 0 || b.Page == p)
                    .Select(b => WatermarkSchemeStore.BoxToDict(b)).ToList();
                return Json(list);
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }

        /// <summary>更新水印框（几何/参数统一入口；json 含全部字段，缺失字段保持原值语义由前端全量传）。返回 "ok" 或 "err:..."。</summary>
        public string UpdateWatermarkBox(long id, string json)
        {
            try
            {
                try { AppLog.Write("[WM-UPDATE] id=" + id + " jsonLen=" + ((json ?? "").Length)); } catch {}
                bool ok = _watermarks.Update(id, b =>
                {
                    var d = ParseJsonObject(json);
                    if (d == null) return;
                    if (d.TryGetValue("x", out object xv)) b.X = Clamp01(ToFloat(xv, b.X));
                    if (d.TryGetValue("y", out object yv)) b.Y = Clamp01(ToFloat(yv, b.Y));
                    if (d.TryGetValue("w", out object wv)) b.W = Clamp01(ToFloat(wv, b.W));
                    if (d.TryGetValue("h", out object hv)) b.H = Clamp01(ToFloat(hv, b.H));
                    if (d.TryGetValue("rotation", out object rv)) b.Rotation = ClampRot(ToFloat(rv, b.Rotation));
                    if (d.TryGetValue("text", out object tv)) b.Text = (tv ?? "").ToString();
                    if (d.TryGetValue("fontName", out object fnv)) b.FontName = (fnv ?? "").ToString();
                    if (d.TryGetValue("colorArgb", out object cav)) b.ColorArgb = ToInt(cav, b.ColorArgb);
                    if (d.TryGetValue("opacity", out object ov)) b.Opacity = ClampInt(ToInt(ov, b.Opacity), 0, 100);
                    if (d.TryGetValue("bold", out object bv)) b.Bold = ToBool(bv, b.Bold);
                    if (d.TryGetValue("italic", out object iv)) b.Italic = ToBool(iv, b.Italic);
                    if (d.TryGetValue("underline", out object uv)) b.Underline = ToBool(uv, b.Underline);
                    if (d.TryGetValue("strike", out object sv)) b.Strike = ToBool(sv, b.Strike);
                    if (d.TryGetValue("letterSpacing", out object lsv)) b.LetterSpacing = ClampInt(ToInt(lsv, b.LetterSpacing), -150, 300);
                    if (d.TryGetValue("lineSpacing", out object lnsv)) b.LineSpacing = ClampInt(ToInt(lnsv, b.LineSpacing), -150, 300);
                    if (d.TryGetValue("align", out object av)) b.Align = ClampInt(ToInt(av, b.Align), 0, 2);
                    if (d.TryGetValue("fontScale", out object fsv)) b.FontScale = (float)(ToFloat(fsv, b.FontScale) > 0 ? ToFloat(fsv, b.FontScale) : 0.8);
                    if (d.TryGetValue("h0", out object h0v)) { b.H0 = ToFloat(h0v, b.H0); } /* V2.4.0.396: 高频路径日志降噪 */
                    // V344: 解析 fsToS（字号/图片短边比例，图片水印字号唯一缩放源）
                    if (d.TryGetValue("fsToS", out object fwv)) { b.FsToS = Math.Max(0f, ToFloat(fwv, b.FsToS)); } /* V2.4.0.396: 高频路径日志降噪 */
                    // V305: 解析 wrapLines（快照方案——前端测量的换行结果）
                    // V311: 修复 ParseJsonObject 把数组存成字符串的问题——如果是字符串，先解析成数组
                    if (d.TryGetValue("wrapLines", out object wlv))
                    {
                        var list = new System.Collections.Generic.List<string>();
                        if (wlv is string wlStr)
                        {
                            // ParseJsonObject 把数组存成了字符串，需要手动解析
                            string s = wlStr.Trim();
                            if (s.StartsWith("["))
                            {
                                int pos = 1;
                                while (pos < s.Length)
                                {
                                    while (pos < s.Length && (s[pos] == ' ' || s[pos] == ',')) pos++;
                                    if (pos >= s.Length || s[pos] == ']') break;
                                    if (s[pos] == '"')
                                    {
                                        pos++;
                                        string val = "";
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
                                                    default: val += nx; break;
                                                }
                                                pos += 2;
                                            }
                                            else { val += ch; pos++; }
                                        }
                                        list.Add(val);
                                    }
                                    else { pos++; }
                                }
                            }
                        }
                        else if (wlv is System.Collections.IEnumerable wlEnum)
                        {
                            foreach (var item in wlEnum) list.Add(item?.ToString() ?? "");
                        }
                        b.WrapLines = list; /* V2.4.0.396: 高频路径日志降噪(原 wrapLines read 日志已删) */
                    }
                    // V315: 解析 wrapLineWidths（前端测量的每行宽度）
                    if (d.TryGetValue("wrapLineWidths", out object wlw))
                    {
                        var widths = new System.Collections.Generic.List<float>();
                        if (wlw is string wlwStr)
                        {
                            string s = wlwStr.Trim();
                            if (s.StartsWith("["))
                            {
                                int pos = 1;
                                while (pos < s.Length)
                                {
                                    while (pos < s.Length && (s[pos] == ' ' || s[pos] == ',')) pos++;
                                    if (pos >= s.Length || s[pos] == ']') break;
                                    int start = pos;
                                    while (pos < s.Length && s[pos] != ',' && s[pos] != ']') pos++;
                                    string numStr = s.Substring(start, pos - start).Trim();
                                    if (float.TryParse(numStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float w))
                                    {
                                        widths.Add(w);
                                    }
                                }
                            }
                        }
                        b.WrapLineWidths = widths; /* V2.4.0.396: 高频路径日志降噪 */
                    }
                    // V323: 解析 wrapLineTopRatios（前端测量的每行高度比例）
                    if (d.TryGetValue("wrapLineTopRatios", out object wltr))
                    {
                        var topRatios = new System.Collections.Generic.List<float>();
                        if (wltr is string wltrStr)
                        {
                            string s = wltrStr.Trim();
                            if (s.StartsWith("["))
                            {
                                int pos = 1;
                                while (pos < s.Length)
                                {
                                    while (pos < s.Length && (s[pos] == ' ' || s[pos] == ',')) pos++;
                                    if (pos >= s.Length || s[pos] == ']') break;
                                    int start = pos;
                                    while (pos < s.Length && s[pos] != ',' && s[pos] != ']') pos++;
                                    string numStr = s.Substring(start, pos - start).Trim();
                                    if (float.TryParse(numStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float h))
                                    {
                                        topRatios.Add(h);
                                    }
                                }
                            }
                        }
                        b.WrapLineTopRatios = topRatios; /* V2.4.0.396: 高频路径日志降噪 */
                    }
                });
                return ok ? "ok" : "err:水印框不存在";
            }
            catch (Exception ex) { return "err:" + ex.Message; }
        }

        /// <summary>删除单个水印框。返回 "ok" 或 "err:..."。</summary>
        public string RemoveWatermarkBox(long id)
        {
            try { return _watermarks.Remove(id) ? "ok" : "err:水印框不存在"; }
            catch (Exception ex) { return "err:" + ex.Message; }
        }

        /// <summary>删除当前文档当前页全部水印框。返回 {ok, removed}。</summary>
        public string RemovePageWatermarks(int page)
        {
            try
            {
                int removed = _watermarks.RemovePage(_watermarkDocPath(), _imgMode ? 1 : Math.Max(1, page));
                return "{\"ok\":true,\"removed\":" + removed + "}";
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }

        /// <summary>替换指定页的全部水印框（输出前调用：清空旧框，加入新框）。返回 {ok, count}。</summary>
        public string ReplaceAllWatermarks(int page, string jsonArray)
        {
            try
            {
                string docPath = _watermarkDocPath();
                if (!_isImgBatch() && !_isPdfBatch() && (string.IsNullOrWhiteSpace(docPath) || !System.IO.File.Exists(docPath)))
                    return "{\"ok\":false,\"error\":\"请先加载 PDF 文件\"}";
                int p = page; // 允许 page=0（全局水印）
                // 1. 清空整个文档的所有水印框（包括全局水印 Page=0）
                int oldCount = _watermarks.Count;
                _watermarks.Clear(docPath);
                try { AppLog.Write("[WM-REPLACE] page=" + p + " oldCount=" + oldCount); } catch { }
                // 2. 解析 jsonArray，加入新集合
                List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
                try
                {
                    var arr = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<List<Dictionary<string, object>>>(jsonArray);
                    if (arr != null) list = arr;
                }
                catch { }
                int count = 0;
                // 调试日志：打印 page 和新框的 Page
                try { AppLog.Write("[WM-REPLACE] page=" + p + " jsonArray=" + jsonArray.Substring(0, Math.Min(200, jsonArray.Length))); } catch { }
                foreach (var d in list)
                {
                        // V2.4.0.349: 改用 WatermarkSchemeStore.DictToBox——完整解析全部字段（含 wrapLines/wrapLineWidths/wrapLineTopRatios，
                        //        与方案保存/加载同口径）。此前手写字段列表漏了 wrapLines 三件套，导致生成前全量同步重建 box 后
                        //        快照丢失 → 输出端恒走 iText 重排 → 横向页换行与竖向页不一致（V347 快照优先形同虚设）。
                        var box = WatermarkSchemeStore.DictToBox(d);
                        box.DocumentPath = docPath;
                        box.Page = p;
                        // 与旧实现一致的边界钳制（DictToBox 用默认值兜底，此处对正常数据无影响，保持零回归）
                        box.X = Clamp01(box.X); box.Y = Clamp01(box.Y);
                        box.W = Clamp01(box.W); box.H = Clamp01(box.H);
                        box.Rotation = ClampRot(box.Rotation);
                        box.Opacity = ClampInt(box.Opacity, 0, 100);
                        box.LetterSpacing = ClampInt(box.LetterSpacing, -150, 300);
                        box.LineSpacing = ClampInt(box.LineSpacing, -150, 300);
                        box.Align = ClampInt(box.Align, 0, 2);
                        if (box.W > 0.01f && box.H > 0.01f)
                        {
                            // V2.4.0.349: 保留前端 id——Add 强制分配新 id，但集合已清空无冲突，改回原 id 使生成后前端 Update 正常
                            long origId = d.ContainsKey("id") ? ToInt(d["id"], 0) : 0;
                            _watermarks.Add(box);
                            if (origId > 0) box.Id = origId;
                            count++;
                        }
                }
                try { AppLog.Write("[WM-REPLACE] newCount=" + count); } catch { }
                return "{\"ok\":true,\"count\":" + count + "}";
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }

        /// <summary>清空当前文档全部水印框。返回 {ok, removed}。</summary>
        public string ClearWatermarks()
        {
            try
            {
                int removed = _watermarks.Clear(_watermarkDocPath());
                return "{\"ok\":true,\"removed\":" + removed + "}";
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }

        /// <summary>保存当前页全部框为命名方案。返回 {ok, error?}。</summary>
        /// <summary>保存水印框为命名方案。返回 {ok, error?}。</summary>
        public string SaveWatermarkScheme(string name, string boxJson)
        {
            try
            {
                WriteDebugLog("[WM-SCHEMES] SaveWatermarkScheme name=" + name + " boxJson=" + boxJson);
                // 直接解析前端传的框参数
                var boxes = new List<WatermarkBox>();
                if (!string.IsNullOrEmpty(boxJson)) {
                    var arr = new JavaScriptSerializer().Deserialize<List<Dictionary<string, object>>>(boxJson);
                    foreach (var d in arr) boxes.Add(WatermarkSchemeStore.DictToBox(d));
                }
                if (boxes.Count == 0) return "{\"ok\":false,\"error\":\"没有水印框数据，无可保存内容\"}";
                if (!WatermarkSchemeStore.Save(name, boxes)) return "{\"ok\":false,\"error\":\"保存失败（名称无效或方案数已达上限）\"}";
                return "{\"ok\":true}";
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }

        /// <summary>全部方案名（JSON 数组）。</summary>
        public string GetWatermarkSchemes()
        {
            try { return Json(WatermarkSchemeStore.GetNames()); }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }

        /// <summary>删除方案。返回 "ok" 或 "err:..."。</summary>
        public string DeleteWatermarkScheme(string name)
        {
            try { return WatermarkSchemeStore.Delete(name) ? "ok" : "err:删除失败"; }
            catch (Exception ex) { return "err:" + ex.Message; }
        }

        /// <summary>应用方案到当前页（新 Id；覆盖原几何、保留页号）。返回 {ok, count}。</summary>
        public string ApplyWatermarkScheme(string name)
        {
            try
            {
                string docPath = _watermarkDocPath();
                if (string.IsNullOrWhiteSpace(docPath)) return "{\"ok\":false,\"error\":\"请先加载 PDF 文件\"}";
                var boxes = WatermarkSchemeStore.Load(name);
                if (boxes == null || boxes.Count == 0) return "{\"ok\":false,\"error\":\"方案不存在或为空\"}";
                int page = _imgMode ? 0 : _currentPageForWatermark();
                foreach (var b in boxes)
                {
                    b.DocumentPath = docPath;
                    b.Page = page;
                    _watermarks.Add(b);
                }
                return "{\"ok\":true,\"count\":" + boxes.Count + "}";
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }

        // V277：图片批次水印公共虚拟文档路径——图片模式下所有水印框共享此 key（一个框应用到所有图）
        private const string IMG_BATCH_DOC = "__IMG_BATCH__";
        // V392：PDF 文件夹批次水印公共虚拟文档路径——文件夹模式下所有文件共享同一组水印框
        //（一个框应用到文件夹内所有 PDF，与图片批次语义一致；单文件/多文件模式仍按文档路径隔离）
        private const string PDF_BATCH_DOC = "__PDF_BATCH__";

        // ---------- 辅助 ----------
        /// <summary>V2.4.0.97：当前水印上下文文档路径——图片模式用当前图片，PDF 模式用当前文档。</summary>
        /// V277：图片模式下返回批次公共虚拟 key（所有图共享同一份水印框），不再按单张图片路径存。</summary>
        private string _watermarkDocPath()
        {
            if (_imgMode && !string.IsNullOrWhiteSpace(_imgDocPath)) return IMG_BATCH_DOC;
            // V392：PDF 文件夹模式水印全局共享——切文件不换 key，所有文件显示/输出同一组水印框
            if (_dirMode && !string.IsNullOrWhiteSpace(_dirSource)) return PDF_BATCH_DOC;
            return _stampDocPath;
        }

        /// <summary>V277：当前是否图片批次模式（水印框存公共虚拟 key，跳过文件存在性检查）。</summary>
        private bool _isImgBatch()
        {
            return _imgMode && !string.IsNullOrWhiteSpace(_imgDocPath);
        }

        /// <summary>V392：当前是否 PDF 文件夹批次模式（水印框存公共虚拟 key PDF_BATCH_DOC，跳过文件存在性检查）。
        /// 与图片批次同语义：虚拟 key 不是真实文件，File.Exists 校验必须放行。</summary>
        private bool _isPdfBatch()
        {
            return _dirMode && !string.IsNullOrWhiteSpace(_dirSource);
        }

        // ---------- 辅助 ----------
        /// <summary>V2.4.0.96：水印功能总开关——关闭时预览隐藏且生成不输出（框数据保留，重新开启恢复）。</summary>
        public string SetWatermarkEnabled(bool enabled)
        {
            _watermarkEnabled = enabled;
            return "ok";
        }
        /// <summary>前端写调试日志到统一 app_log.log（V1.0.0.14 由 wm_debug.log 迁入）。</summary>
        public string WriteDebugLog(string msg) {
            WriteLog(msg);
            return "ok";
        }

        private bool _watermarkEnabled = false;

        private int _currentPageForWatermark()
        {
            return _lastRenderedPage > 0 ? _lastRenderedPage : 1;
        }

        private static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }
        private static float ClampRot(float v) { return v < -180f ? -180f : (v > 180f ? 180f : v); }
        private static int ClampInt(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        private static float ToFloat(object v, float def)
        {
            if (v == null) return def;
            if (v is double db) return (float)db;
            if (float.TryParse(v.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float r)) return r;
            return def;
        }

        private static int ToInt(object v, int def)
        {
            if (v == null) return def;
            if (v is int i) return i;
            if (v is double db) return unchecked((int)(long)Math.Round(db));  // V273：double 先转 long 再 unchecked 转 int，保留 0xFF000000 等大值位模式（原 (int)Math.Round 溢出截断为 0）
            if (int.TryParse(v.ToString(), out int r)) return r;
            return def;
        }

        private static bool ToBool(object v, bool def)
        {
            if (v == null) return def;
            if (v is bool b) return b;
            return v.ToString() == "True" || v.ToString() == "true" || v.ToString() == "1";
        }

        // JSON 字段读取（对 AddWatermarkBox 的 jsonParams 做单层解析）
        private static Dictionary<string, object> ParseParams(string json)
        {
            if (json == null) return new Dictionary<string, object>();
            return ParseJsonObject(json) ?? new Dictionary<string, object>();
        }

        private static string JsonStr(string json, string key)
        {
            var d = ParseParams(json);
            return d.TryGetValue(key, out object v) && v != null ? v.ToString() : null;
        }

        private static int? JsonInt(string json, string key)
        {
            var d = ParseParams(json);
            if (d.TryGetValue(key, out object v) && v != null)
            {
                if (v is int i) return i;
                if (v is double db) return unchecked((int)(long)Math.Round(db));  // V273：同上，保留大值位模式
                if (int.TryParse(v.ToString(), out int r)) return r;
            }
            return null;
        }

        private static double? JsonDouble(string json, string key)
        {
            var d = ParseParams(json);
            if (d.TryGetValue(key, out object v) && v != null)
            {
                if (v is double db) return db;
                if (double.TryParse(v.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double r)) return r;
            }
            return null;
        }

        private static bool JsonBool(string json, string key)
        {
            var d = ParseParams(json);
            if (d.TryGetValue(key, out object v) && v != null)
            {
                if (v is bool b) return b;
                return v.ToString() == "True" || v.ToString() == "true" || v.ToString() == "1";
            }
            return false;
        }
    }
}
