using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PDFQFZ.Library;

namespace PDFQFZ.WPF.Services
{
    /// <summary>仿扫描滤镜参数（每项 0-100 强度）。三功能集成·阶段1：PDF 文本转仿扫描效果。</summary>
    public class FakeScanParams
    {
        /// <summary>噪点强度：颗粒感。</summary>
        public int Noise { get; set; }
        /// <summary>轻微模糊：纸张扫描的柔和感。</summary>
        public int Blur { get; set; }
        /// <summary>轻微扭曲：纸张按压/进纸的轻微形变。</summary>
        public int Warp { get; set; }
        /// <summary>暗角：扫描件四角自然变暗。</summary>
        public int Vignette { get; set; }
        /// <summary>纸张底色：向米白做旧。</summary>
        public int BgTone { get; set; }
        /// <summary>整体倾斜：-100~100，0=不倾斜；负=逆时针、正=顺时针，映射 ±3°（模拟扫描件整体歪斜）。</summary>
        public int Tilt { get; set; }
    }

    /// <summary>
    /// PDF 仿扫描生成（C# 侧独立实现）。
    /// 算法按通用"扫描效果"思路独立编写（噪点/纸张底色/轻微扭曲/轻微模糊/暗角），不复制 ToolKnit 代码，合规可用。
    /// 链路：Pdfium 渲染位图 → System.Drawing 滤镜 → 复用 StampEngine.ImageToPDF 合成输出。
    /// </summary>
    public static class PdfFakeScan
    {
        /// <summary>整本 PDF 转为仿扫描 PDF。返回 {"ok":true} 或 {"ok":false,"error":"..."}。</summary>
        public static string ConvertPdfToFakeScan(string pdfPath, FakeScanParams p, string outPath, int dpi = 200)
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

        /// <summary>对单页位图应用仿扫描滤镜，返回新的位图。</summary>
        public static Bitmap Apply(Bitmap src, FakeScanParams p)
        {
            int w = src.Width, h = src.Height;
            int noise = Clamp(p.Noise);
            int blur = Clamp(p.Blur);
            int warp = Clamp(p.Warp);
            int vig = Clamp(p.Vignette);
            int bg = Clamp(p.BgTone);
            int tilt = p.Tilt < -100 ? -100 : p.Tilt > 100 ? 100 : p.Tilt;

            byte[] cur = ReadPixelsTight(src);
            int stride = w * 4;

            // 0) 整体倾斜：先旋转整页（露出区域补白色纸基），后续滤镜（模糊/扭曲/底色/噪点/暗角）统一作用于旋转后的整页，
            //    使露出区域与页面背景效果一致（底色+噪点+暗角+模糊统一叠加，避免仅补单一底色而产生差异）
            if (tilt != 0)
            {
                using (Bitmap baseBmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                {
                    WritePixelsTight(baseBmp, cur);
                    using (Bitmap rot = RotateImage(baseBmp, (float)(tilt / 100.0 * 3.0), Color.White))
                    {
                        cur = ReadPixelsTight(rot);
                    }
                }
            }
            // 1) 模糊（线性混合到固定模糊图，彻底平滑：blur 0→100 从原图连续过渡到 maxR，消除取整跳跃）
            if (blur > 0)
            {
                double t = blur / 100.0;
                if (t >= 0.02)
                {
                    int maxR = 5;
                    var blurred = BoxBlur(cur, w, h, stride, maxR);
                    cur = BlendLerp(cur, blurred, w, h, stride, t);
                }
            }
            // 2) 轻微扭曲（行正弦位移）：滑杆 0-100，100 时振幅 6px（= 原 100 档 3px 的两倍），50 时 = 原 100 档 3px
            if (warp > 0)
            {
                int amp = Math.Max(1, (int)Math.Round(warp / 100.0 * 6.0));
                cur = WarpRows(cur, w, h, stride, amp);
            }
            // 3) 底色 + 噪点 + 暗角（一遍逐像素）
            byte[] outB = new byte[cur.Length];
            ApplyToneNoiseVignette(cur, outB, w, h, stride, bg, noise, vig);

            Bitmap dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            WritePixelsTight(dst, outB);
            return dst;
        }

        static int Clamp(int v) { return v < 0 ? 0 : v > 100 ? 100 : v; }

        // ---------- 像素读写（统一为紧密 w*4 BGRA，忽略 stride 对齐） ----------

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

        // ---------- BoxBlur（水平+垂直两遍滑动窗口） ----------

        static byte[] BoxBlur(byte[] src, int w, int h, int stride, int radius)
        {
            byte[] tmp = new byte[src.Length];
            byte[] dst = new byte[src.Length];
            // 水平
            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                for (int c = 0; c < 4; c++)
                {
                    long sum = 0; int cnt = 0;
                    for (int x = -radius; x <= radius; x++)
                    {
                        int sx = x < 0 ? 0 : x >= w ? w - 1 : x;
                        sum += src[row + sx * 4 + c]; cnt++;
                    }
                    for (int x = 0; x < w; x++)
                    {
                        tmp[row + x * 4 + c] = (byte)(sum / cnt);
                        int xout = x + radius + 1;
                        int xin = x - radius;
                        if (xout < w) { sum += src[row + xout * 4 + c]; cnt++; }
                        if (xin >= 0) { sum -= src[row + xin * 4 + c]; cnt--; }
                    }
                }
            }
            // 垂直
            for (int x = 0; x < w; x++)
            {
                int col = x * 4;
                for (int c = 0; c < 4; c++)
                {
                    long sum = 0; int cnt = 0;
                    for (int y = -radius; y <= radius; y++)
                    {
                        int sy = y < 0 ? 0 : y >= h ? h - 1 : y;
                        sum += tmp[sy * stride + col + c]; cnt++;
                    }
                    for (int y = 0; y < h; y++)
                    {
                        dst[y * stride + col + c] = (byte)(sum / cnt);
                        int yout = y + radius + 1;
                        int yin = y - radius;
                        if (yout < h) { sum += tmp[yout * stride + col + c]; cnt++; }
                        if (yin >= 0) { sum -= tmp[yin * stride + col + c]; cnt--; }
                    }
                }
            }
            return dst;
        }

