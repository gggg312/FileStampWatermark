using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PDFQFZ.Library;

namespace PDFQFZ.WPF.Services
{
    /// <summary>扫描件优化参数（每项 0-100 强度）。三功能集成·阶段2：模糊/歪斜/灰底扫描 PDF 的修复增强。</summary>
    public class ScanEnhanceParams
    {
        /// <summary>纠偏：手动旋转摆正（-100~+100，负=向左转，正=向右转，±3°；0=不转）。</summary>
        public int Deskew { get; set; }
        /// <summary>裁边：手动每边按比例裁剪（0-100，0=不裁，100=每边裁 8%）。</summary>
        public int Crop { get; set; }
        /// <summary>去底灰：去除背景灰底/黄底/杂色，还原白纸；文字笔画不变浅（前景保护）。0=关闭。</summary>
        public int DeTone { get; set; }
        /// <summary>去灰阈值（0-255 亮度）：只把亮度低于此值的背景灰调白（高于的浅底不动）。默认 200。</summary>
        public int DeToneTh { get; set; }
        /// <summary>对比度增强：增强明暗对比（0=关闭）。</summary>
        public int Contrast { get; set; }
        /// <summary>文字加强：背景减法前景检测（比背景暗→前景），前景压到目标深度。false=关闭。</summary>
        public bool EnhEn { get; set; }
        /// <summary>背景平滑（0-100，映射背景面平滑半径 5-100px；滑条 0→半径5、100→半径100）。越大背景估计越平滑、文字与背景差异越明显。</summary>
        public int EnhSmooth { get; set; }
        /// <summary>敏感度（0-100，前景判定松紧，越大越容易把淡色笔画判为前景→越黑）。</summary>
        public int EnhSens { get; set; }
        /// <summary>加深程度（0-100，前景压到目标深度 255×(1−加深×0.95)，越大越黑，100 接近纯黑）。</summary>
        public int EnhDeep { get; set; }
    }

    /// <summary>
    /// PDF 扫描件优化增强（C# 侧独立实现）。
    /// 算法按通用"文档扫描修复"思路独立编写，参考 ScanTailor/Leptonica 文档扫描管线的成熟思路（形态学闭运算估计背景面、前景/背景分离、连通域处理），不复制任何项目代码，合规可用。
    /// 核心：先用形态学闭运算估计平滑背景面（抹平暗笔画），原图−背景面≥阈值 得前景（文字）掩码；
    /// 去底灰只作用背景像素（前景保护，文字不变浅）；文字加深只作用前景连通域且整块统一到最深色。
    /// 链路：Pdfium 渲染位图 → System.Drawing 滤镜 → 复用 StampEngine.ImageToPDF 合成输出。
    /// </summary>
    public static class PdfScanEnhance
    {
        /// <summary>整本 PDF 做扫描件优化，输出新 PDF。返回 {"ok":true} 或 {"ok":false,"error":"..."}。</summary>
        public static string ConvertPdfToScanEnhance(string pdfPath, ScanEnhanceParams p, string outPath, int dpi = 200)
        {
            try
            {
                if (dpi < 72) dpi = 72;
                if (dpi > 300) dpi = 300;
                float bl = 72f / dpi;
                using (IPdfDocumentRenderer r = PdfiumDocumentRenderer.Open(pdfPath))
                {
                    int n = r.PageCount;
                    if (n < 1) return "{\"ok\":false,\"error\":\"PDF 无页面\"}";
                    Bitmap[] pages = new Bitmap[n];
                    try
                    {
                        for (int i = 0; i < n; i++)
                        {
                            using (Bitmap b = r.RenderPage(i, dpi))
                                pages[i] = Apply(b, p);
                        }
                        StampEngine.ImageToPDF(pages, bl, outPath);
                    }
                    finally
                    {
                        for (int i = 0; i < pages.Length; i++) if (pages[i] != null) pages[i].Dispose();
                    }
                }
                return "{\"ok\":true}";
            }
            catch (Exception ex)
            {
                return "{\"ok\":false,\"error\":\"" + JsonEscape(ex.Message) + "\"}";
            }
        }

        static string JsonEscape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        static int Clamp(int v) { return v < 0 ? 0 : v > 100 ? 100 : v; }
        static int ClampTh(int v) { return v < 0 ? 0 : v > 255 ? 255 : v; } // 阈值 0-255 亮度

