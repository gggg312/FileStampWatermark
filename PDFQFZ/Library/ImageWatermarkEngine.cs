using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using iTextSharp.text.pdf;

namespace PDFQFZ.Library
{
    /// <summary>
    /// 图片文字水印引擎（V2.4.0.97 新增，V97 第二步：图片批量水印）。
    /// 移植自 WPF「GG印章水印助手」OutputPipeline + PreviewRenderer.DrawBox，公式对齐 Web 端
    /// StampEngine.DrawTextWatermark / 前端 wmTextStyle（预览=输出）：
    ///   字号 = 框高 × FontScale（超宽按最长行收窄 ×1.05，下限 0.5px）→ 字距 adv = 字号×max(0.2,1+ls/100) →
    ///   行距 lineH = 字号×max(0.5,1+LineSpacing/100) → 逐行逐字符定位，绕框中心旋转。
    /// 与 PDF 侧唯一差异：图片坐标系 Y 向下（与屏幕系/前端 CSS 一致），旋转【不取反】；
    /// 斜体用真实 FontStyle.Italic（System.Drawing 支持，与浏览器 CSS italic 一致；PDF 侧 iText 无中文斜体才用错切近似）。
    /// 下划线/删除线按行端点连线（线宽 fs×0.05；下划线 = 行中心下方 0.46em，删除线 = 行中心）。
    /// 输出保留原格式（JPEG 质量 100），命名走 OutputFileNamingPolicy（默认 原文件名_已加水印V1.ext）。
    /// </summary>
    public static class ImageWatermarkEngine
    {
        /// <summary>支持的图片扩展名（对齐 WPF ImageQueueManager.SupportedExtensions）。
        /// V1.0.0.37: 声明含 GIF/TIFF，但按单帧处理——动画 GIF 取首帧、多页 TIFF 取首页；如需多帧/多页支持请另行扩展。</summary>
        public static readonly string[] SupportedExtensions =
            { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };
        /// <summary>V1.0.0.38：水印高频诊断日志开关（默认关；config.ini diagWatermark=1 时由壳层启动注入）。关时跳过逐行/逐框写盘 I/O。</summary>
        public static bool DiagEnabled = false;

        public static bool IsSupported(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string ext = Path.GetExtension(path);
            return ext != null && SupportedExtensions.Contains(ext.ToLowerInvariant());
        }

        public class BatchResult
        {
            public int Total, Done, Skipped, Failed;
            public readonly List<string> Outputs = new List<string>();
            public readonly List<string> Failures = new List<string>();
        }

        /// <summary>批量输出：每张图取其各自水印框（boxesByFile 返回该图框列表；空列表=跳过并提示）。</summary>
        public static BatchResult RunBatch(
            IList<string> files,
            Func<string, IList<WatermarkBox>> boxesByFile,
            string outputFolder,
            OutputNamingOptions naming,
            Action<int, int, string> progress,
            Action<string> log)
        {
            var result = new BatchResult { Total = files.Count };
            for (int i = 0; i < files.Count; i++)
            {
                string file = files[i];
                string name = Path.GetFileName(file);
                try
                {
                    var boxes = boxesByFile != null ? boxesByFile(file) : null;
                    if (boxes == null || boxes.Count == 0)
                    {
                        result.Skipped++;
                        if (log != null) log("跳过（无水印框）：" + name);
                        if (progress != null) progress(i + 1, files.Count, name);
                        continue;
                    }
                    string outPath = ApplyOne(file, boxes, outputFolder, naming);
                    result.Done++;
                    result.Outputs.Add(outPath);
                    if (log != null) log("完成：" + name + " → " + Path.GetFileName(outPath));
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Failures.Add(name + "：" + ex.Message);
                    if (log != null) log("失败：" + name + "（" + ex.Message + "）");
                }
                if (progress != null) progress(i + 1, files.Count, name);
            }
            return result;
        }

        /// <summary>单张图加水印输出。返回输出文件完整路径。</summary>
        public static string ApplyOne(
            string srcPath, IList<WatermarkBox> boxes, string outputFolder, OutputNamingOptions naming)
        {
            if (!File.Exists(srcPath)) throw new FileNotFoundException("源图片不存在：" + srcPath);
            if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);
            if (boxes == null || boxes.Count == 0) throw new InvalidOperationException("水印框为空");