        // ---------- 线性混合（模糊平滑：原图与固定模糊图按 t 混合，消除取整跳跃） ----------

        static byte[] BlendLerp(byte[] a, byte[] b, int w, int h, int stride, double t)
        {
            byte[] dst = new byte[a.Length];
            for (int i = 0; i < a.Length; i++)
            {
                int v = (int)(a[i] * (1.0 - t) + b[i] * t);
                dst[i] = v < 0 ? (byte)0 : v > 255 ? (byte)255 : (byte)v;
            }
            return dst;
        }

        // ---------- 行正弦扭曲 ----------

        static byte[] WarpRows(byte[] src, int w, int h, int stride, int amp)
        {
            byte[] dst = new byte[src.Length];
            // 对称低频波浪：相位固定使页面中部位移为 0、上下对称（模拟进纸不平的整体缓慢起伏，不偏向一侧，更接近真实扫描件）
            double freq = Math.PI * 2 / h;
            double phase = -freq * (h / 2.0);
            for (int y = 0; y < h; y++)
            {
                int shift = (int)Math.Round(amp * Math.Sin(y * freq + phase));
                int row = y * stride;
                for (int x = 0; x < w; x++)
                {
                    int sx = x - shift;
                    if (sx < 0) sx = 0; else if (sx >= w) sx = w - 1;
                    int si = row + sx * 4, di = row + x * 4;
                    dst[di] = src[si]; dst[di + 1] = src[si + 1]; dst[di + 2] = src[si + 2]; dst[di + 3] = src[si + 3];
                }
            }
            return dst;
        }

        // ---------- 整体倾斜（整页旋转，露出区域补纸色基底，后续滤镜统一叠加） ----------

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

        // ---------- 底色 + 噪点 + 暗角（合并一遍） ----------

        static void ApplyToneNoiseVignette(byte[] src, byte[] dst, int w, int h, int stride, int bg, int noise, int vig)
        {
            // 纸张底色目标（米黄做旧）；底色强度上限 0.90（底色100时背景明显泛黄，与暗角比例合理），暗角上限 0.35（更柔和，避免小数值过于明显）
            const int bgR = 245, bgG = 237, bgB = 218;
            double k = bg / 100.0 * 0.90;
            double nK = noise / 100.0 * 30.0;
            double vK = vig / 100.0 * 0.35;
            double cx = (w - 1) / 2.0, cy = (h - 1) / 2.0;
            double maxD = Math.Sqrt(cx * cx + cy * cy);
            if (maxD <= 0) maxD = 1;
            uint seed = 0x9E3779B9;
            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                double dy = y - cy;
                for (int x = 0; x < w; x++)
                {
                    int i = row + x * 4;
                    int B = src[i], G = src[i + 1], R = src[i + 2], A = src[i + 3];
                    if (k > 0)
                    {
                        // 纸张底色只作用于浅色背景像素（文字等深色像素保持原样，避免文字随底色变糊/与模糊冲突）
                        int lum = (B + G + R) / 3;
                        if (lum > 150)
                        {
                            B = (int)(B + (bgB - B) * k);
                            G = (int)(G + (bgG - G) * k);
                            R = (int)(R + (bgR - R) * k);
                        }
                    }
                    if (nK > 0)
                    {
                        seed = seed * 1103515245u + 12345u;
                        int n = (int)((seed >> 8) % 256);
                        int off = (int)Math.Round((n - 128) / 128.0 * nK);
                        B += off; G += off; R += off;
                    }
                    if (vK > 0)
                    {
                        double dx = x - cx;
                        double d = Math.Sqrt(dx * dx + dy * dy) / maxD;
                        double f = 1.0 - vK * d * d;
                        B = (int)(B * f); G = (int)(G * f); R = (int)(R * f);
                    }
                    dst[i] = (byte)(B < 0 ? 0 : B > 255 ? 255 : B);
                    dst[i + 1] = (byte)(G < 0 ? 0 : G > 255 ? 255 : G);
                    dst[i + 2] = (byte)(R < 0 ? 0 : R > 255 ? 255 : R);
                    dst[i + 3] = (byte)A;
                }
            }
        }
    }
}
