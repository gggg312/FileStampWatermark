using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using PDFQFZ.WPF.Services; // AppConfig

namespace PDFQFZ.WebShell.Services
{
    /// <summary>
    /// V2.4.0.373 命令行批处理模式（智能体调用入口）：
    ///   PDFQFZ.exe --batch <任务.json> [--log <日志路径>]
    /// 供智能体（豆包工作任务模式等）调用：生成任务 JSON → 执行 EXE → 读退出码与日志 → 向用户反馈。
    /// 任务类型：pdf-stamp（全部盖章）/ pdf-watermark（PDF 加水印）/ pdf-text-stamp（按文字盖章）/ img-watermark（图片水印）
    /// 退出码：0=全部成功；1=参数或执行错误；2=部分失败（有文件失败，明细在日志）。
    /// 约定：路径校验由 EXE 完成（智能体只需传路径）；输出永不覆盖源文件（默认 源\已处理 + V 递增命名）。
    /// </summary>
    public static class BatchCli
    {
        // V373：批处理完成事件（GenerateFiles 文件夹模式为 Task.Run 异步，结果经 batch-done 事件推送）
        static ManualResetEvent _done;
        static Dictionary<string, object> _batchResult;
        // V381：批量放置预览事件（BatchPreviewAll 为 Task.Run 异步，结果经 batch-preview-done 事件推送）
        static ManualResetEvent _previewDone;
        static Dictionary<string, object> _previewResult;
        // V384：input 数组（多文件夹）时按文件夹汇总反馈段
        static readonly List<string> _folderSummaries = new List<string>();

        // ---------- 入口 ----------
        public static bool IsBatchMode(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
                if (args[i] == "--batch") return true;
            return false;
        }

        // V376：--version 自证接口——智能体运行 EXE --version 读取输出即可确认身份（文件名带版本号会变，不依赖名称匹配）
        public static bool IsVersionMode(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
                if (args[i] == "--version" || args[i] == "-v") return true;
            return false;
        }

        // V1.0.0.33：--stamp-info 配置自证接口——智能体自检时读取 config.ini 全部参数（UTF-8 JSON）。
        // 铁律：只读不写（智能体不得修改 config.ini；软件配置由软件界面维护）。
        public static bool IsStampInfoMode(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
                if (args[i] == "--stamp-info") return true;
            return false;
        }

        public static int RunStampInfo()
        {
            try
            {
                var result = new Dictionary<string, object>();
                result["tool"] = "文件批量盖章与水印工具";
                result["version"] = GetVersionText();
                // config.ini 全部参数：按行解析 [节] + key=value，GBK→UTF-8 原样输出
                var cfg = new Dictionary<string, object>();
                string iniPath = AppConfig.IniPath;
                if (File.Exists(iniPath))
                {
                    string[] lines;
                    try { lines = File.ReadAllLines(iniPath, Encoding.GetEncoding(936)); }
                    catch { lines = new string[0]; }
                    string section = "";
                    foreach (var ln in lines)
                    {
                        string l = (ln ?? "").Trim();
                        if (l.Length == 0 || l.StartsWith(";") || l.StartsWith("#")) continue;
                        if (l.StartsWith("[") && l.EndsWith("]"))
                        {
                            section = l.Substring(1, l.Length - 2).Trim();
                            if (!cfg.ContainsKey(section)) cfg[section] = new Dictionary<string, string>();
                            continue;
                        }
                        int eq = l.IndexOf('=');
                        if (eq > 0)
                        {
                            string k = l.Substring(0, eq).Trim();
                            string v = l.Substring(eq + 1).Trim();
                            if (!cfg.ContainsKey(section)) cfg[section] = new Dictionary<string, string>();
                            ((Dictionary<string, string>)cfg[section])[k] = v;
                        }
                    }
                }
                result["config"] = cfg;
                // 印章清单 + 每章已保存参数（结构化，智能体可直接引用）
                var seals = new List<object>();
                try
                {
                    foreach (var e in AppConfig.LoadStampEntries())
                    {
                        var sp = AppConfig.LoadStampParams(e.DisplayName);
                        seals.Add(new Dictionary<string, object>
                        {
                            ["name"] = e.DisplayName,
                            ["path"] = e.Path,
                            ["params"] = new Dictionary<string, object>
                            {
                                ["sizeMm"] = sp.Size,
                                ["rotation"] = sp.Rotation,
                                ["rotationHandle"] = sp.RotationHandle,
                                ["opacity"] = sp.Opacity,
                                ["randomParams"] = sp.RandomParams,
                                ["randomRange"] = sp.RandomRange,
                                ["randomOffsetXMm"] = sp.RandomOffsetXMm,
                                ["randomOffsetYMm"] = sp.RandomOffsetYMm,
                                ["removeWhite"] = sp.RemoveWhite,
                                ["tolerance"] = sp.Tolerance,
                                ["maxSplit"] = sp.MaxSplit,
                                ["textureQuality"] = sp.TextureQuality,
                                ["textureBrightness"] = sp.TextureBrightness,
                                ["textureBlob"] = sp.TextureBlob,
                                ["textureGradient"] = sp.TextureGradient,
                                ["textureWhite"] = sp.TextureWhite,
                                ["textureSpot"] = sp.TextureSpot,
                                ["textureRadial"] = sp.TextureRadial,
                                ["textureCast"] = sp.TextureCast,
                                ["texturePresetIndex"] = sp.TexturePresetIndex
                            }
                        });
                    }
                }
                catch { }
                result["seals"] = seals;
                string json = new JavaScriptSerializer().Serialize(result);
                byte[] line = Encoding.UTF8.GetBytes(json + "\r\n");
                using (var st = Console.OpenStandardOutput()) st.Write(line, 0, line.Length);
                return 0;
            }
            catch (Exception ex)
            {
                try
                {
                    byte[] line = Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":" +
                        new JavaScriptSerializer().Serialize(ex.Message) + "}\r\n");
                    using (var st = Console.OpenStandardOutput()) st.Write(line, 0, line.Length);
                }
                catch { }
                return 1;
            }
        }