            var namingOpts = naming ?? new OutputNamingOptions { Mark = "已加水印V" };
            string outPath = OutputFileNamingPolicy.GetNextOutputPath(outputFolder, srcPath, namingOpts);

            using (Image src = Image.FromFile(srcPath))
            {
                using (Bitmap outBmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(outBmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TextRenderingHint = TextRenderingHint.AntiAlias;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(src, 0, 0, src.Width, src.Height);
                        foreach (var box in boxes) DrawBox(g, box, src.Width, src.Height);
                    }
                    SaveWithFormat(outBmp, srcPath, outPath);
                }
            }
            return outPath;
        }

        private static readonly object WmFontLock = new object();
        private static readonly Dictionary<string, BaseFont> WmFontCache = new Dictionary<string, BaseFont>();

        private static string ResolveWatermarkFontPath(string fontName, bool bold)
        {
            string wf = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + @"\Fonts";
            string n = string.IsNullOrWhiteSpace(fontName) ? "微软雅黑" : fontName.Trim();
            if (n.IndexOf("雅黑", StringComparison.Ordinal) >= 0 || n.IndexOf("YaHei", StringComparison.OrdinalIgnoreCase) >= 0)
                return bold ? wf + "msyhbd.ttc,0" : wf + "msyh.ttc,0";
            if (n.IndexOf("宋体", StringComparison.Ordinal) >= 0 || n.IndexOf("SimSun", StringComparison.OrdinalIgnoreCase) >= 0)
                return wf + "simsun.ttc,0";
            if (n.IndexOf("黑体", StringComparison.Ordinal) >= 0 || n.IndexOf("SimHei", StringComparison.OrdinalIgnoreCase) >= 0)
                return wf + "simhei.ttf";
            if (n.IndexOf("楷体", StringComparison.Ordinal) >= 0 || n.IndexOf("KaiTi", StringComparison.OrdinalIgnoreCase) >= 0)
                return wf + "simkai.ttf";
            if (n.IndexOf("仿宋", StringComparison.Ordinal) >= 0 || n.IndexOf("FangSong", StringComparison.OrdinalIgnoreCase) >= 0)
                return wf + "simfang.ttf";
            return wf + "msyh.ttc,0";
        }

        private static BaseFont GetWatermarkFont(string fontName, bool bold)
        {
            string path = ResolveWatermarkFontPath(fontName, bold);
            lock (WmFontLock)
            {
                if (WmFontCache.TryGetValue(path, out BaseFont cached)) return cached;
                try
                {
                    BaseFont bf = BaseFont.CreateFont(path, BaseFont.IDENTITY_H, BaseFont.EMBEDDED);
                    WmFontCache[path] = bf;
                    return bf;
                }
                catch
                {
                    BaseFont bf2 = BaseFont.CreateFont(BaseFont.HELVETICA, BaseFont.WINANSI, BaseFont.NOT_EMBEDDED);
                    WmFontCache[path] = bf2;
                    return bf2;
                }
            }
        }