        /// <summary>对单页位图应用扫描件优化，返回新位图（纠偏/裁边改变尺寸，其余为像素滤镜）。</summary>
        public static Bitmap Apply(Bitmap src, ScanEnhanceParams p)
        {
            int deskew = p.Deskew; // -100~+100：负=向左转，正=向右转（用户手动摆正）
            int crop = Clamp(p.Crop);
            int detone = Clamp(p.DeTone);
            int contrast = Clamp(p.Contrast);
            int detoneTh = ClampTh(p.DeToneTh); // V1.2.1.33：去灰阈值（低于此亮度的背景灰调白）
            // 文字加强（V1.2.1.38：合并 B 二值化 + C 背景减法 为一种；参数=背景平滑/敏感度/加深程度）
            bool enh = p.EnhEn;
            int enhSmooth = Clamp(p.EnhSmooth), enhSens = Clamp(p.EnhSens), enhDeep = Clamp(p.EnhDeep);

            Bitmap cur = (Bitmap)src.Clone();

            // 1) 纠偏：用户手动双向旋转摆正（±3°，露出区域补白）
            if (deskew != 0)
                cur = Deskew(cur, deskew);

            // 2) 裁边：用户手动按比例裁剪四周
            if (crop > 0)
                cur = CropEdges(cur, crop);

            // 3) 像素级滤镜（去底灰 → 对比度 → 文字加强）
            int w = cur.Width, h = cur.Height, stride = w * 4;
            byte[] px = ReadPixelsTight(cur);
            if (detone > 0 || contrast > 0 || enh)
                px = PixelOps(px, w, h, stride, detone / 100.0, contrast / 100.0, detoneTh, enh, enhSmooth, enhSens, enhDeep);
            WritePixelsTight(cur, px);
            return cur;
        }

        // ---------- 像素读写（统一紧密 w*4 BGRA） ----------