        public static int Run(string[] args)
        {
            string jsonPath = null, logPath = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--batch" && i + 1 < args.Length) jsonPath = args[i + 1];
                else if (args[i] == "--log" && i + 1 < args.Length) logPath = args[i + 1];
            }
            if (string.IsNullOrWhiteSpace(jsonPath)) return 1;

            if (string.IsNullOrWhiteSpace(logPath))
                logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "运行组件", "batch.log");
            var log = new BatchLog(logPath);

            log.Line("===== PDFQFZ 批处理任务 =====");
            log.Line("版本: " + GetVersionText());
            log.Line("开始时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            if (!File.Exists(jsonPath)) { log.Err("任务文件不存在：" + jsonPath); return 1; }
            Dictionary<string, object> task;
            try
            {
                task = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
                    File.ReadAllText(jsonPath, Encoding.UTF8));
            }
            catch (Exception ex) { log.Err("任务 JSON 解析失败：" + ex.Message); return 1; }
            if (task == null) { log.Err("任务 JSON 为空或格式错误"); return 1; }

            string type = S(task, "task");
            if (string.IsNullOrWhiteSpace(type)) { log.Err("缺少 task 字段（pdf-stamp/pdf-watermark/pdf-text-stamp/img-watermark）"); return 1; }
            var inputs = ParseInputs(task);
            if (inputs.Count == 0) { log.Err("缺少 input 字段（源文件夹路径，可为单个字符串或字符串数组）"); return 1; }
            foreach (var ip in inputs)
                if (!Directory.Exists(ip)) { log.Err("输入目录不存在：" + ip); return 1; }

            log.Line("任务类型: " + TaskTypeName(type));
            log.Line(inputs.Count == 1 ? "输入目录: " + inputs[0] : "输入目录（" + inputs.Count + " 个）: " + string.Join("；", inputs));

            _done = new ManualResetEvent(false);
            _batchResult = null;
            var bridge = new CSharpBridge(msg =>
            {
                try
                {
                    var m = Parse(msg);
                    if (m != null && S(m, "kind") == "batch-done")
                    {
                        _batchResult = (m.ContainsKey("payload") && m["payload"] is Dictionary<string, object>)
                            ? (Dictionary<string, object>)m["payload"] : null;
                        _done.Set();
                    }
                    if (m != null && S(m, "kind") == "batch-preview-done")
                    {
                        _previewResult = (m.ContainsKey("payload") && m["payload"] is Dictionary<string, object>)
                            ? (Dictionary<string, object>)m["payload"] : null;
                        _previewDone.Set();
                    }
                }
                catch { }
            });
            int code = 0;
            try
            {
                _folderSummaries.Clear();
                for (int idx = 0; idx < inputs.Count; idx++)
                {
                    string input = inputs[idx];
                    if (inputs.Count > 1)
                    {
                        log.Line("");
                        log.Line("===== 文件夹 " + (idx + 1) + "/" + inputs.Count + "：" + input + " =====");
                    }
                    int c;
                    switch (type)
                    {
                        case "pdf-stamp": c = PdfStamp(bridge, task, input, log); break;
                        case "pdf-watermark": c = PdfWatermark(bridge, task, input, log); break;
                        case "pdf-text-stamp": c = PdfTextStamp(bridge, task, input, log); break;
                        case "img-watermark": c = ImgWatermark(bridge, task, input, log); break;
                        default: log.Err("未知任务类型：" + type); c = 1; break;
                    }
                    _folderSummaries.Add(log.Summary ?? "");
                    if (c == 2) code = 2;
                    else if (c == 1 && code == 0) code = 1;
                }
            }
            catch (Exception ex) { log.Err("执行异常：" + ex.Message); code = 1; }

            log.Line("结束时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            log.Line("退出码: " + code + (code == 0 ? "（全部成功）" : code == 1 ? "（参数或执行错误）" : "（部分失败）"));
            log.Line("");
            log.Line("===== 给用户的反馈 =====");
            if (_folderSummaries.Count <= 1) log.Line(log.Summary);
            else
                for (int i = 0; i < _folderSummaries.Count; i++)
                    log.Line("文件夹" + (i + 1) + "（" + inputs[i] + "）：" + _folderSummaries[i]);
            log.End(code);
            return code;
        }

        static string TaskTypeName(string t)
        {
            switch (t)
            {
                case "pdf-stamp": return "PDF 全部盖章";
                case "pdf-watermark": return "PDF 加水印";
                case "pdf-text-stamp": return "PDF 按文字盖章";
                case "img-watermark": return "图片水印";
                default: return t;
            }
        }

        /// <summary>V384：input 支持单个字符串或字符串数组（同类型任务一个 JSON 处理多个文件夹）。</summary>
        static List<string> ParseInputs(Dictionary<string, object> task)
        {
            var list = new List<string>();
            object v;
            if (task.TryGetValue("input", out v))
            {
                if (v is string s) { if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim()); }
                else if (v is System.Collections.ArrayList arr)
                    foreach (var e in arr)
                        if (e is string es && !string.IsNullOrWhiteSpace(es)) list.Add(es.Trim());
            }
            return list;
        }

        /// <summary>V384：按「源文件名_ + mark 前缀」在输出目录反查实际输出文件名（支持已处理V序号、时间戳命名）。</summary>
        static string FindOutputName(string outDir, string srcFile, string mark)
        {
            if (string.IsNullOrEmpty(srcFile)) return "";
            string baseName = Path.GetFileNameWithoutExtension(srcFile);
            try
            {
                var matched = Directory.GetFiles(outDir)
                    .Where(x =>
                    {
                        string fn = Path.GetFileName(x);
                        return fn.StartsWith(baseName + "_", StringComparison.OrdinalIgnoreCase)
                            && (string.IsNullOrEmpty(mark) || fn.IndexOf(mark, StringComparison.OrdinalIgnoreCase) >= 0);
                    })
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (matched.Length > 0) return Path.GetFileName(matched[matched.Length - 1]);
            }
            catch { }
            return baseName + "_" + mark + "?";
        }

        static string GetVersionText()
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return v == null ? "2.4.0.0" : v.ToString(4);
        }

        // ---------- 任务执行器 ----------

        /// <summary>pdf-stamp：文件夹内所有 PDF 全部盖章（可带骑缝章）。</summary>
        static int PdfStamp(CSharpBridge bridge, Dictionary<string, object> task, string input, BatchLog log)
        {
            var stamp = task.ContainsKey("stamp") ? task["stamp"] as Dictionary<string, object> : null;
            if (stamp == null || string.IsNullOrWhiteSpace(S(stamp, "image")))
            { log.Err("缺少 stamp.image（印章图片路径）"); return 1; }
            string img = S(stamp, "image");
            if (!File.Exists(img)) { log.Err("印章图片不存在：" + img); return 1; }

            var r = Parse(bridge.OpenDirectory(input));
            if (!Ok(r)) { log.Err("加载文件夹失败：" + Err(r)); return 1; }
            int n = Count(r, "count");
            log.Line("[步骤] 已加载文件夹：共 " + n + " 个 PDF");

            string stampName = SetupStamp(bridge, stamp);
            if (stampName == null) { log.Err("印章设置失败"); return 1; }
            log.Line("[步骤] 印章：" + Path.GetFileName(img));

            var args = BuildGenArgs(task, input);
            // V381 修复：文件夹模式"所见即所得"——RunBatchFiles 只输出已放置章（previewSnap），
            // 必须先按范围批量放置章（BatchPreviewAll，batchId=-1）再生成，否则输出 PDF 无章却报成功。
            _previewDone = new ManualResetEvent(false);
            _previewResult = null;
            var pv = Parse(bridge.BatchPreviewAll(args.Range, args.RangeStart, args.RangeEnd, args.XPct, args.YPct, true, args.OutDir));
            if (!Ok(pv) || !B(pv, "started")) { log.Err("批量放置印章失败：" + Err(pv)); return 1; }
            if (!_previewDone.WaitOne(TimeSpan.FromMinutes(10))) { log.Err("批量放置超时（10 分钟）"); return 1; }
            if (_previewResult == null || !Ok(_previewResult)) { log.Err("批量放置印章失败：" + (_previewResult == null ? "无结果" : Err(_previewResult))); return 1; }
            int placedN = 0;
            if (_previewResult.ContainsKey("results") && _previewResult["results"] is System.Collections.ArrayList parr)
                foreach (var pr in parr)
                {
                    var prd = pr as Dictionary<string, object>;
                    if (prd != null && Ok(prd)) placedN += I(prd, "count", 0);
                }
            log.Line("[步骤] 批量放置印章完成：共放置 " + placedN + " 枚");

            _done.Reset();
            string json = bridge.GenerateFiles(args.OutDir, args.Mode, args.Dpi, args.Mark, args.Pos, args.SeqType,
                args.Pad, args.Ts, args.TsFormat, args.QfzType, args.WzType, args.WzPercent, args.MaxSplit,
                false, args.Range, args.RangeStart, args.RangeEnd, args.XPct, args.YPct, true, false);
            var g = Parse(json);
            if (Ok(g) && B(g, "started"))
            {
                if (!_done.WaitOne(TimeSpan.FromMinutes(10))) { log.Err("批处理超时（10 分钟）"); return 1; }
                g = _batchResult ?? g;
            }
            return ReportGen(g, args.OutDir, args.Mark, log);
        }

        /// <summary>pdf-watermark：文件夹内所有 PDF 加文字水印（无章）。</summary>
        static int PdfWatermark(CSharpBridge bridge, Dictionary<string, object> task, string input, BatchLog log)
        {
            var wm = task.ContainsKey("watermark") ? task["watermark"] as Dictionary<string, object> : null;
            if (wm == null || string.IsNullOrWhiteSpace(S(wm, "text")))
            { log.Err("缺少 watermark.text（水印文字）"); return 1; }

            var r = Parse(bridge.OpenDirectory(input));
            if (!Ok(r)) { log.Err("加载文件夹失败：" + Err(r)); return 1; }
            int n = Count(r, "count");
            log.Line("[步骤] 已加载文件夹：共 " + n + " 个 PDF");

            bridge.SetWatermarkEnabled(true);
            string wmJson = BuildWmJson(wm);
            var box = Parse(bridge.AddWatermarkBox(0, WmX(wm), WmY(wm), WmW(wm), WmH(wm), wmJson));
            if (!Ok(box)) { log.Err("添加水印框失败：" + Err(box)); return 1; }
            log.Line("[步骤] 水印：「" + S(wm, "text") + "」已应用到所有页");

            var args = BuildGenArgs(task, input);
            // 水印任务：无章 → qfzType 必须为 1（不加骑缝章），needStamp=false 不检查印章
            args.QfzType = 1;
            _done.Reset();
            string json = bridge.GenerateFiles(args.OutDir, args.Mode, args.Dpi, args.Mark, args.Pos, args.SeqType,
                args.Pad, args.Ts, args.TsFormat, 1, 0, 50, 0,
                false, 0, 1, 1, 50, 50, true, false);
            var g = Parse(json);
            if (Ok(g) && B(g, "started"))
            {
                if (!_done.WaitOne(TimeSpan.FromMinutes(10))) { log.Err("批处理超时（10 分钟）"); return 1; }
                g = _batchResult ?? g;
            }
            return ReportGen(g, args.OutDir, args.Mark, log);
        }

        /// <summary>pdf-text-stamp：按文字自动定位盖章（文件夹模式对全部文件）。</summary>
        static int PdfTextStamp(CSharpBridge bridge, Dictionary<string, object> task, string input, BatchLog log)
        {
            var stamp = task.ContainsKey("stamp") ? task["stamp"] as Dictionary<string, object> : null;
            var ts = task.ContainsKey("textStamp") ? task["textStamp"] as Dictionary<string, object> : null;
            if (stamp == null || string.IsNullOrWhiteSpace(S(stamp, "image")))
            { log.Err("缺少 stamp.image（印章图片路径）"); return 1; }
            if (ts == null || string.IsNullOrWhiteSpace(S(ts, "keyword")))
            { log.Err("缺少 textStamp.keyword（盖章文字）"); return 1; }
            string img = S(stamp, "image");
            if (!File.Exists(img)) { log.Err("印章图片不存在：" + img); return 1; }

            var r = Parse(bridge.OpenDirectory(input));
            if (!Ok(r)) { log.Err("加载文件夹失败：" + Err(r)); return 1; }
            log.Line("[步骤] 已加载文件夹：共 " + Count(r, "count") + " 个 PDF");

            SetupStamp(bridge, stamp);
            log.Line("[步骤] 印章：" + Path.GetFileName(img));

            string keyword = S(ts, "keyword");
            string keywords = S(ts, "keywords");                     // 附加关键词（可空）
            int ctxRange = I(ts, "ctxRange", 10);
            bool requireAll = (S(ts, "matchMode") ?? "all") == "all";
            bool ignoreSpace = B(ts, "ignoreSpace");
            var off = ts.ContainsKey("offset") ? ts["offset"] as Dictionary<string, object> : null;
            bool offEnabled = off != null && (D(off, "x") != 0 || D(off, "y") != 0);
            double offX = off != null ? D(off, "x") : 0, offY = off != null ? D(off, "y") : 0;

            log.Line("[步骤] 按文字「" + keyword + "」放置印章…");
            var a = Parse(bridge.AutoStampDir(keyword, keywords, ctxRange, requireAll, ignoreSpace, offEnabled, (float)offX, (float)offY));
            if (!Ok(a)) { log.Err("按文字盖章失败：" + Err(a)); return 1; }
            log.Line("[步骤] 按文字盖章完成：共新增 " + (I(a, "total", 0)) + " 个章");

            var args = BuildGenArgs(task, input);
            _done.Reset();
            string json = bridge.GenerateFiles(args.OutDir, args.Mode, args.Dpi, args.Mark, args.Pos, args.SeqType,
                args.Pad, args.Ts, args.TsFormat, args.QfzType, args.WzType, args.WzPercent, args.MaxSplit,
                false, args.Range, args.RangeStart, args.RangeEnd, args.XPct, args.YPct, true, false);
            var g = Parse(json);
            if (Ok(g) && B(g, "started"))
            {
                if (!_done.WaitOne(TimeSpan.FromMinutes(10))) { log.Err("批处理超时（10 分钟）"); return 1; }
                g = _batchResult ?? g;
            }
            return ReportGen(g, args.OutDir, args.Mark, log);
        }

        /// <summary>img-watermark：图片文件夹全部加文字水印。</summary>
        static int ImgWatermark(CSharpBridge bridge, Dictionary<string, object> task, string input, BatchLog log)
        {
            var wm = task.ContainsKey("watermark") ? task["watermark"] as Dictionary<string, object> : null;
            if (wm == null || string.IsNullOrWhiteSpace(S(wm, "text")))
            { log.Err("缺少 watermark.text（水印文字）"); return 1; }

            var r = Parse(bridge.LoadImageFolder(input));
            if (!Ok(r)) { log.Err("加载图片文件夹失败：" + Err(r)); return 1; }
            int n = Count(r, "total");
            log.Line("[步骤] 已加载图片：共 " + n + " 张");

            string wmJson = BuildWmJson(wm);
            var box = Parse(bridge.AddWatermarkBox(0, WmX(wm), WmY(wm), WmW(wm), WmH(wm), wmJson));
            if (!Ok(box)) { log.Err("添加水印框失败：" + Err(box)); return 1; }
            log.Line("[步骤] 水印：「" + S(wm, "text") + "」已应用");

            var outDir = OutDir(task, input);
            var g = Parse(bridge.ApplyImageWatermarks(outDir));
            if (!Ok(g)) { log.Err("图片水印输出失败：" + Err(g)); return 1; }
            int doneN = Count(g, "done"), failN = Count(g, "failed");
            // V384：反馈列出实际输出文件名（输出目录内含 mark 的文件；图片水印输出名 = 源名_mark序号.ext）
            var outs = new List<string>();
            try
            {
                string mark = "已处理V";
                var outCfg = task.ContainsKey("output") ? task["output"] as Dictionary<string, object> : null;
                if (outCfg != null && !string.IsNullOrWhiteSpace(S(outCfg, "mark"))) mark = S(outCfg, "mark");
                foreach (var fp in Directory.GetFiles(outDir))
                {
                    string fn = Path.GetFileName(fp);
                    if (fn.IndexOf(mark, StringComparison.OrdinalIgnoreCase) >= 0) outs.Add(fn);
                }
            }
            catch { }
            log.Summary = "已完成 " + n + " 张图片的水印处理；输出文件：" + (outs.Count > 0 ? string.Join("、", outs) : outDir) + "（位于 " + outDir + "）";
            log.Line("[完成] 图片水印输出：成功 " + doneN + " 个，失败 " + failN + " 个" + (Count(g, "skipped") > 0 ? "（跳过 " + Count(g, "skipped") + " 张无水印框）" : ""));
            log.Line("输出目录：" + outDir);
            return failN > 0 ? 2 : 0;
        }

        // ---------- 结果报告 ----------
        static int ReportGen(Dictionary<string, object> g, string outDir, string mark, BatchLog log)
        {
            if (!Ok(g)) { log.Err("生成失败：" + Err(g)); return 1; }
            var success = g.ContainsKey("success") ? g["success"] as System.Collections.ArrayList : null;
            var failed = g.ContainsKey("failed") ? g["failed"] as System.Collections.ArrayList : null;
            int okN = success != null ? success.Count : 0;
            int failN = failed != null ? failed.Count : 0;
            // V384：反馈列出实际输出文件名（S5/S7）
            var outNames = new List<string>();
            if (success != null)
                foreach (var f in success)
                {
                    string outName = FindOutputName(outDir, Convert.ToString(f), mark);
                    if (!string.IsNullOrEmpty(outName)) outNames.Add(outName);
                    log.Line("[生成] " + f + " → " + outName + " 成功");
                }
            if (failed != null)
                foreach (var f in failed)
                {
                    var d = f as Dictionary<string, object>;
                    log.Line("[失败] " + (d != null ? S(d, "name") : "") + "：" + (d != null ? S(d, "reason") : ""));
                }
            log.Summary = "已完成 " + okN + " 个文件处理" + (failN > 0 ? "，失败 " + failN + " 个（原因见日志）" : "")
                + "；输出文件：" + (outNames.Count > 0 ? string.Join("、", outNames) : outDir) + "（位于 " + outDir + "）";
            log.Line("[结果] 成功 " + okN + " 个，失败 " + failN + " 个");
            log.Line("输出目录：" + outDir);
            return failN > 0 ? 2 : 0;
        }

        // ---------- 参数构建 ----------
        static string SetupStamp(CSharpBridge bridge, Dictionary<string, object> stamp)
        {
            try
            {
                string img = S(stamp, "image");
                var paths = new List<string>(AppConfig.LoadStampPaths());
                if (!paths.Any(p => string.Equals(Path.GetFullPath(p), Path.GetFullPath(img), StringComparison.OrdinalIgnoreCase)))
                    AppConfig.AppendStampPaths(new[] { img });
                // V381：显示名优先按路径匹配现有印章库条目——内置默认章显示名"测试公章"≠文件名"公章"，
                // 按文件名 SelectStamp 会导致 CurrentStampPath（按显示名找路径）落空 → "请先选择印章图片"。
                // 新印章（不在库）AppendStampPaths 会以文件名登记显示名，路径匹配后仍取文件名，行为不变。
                var entry = AppConfig.LoadStampEntries()
                    .FirstOrDefault(x => string.Equals(Path.GetFullPath(x.Path), Path.GetFullPath(img), StringComparison.OrdinalIgnoreCase));
                string name = entry != null ? entry.DisplayName : Path.GetFileNameWithoutExtension(img);
                bridge.SelectStamp(name);
                // 印章尺寸/旋转/不透明度/随机位移/盖章渲染
                // V382（P4）：任务 JSON 显式给出字段才覆盖该章参数；未给出的保留 config.ini 该章配置，
                // 避免模板默认值覆盖用户界面已配置的参数。
                // V382（P9）：随机角度位移 / 盖章渲染参数支持任务 JSON 传入（字段名与默认值见《智能体调用手册》/任务模板）。
                var sp = AppConfig.LoadStampParams(name);
                if (stamp.ContainsKey("sizeMm")) { int sz = I(stamp, "sizeMm", 20); if (sz > 0) sp.Size = sz; }
                if (stamp.ContainsKey("rotation")) sp.Rotation = I(stamp, "rotation", 0);
                if (stamp.ContainsKey("opacity")) sp.Opacity = I(stamp, "opacity", 100);
                if (stamp.ContainsKey("randomParams")) sp.RandomParams = B(stamp, "randomParams");
                if (stamp.ContainsKey("randomRotation")) sp.RandomRange = I(stamp, "randomRotation", 45);
                if (stamp.ContainsKey("randomOffsetXMm")) sp.RandomOffsetXMm = I(stamp, "randomOffsetXMm", 10);
                if (stamp.ContainsKey("randomOffsetYMm")) sp.RandomOffsetYMm = I(stamp, "randomOffsetYMm", 10);
                if (stamp.ContainsKey("textureQuality")) sp.TextureQuality = B(stamp, "textureQuality");
                if (stamp.ContainsKey("textureBrightness")) sp.TextureBrightness = I(stamp, "textureBrightness", 0);
                if (stamp.ContainsKey("textureBlob")) sp.TextureBlob = I(stamp, "textureBlob", 0);
                if (stamp.ContainsKey("textureGradient")) sp.TextureGradient = I(stamp, "textureGradient", 0);
                if (stamp.ContainsKey("textureWhite")) sp.TextureWhite = I(stamp, "textureWhite", 0);
                if (stamp.ContainsKey("textureSpot")) sp.TextureSpot = I(stamp, "textureSpot", 0);
                if (stamp.ContainsKey("textureRadial")) sp.TextureRadial = I(stamp, "textureRadial", 0);
                if (stamp.ContainsKey("textureCast")) sp.TextureCast = I(stamp, "textureCast", 0);
                if (stamp.ContainsKey("texturePresetIndex")) sp.TexturePresetIndex = I(stamp, "texturePresetIndex", 0);
                AppConfig.SaveStampParams(name, sp);
                // 同步静态字段（供桥接/其他调用方读取）
                AppConfig.Size = sp.Size;
                AppConfig.Rotation = sp.Rotation;
                AppConfig.Opacity = sp.Opacity;
                AppConfig.SaveUiConfig();
                return name;
            }
            catch (Exception) { return null; }
        }

        sealed class GenArgs
        {
            public string OutDir, Mode, Mark, TsFormat;
            public int Dpi, Pos, SeqType, Pad, QfzType, WzType, WzPercent, MaxSplit, Range, RangeStart, RangeEnd, XPct, YPct;
            public bool Ts;
        }

        static GenArgs BuildGenArgs(Dictionary<string, object> task, string input)
        {
            var args = new GenArgs();
            args.OutDir = OutDir(task, input);
            var outCfg = task.ContainsKey("output") ? task["output"] as Dictionary<string, object> : null;
            args.Mode = (S(outCfg, "mode") ?? "merge").ToLowerInvariant() == "overlay" ? "overlay" : "merge";
            args.Dpi = I(outCfg, "dpi", 150);
            args.Mark = S(outCfg, "mark") ?? "已处理V";
            args.Pos = I(outCfg, "pos", 0);
            args.SeqType = I(outCfg, "seqType", 0);
            args.Pad = I(outCfg, "pad", 1);
            args.Ts = B(outCfg, "ts");
            args.TsFormat = S(outCfg, "tsFormat") ?? "yyyyMMdd";
            // 骑缝章
            var seam = task.ContainsKey("stamp") ? (task["stamp"] as Dictionary<string, object>)
                : null;
            if (seam != null && seam.ContainsKey("seam"))
            {
                var sc = seam["seam"] as Dictionary<string, object>;
                bool on = sc != null && B(sc, "enabled");
                // V1.0.0.32：骑缝章类型新值 全部页/奇数页/偶数页（默认全部页）；兼容旧值 加盖/单页/双页
                string st = on ? (S(sc, "type") ?? "全部页") : "不加";
                args.QfzType = (st == "全部页" || st == "加盖") ? 0 : (st == "奇数页" || st == "单页") ? 2 : (st == "偶数页" || st == "双页") ? 3 : 0;
                string pos = S(sc, "pos") ?? "右";
                args.WzType = pos == "下" ? 0 : pos == "上" ? 1 : pos == "左" ? 2 : 3;
                args.WzPercent = I(sc, "percent", 50);
                args.MaxSplit = (S(sc, "split") ?? "auto") == "auto" ? 0 : Math.Max(1, I(sc, "split", 0));
            }
            else { args.QfzType = 1; args.WzType = 3; args.WzPercent = 50; args.MaxSplit = 0; }
            // 范围（pdf-stamp/pdf-text-stamp 用）
            var range = task.ContainsKey("range") ? task["range"] as Dictionary<string, object> : null;
            string rm = S(range, "mode") ?? "all";
            args.Range = rm == "first" ? 1 : rm == "last" ? 2 : rm == "custom" ? 3 : 0;
            args.RangeStart = I(range, "start", 1);
            args.RangeEnd = I(range, "end", 1);
            args.XPct = I(range, "xPct", 50);
            args.YPct = I(range, "yPct", 50);
            return args;
        }

        static string OutDir(Dictionary<string, object> task, string input)
        {
            var outCfg = task.ContainsKey("output") ? task["output"] as Dictionary<string, object> : null;
            string dir = S(outCfg, "dir");
            if (string.IsNullOrWhiteSpace(dir)) dir = Path.Combine(input, "已处理");
            try { Directory.CreateDirectory(dir); } catch { }
            return dir;
        }

        /// <summary>水印参数 → AddWatermarkBox jsonParams（对齐 CSharpBridge.Watermark.cs 解析字段）。</summary>
        static string BuildWmJson(Dictionary<string, object> wm)
        {
            var d = new Dictionary<string, object>
            {
                ["text"] = S(wm, "text") ?? "",
                ["fontName"] = S(wm, "fontName") ?? "微软雅黑",
                ["colorArgb"] = I(wm, "colorArgb", unchecked((int)0xFF1F2329)),
                // V384（S9）：水印默认透明度 70 -> 30（用户指令）
                ["opacity"] = I(wm, "opacity", 30),
                ["rotation"] = I(wm, "rotation", 30),
                ["align"] = I(wm, "align", 0),
                ["letterSpacing"] = I(wm, "letterSpacing", 0),
                ["lineSpacing"] = I(wm, "lineSpacing", 0),
                ["fontScale"] = D(wm, "fontScale") > 0 ? D(wm, "fontScale") : 0.8,
                // V382（P8）：fsToS（字号/页面短边比例，PDF/图片水印统一短边缩放，横竖页字号一致）；
                // 默认 0.08（字号≈页面短边 8%）。不传时旧版 fallback 会把字号算成框高×fontScale（≈页面高48%），导致水印特大。
                ["fsToS"] = D(wm, "fsToS") > 0 ? D(wm, "fsToS") : 0.08,
                ["bold"] = B(wm, "bold"),
                ["italic"] = B(wm, "italic")
            };
            return new JavaScriptSerializer().Serialize(d);
        }

        // 水印框位置（x/y/w/h 相对页面 0~1，Y 向下）
        static double WmX(Dictionary<string, object> wm)
        {
            if (wm.ContainsKey("x")) return D(wm, "x");
            return S(wm, "pos") == "bottom" ? 0.2 : 0.2;
        }
        static double WmY(Dictionary<string, object> wm)
        {
            if (wm.ContainsKey("y")) return D(wm, "y");
            return S(wm, "pos") == "bottom" ? 0.72 : 0.2;
        }
        static double WmW(Dictionary<string, object> wm)
        {
            if (wm.ContainsKey("w")) return D(wm, "w");
            return 0.6;
        }
        static double WmH(Dictionary<string, object> wm)
        {
            if (wm.ContainsKey("h")) return D(wm, "h");
            return S(wm, "pos") == "bottom" ? 0.2 : 0.6;
        }

        // ---------- JSON 辅助 ----------
        static Dictionary<string, object> Parse(string json)
        {
            try { return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json); }
            catch { return null; }
        }
        static bool Ok(Dictionary<string, object> d) { return d != null && B(d, "ok"); }
        static string Err(Dictionary<string, object> d) { return d != null ? S(d, "error") : "返回解析失败"; }
        static int Count(Dictionary<string, object> d, string key)
        {
            if (d == null || !d.ContainsKey(key)) return 0;
            var v = d[key];
            if (v is int) return (int)v;
            if (v is long) return (int)(long)v;
            try { return Convert.ToInt32(v); } catch { return 0; }
        }
        static string S(Dictionary<string, object> d, string key)
        {
            if (d == null || !d.ContainsKey(key) || d[key] == null) return "";
            return Convert.ToString(d[key]);
        }
        static int I(Dictionary<string, object> d, string key, int def)
        {
            if (d == null || !d.ContainsKey(key) || d[key] == null) return def;
            try { return Convert.ToInt32(d[key]); } catch { return def; }
        }
        static double D(Dictionary<string, object> d, string key)
        {
            if (d == null || !d.ContainsKey(key) || d[key] == null) return 0;
            try { return Convert.ToDouble(d[key]); } catch { return 0; }
        }
        static bool B(Dictionary<string, object> d, string key)
        {
            if (d == null || !d.ContainsKey(key) || d[key] == null) return false;
            try { return Convert.ToBoolean(d[key]); } catch { return false; }
        }
    }

    /// <summary>批处理日志（UTF-8 带 BOM；末尾含"给用户的反馈"摘要，供智能体转述）。</summary>
    public sealed class BatchLog
    {
        readonly string _path;
        readonly List<string> _lines = new List<string>();
        public string Summary = "";

        public BatchLog(string path)
        {
            _path = path;
            try { Directory.CreateDirectory(Path.GetDirectoryName(path)); } catch { }
        }
        public void Line(string s) { _lines.Add(s); }
        public void Err(string s) { _lines.Add("[错误] " + s); }

        public void End(int code)
        {
            try
            {
                File.WriteAllText(_path, string.Join("\r\n", _lines) + "\r\n", new UTF8Encoding(true));
            }
            catch { }
        }
    }
}