        /// <summary>V300: 公共换行算法（用 iTextSharp BaseFont.GetWidth，和 PDF 完全一样）。</summary>
        public static string WrapText(string text, float boxWpx, float fs, float letterSpacing, string fontName, bool bold, bool italic)
        {
            if (DiagEnabled) try { System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wm_debug.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " [C#-WRAP] textLen=" + (text==null?0:text.Length) + " boxWpx=" + boxWpx.ToString("F1") + " fs=" + fs.ToString("F1") + " ls=" + letterSpacing + " font=" + fontName + Environment.NewLine, System.Text.Encoding.UTF8); } catch { }
            if (string.IsNullOrEmpty(text) || boxWpx <= 0 || fs < 0.5f) return text ?? "";
            if (string.IsNullOrWhiteSpace(fontName)) fontName = "微软雅黑";
            FontStyle style = FontStyle.Regular;
            if (bold) style |= FontStyle.Bold;
            if (italic) style |= FontStyle.Italic;
            using (Font measureFont = new Font(fontName, fs, style, GraphicsUnit.Pixel))
            using (Bitmap bmp = new Bitmap(1, 1))
            using (Graphics mg = Graphics.FromImage(bmp))
            {
                mg.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                string[] rawLines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
                var wrappedLines = new List<string>();
                foreach (string rawLine in rawLines)
                {
                    if (string.IsNullOrEmpty(rawLine)) { wrappedLines.Add(""); continue; }
                    string cur = "";
                    float curW = 0;
                    foreach (char ch in rawLine)
                    {
                        float cw = (ch >= 0x4e00 && ch <= 0x9fff)
                            ? mg.MeasureString(ch.ToString(), measureFont, 10000, StringFormat.GenericTypographic).Width
                            : mg.MeasureString(ch.ToString(), measureFont, 10000, StringFormat.GenericTypographic).Width * 0.9f;
                        if (letterSpacing > 0) cw += letterSpacing;
                        if (curW + cw > boxWpx && cur.Length > 0)
                        {
                            wrappedLines.Add(cur);
                            cur = ch.ToString();
                            curW = cw;
                        }
                        else
                        {
                            cur += ch;
                            curW += cw;
                        }
                    }
                    wrappedLines.Add(cur);
                }
                return string.Join("\n", wrappedLines);
            }
        }

        /// <summary>绘制单个水印框（预览与输出共用公式；Y 向下坐标系，旋转不取反）。</summary>
        private static void DrawBox(Graphics g, WatermarkBox box, int imgW, int imgH)
        {
            if (box == null || imgW <= 0 || imgH <= 0) return;
            if (DiagEnabled) try { System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wm_debug.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " [IMG-DRAW] H0=" + box.H0 + " FsToS=" + box.FsToS + " H=" + box.H + " imgW=" + imgW + " imgH=" + imgH + " boxWpx=" + (imgW * box.W).ToString("F1") + " fs_calc=" + (box.FsToS > 0.0001f ? (box.FsToS * Math.Min(imgW, imgH)) : ((box.H0 > 0.01f ? (imgH * box.H0) : (imgH * box.H)) * (box.FontScale > 0.01f ? box.FontScale : 0.8f))).ToString("F1") + Environment.NewLine, System.Text.Encoding.UTF8); } catch { }
            string raw = box.Text ?? "";
            var lines = new List<string>();
            // V303: 快照方案——如果前端测量了换行结果，直接用，不再重新计算换行
            bool useWrapLines = (box.WrapLines != null && box.WrapLines.Count > 0);
            if (DiagEnabled) try { System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wm_debug.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " [IMG-DRAW-WL] useWrapLines=" + useWrapLines + " wrapLines.Count=" + (box.WrapLines == null ? "null" : box.WrapLines.Count.ToString()) + Environment.NewLine, System.Text.Encoding.UTF8); } catch { }
            if (useWrapLines)
            {
                foreach (string l in box.WrapLines) if (!string.IsNullOrEmpty(l)) lines.Add(l);
                if (DiagEnabled) try { System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wm_debug.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " [IMG-DRAW-WL-USED] lines=" + lines.Count + " firstLine=" + (lines.Count > 0 ? lines[0].Substring(0, Math.Min(20, lines[0].Length)) : "") + Environment.NewLine, System.Text.Encoding.UTF8); } catch { }
            }
            else
            {
                string[] rawLines = raw.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
                foreach (string l in rawLines) if (!string.IsNullOrEmpty(l)) lines.Add(l);
            }
            if (lines.Count == 0) return;

            float boxWpx = Math.Max(1f, imgW * box.W);
            float boxHpx = Math.Max(1f, imgH * box.H);
            // V344: 字号 = 前端字号/短边比例 × 本图短边（短边基准，横竖图短边相同时字号一致——方案B落地）
            //        无快照（fsToS<=0）时 fallback 回 H0/框高基准（兼容旧方案/旧数据）
            float fs = box.FsToS > 0.0001f
                ? box.FsToS * Math.Min(imgW, imgH)
                : (box.H0 > 0.01f ? (imgH * box.H0) : boxHpx) * (box.FontScale > 0.01f ? box.FontScale : 0.8f);
            float adv0 = fs * Math.Max(0.2f, 1f + box.LetterSpacing / 100f);
            if (fs < 0.5f) return;

            // V300: 自动换行——用 iTextSharp BaseFont.GetWidth（和 PDF 完全一样，保证和浏览器一致）
            string fontName = string.IsNullOrWhiteSpace(box.FontName) ? "微软雅黑" : box.FontName;
            iTextSharp.text.pdf.BaseFont fontForMeasure = GetWatermarkFont(fontName, box.Bold);
            FontStyle style = FontStyle.Regular;
            if (box.Bold) style |= FontStyle.Bold;
            if (box.Italic) style |= FontStyle.Italic;
            using (Font measureFont = new Font(fontName, fs, style, GraphicsUnit.Pixel))
            using (Bitmap bmp = new Bitmap(1, 1))
            using (Graphics mg = Graphics.FromImage(bmp))
            {
            mg.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            // V1.0.0.6: 快照直接用（不再按本图校验重排）。快照=前端 CSS 换行结果，输出必须与前端预览一致；
            //          若后端按本图框宽校验快照（V405 做法），GDI 与 CSS 度量差异会把"前端一行"误判为放不下而重排换行（用户实测横图一行数字在竖图输出被换行）。
            // V339: 有前端快照（wrapLines）时直接用；无快照时后端用 iText 测量按框宽重排（兜底，旧数据/无快照场景）
            bool useSnap = useWrapLines;
            if (!useSnap)
            {
                lines.Clear();
                string[] rawLines2 = raw.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
                foreach (string l in rawLines2) if (!string.IsNullOrEmpty(l)) lines.Add(l);
                var wrappedLines = new List<string>();
                foreach (string rawLine in lines)
                {
                    string cur = "";
                    float curW = 0;
                    foreach (char ch in rawLine)
                    {
                        // 和 PDF 完全一样：fontForMeasure.GetWidth() * fs / 1000f
                        float cw = fontForMeasure.GetWidth(ch.ToString()) * fs / 1000f;
                        if (box.LetterSpacing > 0) cw += box.LetterSpacing;
                        if (curW + cw > boxWpx && cur.Length > 0)
                        {
                            wrappedLines.Add(cur);
                            cur = ch.ToString();
                            curW = cw;
                        }
                        else
                        {
                            cur += ch;
                            curW += cw;
                        }
                    }
                    if (cur.Length > 0) wrappedLines.Add(cur);
                }
                lines = wrappedLines;
            }

            int alpha = Math.Max(0, Math.Min(100, box.Opacity)) * 255 / 100;
            Color color = Color.FromArgb(alpha, Color.FromArgb(box.ColorArgb & 0xFFFFFF));
            float lineH = fs * Math.Max(0.5f, 1f + box.LineSpacing / 100f);

            // V340: 先按当前字号用 GDI+ 实测每行宽度（自洽，供对齐与超界判断用）
            var lineWidths = new float[lines.Count];
            float maxLw = 0f;
            for (int li = 0; li < lines.Count; li++)
            {
                string line = lines[li];
                lineWidths[li] = (line == null || line.Length == 0) ? 0f
                    : mg.MeasureString(line, measureFont, 10000, StringFormat.GenericTypographic).Width;
                if (lineWidths[li] > maxLw) maxLw = lineWidths[li];
            }
            // V340: 文字块总高 = (行数-1)×行距 + 最后一行的字号高（最后一行不计行距，更贴近视觉）
            float totalH = (lines.Count - 1) * lineH + fs;
            // V1.0.0.10: 已移除收窄（用户决策：超边界不调字号，显示不全就显示不全）——字号保持短边比例（V344），
            //           文字块超图界时按"显示不全"处理，与前端预览一致（前端贴字同样不缩字号）
            //           旋转后外接矩形仅用于中心 clamp（文字块超图时中心尽量留在图内），不参与字号调整
            float rad = ((box.Rotation % 360f) % 180f) * (float)Math.PI / 180f;
            float cAbs = Math.Abs((float)Math.Cos(rad)), sAbs = Math.Abs((float)Math.Sin(rad));
            float rotW = maxLw * cAbs + totalH * sAbs;
            float rotH = maxLw * sAbs + totalH * cAbs;

            GraphicsState st = g.Save();
            try
            {
                using (Font font = new Font(fontName, fs, style, GraphicsUnit.Pixel))
                using (Brush tb = new SolidBrush(color))
                using (Pen linePen = new Pen(color, Math.Max(1f, fs * 0.05f)))
                {
                    float cx = (box.X + box.W / 2f) * imgW;   // 框中心 x
                    float cy = (box.Y + box.H / 2f) * imgH;   // 框中心 y（Y 向下）
                    if (rotW >= imgW) cx = imgW / 2f; else cx = ClampF(cx, rotW / 2f, imgW - rotW / 2f);
                    if (rotH >= imgH) cy = imgH / 2f; else cy = ClampF(cy, rotH / 2f, imgH - rotH / 2f);
                    // V1.0.0.10: 贴字框基准——框宽 = 最宽行 + 左右各10px、框高 = 文字总高 + 上下各10px（与前端 wmOuterStyle 贴字一致）；
                    //           行按贴字框内对齐，文字块垂直居中于框中心（中心 = 保存的框中心，跨图位置不变）
                    float fitWpx = maxLw + 20f;
                    float halfFit = fitWpx / 2f;
                    float firstTop = -totalH / 2f;
                    g.TranslateTransform(cx, cy);
                    g.RotateTransform(box.Rotation);       // Y 向下：屏幕系角度直接用（不取反）
                    for (int li = 0; li < lines.Count; li++)
                    {
                        string line = lines[li];
                        int n = line.Length;
                        if (n == 0) continue;
                        // V339: 行宽用 GDI+ 实测（自洽居中），不再用前端 wrapLineWidths 比例
                        float lw = lineWidths[li];
                        float lx = -halfFit;
                        if (lw <= fitWpx)
                        {
                            if (box.Align == 1) lx = -halfFit + (fitWpx - lw) / 2f;
                            else if (box.Align == 2) lx = -halfFit + (fitWpx - lw);
                        }
                        float ly = firstTop + li * lineH;
                        if (DiagEnabled) try { System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wm_debug.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " [IMG-DRAW-LINE] li=" + li + " lw=" + lw.ToString("F1") + " lx=" + lx.ToString("F1") + " ly=" + ly.ToString("F1") + " boxWpx=" + boxWpx.ToString("F1") + " fs=" + fs.ToString("F1") + " fsToS=" + box.FsToS.ToString("F6") + Environment.NewLine, System.Text.Encoding.UTF8); } catch { }
                        // V322: 整行画文字（不用逐字符画，避免iTextSharp和GDI+字体度量不一致）
                        g.DrawString(line, font, tb, lx, ly, StringFormat.GenericTypographic);
                        if (box.Underline)
                            g.DrawLine(linePen, lx, ly + fs * 0.96f, lx + lw, ly + fs * 0.96f);
                        if (box.Strike)
                            g.DrawLine(linePen, lx, ly + fs * 0.50f, lx + lw, ly + fs * 0.50f);
                    }
                }
            }
            finally { g.Restore(st); }
            }
        }

        private static float ClampF(float v, float lo, float hi)
        {
            if (hi < lo) return v;
            return v < lo ? lo : (v > hi ? hi : v);
        }

        /// <summary>按原格式保存（JPEG 最高质量 100），输出到 outputFolder 下命名文件。</summary>
        private static void SaveWithFormat(Bitmap bmp, string sourceFile, string outFile)
        {
            string ext = Path.GetExtension(sourceFile).ToLowerInvariant();
            ImageFormat fmt;
            switch (ext)
            {
                case ".jpg":
                case ".jpeg": fmt = ImageFormat.Jpeg; break;
                case ".png": fmt = ImageFormat.Png; break;
                case ".bmp": fmt = ImageFormat.Bmp; break;
                case ".gif": fmt = ImageFormat.Gif; break;
                case ".tif":
                case ".tiff": fmt = ImageFormat.Tiff; break;
                default: fmt = ImageFormat.Png; break;
            }
            if (fmt == ImageFormat.Jpeg)
            {
                ImageCodecInfo codec = FindCodec("image/jpeg");
                if (codec != null)
                {
                    using (var eps = new EncoderParameters(1))
                    {
                        eps.Param[0] = new EncoderParameter(Encoder.Quality, 100L);
                        bmp.Save(outFile, codec, eps);
                        return;
                    }
                }
            }
            bmp.Save(outFile, fmt);
        }

        private static ImageCodecInfo FindCodec(string mime)
        {
            foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders())
                if (c.MimeType == mime) return c;
            return null;
        }
    }
}
