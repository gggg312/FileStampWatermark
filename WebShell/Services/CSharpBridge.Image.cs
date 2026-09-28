using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using PDFQFZ.Library;

namespace PDFQFZ.WebShell.Services
{
    /// <summary>
    /// C# → JS 桥对象。图片批量水印桥（V2.4.0.97 新增，第二步）。
    /// 交互（用户定稿）：不设专门图片模式 Tab——拖入图片/图片文件夹自动进入图片模式；
    /// 文件夹内图片+PDF 混合时由前端弹窗让用户选择处理方式（本桥只提供能力，分类在 C#）。
    /// 水印框复用 _watermarks：DocumentPath=图片完整路径、Page=1（见 Watermark.cs _watermarkDocPath）。
    /// 输出：ImageWatermarkEngine（System.Drawing），命名 原文件名_已加水印V1.ext。
    /// </summary>
    public partial class CSharpBridge
    {
        // ===================== 队列管理 =====================

        /// <summary>按本地路径批量入队（按钮/E2E/内部通道；新批次会清空旧队列与其水印框）。返回 {ok, total, list}。</summary>
        public string LoadImagePaths(string jsonPaths)
        {
            try
            {
                var paths = ParseJsonArray(jsonPaths);
                var list = paths.Where(p => ImageWatermarkEngine.IsSupported(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (list.Count == 0) return "{\"ok\":false,\"error\":\"没有可用的图片文件（jpg/jpeg/png/bmp/gif/tif/tiff）\"}";
                BeginImageBatch();
                _imgQueue.AddRange(list);
                EnterImageMode(list[0]);
                return "{\"ok\":true,\"total\":" + list.Count + ",\"list\":" + Json(list) + "}";
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }

        /// <summary>拖入图片 base64 通道（WebView2 拖入拿不到路径，只能读内容；追加进队列）。返回 {ok, path, name, idx}。</summary>
        public string AddImageFromBytes(string base64, string fileName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(base64)) return "{\"ok\":false,\"error\":\"空数据\"}";
                byte[] bytes;
                try { bytes = Convert.FromBase64String(base64); }
                catch { return "{\"ok\":false,\"error\":\"base64 无效\"}";
                }
                string safeName = Path.GetFileName((fileName ?? "img.png").Replace('\\', '/'));
                if (!ImageWatermarkEngine.IsSupported(safeName)) safeName += ".png";
                string dir = Path.Combine(Path.GetTempPath(), "pdfqfz_imgdrop_" + _procTag);
                Directory.CreateDirectory(dir);
                string tmp = Path.Combine(dir, Guid.NewGuid().ToString("N").Substring(0, 8) + "_" + safeName);
                File.WriteAllBytes(tmp, bytes);
                if (!_imgMode) BeginImageBatch();
                _imgQueue.Add(tmp);
                if (_imgCurrentIndex < 0) EnterImageMode(tmp);
                return "{\"ok\":true,\"path\":" + Json(tmp) + ",\"name\":" + Json(safeName) + ",\"idx\":" + (_imgQueue.Count - 1) + "}";
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }

        /// <summary>选择图片文件（OpenFileDialog 多选，追加进队列）。返回 {ok, total} 或 {ok:false, cancel:true}。</summary>
        public string PickImages()
        {
            try
            {
                using (var dlg = new System.Windows.Forms.OpenFileDialog())
                {
                    dlg.Title = "选择图片（可多选）";
                    dlg.Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff|所有文件|*.*";
                    dlg.Multiselect = true;
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return "{\"ok\":false,\"cancel\":true}";
                    if (!_imgMode) BeginImageBatch();
                    int added = 0;
                    foreach (string p in dlg.FileNames)
                    {
                        if (!ImageWatermarkEngine.IsSupported(p)) continue;
                        if (_imgQueue.Contains(p, StringComparer.OrdinalIgnoreCase)) continue;
                        _imgQueue.Add(p); added++;
                    }
                    if (_imgCurrentIndex < 0 && _imgQueue.Count > 0) EnterImageMode(_imgQueue[0]);
                    return "{\"ok\":true,\"total\":" + added + "}";
                }
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }

        /// <summary>选择图片文件夹（FolderBrowserDialog，递归收集图片；新批次清空旧队列与其水印框）。返回 {ok, total, list}。</summary>
        public string OpenImageFolder()
        {
            try
            {
                using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dlg.Description = "选择图片文件夹";
                    dlg.ShowNewFolderButton = false;
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return "{\"ok\":false,\"cancel\":true}";
                    return LoadImageFolder(dlg.SelectedPath);
                }
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }

        /// <summary>按文件夹路径收集图片（递归、容错跳过无权限子目录与输出目录）；返回 {ok, total, list, pdfs}——pdfs 为文件夹内 PDF 数（混合类型前端弹窗用）。</summary>
        public string LoadImageFolder(string dir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return "{\"ok\":false,\"error\":\"文件夹不存在\"}";
                // V2.4.0.351：只扫根目录（与 PDF 文件夹一致：拖入哪个文件夹只看根目录，不看子目录——无穿透机制）。
                // 不再递归子目录（含历史输出目录"已盖章"/"已处理"天然不进入列表）。
                var files = new List<string>();
                int pdfCount = 0;
                try
                {
                    foreach (var f in Directory.GetFiles(dir))
                    {
                        try
                        {
                            if (ImageWatermarkEngine.IsSupported(f)) files.Add(f);
                            else if (string.Equals(Path.GetExtension(f), ".pdf", StringComparison.OrdinalIgnoreCase)) pdfCount++;
                        }
                        catch { }
                    }
                }
                catch { }
                files.Sort(StringComparer.OrdinalIgnoreCase);
                if (files.Count == 0 && pdfCount == 0) return "{\"ok\":false,\"error\":\"该文件夹下没有图片或 PDF 文件\"}";
                if (files.Count > 0)
                {
                    BeginImageBatch();
                    _imgQueue.AddRange(files);
                    EnterImageMode(files[0]);
                }
                return "{\"ok\":true,\"total\":" + files.Count + ",\"pdfs\":" + pdfCount + ",\"list\":" + Json(files) + "}";
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }

        /// <summary>清空图片队列与图片模式全部水印框（新批次/切回 PDF 时前端调用）。返回 "ok"。</summary>
        public string ClearImages()
        {
            try
            {
                _watermarks.Clear(IMG_BATCH_DOC);  // V277：清批次公共水印框（不再按单图路径清）
                _imgQueue.Clear();
                _imgDocPath = "";
                _imgCurrentIndex = -1;
                _imgMode = false;
                return "ok";
            }
            catch (Exception ex) { return "err:" + ex.Message; }
        }

        /// <summary>移除队列中某张图（含其水印框）。返回 "ok" 或 "err:..."。</summary>
        public string RemoveImageAt(int index)
        {
            try
            {
                if (index < 0 || index >= _imgQueue.Count) return "err:索引无效";
                string p = _imgQueue[index];
                _imgQueue.RemoveAt(index);
                _watermarks.Clear(p);
                if (_imgCurrentIndex >= _imgQueue.Count) _imgCurrentIndex = _imgQueue.Count - 1;
                if (_imgCurrentIndex < 0) { _imgMode = false; _imgDocPath = ""; }
                else { _imgDocPath = _imgQueue[_imgCurrentIndex]; }
                return "ok";
            }
            catch (Exception ex) { return "err:" + ex.Message; }
        }

        /// <summary>队列信息（前端渲染工具条/模式切换）。返回 {mode, idx, total, list:[{path,name}]}。</summary>
        public string GetImageQueueInfo()
        {
            try
            {
                var list = _imgQueue.Select(p => new { path = p, name = Path.GetFileName(p) }).ToList();
                return "{\"mode\":" + (_imgMode ? "true" : "false") + ",\"idx\":" + _imgCurrentIndex + ",\"total\":" + _imgQueue.Count + ",\"list\":" + Json(list) + "}";
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }

        // ===================== 预览 =====================

        /// <summary>设置当前预览图片（进入/切换图片；水印上下文随之切换）。返回 "ok" 或 "err:..."。</summary>
        public string SetCurrentImage(int index)
        {
            try
            {
                if (index < 0 || index >= _imgQueue.Count) return "err:索引无效";
                EnterImageMode(_imgQueue[index]);
                return "ok";
            }
            catch (Exception ex) { return "err:" + ex.Message; }
        }

        /// <summary>当前图片预览 data URL（base64 原格式）。返回 {ok, url, w, h, name, idx, total}。</summary>
        public string GetImagePreview(int index)
        {
            try
            {
                if (index < 0 || index >= _imgQueue.Count) return "{\"ok\":false,\"error\":\"索引无效\"}";
                string p = _imgQueue[index];
                if (!File.Exists(p)) return "{\"ok\":false,\"error\":\"图片文件不存在\"}";
                using (Image img = Image.FromFile(p))
                {
                    byte[] bytes = File.ReadAllBytes(p);
                    string mime = MimeFor(Path.GetExtension(p));
                    string url = "data:" + mime + ";base64," + Convert.ToBase64String(bytes);
                    return "{\"ok\":true,\"url\":\"" + url + "\",\"w\":" + img.Width + ",\"h\":" + img.Height +
                        ",\"filePath\":" + Json(p) + ",\"name\":" + Json(Path.GetFileName(p)) + ",\"idx\":" + index + ",\"total\":" + _imgQueue.Count + "}";
                }
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json("读取图片失败：" + ex.Message) + "}"; }
        }

        // ===================== 批量输出 =====================

        /// <summary>图片批量加水印输出（每张图取各自水印框；无框跳过）。返回 {ok, total, done, skipped, failed, outputs, failures}。
        /// V1.0.0.18：新增 perFileJson——{filePath:[boxJson,...]} 每文件独立框集（含该图输出前实测的换行快照），
        /// 输出 = 该图预览（用户 v1.0.0.6 确认的"输出逐图重算"方案；不再跨图共享同一份快照）。</summary>
        public string ApplyImageWatermarks(string outDir, string perFileJson = null)
        {
            try
            {
                if (_imgQueue.Count == 0) return "{\"ok\":false,\"error\":\"图片队列为空\"}";
                if (string.IsNullOrWhiteSpace(outDir)) { string _srcDir = _imgQueue[0] != null ? Path.GetDirectoryName(_imgQueue[0]) : ""; if (string.IsNullOrEmpty(_srcDir)) outDir = ""; else outDir = _imgQueue.Count <= 1 ? _srcDir : Path.Combine(_srcDir, "已处理"); }
                if (string.IsNullOrWhiteSpace(outDir) || !Directory.Exists(outDir))
                {
                    if (outDir != null && outDir.Length > 0) Directory.CreateDirectory(outDir);
                    else return "{\"ok\":false,\"error\":\"输出目录不可用\"}";
                }
                var naming = new PDFQFZ.Library.OutputNamingOptions { Mark = PDFQFZ.WPF.Services.AppConfig.OutputNameMark ?? "已处理V", BeforeName = false };
                Dictionary<string, List<PDFQFZ.Library.WatermarkBox>> perFile = null;
                if (!string.IsNullOrWhiteSpace(perFileJson))
                {
                    try { perFile = ParsePerFileBoxes(perFileJson); } catch { perFile = null; }
                }
                var result = ImageWatermarkEngine.RunBatch(
                    _imgQueue.ToList(),
                    file =>
                    {
                        if (perFile != null && perFile.ContainsKey(file) && perFile[file] != null && perFile[file].Count > 0)
                            return perFile[file];
                        return _watermarks.ForDocument(IMG_BATCH_DOC).ToList();
                    },
                    outDir, naming, null, null);
                return "{\"ok\":true,\"total\":" + result.Total + ",\"done\":" + result.Done +
                    ",\"skipped\":" + result.Skipped + ",\"failed\":" + result.Failed +
                    ",\"outputs\":" + Json(result.Outputs) + ",\"failures\":" + Json(result.Failures) +
                    ",\"outDir\":" + Json(Path.GetFullPath(outDir)) + "}"; // V2.4.0.407：输出目录供前端日志可点击
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }

        /// <summary>V1.0.0.18：输出前每图实际像素尺寸（供前端模拟测换行快照——每图输出=该图预览）。返回 [{path,w,h}]。</summary>
        public string GetImageDims()
        {
            try
            {
                var list = _imgQueue.Select(p =>
                {
                    using (Image img = Image.FromFile(p)) return new { path = p, w = img.Width, h = img.Height };
                }).ToList();
                return Json(list);
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }

        /// <summary>V1.0.0.18：解析 perFileJson（{filePath:[boxJson,...]}）→ 独立 WatermarkBox（不污染共享框）。</summary>
        private Dictionary<string, List<PDFQFZ.Library.WatermarkBox>> ParsePerFileBoxes(string perFileJson)
        {
            var result = new Dictionary<string, List<PDFQFZ.Library.WatermarkBox>>();
            var d = ParseJsonObject(perFileJson);
            if (d == null) return result;
            foreach (var kv in d)
            {
                if (kv.Value is string s && s.Trim().StartsWith("["))
                {
                    var arr = ParseJsonArray(s);
                    var boxes = new List<PDFQFZ.Library.WatermarkBox>();
                    foreach (var item in arr)
                    {
                        string js = item?.ToString();
                        if (string.IsNullOrWhiteSpace(js)) continue;
                        var box = BoxFromJson(js);
                        if (box != null) boxes.Add(box);
                    }
                    result[kv.Key] = boxes;
                }
            }
            return result;
        }

        /// <summary>V1.0.0.18：前端框 json → 独立 WatermarkBox（字段解析与 UpdateWatermarkBox 一致；仅输出所需字段）。</summary>
        private PDFQFZ.Library.WatermarkBox BoxFromJson(string json)
        {
            var d = ParseJsonObject(json);
            if (d == null) return null;
            var b = new PDFQFZ.Library.WatermarkBox();
            if (d.TryGetValue("x", out object xv)) b.X = Clamp01(ToFloat(xv, 0.225f));
            if (d.TryGetValue("y", out object yv)) b.Y = Clamp01(ToFloat(yv, 0.42f));
            if (d.TryGetValue("w", out object wv)) b.W = Clamp01(ToFloat(wv, 0.55f));
            if (d.TryGetValue("h", out object hv)) b.H = Clamp01(ToFloat(hv, 0.08f));
            if (d.TryGetValue("rotation", out object rv)) b.Rotation = ClampRot(ToFloat(rv, 0));
            if (d.TryGetValue("text", out object tv)) b.Text = (tv ?? "").ToString();
            if (d.TryGetValue("fontName", out object fnv)) b.FontName = (fnv ?? "").ToString();
            if (d.TryGetValue("colorArgb", out object cav)) b.ColorArgb = ToInt(cav, unchecked((int)0xFF1F2329));
            if (d.TryGetValue("opacity", out object ov)) b.Opacity = ClampInt(ToInt(ov, 100), 0, 100);
            if (d.TryGetValue("bold", out object bv)) b.Bold = ToBool(bv, false);
            if (d.TryGetValue("italic", out object iv)) b.Italic = ToBool(iv, false);
            if (d.TryGetValue("underline", out object uv)) b.Underline = ToBool(uv, false);
            if (d.TryGetValue("strike", out object sv)) b.Strike = ToBool(sv, false);
            if (d.TryGetValue("letterSpacing", out object lsv)) b.LetterSpacing = ClampInt(ToInt(lsv, 0), -150, 300);
            if (d.TryGetValue("lineSpacing", out object lnsv)) b.LineSpacing = ClampInt(ToInt(lnsv, 0), -150, 300);
            if (d.TryGetValue("align", out object av)) b.Align = ClampInt(ToInt(av, 1), 0, 2);
            if (d.TryGetValue("fontScale", out object fsv)) b.FontScale = (float)(ToFloat(fsv, 0.8f) > 0 ? ToFloat(fsv, 0.8f) : 0.8);
            if (d.TryGetValue("h0", out object h0v)) b.H0 = ToFloat(h0v, 0);
            if (d.TryGetValue("fsToS", out object fwv)) b.FsToS = Math.Max(0f, ToFloat(fwv, 0));
            // wrapLines（快照行；与 UpdateWatermarkBox V305/V311 解析一致——ParseJsonObject 数组可能存成字符串）
            if (d.TryGetValue("wrapLines", out object wlv))
            {
                var list = new System.Collections.Generic.List<string>();
                if (wlv is string wlStr)
                {
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
                b.WrapLines = list;
            }
            return b;
        }

        // ===================== 辅助 =====================

        /// <summary>新批次：清空旧队列与其水印框，重置图片模式上下文。</summary>
        private void BeginImageBatch()
        {
            _watermarks.Clear(IMG_BATCH_DOC);  // V277：新批次清公共水印框
            _imgQueue.Clear();
            _imgDocPath = "";
            _imgCurrentIndex = -1;
            _imgMode = true;
        }

        private void EnterImageMode(string path)
        {
            _imgMode = true;
            _imgDocPath = path;
            int i = _imgQueue.IndexOf(path);
            _imgCurrentIndex = i >= 0 ? i : 0;
        }

        private static string MimeFor(string ext)
        {
            switch ((ext ?? "").ToLowerInvariant())
            {
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".png": return "image/png";
                case ".bmp": return "image/bmp";
                case ".gif": return "image/gif";
                case ".tif":
                case ".tiff": return "image/tiff";
                default: return "application/octet-stream";
            }
        }

        /// <summary>JSON 数组字符串解析（["a","b"] → List）。</summary>
        private static List<string> ParseJsonArray(string json)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(json)) return list;
            try
            {
                var arr = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<List<string>>(json);
                if (arr != null) list = arr;
            }
            catch { }
            return list;
        }

        /// <summary>V297: 前端预览换行计算（和输出端同一套 GDI+ 字符宽度算法）。</summary>
        /// <param name="json">{"text":"...","boxWpx":487,"fs":42.8,"letterSpacing":0,"fontName":"微软雅黑","bold":false,"italic":false}</param>
        /// <returns>{"ok":true,"text":"换行后文本\n拼接"}</returns>
        public string CalcImageWrapLines(string json)
        {
            try
            {
                var j = new System.Web.Script.Serialization.JavaScriptSerializer();
                var dic = j.Deserialize<Dictionary<string, object>>(json);
                string text = dic.ContainsKey("text") ? (dic["text"]?.ToString() ?? "") : "";
                float boxWpx = 1;
                if (dic.ContainsKey("boxWpx")) float.TryParse(dic["boxWpx"]?.ToString(), out boxWpx);
                float fs = 20; if (dic.ContainsKey("fs")) float.TryParse(dic["fs"]?.ToString(), out fs);
                float ls = 0; if (dic.ContainsKey("letterSpacing")) float.TryParse(dic["letterSpacing"]?.ToString(), out ls);
                string fontName = dic.ContainsKey("fontName") ? (dic["fontName"]?.ToString() ?? "微软雅黑") : "微软雅黑";
                bool bold = dic.ContainsKey("bold") && dic["bold"]?.ToString() == "true";
                bool italic = dic.ContainsKey("italic") && dic["italic"]?.ToString() == "true";
                string result = ImageWatermarkEngine.WrapText(text, boxWpx, fs, ls, fontName, bold, italic);
                return "{\"ok\":true,\"text\":" + Json(result) + "}";
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }
    }
}