        static byte[] ReadPixelsTight(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height, stride = w * 4;
            byte[] tight = new byte[stride * h];
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] raw = new byte[bd.Stride * h];
                Marshal.Copy(bd.Scan0, raw, 0, raw.Length);
                for (int y = 0; y < h; y++)
                    Buffer.BlockCopy(raw, y * bd.Stride, tight, y * stride, stride);
            }
            finally { bmp.UnlockBits(bd); }
            return tight;
        }

        static void WritePixelsTight(Bitmap bmp, byte[] tight)
        {
            int w = bmp.Width, h = bmp.Height, stride = w * 4;
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] raw = new byte[bd.Stride * h];
                for (int y = 0; y < h; y++)
                    Buffer.BlockCopy(tight, y * stride, raw, y * bd.Stride, stride);
                Marshal.Copy(raw, 0, bd.Scan0, raw.Length);
            }
            finally { bmp.UnlockBits(bd); }
        }

        // ---------- 亮度辅助 ----------

        static byte[] BrightnessOf(byte[] px, int w, int h)
        {
            int n = w * h;
            byte[] lum = new byte[n];
            for (int i = 0, k = 0; k < n; k++, i += 4)
                lum[k] = (byte)((px[i] + px[i + 1] + px[i + 2]) / 3);
            return lum;
        }

        // ---------- Sauvola 局部自适应阈值：生成前景（文字）掩码 ----------
        // 每个像素按窗口内 均值m + 标准差s 动态算阈值 T = m·(1 + k·(s/R − 1))。
        // 均匀深灰底（s 小）→ T 略低于均值 → 判背景；文字笔画处（s 大）→ T 升高 → 判前景。
        // 窗口统计用积分图（Integral Image）O(n) 计算，任意窗口均值/方差一次查表。
        static byte[] SauvolaMask(byte[] lum, int w, int h, int r, double k, double R)
        {
            int Wp = w + 1, Hp = h + 1;
            long[] I = new long[Wp * Hp];   // 亮度积分图
            long[] I2 = new long[Wp * Hp];  // 亮度平方积分图
            for (int y = 0; y < h; y++)
            {
                long rowSum = 0, rowSum2 = 0;
                for (int x = 0; x < w; x++)
                {
                    int v = lum[y * w + x];
                    rowSum += v; rowSum2 += (long)v * v;
                    int idx = (y + 1) * Wp + (x + 1);
                    I[idx] = I[y * Wp + (x + 1)] + rowSum;
                    I2[idx] = I2[y * Wp + (x + 1)] + rowSum2;
                }
            }
            byte[] mask = new byte[lum.Length];
            for (int y = 0; y < h; y++)
            {
                int y0 = y - r, y1 = y + r;
                if (y0 < 0) y0 = 0; if (y1 >= h) y1 = h - 1;
                int cntH = y1 - y0 + 1;
                for (int x = 0; x < w; x++)
                {
                    int x0 = x - r, x1 = x + r;
                    if (x0 < 0) x0 = 0; if (x1 >= w) x1 = w - 1;
                    int cnt = cntH * (x1 - x0 + 1);
                    long sum = Rect(I, Wp, x0, y0, x1, y1);
                    long sum2 = Rect(I2, Wp, x0, y0, x1, y1);
                    double m = sum / (double)cnt;
                    double var = sum2 / (double)cnt - m * m;
                    if (var < 0) var = 0;
                    double s = Math.Sqrt(var);
                    double T = m * (1.0 + k * (s / R - 1.0));
                    if (lum[y * w + x] < T) mask[y * w + x] = 1;
                }
            }
            return mask;
        }

        // 积分图矩形和（含 x0,y0 .. x1,y1，闭区间）
        static long Rect(long[] I, int Wp, int x0, int y0, int x1, int y1)
        {
            return I[(y1 + 1) * Wp + (x1 + 1)] - I[y0 * Wp + (x1 + 1)] - I[(y1 + 1) * Wp + x0] + I[y0 * Wp + x0];
        }

        // ---------- 1) 纠偏：用户手动双向旋转摆正（-100~+100，±3°，对齐仿扫描的整体倾斜交互） ----------

        static Bitmap Deskew(Bitmap src, int signedAmount)
        {
            if (signedAmount == 0) return src;
            double deg = signedAmount / 100.0 * 3.0; // 正=顺时针（向右转），负=逆时针（向左转）
            return RotateImage(src, (float)deg, Color.White);
        }

        static Bitmap RotateImage(Bitmap src, float angle, Color back)
        {
            int w = src.Width, h = src.Height;
            Bitmap dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.Clear(back);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.TranslateTransform(w / 2f, h / 2f);
                g.RotateTransform(angle);
                g.TranslateTransform(-w / 2f, -h / 2f);
                g.DrawImage(src, 0, 0, w, h);
            }
            return dst;
        }

        // ---------- 2) 裁边：用户手动按比例裁剪四周（0-100，100=每边裁 8% 宽/高），连续可微调 ----------

        static Bitmap CropEdges(Bitmap src, int amount)
        {
            double per = amount / 100.0 * 0.08;
            if (per <= 0) return src;
            int w = src.Width, h = src.Height;
            int L = (int)(w * per), T = (int)(h * per);
            int R = w - 1 - (int)(w * per), B = h - 1 - (int)(h * per);
            if (R - L < 16 || B - T < 16) return src;
            return src.Clone(new Rectangle(L, T, R - L, B - T), src.PixelFormat);
        }

        // ---------- 3) 对比度增强：以均值为中心线性拉伸，最大 factor=3（全局） ----------

        static byte[] ContrastAdjust(byte[] src, int w, int h, int stride, double amount)
        {
            if (amount <= 0) return src;
            long sum = 0; int n = src.Length / 4;
            for (int i = 0; i < src.Length; i += 4) sum += (src[i] + src[i + 1] + src[i + 2]) / 3;
            double mean = n > 0 ? sum / (double)n : 128.0;
            double factor = 1.0 + amount * 2.0;
            byte[] dst = new byte[src.Length];
            for (int i = 0; i < src.Length; i += 4)
            {
                int a = src[i + 3];
                for (int c = 0; c < 3; c++)
                {
                    double v = (src[i + c] - mean) * factor + mean;
                    dst[i + c] = (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
                }
                dst[i + 3] = (byte)a;
            }
            return dst;
        }

        // ---------- 4) 核心：前景/背景分离 + 去底灰（前景保护） + 对比度 + 文字加强 ----------

        static byte[] PixelOps(byte[] src, int w, int h, int stride, double detone, double contrast, int detoneTh,
            bool enh, int enhSmooth, int enhSens, int enhDeep)
        {
            int n = w * h;
            byte[] cur = (byte[])src.Clone();

            // 1) 亮度图
            byte[] lum = BrightnessOf(cur, w, h);

            // 2) 前景（文字）掩码：Sauvola 局部自适应阈值
            //    （深灰底/低对比也能准确区分文字与背景：均匀深灰底局部标准差小→判背景，文字笔画处局部标准差大→判前景）
            int sr = Math.Max(8, Math.Min(32, (int)(w / 50.0)));
            byte[] mask = SauvolaMask(lum, w, h, sr, 0.2, 128.0);
            // 不做闭运算填洞（避免小字空心被填黑）；改由下方"孔洞保留"区分空心与笔画内部浅点

            // 3) 前景连通域标记（8 连通，并查集）+ 面积过滤小噪点
            int[] parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (mask[i] == 0) continue;
                    if (x > 0 && mask[i - 1] == 1) Union(parent, i, i - 1);
                    if (y > 0 && mask[i - w] == 1) Union(parent, i, i - w);
                    if (x > 0 && y > 0 && mask[i - w - 1] == 1) Union(parent, i, i - w - 1);
                    if (x < w - 1 && y > 0 && mask[i - w + 1] == 1) Union(parent, i, i - w + 1);
                }
            int[] fgSize = new int[n];
            for (int i = 0; i < n; i++) if (mask[i] == 1) fgSize[Find(parent, i)]++;
            int minArea = Math.Max(4, n / 300000);
            // 有效前景（面积达标）+ 记录每个有效像素所属前景连通域 root 与最暗亮度
            bool[] valid = new bool[n];
            int[] fgRoot = new int[n];
            int[] minLumA = new int[n];
            for (int i = 0; i < n; i++) minLumA[i] = 255;
            for (int k = 0; k < n; k++)
                if (mask[k] == 1 && fgSize[Find(parent, k)] >= minArea)
                {
                    valid[k] = true;
                    int root = Find(parent, k);
                    fgRoot[k] = root;
                    if (lum[k] < minLumA[root]) minLumA[root] = lum[k];
                }

            // 4) 背景孔洞连通域（复用 parent 数组，重新初始化）
            for (int i = 0; i < n; i++) parent[i] = i;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (mask[i] == 1) continue;
                    if (x > 0 && mask[i - 1] == 0) Union(parent, i, i - 1);
                    if (y > 0 && mask[i - w] == 0) Union(parent, i, i - w);
                    if (x > 0 && y > 0 && mask[i - w - 1] == 0) Union(parent, i, i - w - 1);
                    if (x < w - 1 && y > 0 && mask[i - w + 1] == 0) Union(parent, i, i - w + 1);
                }
            int[] bgSize = new int[n];
            bool[] bgTouch = new bool[n];
            for (int k = 0; k < n; k++)
                if (mask[k] == 0) bgSize[Find(parent, k)]++;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (mask[y * w + x] == 1) continue;
                    if (x == 0 || y == 0 || x == w - 1 || y == h - 1)
                        bgTouch[Find(parent, y * w + x)] = true;
                }
            // 字符空心 vs 笔画内部浅点：面积大的内部背景孔洞视为空心保留，面积小的并入笔画加深
            // V1.2.1.31：holeTh 从 sr*sr/4(256) 调小为 sr*sr/16(64)——原阈值把 0/目/O/B 等字符真实内腔(<256px²)误当笔画浅点加深变黑；调小后真实空心保留、只加深极小笔环浅点
            int holeTh = Math.Max(24, sr * sr / 16);
            int[] minLumB = new int[n];
            for (int i = 0; i < n; i++) minLumB[i] = 255;
            for (int k = 0; k < n; k++)
                if (mask[k] == 0)
                {
                    int r = Find(parent, k);
                    if (!bgTouch[r] && bgSize[r] < holeTh)
                    {
                        int l = lum[k];
                        if (l < minLumB[r]) minLumB[r] = l;
                    }
                }

            // 5) 输出（顺序：先去底灰 → 全局对比度 → 文字加强）
            //    A 先 去底灰：全部背景（含字符内部大孔洞空心）拉白，前景保护不变浅；空心只去灰不加深不填黑
            //    B 再 全局对比度：基于去底灰后的实际页面，深色更深、浅色（已白背景）更浅
            //    C 后 文字加强（V1.2.1.36）：A 局部对比度(CLAHE) / B 二值化(Sauvola) / C 背景减法，多选，按 A→B→C 顺序叠加
            double kDetone = Math.Min(detone * 1.96, 1.0); // 去灰最大强度翻倍（上限 1.0=完全拉白）
            double cFactor = 1.0 + contrast * 2.0;
            bool doDetone = kDetone > 0;
            bool doContrast = contrast > 0;
            byte[] mid = (byte[])cur.Clone();

            // A) 去底灰：全部背景拉白（仅保护"有效前景"真文字连通域不变浅；mask=1 但面积<minArea 的小噪点也去灰拉白，消除残留灰色不规则斑点）
            //    V1.2.1.30：原为 mask[k]==1 continue（连小噪点都保护→残留斑点），改为 valid[k] continue（只保护真文字）
            //    V1.2.1.33：加去灰阈值 detoneTh——只拉白"亮度≤阈值"的背景灰，高于阈值(浅底)不动（默认200）
            if (doDetone)
            {
                for (int i = 0, k = 0; k < n; k++, i += 4)
                {
                    if (valid[k]) continue; // 有效前景（真文字）保持，不变浅
                    int B2 = cur[i], G = cur[i + 1], R = cur[i + 2];
                    if (detoneTh < 255 && (B2 + G + R) / 3 > detoneTh) continue; // 亮度高于去灰阈值：浅底，不调
                    mid[i] = (byte)(B2 + (255 - B2) * kDetone);
                    mid[i + 1] = (byte)(G + (255 - G) * kDetone);
                    mid[i + 2] = (byte)(R + (255 - R) * kDetone);
                }
            }

            // B) 全局对比度：深更深、浅更浅（基于去底灰后的实际页面）
            if (doContrast)
            {
                long s2 = 0;
                for (int i = 0; i < mid.Length; i += 4) s2 += (mid[i] + mid[i + 1] + mid[i + 2]) / 3;
                double m2 = n > 0 ? s2 / (double)n : 128.0;
                for (int i = 0; i < mid.Length; i += 4)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        double v = (mid[i + c] - m2) * cFactor + m2;
                        mid[i + c] = (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
                    }
                }
            }

            // C) 文字加强（V1.2.1.40：加深程度固定 100=纯黑硬输出，中间灰对增强无意义；参数=背景平滑/敏感度）
            //    背景平滑(enhSmooth→半径5-100) / 敏感度(enhSens 前景判定松紧, 越大越黑)
            //    敏感度越大→binTh 越小→越容易把淡色笔画判为前景→越黑
            //    加深程度固定100→dt≈12(近纯黑, 即二值化硬输出)
            byte[] lum2 = BrightnessOf(mid, w, h);
            if (enh)
            {
                int radius = 5 + enhSmooth * 95 / 100;      // V1.2.1.40：背景面平滑半径映射 5-100（绕开 1-5 的失真区；0→5、100→100）
                int binTh = 31 - enhSens * 30 / 100;        // 前景判定阈值 31→1：敏感度越大→binTh 越小→越黑
                int dt = (int)(255 * (1 - enhDeep / 100.0 * 0.95)); if (dt < 0) dt = 0; // 目标深度（加深程度越大越黑）
                byte[] bg = BoxBlurLum(lum2, w, h, radius);
                for (int i = 0, k = 0; k < n; k++, i += 4)
                {
                    mid[i + 3] = cur[i + 3];
                    int diff = bg[k] - lum2[k];
                    if (diff > binTh)
                    {
                        int t = lum2[k] < dt ? lum2[k] : dt;
                        int delta = t - lum2[k];
                        mid[i] = ClampB(mid[i] + delta); mid[i + 1] = ClampB(mid[i + 1] + delta); mid[i + 2] = ClampB(mid[i + 2] + delta);
                    }
                }
            }
            return mid;
        }

        static byte ClampB(int v) { return v < 0 ? (byte)0 : v > 255 ? (byte)255 : (byte)v; }

        // ---------- 文字加强 C：背景减法（平滑背景面估计） ----------
        // 大半径方框均值滤波作为背景面（抹平文字笔画），原图亮度−背景亮度=前景差，只加深差值为正的像素。
        static byte[] BoxBlurLum(byte[] lum, int w, int h, int radius)
        {
            int Wp = w + 1;
            long[] I = new long[Wp * (h + 1)];
            for (int y = 0; y < h; y++)
            {
                long rowSum = 0;
                for (int x = 0; x < w; x++)
                {
                    rowSum += lum[y * w + x];
                    I[(y + 1) * Wp + (x + 1)] = I[y * Wp + (x + 1)] + rowSum;
                }
            }
            byte[] outLum = new byte[w * h];
            for (int y = 0; y < h; y++)
            {
                int y0 = y - radius, y1 = y + radius; if (y0 < 0) y0 = 0; if (y1 >= h) y1 = h - 1;
                int cntH = y1 - y0 + 1;
                for (int x = 0; x < w; x++)
                {
                    int x0 = x - radius, x1 = x + radius; if (x0 < 0) x0 = 0; if (x1 >= w) x1 = w - 1;
                    int cnt = cntH * (x1 - x0 + 1);
                    long sum = I[(y1 + 1) * Wp + (x1 + 1)] - I[y0 * Wp + (x1 + 1)] - I[(y1 + 1) * Wp + x0] + I[y0 * Wp + x0];
                    outLum[y * w + x] = (byte)(sum / cnt);
                }
            }
            return outLum;
        }

        static int Find(int[] p, int x)
        {
            while (p[x] != x) { p[x] = p[p[x]]; x = p[x]; }
            return x;
        }
        static void Union(int[] p, int a, int b)
        {
            int ra = Find(p, a), rb = Find(p, b);
            if (ra != rb) p[rb] = ra;
        }
    }
}
