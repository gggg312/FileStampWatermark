using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using iTextSharp.text.exceptions;
using iTextSharp.text.pdf;
using PDFQFZ.Library;

namespace PDFQFZ.WPF.Services
{
    /// <summary>盖章参数（从原 Form1 的实例字段参数化而来）。</summary>
    internal sealed class StampOptions
    {
        public string StampImagePath;        // 印章原图路径
        public string OutputPath;            // 输出目录
        public int QfzType;                  // 骑缝章类型 0加盖/1不加/2单页/3双页/4随意
        public bool RemoveWhite;             // 去除白色背景
        public int Opacity;                  // 印章不透明度
        public int Rotation;                 // 印章旋转角度
        public bool OriginalRotationCrop;    // 旋转切边（qbflag==0）
        public int SizeMm;                   // 印章尺寸(mm)
        public int WzType;                   // 骑缝章位置 0下/1上/2左/3右
        public int WzPercent;                // 骑缝章位置百分比
        public int MaxSplit;                 // 骑缝章最大分割数
        public StampPlacementCollection Placements;   // 页面章放置集合
        // V2.4.0.96：文字水印框列表（预览放置的水印框快照；null/空 = 不加文字水印，与盖章互不干扰）
        public List<WatermarkBox> Watermarks;
        // V2.4.0.58：盖章渲染（纹理/色偏）参数——骑缝章基准图同步应用，保证与页面章渲染一致
        public bool TextureEnabled;
        public int TextureSeed;
        public int TextureBrightness, TextureBlob, TextureGradient, TextureWhite, TextureSpot, TextureRadial, TextureCast;
        public float TextureKb, TextureKblob, TextureKgrad, TextureKwhite, TextureKspot, TextureKradial, TextureKcast;
    }

    /// <summary>
    /// 盖章引擎：从原 Form1.cs 移植的核心业务（iTextSharp 盖章、图片处理、骑缝章、转图）。
    /// 纯逻辑，不依赖任何 UI 控件；进度与提示通过回调上抛。
    /// 数字签名 / PDF密码加密功能已于 V2.3.2 移除。
    /// </summary>
    internal static class StampEngine
    {
        // ===== 打点结束 =====

        // ===================== 印章资源准备 =====================
        /// <summary>准备骑缝章图像。返回 false 表示失败（日志已写）。</summary>
        public static bool PrepareStampResources(
            StampOptions opt,
            out Bitmap seamImage,
            out float xzbl,
            Action<string> log)
        {
            seamImage = null;
            xzbl = 1f;

            // V2.4.0.55：空值容错——OutputPath 仅用于骑缝章资源预备的目录存在性检查/创建，
            // 为空时跳过（修复 Directory.CreateDirectory("") 抛"路径不能为空字符串或全为空白"）
            if (!string.IsNullOrWhiteSpace(opt.OutputPath) && !Directory.Exists(opt.OutputPath))
            {
                Directory.CreateDirectory(opt.OutputPath);
            }

            try
            {
                // 无盖章、无印章：占位图走"仅输出文件"流程
                if (opt.QfzType == 1 && opt.Placements.Count == 0
                    && (string.IsNullOrEmpty(opt.StampImagePath) || !File.Exists(opt.StampImagePath)))
                {
                    seamImage = new Bitmap(1, 1);
                    return true;
                }

                seamImage = new Bitmap(opt.StampImagePath);
                if (opt.RemoveWhite)
                {
                    seamImage = WhiteTransparencyHelper.Apply(seamImage, 20);
                }
                if (opt.Opacity < 100)
                {
                    seamImage = SetImageOpacity(seamImage, opt.Opacity);
                }
                // V2.4.0.58：盖章渲染（纹理/色偏）同步应用到骑缝章基准图（对齐页面章 CreatePlacementBitmap 的
                // 去白→透明度→纹理→旋转 顺序；种子每次资源准备固定一次，骑缝章各页切片纹理一致）
                if (opt.TextureEnabled
                    && (opt.TextureBrightness > 0 || opt.TextureBlob > 0
                        || opt.TextureGradient > 0 || opt.TextureWhite > 0
                        || opt.TextureSpot > 0 || opt.TextureRadial > 0
                        || opt.TextureCast > 0))
                {
                    seamImage = ApplyInkTexture(seamImage, opt.TextureSeed,
                        opt.TextureBrightness, opt.TextureBlob,
                        opt.TextureGradient, opt.TextureWhite,
                        opt.TextureSpot, opt.TextureRadial,
                        opt.TextureCast,
                        opt.TextureKb, opt.TextureKblob, opt.TextureKgrad,
                        opt.TextureKwhite, opt.TextureKspot,
                        opt.TextureKradial, opt.TextureKcast);
                }
                if (opt.Rotation != 0)
                {
                    int iw = seamImage.Width;
                    seamImage = RotateImg(seamImage, opt.Rotation, true); // v2.4.0.76: 固定切边（骑缝章与页面章统一）
                    xzbl = 1f * seamImage.Width / iw;
                }
                return true;
            }
            catch (Exception ex)
            {
                log("印章准备失败：" + ex.Message);
                if (seamImage != null) { seamImage.Dispose(); seamImage = null; }
                return false;
            }
        }

        // ===================== 盖章核心 =====================
        public static bool PDFWatermark(
            StampOptions opt,
            Bitmap seamImage,
            float xzbl,
            string inputfilepath,
            string outputfilepath,
            string sourcepath,
            Action<string> log,
            Action<int, int> pageProgress = null,
            Action<bool> savingIndicator = null)
        {
            float sfbl = (100f * opt.SizeMm * xzbl * 72) / (25.4f * seamImage.Width);

            PdfReader pdfReader = null;
            PdfStamper pdfStamper = null;
            FileStream fileStream = null;
            try
            {
                fileStream = new FileStream(outputfilepath, FileMode.Create);
                pdfReader = new PdfReader(inputfilepath);
                pdfStamper = new PdfStamper(pdfReader, fileStream);

                int numberOfPages = pdfReader.NumberOfPages;
                int qfzPages = 0;
                List<int> qfzList = new List<int>();

                bool skipSeamStamp = SeamStampPolicy.ShouldSkipForSinglePage(numberOfPages, opt.QfzType);
                if (!skipSeamStamp && opt.QfzType == 0)
                {
                    for (int i = 1; i <= numberOfPages; i++)
                    {
                        qfzList.Add(i);
                        qfzPages++;
                    }
                }
                else if (!skipSeamStamp && opt.QfzType == 2)
                {
                    for (int i = 1; i <= numberOfPages; i += 2)
                    {
                        qfzList.Add(i);
                        qfzPages++;
                    }
                }
                else if (!skipSeamStamp && opt.QfzType == 3)
                {
                    for (int i = 2; i <= numberOfPages; i += 2)
                    {
                        qfzList.Add(i);
                        qfzPages++;
                    }
                }
                else if (!skipSeamStamp && opt.QfzType == 4)
                {
                    foreach (int page in opt.Placements.DistinctPages(sourcepath))
                    {
                        qfzList.Add(page);
                        qfzPages++;
                    }
                }

                // V390：左/上骑缝（wzType 2/1）物理顺序反向——页1 显示章最右条（用户实测：右/下正常、左/上反）
                if (opt.WzType == 2 || opt.WzType == 1) qfzList.Reverse();

                PdfContentByte waterMarkContent;

                if (opt.QfzType != 1 && qfzPages > 1)
                {
                    int max = opt.MaxSplit;
                    if (max < 1) max = numberOfPages; // V390：自动(0)=该PDF页数，对齐预览 GetSeamPreview（此前解析为 1 → 3 页文件 tmp=0 → subImages 除零）
                    // 段数向上取整：max 大于等于骑缝章页数时一整段盖完；
                    // 旧公式 qfzPages/max+1 在整除（如 29 页、分割数 29）时会多拆一段，导致效果减半。
                    int ss = (qfzPages + max - 1) / max;
                    int sy = qfzPages - ss * max / 2;
                    int sys = sy / ss;
                    int syy = sy % ss;
                    int pp = max / 2 + sys;
                    Bitmap[] nImage;
                    int startIndex = 0;
                    for (int i = 0; i < ss; i++)
                    {
                        int tmp = pp;
                        if (i < syy)
                        {
                            tmp++;
                        }
                        if (tmp <= 0) { startIndex += tmp; continue; } // V390：该段无切片跳过（对齐预览），防 subImages 除零
                        nImage = subImages(seamImage, tmp);
                        for (int y = 0; y < tmp; y++)
                        {
                            int page = qfzList[startIndex + y];
                            waterMarkContent = pdfStamper.GetOverContent(page);
                            int rotation = pdfReader.GetPageRotation(page);
                            iTextSharp.text.Rectangle psize = pdfReader.GetPageSize(page);
                            float pWidth, pHeight;
                            if (rotation == 90 || rotation == 270)
                            {
                                pWidth = psize.Height;
                                pHeight = psize.Width;
                            }
                            else
                            {
                                pWidth = psize.Width;
                                pHeight = psize.Height;
                            }
                            // V1.0.0.32：横向页（rotation 90/270）骑缝章方向映射——现实装订横页"竖起来"与竖页一起盖：
                            // 竖右→横下、竖下→横左、竖左→横上、竖上→横右
                            int effWz = opt.WzType;
                            if (rotation == 90 || rotation == 270)
                            {
                                effWz = opt.WzType == 3 ? 0 : opt.WzType == 0 ? 2 : opt.WzType == 2 ? 1 : 3;
                            }
                            Bitmap qfzImage;
                            if (effWz == 3 || effWz == 2)
                            {
                                qfzImage = nImage[y];
                            }
                            else
                            {
                                qfzImage = RotateImg(nImage[y], 90, false);
                            }
                            iTextSharp.text.Image image = iTextSharp.text.Image.GetInstance(qfzImage, ImageFormat.Png);
                            float imageW, imageH;
                            image.ScalePercent(sfbl);
                            imageW = image.Width * sfbl / 100f;
                            imageH = image.Height * sfbl / 100f;

                            float xPos = 0, yPos = 0;
                            if (effWz == 3)
                            {
                                xPos = pWidth - imageW;
                                yPos = (pHeight - imageH) * (100 - opt.WzPercent) / 100;
                            }
                            else if (effWz == 2)
                            {
                                xPos = 0;
                                yPos = (pHeight - imageH) * (100 - opt.WzPercent) / 100;
                            }
                            else if (effWz == 1)
                            {
                                // V1.0.0.35：上=贴顶（iText y 向上，左下角 y = 页高-章高）；此前 yPos=0 贴底与预览相反
                                xPos = (pWidth - imageW) * opt.WzPercent / 100;
                                yPos = pHeight - imageH;
                            }
                            else
                            {
                                // V1.0.0.35：下=贴底（iText y 向上，左下角 y = 0）；此前 yPos=pHeight-imageH 贴顶与预览相反
                                xPos = (pWidth - imageW) * opt.WzPercent / 100;
                                yPos = 0;
                            }
                            image.SetAbsolutePosition(xPos, yPos);
                            waterMarkContent.AddImage(image);
                        }
                        startIndex += tmp;
                    }
                }

                // 页面章渲染：预览上实际放置了什么章，生成时就盖什么章
                if (opt.Placements.Count > 0)
                {
                    int totalProcessPages = numberOfPages;
                    for (int page = 1; page <= numberOfPages; page++)
                    {
                        if (pageProgress != null)
                        {
                            pageProgress(Math.Min(page, totalProcessPages), totalProcessPages);
                        }

                        List<StampPlacement> pagePlacements = opt.Placements.ForPage(sourcepath, page).ToList();
                        if (pagePlacements.Count == 0)
                        {
                            continue;
                        }

                        waterMarkContent = pdfStamper.GetOverContent(page);
                        int pageRotation = pdfReader.GetPageRotation(page);
                        iTextSharp.text.Rectangle pageSize = pdfReader.GetPageSize(page);

                        foreach (StampPlacement placement in pagePlacements)
                        {
                            using (Bitmap placementBitmap = CreatePlacementBitmap(placement))
                            {
                                iTextSharp.text.Image placementImage = iTextSharp.text.Image.GetInstance(
                                    placementBitmap, ImageFormat.Png);
                                float placementScale = 100f * placement.SizeMm * 72f /
                                    (25.4f * placementBitmap.Width);
                                placementImage.ScalePercent(placementScale);

                                // 随机旋转：位图未旋转（布局尺寸恒定=SizeMm），旋转在输出绘制层用 iText RotationDegrees 完成，
                                // 预览端用 CSS rotate 旋转。V2.4.0.71 方向实证修正：iText RotationDegrees 正角度=PDF y-up 逆时针，
                                // 渲染到屏幕（y-down）为视觉逆时针；CSS rotate 正角度=屏幕顺时针。两端方向相反（同一 θ 视觉差 2θ），
                                // 故输出端取反（-Rotation），使输出与预览视觉一致（ItextVsCssVisualTests 四角度实证）。
                                bool drawRotate = placement.RandomRotation && placement.Rotation != 0;
                                if (drawRotate)
                                {
                                    placementImage.RotationDegrees = -placement.Rotation;
                                }

                                float placementWidth = placementImage.Width * placementScale / 100f;
                                float placementHeight = placementImage.Height * placementScale / 100f;
                                // V2.4.0.64：内容中心 + 旋转后 bbox 钳制（与预览端 stampStyle/拖拽边界同一套公式，两端位置统一）——
                                // 中心比例（或文字章左上区间）→ 随机位移 → 按 bbox 钳中心 → 反推 iText 左下角。
                                // 废弃 V57 三段式（原尺寸钳左下角 + 旋转补偿 + ClampAnchorAfterRotate）：bbox 钳制后旋转角不再出界，
                                // 且无旋转时 bbox=原尺寸与旧逻辑等价（零回归）。
                                float pageW = (pageRotation == 90 || pageRotation == 270) ? pageSize.Height : pageSize.Width;
                                float pageH = (pageRotation == 90 || pageRotation == 270) ? pageSize.Width : pageSize.Height;
                                float centerX = placement.CenterRatio
                                    ? pageW * placement.X
                                    : (pageW - placementWidth) * placement.X + placementWidth / 2f;
                                float centerY = placement.CenterRatio
                                    ? pageH * (1f - placement.Y)
                                    : (pageH - placementHeight) * (1f - placement.Y) + placementHeight / 2f;
                                // 随机位移（mm→PDF点）：放置时已固定随机值，预览与输出一致；
                                // iText 坐标系 y 向上为正，预览坐标系 y 向下为正，故 Y 方向取反。
                                centerX += placement.OffsetXmm * 72f / 25.4f;
                                centerY -= placement.OffsetYmm * 72f / 25.4f;
                                // bbox 钳中心（旋转角度取实际绘制角度；bbox 大于页面时保持原语义不钳）
                                float rotateDeg = drawRotate ? placement.Rotation : 0f;
                                PDFQFZ.Library.StampAnchorClamp.ClampCenterByBBox(ref centerX, ref centerY,
                                    placementWidth, placementHeight, rotateDeg, pageW, pageH);
                                // V2.4.0.70：反推 iText 锚点。实证 iTextSharp SetAbsolutePosition+RotationDegrees
                                // = 旋转后 bbox 左下角落在锚点（Image.GetMatrix 按象限算 CX/CY），旧 AnchorFromCenter
                                // 假设“旋转绕未旋转左下角”在 A≠0 时反推错误导致输出中心偏移（预览 CSS 绕中心，两端不一致）。
                                // 新公式：锚点 = 中心 − (bw/2, bh/2)，A=0 时与旧逻辑逐位一致（零回归）。
                                float placementX, placementY;
                                PDFQFZ.Library.StampAnchorClamp.AnchorFromBBoxCenter(centerX, centerY,
                                    placementWidth, placementHeight, rotateDeg, out placementX, out placementY);
                                placementImage.SetAbsolutePosition(placementX, placementY);
                                waterMarkContent.AddImage(placementImage);
                            }
                        }
                    }
                }

                // V2.4.0.96：文字水印叠加——预览放置的水印框（Watermarks）逐页绘制，与盖章互不干扰
                // （水印在 GetOverContent 上与章同一层，但参数独立存储；无章/无骑缝章时也生效）
                if (opt.Watermarks != null && opt.Watermarks.Count > 0)
                {
                    for (int page = 1; page <= numberOfPages; page++)
                    {
                        List<WatermarkBox> pageBoxes = null;
                        try { pageBoxes = opt.Watermarks.Where(b => b != null && (b.Page == 0 || b.Page == page)).ToList(); }
                        catch { pageBoxes = null; }

                        if (pageBoxes == null || pageBoxes.Count == 0) continue;

                        PdfContentByte wmContent = pdfStamper.GetOverContent(page);
                        int pageRotation = pdfReader.GetPageRotation(page);
                        iTextSharp.text.Rectangle pageSize = pdfReader.GetPageSize(page);
                        float pageW = (pageRotation == 90 || pageRotation == 270) ? pageSize.Height : pageSize.Width;
                        float pageH = (pageRotation == 90 || pageRotation == 270) ? pageSize.Width : pageSize.Height;
                        try { System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wm_debug.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " [WM-DIAG] 输出 page=" + page + " 旋转=" + pageRotation + " 交换前WxH=" + pageSize.Width.ToString("F0") + "x" + pageSize.Height.ToString("F0") + " 交换后WxH=" + pageW.ToString("F0") + "x" + pageH.ToString("F0") + " 匹配水印框=" + (pageBoxes==null?0:pageBoxes.Count) + " 框Page字段=" + (pageBoxes!=null && pageBoxes.Count>0 ? string.Join("/", pageBoxes.Select(b=>b.Page.ToString())) : "") + Environment.NewLine, System.Text.Encoding.UTF8); } catch { }
                        foreach (WatermarkBox box in pageBoxes)
                        {
                            try { DrawTextWatermark(wmContent, box, pageW, pageH); }
                            catch { /* 单框失败不阻断整文件（坏字体/超界容错） */ }
                        }
                    }
                }

                return true;
            }
            catch (BadPasswordException)
            {
                log("文件“" + Path.GetFileName(inputfilepath) + "”打不开：PDF 密码错误或文件已加密。");
                return false;
            }
            catch (Exception ex)
            {
                try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "运行组件", "pdfqfz_error.log"), System.DateTime.Now.ToString("HH:mm:ss") + " " + ex.ToString() + "\\r\\n---\\r\\n"); } catch { }
                log("文件“" + Path.GetFileName(inputfilepath) + "”盖章失败：" + ex.Message);
                return false;
            }
            finally
            {
                if (savingIndicator != null)
                {
                    savingIndicator(true);
                }

                try
                {
                    if (pdfStamper != null) pdfStamper.Close();
                    if (pdfReader != null) pdfReader.Close();
                    if (fileStream != null) fileStream.Close();
                }
                finally
                {
                    if (savingIndicator != null)
                    {
                        savingIndicator(false);
                    }
                }

                if (File.Exists(outputfilepath))
                {
                    try
                    {
                        FileInfo fi = new FileInfo(outputfilepath);
                        if (fi.Length == 0)
                        {
                            File.Delete(outputfilepath);
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        // ===================== 文字水印（V2.4.0.96） =====================
        private static readonly object WmFontLock = new object();
        private static readonly Dictionary<string, BaseFont> WmFontCache = new Dictionary<string, BaseFont>();

        /// <summary>系统字体路径解析（Windows 字体目录；未知字体回退微软雅黑）。粗体优先专用粗体字库。</summary>
        private static string ResolveWatermarkFontPath(string fontName, bool bold)
        {
            string wf = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + @"\Fonts\";
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
                    // 字体缺失（精简系统）：回退内置 Helvetica（中文丢字形但保证不崩，其余场景正常）
                    BaseFont bf2 = BaseFont.CreateFont(BaseFont.HELVETICA, BaseFont.WINANSI, BaseFont.NOT_EMBEDDED);
                    WmFontCache[path] = bf2;
                    return bf2;
                }
            }
        }

        /// <summary>
        /// 文字水印绘制（对齐 WPF WatermarkCanvas.DrawText / PreviewRenderer.DrawBox 算法）：
        /// 字号 = 框高 × FontScale（超宽按最长行收窄 ×1.05）→ 字距 adv = 字号×(1+ls/100)（下限 0.2）→
        /// 行距 lineH = 字号×(1+LineSpacing/100)（下限 0.5）→ 逐行逐字符矩阵定位，绕框中心旋转（屏幕顺时针→iText 取反）。
        /// 下划线/删除线按旋转后行端点连线；斜体用本地错切近似（iText 无中文斜体字形）。
        /// </summary>
        private static void DrawTextWatermark(PdfContentByte cb, WatermarkBox box, float pageW, float pageH)
        {
            if (box == null) return;
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wm_debug.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " [WM-OUT] DrawTextWatermark pageW=" + pageW.ToString("F1") + " pageH=" + pageH.ToString("F1") + " box.Page=" + box.Page + " X=" + box.X + " Y=" + box.Y + " W=" + box.W + " H=" + box.H + " h0=" + box.H0 + " fontScale=" + box.FontScale + " text=" + (box.Text ?? "").Substring(0, Math.Min(30, (box.Text ?? "").Length)) + Environment.NewLine, System.Text.Encoding.UTF8); } catch { }
            string raw = box.Text ?? "";
            string[] rawLines = raw.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            List<string> lines = new List<string>();
            foreach (string l in rawLines) if (!string.IsNullOrEmpty(l)) lines.Add(l);
            if (lines.Count == 0) return;

            float boxWpt = Math.Max(1f, pageW * box.W);
            float boxHpt = Math.Max(1f, pageH * box.H);
            // V2.4.0.350: PDF 水印短边比例字号（与图片引擎 ImageWatermarkEngine.DrawBox V344 同构）——
            //       fs = FsToS × 页面短边。基准页（用户设水印页）数学恒等于原公式（H0 与 FsToS 同源于前端 fs1/dispW/ptW），
            //       旋转页（90/270 交换后 pageH 变小）字号不再随 pageH 缩水——横竖页字号一致（A4 短边均为 595）。
            //       无 FsToS（旧方案/纯后端）fallback 原公式（逐位不变，零回归）。
            float fs = box.FsToS > 0.0001f
                ? box.FsToS * Math.Min(pageW, pageH)
                : (box.H0 > 0.01f ? (pageH * box.H0) : boxHpt) * (box.FontScale > 0.01f ? box.FontScale : 0.8f);
            // V2.4.0.347：换行快照优先——前端 wmCaptureWrapLines（隐藏 div + Range）已按浏览器 CSS 实测换行，
            //        与图片引擎（ImageWatermarkEngine.DrawBox V303）同源：有快照直接用（所见即所得，横竖页一致），
            //        无快照（旧方案/旧数据/纯后端）才用 iText 逐字符测量重排兜底（原逻辑逐位不变，零回归）。
            bool useWrapLines = (box.WrapLines != null && box.WrapLines.Count > 0);
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wm_debug.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " [WM-OUT] useWrapLines=" + useWrapLines + " box.WrapLines.Count=" + (box.WrapLines==null?0:box.WrapLines.Count) + " 快照行=" + (box.WrapLines!=null?string.Join("|", box.WrapLines.Where(l=>!string.IsNullOrEmpty(l)).Take(6).Select(l=>l.Substring(0, Math.Min(12, l.Length)))):"") + Environment.NewLine, System.Text.Encoding.UTF8); } catch { }
            if (useWrapLines)
            {
                lines.Clear();
                foreach (string l in box.WrapLines) if (!string.IsNullOrEmpty(l)) lines.Add(l);
            }
            else
            {
                // 自动分行：逐字符测量宽度，超过框宽就换行（和前端预览一致）
                BaseFont fontForMeasure = GetWatermarkFont(box.FontName, box.Bold);
                List<string> autoLines = new List<string>();
                foreach (string l in lines)
                {
                    float curW = 0;
                    string curLine = "";
                    foreach (char ch in l)
                    {
                        float cw = fontForMeasure.GetWidth(ch.ToString()) * fs / 1000f;
                        if (box.LetterSpacing > 0) cw += box.LetterSpacing;
                        if (curW > 0 && curW + cw > boxWpt)
                        {
                            autoLines.Add(curLine);
                            curLine = ch.ToString();
                            curW = cw;
                        }
                        else
                        {
                            curLine += ch;
                            curW += cw;
                        }
                    }
                    if (!string.IsNullOrEmpty(curLine)) autoLines.Add(curLine);
                }
                lines = autoLines;
            }
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wm_debug.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " [WM-OUT] fs=" + fs.ToString("F1") + " fsToS=" + box.FsToS.ToString("F6") + " shortEdge=" + Math.Min(pageW, pageH).ToString("F1") + " boxWpt=" + boxWpt.ToString("F1") + " boxHpt=" + boxHpt.ToString("F1") + " lines=" + lines.Count + " firstLine=" + (lines.Count > 0 ? lines[0].Substring(0, Math.Min(20, lines[0].Length)) : "") + Environment.NewLine, System.Text.Encoding.UTF8); } catch { }
            if (fs < 0.5f) return;

            cb.SaveState();
            try
            {
                PdfGState gs = new PdfGState(); // iTextSharp PdfGState 不实现 IDisposable：SaveState/RestoreState 管理，无需 using
                float op = box.Opacity / 100f;
                gs.FillOpacity = op < 0.01f ? 0.01f : (op > 1f ? 1f : op);
                cb.SetGState(gs);
                int rgb = box.ColorArgb & 0xFFFFFF;

                BaseFont font = GetWatermarkFont(box.FontName, box.Bold);
                float lineH = fs * Math.Max(0.5f, 1f + box.LineSpacing / 100f);
                float cx = (box.X + box.W / 2f) * pageW;   // 框中心 x（页面系）
                float cyTop = (1f - box.Y) * pageH;        // 框顶 y（iText y-up）
                float cy = cyTop - boxHpt / 2f;            // 框中心 y（y-up）
                float rotDeg = -box.Rotation;              // 屏幕顺时针 → iText y-up 逆时针取反（同章 V2.4.0.71 实证）
                float rad = rotDeg * (float)Math.PI / 180f;
                float cosR = (float)Math.Cos(rad), sinR = (float)Math.Sin(rad);
                float skew = box.Italic ? 0.2f : 0f;
                // 斜体组合矩阵：a=cos, b=sin, c=cos*skew-sin, d=sin*skew+cos（先本地错切再旋转）
                float ma = cosR, mb = sinR, mc = cosR * skew - sinR, md = sinR * skew + cosR;
                float baselineOffset = fs * 0.36f;         // 汉字中心 → 基线偏移（雅黑 ascent/descent 近似 0.36em）
                float lineWid = fs * 0.05f;                // 下划线/删除线宽（WPF 近似）

                cb.BeginText();
                cb.SetFontAndSize(font, fs);
                cb.SetTextMatrix(0, 0);
                // 多行整体垂直居中（对齐前端 flex justify-content:center）
                float totalLinesH = lineH * lines.Count;
                float firstLineCenterY = cyTop - (boxHpt - totalLinesH) / 2f - lineH * 0.5f;
                for (int li = 0; li < lines.Count; li++)
                {
                    string line = lines[li];
                    int n = line.Length;
                    if (n == 0) continue;
                    float lw = 0;
                    float[] charWidths = new float[n];
                    for (int ci = 0; ci < n; ci++) { float cw = font.GetWidth(line[ci].ToString()) * fs / 1000f; if (box.LetterSpacing > 0) cw += box.LetterSpacing; charWidths[ci] = cw; lw += cw; }
                    try { System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wm_debug.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " [WM-OUT-LINE] li=" + li + " 行内容=[" + line.Substring(0, Math.Min(16, line.Length)) + "] 字符数=" + n + " lw=" + lw.ToString("F1") + " boxWpt=" + boxWpt.ToString("F1") + " 占比=" + (boxWpt>0?(lw/boxWpt).ToString("F3"):"0") + Environment.NewLine, System.Text.Encoding.UTF8); } catch { }
                    // 未旋转行起点（按对齐；y = 行中心 y-up）
                    float lineStartX0 = box.X * pageW;
                    if (box.Align == 1) lineStartX0 += (boxWpt - lw) / 2f;
                    else if (box.Align == 2) lineStartX0 += (boxWpt - lw);
                    float lineCenterY0 = firstLineCenterY - lineH * li;
                    // 旋转后行起点/终点（下划线/删除线端点）
                    float sx = cx + (lineStartX0 - cx) * cosR - (lineCenterY0 - cy) * sinR;
                    float sy = cy + (lineStartX0 - cx) * sinR + (lineCenterY0 - cy) * cosR;
                    float ex = cx + (lineStartX0 + lw - cx) * cosR - (lineCenterY0 - cy) * sinR;
                    float ey = cy + (lineStartX0 + lw - cx) * sinR + (lineCenterY0 - cy) * cosR;

                    float cx0 = lineStartX0;  // 逐字符累加实际宽度
                    for (int ci = 0; ci < n; ci++)
                    {
                        float chx0 = cx0;  // 逐字符累加实际宽度  // 字符左边缘（SetTextMatrix x 是文本起点，不是中心）
                        float chy0 = lineCenterY0;
                        float dx = chx0 - cx, dy = chy0 - cy;
                        float rx = cx + dx * cosR - dy * sinR;
                        float ry = cy + dx * sinR + dy * cosR;
                        cb.SetTextMatrix(ma, mb, mc, md, rx, ry - baselineOffset);
                        cb.ShowText(new string(line[ci], 1));
                        cx0 += charWidths[ci];
                    }
                    // 下划线（基线下方 0.1em）/删除线（行中心）——按旋转后端点连线
                    if (box.Underline)
                    {
                        float ulY0 = lineCenterY0 - fs * 0.46f;
                        float usx = cx + (lineStartX0 - cx) * cosR - (ulY0 - cy) * sinR;
                        float usy = cy + (lineStartX0 - cx) * sinR + (ulY0 - cy) * cosR;
                        float uex = cx + (lineStartX0 + lw - cx) * cosR - (ulY0 - cy) * sinR;
                        float uey = cy + (lineStartX0 + lw - cx) * sinR + (ulY0 - cy) * cosR;
                        cb.SetLineWidth(lineWid);
                        cb.MoveTo(usx, usy); cb.LineTo(uex, uey); cb.Stroke();
                    }
                    if (box.Strike)
                    {
                        cb.SetLineWidth(lineWid);
                        cb.MoveTo(sx, sy); cb.LineTo(ex, ey); cb.Stroke();
                    }
                }
                cb.EndText();
            }
            finally { cb.RestoreState(); }
        }

        // ===================== 图片处理 =====================
        /// <summary>按放置参数生成最终印章位图（去白/透明度/旋转）。供预览叠加与盖章共用。</summary>
        public static Bitmap CreatePlacementBitmap(StampPlacement placement)
        {
            // V2.4.0.62：空/无效印章路径抛明确异常，替代 new Bitmap("") 的 "路径的形式不合法" 模糊信息
            if (placement == null || string.IsNullOrWhiteSpace(placement.StampPath) || !System.IO.File.Exists(placement.StampPath))
                throw new InvalidOperationException("印章图片不存在或路径无效：" + (placement == null ? "(空)" : placement.StampPath));
            Bitmap processed = new Bitmap(placement.StampPath);
            if (placement.UseWhiteTransparency)
            {
                Bitmap transparent = WhiteTransparencyHelper.Apply(processed, placement.WhiteTransparencyTolerance);
                processed.Dispose();
                processed = transparent;
            }

            if (placement.Opacity < 100)
            {
                processed = SetImageOpacity(processed, placement.Opacity);
            }

            // 盖章渲染：七维随机质感（明暗/斑块大小/渐变/局部露白/内部斑点/径向压印/整体色偏；
            // 放置时固定种子 → 预览刷新与输出纹理一致）
            if (placement.TextureEnabled
                && (placement.TextureBrightness > 0 || placement.TextureBlob > 0
                    || placement.TextureGradient > 0 || placement.TextureWhite > 0
                    || placement.TextureSpot > 0 || placement.TextureRadial > 0
                    || placement.TextureCast > 0))
            {
                processed = ApplyInkTexture(processed, placement.TextureSeed,
                    placement.TextureBrightness, placement.TextureBlob,
                    placement.TextureGradient, placement.TextureWhite,
                    placement.TextureSpot, placement.TextureRadial,
                    placement.TextureCast,
                    placement.TextureKb, placement.TextureKblob, placement.TextureKgrad,
                    placement.TextureKwhite, placement.TextureKspot,
                    placement.TextureKradial, placement.TextureKcast);
            }

            // 随机旋转的章：位图不旋转（布局尺寸恒定=SizeMm，宽高比不变，杜绝“同一批章有大有小”）；
            // 旋转在绘制层完成：预览用 WPF RenderTransform、输出用 iText RotationDegrees（与原版骑缝章随机旋转一致）。
            // 固定旋转：v2.4.0.76 固定为切边模式（旋转后按原画布宽裁切，章尺寸恒 = SizeMm；不切边选项已删除，两端统一）
            if (placement.Rotation != 0 && !placement.RandomRotation)
            {
                Bitmap rotated = RotateImg(processed, placement.Rotation, true);
                if (!ReferenceEquals(rotated, processed))
                {
                    processed.Dispose();
                }
                processed = rotated;
            }

            return processed;
        }

        private static void CalculateStampPosition(
            int pageRotation,
            iTextSharp.text.Rectangle pageSize,
            float imageWidth,
            float imageHeight,
            float widthRatio,
            float heightRatio,
            bool centerRatio,
            out float x,
            out float y)
        {
            // centerRatio=true（手动/范围页盖章）：印章中心在页面内的比例，超出部分由页面边界自然裁剪；
            // centerRatio=false（按文字盖章）：印章完整在页面内（左上角在"页面减章宽"区间内的比例）
            if (pageRotation == 90 || pageRotation == 270)
            {
                x = centerRatio
                    ? pageSize.Height * widthRatio - imageWidth / 2f
                    : (pageSize.Height - imageWidth) * widthRatio;
                y = centerRatio
                    ? pageSize.Width * heightRatio - imageHeight / 2f
                    : (pageSize.Width - imageHeight) * heightRatio;
            }
            else
            {
                x = centerRatio
                    ? pageSize.Width * widthRatio - imageWidth / 2f
                    : (pageSize.Width - imageWidth) * widthRatio;
                y = centerRatio
                    ? pageSize.Height * heightRatio - imageHeight / 2f
                    : (pageSize.Height - imageHeight) * heightRatio;
            }
        }

        private static Bitmap SetImageOpacity(Bitmap src, int opacity)
        {
            byte alpha = (byte)(opacity * 255 / 100);
            BitmapData data = src.LockBits(new Rectangle(0, 0, src.Width, src.Height),
                                         ImageLockMode.ReadWrite,
                                         PixelFormat.Format32bppArgb);
            try
            {
                byte[] buffer = new byte[data.Stride * data.Height];
                Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
                for (int i = 3; i < buffer.Length; i += 4)
                {
                    if (buffer[i] != 0) buffer[i] = alpha;
                }
                Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
            }
            finally
            {
                src.UnlockBits(data);
            }
            return src;
        }

        /// <summary>盖章渲染：五维随机质感。每章以种子确定性派生：明暗幅度(0~上限)、
        /// 斑块大小(0~上限)、渐变方向与幅度(0~上限)、局部露白(0~上限)、内部斑点(0~上限，簇状)，
        /// 组合生成印泥纹理。强度上限已按用户要求整体加倍（100 档 ≈ 手工盖章强效果）。</summary>
        private static Bitmap ApplyInkTexture(Bitmap src, int seed,
            int brightness, int blob, int gradient, int white, int spot,
            int radial, int cast,
            float kb, float kblob, float kgrad, float kwhite, float kspot,
            float kradial, float kcast)
        {
            // 分布随机（噪声/方向/斑点位置，固定于本章，与参数无关 → 调参时分布稳定）
            Random distRnd = new Random(seed);
            // 强度 = 本章固化系数(0.85~1.15，±15% 浮动) × 参数上限 → 参数主控、章与章微调
            double amp = kb * brightness / 100.0 * 2.4;                 // 印泥浓淡不均 0~2.4（最大值按 40% 档封顶）
            double blobLevel = kblob * blob / 100.0;                    // 斑点大小 0~1
            double gradAmp = kgrad * gradient / 100.0 * 1.0;            // 渐变幅度 0~1.0
            double whiteLevel = kwhite * white / 100.0;                 // 局部露白随滑块（分布随机）
            double radialLevel = kradial * radial / 100.0 * 2.0;        // 径向压印 0~2（最大值提高 2 倍）
            // 色调：参数 -100~+100，0=原色；负=偏冷(玫红/紫红)、正=偏暖(橘红/橙)；
            // 方向全局一致（同一批印泥同一色调）、幅度按章浮动；红章始终是红，只是冷暖微差
            double castT = Math.Abs(cast) / 100.0 * kcast;
            double hueShift = cast / 100.0 * 20.0;   // 100 档最多偏 20°（HSV 色相）
            if (amp < 0.005 && gradAmp < 0.005 && whiteLevel < 0.005 && spot <= 0
                && radialLevel < 0.005 && castT < 0.005)
            {
                return src;
            }
            int w = src.Width, h = src.Height;
            if (w < 8 || h < 8)
            {
                return src;
            }
            // 低频噪声源：网格固定细碎（w/9），与斑点大小无关（V2.3.2.6 修正：
            // 斑点大小只作用斑点直径，不再放大印泥浓淡不均的斑块尺寸）
            int nw = Math.Max(6, (int)Math.Round(w / 9.0));
            int nh = Math.Max(6, (int)Math.Round(h / 9.0));
            Bitmap noise = new Bitmap(nw, nh, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            System.Drawing.Imaging.BitmapData nd = noise.LockBits(new Rectangle(0, 0, nw, nh),
                System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                byte[] nb = new byte[nd.Stride * nh];
                for (int yy = 0; yy < nh; yy++)
                {
                    for (int xx = 0; xx < nw; xx++)
                    {
                        byte val = (byte)distRnd.Next(256);
                        int i = yy * nd.Stride + xx * 4;
                        nb[i] = val;
                        nb[i + 1] = val;
                        nb[i + 2] = val;
                        nb[i + 3] = 255;
                    }
                }
                Marshal.Copy(nb, 0, nd.Scan0, nb.Length);
            }
            finally
            {
                noise.UnlockBits(nd);
            }
            noise = BoxBlurGray(noise);
            // 放大到章尺寸（双三次 → 大块低频斑块）
            Bitmap bigNoise = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics gn = Graphics.FromImage(bigNoise))
            {
                gn.Clear(Color.Transparent);
                gn.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                gn.DrawImage(noise, 0, 0, w, h);
            }
            noise.Dispose();
            // 渐变方向（种子派生，固定于本章）
            double gradDir = gradient > 0 ? distRnd.NextDouble() * Math.PI * 2.0 : 0.0;
            double gx = Math.Cos(gradDir), gy = Math.Sin(gradDir);
            // 逐像素亮度调制：仅章内像素（alpha>0）生效，章外保持完全透明。
            // 渲染顺序：先画内部斑点（作为底层融入章面）→ 再整体逐像素调制，斑点与章面一体处理、不割裂。
            Bitmap dst = (Bitmap)src.Clone();
            System.Drawing.Imaging.BitmapData d1 = src.LockBits(new Rectangle(0, 0, w, h),
                System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            long sr = 0, sg = 0, sb = 0;
            int scnt = 0;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            byte[] b1 = null;
            int stride1 = d1.Stride;
            try
            {
                b1 = new byte[d1.Stride * h];
                Marshal.Copy(d1.Scan0, b1, 0, b1.Length);
            }
            finally
            {
                src.UnlockBits(d1);
            }
            // 第一遍：扫描章面边界 + 统计平均色（供斑点采样）
            for (int yy = 0; yy < h; yy++)
            {
                int row1 = yy * stride1;
                for (int xx = 0; xx < w; xx++)
                {
                    int i1 = row1 + xx * 4;
                    if (b1[i1 + 3] == 0)
                    {
                        continue;
                    }
                    sr += b1[i1 + 2];   // R（内存序 B,G,R,A）
                    sg += b1[i1 + 1];   // G
                    sb += b1[i1];       // B
                    scnt++;
                    if (xx < minX) minX = xx;
                    if (xx > maxX) maxX = xx;
                    if (yy < minY) minY = yy;
                    if (yy > maxY) maxY = yy;
                }
            }
            // 内部斑点：章形内部随机簇状不规则颗粒（有的连片、有的孤立，模拟印泥颗粒）。
            // 颜色从章内像素随机采样（红章取红系；BGR 通道序已修正，不再发蓝）；
            // 允许落在印章原有红色上；画在章面下方（先画），后续整体调制与章面融为一体。
            if (spot > 0 && scnt > 0 && maxX >= minX && maxY >= minY)
            {
                byte mr = (byte)(sr / scnt);
                byte mg = (byte)(sg / scnt);
                byte mb = (byte)(sb / scnt);
                // 预采样章面颜色（最多 48 个），斑点随机取其一 → 各点颜色略异更真实
                List<Color> inkColors = new List<Color>();
                for (int s = 0; s < 48; s++)
                {
                    int sx = minX + distRnd.Next(maxX - minX + 1);
                    int sy = minY + distRnd.Next(maxY - minY + 1);
                    if (b1[sy * stride1 + sx * 4 + 3] != 0)
                    {
                        inkColors.Add(Color.FromArgb(255,
                            b1[sy * stride1 + sx * 4 + 2],   // R
                            b1[sy * stride1 + sx * 4 + 1],   // G
                            b1[sy * stride1 + sx * 4]));     // B
                    }
                }
                // 斑点密度 = 参数上限 × 本章系数（调参平滑变化、章与章不同）；V2.3.2.5 内部最大值按 60% 档封顶（100 ≈ 原 60 档）
                int clusterCount = (int)Math.Round(spot / 100.0 * 297.0 * kspot);
                // 斑点尺寸（V2.3.2.7）：三三开——1/3 放大（×1~2.5，blob=100）、1/3 不变、1/3 去掉；
                // 大点突出、总数自然减少 1/3、不显密；上限恢复 ×1.5（V2.3.2.5 前水平，且 blobBoost 已移除不会再放大浓淡不均）
                using (Graphics gs = Graphics.FromImage(dst))
                {
                    for (int k = 0; k < clusterCount; k++)
                    {
                        double spotSizeScale;
                        int dice = distRnd.Next(3);
                        if (dice == 0)
                        {
                            spotSizeScale = 1.0 + blobLevel * 2.5 * distRnd.NextDouble();   // V2.3.2.8：放大 1~3.5×（对比更明显）
                        }
                        else if (dice == 1)
                        {
                            spotSizeScale = 1.0;                                            // 不变
                        }
                        else
                        {
                            continue;                                                       // 去掉（不画）
                        }
                        int cx = minX + distRnd.Next(maxX - minX + 1);
                        int cy = minY + distRnd.Next(maxY - minY + 1);
                        // 簇中心必须在章形内部（四方向都能扫描到章面），防止撒到圆章外接方框角落
                        if (!IsInsideStamp(b1, stride1, cx, cy, minX, minY, maxX, maxY))
                        {
                            continue;
                        }
                        int clusterSize = 1 + distRnd.Next(5); // 1~5 个点：孤立或连片
                        int spread = Math.Max(2, (int)Math.Round(8.0 * spotSizeScale)); // 簇散布
                        for (int j = 0; j < clusterSize; j++)
                        {
                            int ox = cx + distRnd.Next(-spread, spread + 1);
                            int oy = cy + distRnd.Next(-spread, spread + 1);
                            if (ox < 0) ox = 0;
                            if (oy < 0) oy = 0;
                            if (ox >= w) ox = w - 1;
                            if (oy >= h) oy = h - 1;
                            // 偏移点必须在章形内部，避免斑点出界
                            if (!IsInsideStamp(b1, stride1, ox, oy, minX, minY, maxX, maxY))
                            {
                                continue;
                            }
                            int r = Math.Max(1, (int)Math.Round((1 + distRnd.Next(3)) * spotSizeScale));
                            int al = 100 + distRnd.Next(156);            // 半透明~实色
                            Color cc = inkColors.Count > 0
                                ? inkColors[distRnd.Next(inkColors.Count)]
                                : Color.FromArgb(255, mr, mg, mb);
                            using (SolidBrush br = new SolidBrush(Color.FromArgb(al, cc.R, cc.G, cc.B)))
                            {
                                // 不规则形状：随机 6~9 个顶点的多边形（半径 0.6~1.4r、顶点角度扰动），模拟印泥吸附不均的毛边
                                int verts = 6 + distRnd.Next(4);
                                System.Drawing.PointF[] pts = new System.Drawing.PointF[verts];
                                for (int v = 0; v < verts; v++)
                                {
                                    double ang = 2.0 * Math.PI * v / verts + (distRnd.NextDouble() - 0.5) * 1.2;
                                    double rr = r * (0.45 + distRnd.NextDouble() * 1.25);
                                    pts[v] = new System.Drawing.PointF(
                                        ox + (float)(Math.Cos(ang) * rr),
                                        oy + (float)(Math.Sin(ang) * rr));
                                }
                                gs.FillPolygon(br, pts);
                            }
                        }
                    }
                }
            }
            // 低频噪声整体调制（含斑点区域，斑点与章面一起被处理 → 融为一体）
            System.Drawing.Imaging.BitmapData d2 = dst.LockBits(new Rectangle(0, 0, w, h),
                System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            System.Drawing.Imaging.BitmapData dn = bigNoise.LockBits(new Rectangle(0, 0, w, h),
                System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            byte[] b2 = new byte[d2.Stride * h];
            Marshal.Copy(d2.Scan0, b2, 0, b2.Length);   // 读入当前像素（含斑点混合色）
            byte[] bn = new byte[dn.Stride * h];
            Marshal.Copy(dn.Scan0, bn, 0, bn.Length);
            try
            {
                // V2.3.2.6：斑点大小不再放大明暗对比（blobBoost 移除），浓淡不均只由 amp 控制
                double maxF = 1.0 + amp + 1.6 * whiteLevel + 0.6 * radialLevel;
                for (int yy = 0; yy < h; yy++)
                {
                    int row2 = yy * d2.Stride, rown = yy * dn.Stride;
                    for (int xx = 0; xx < w; xx++)
                    {
                        int i2 = row2 + xx * 4;
                        byte a = b2[i2 + 3];
                        if (a == 0)
                        {
                            continue;
                        }
                        double n = bn[rown + xx * 4] / 255.0; // 0~1
                        double f = 1.0 + (n - 0.5) * 2.0 * amp;
                        if (gradAmp > 0.0)
                        {
                            double t = (xx - w / 2.0) / w * gx + (yy - h / 2.0) / h * gy;
                            f *= 1.0 - gradAmp * Math.Max(0.0, t + 0.5);
                        }
                        // 局部露白：噪声亮区显著变淡（印泥薄处接近纸色），100 档效果明显
                        if (whiteLevel > 0.004 && n > 1.0 - whiteLevel * 0.85)
                        {
                            f *= 1.0 + 1.6 * whiteLevel * (0.5 + n * 0.5);
                        }
                        // 径向压印：中心不动、边缘平滑渐淡（模拟印泥边缘薄），系数 0.6
                        if (radialLevel > 0.004)
                        {
                            double dxx = (xx - w / 2.0) / (w / 2.0);
                            double dyy = (yy - h / 2.0) / (h / 2.0);
                            double dist = Math.Sqrt(dxx * dxx + dyy * dyy);
                            if (dist > 1.0) dist = 1.0;
                            f *= 1.0 + radialLevel * dist * 0.6;
                        }
                        if (f < 0.02) f = 0.02;
                        if (f > maxF) f = maxF;
                        // 读当前像素（含斑点混合色）整体调制
                        double rr = b2[i2 + 2] * f;
                        double gg = b2[i2 + 1] * f;
                        double bb = b2[i2] * f;
                        // 色调：HSV 色相微偏（正=偏暖/橘红，负=偏冷/玫红），饱和度与明度不变
                        if (castT > 0.004)
                        {
                            HsvShift(ref rr, ref gg, ref bb, hueShift);
                        }
                        b2[i2] = (byte)Math.Max(0, Math.Min(255, (int)bb));
                        b2[i2 + 1] = (byte)Math.Max(0, Math.Min(255, (int)gg));
                        b2[i2 + 2] = (byte)Math.Max(0, Math.Min(255, (int)rr));
                        // alpha 保持（含斑点混合后的透明度）
                    }
                }
                Marshal.Copy(b2, 0, d2.Scan0, b2.Length);
            }
            finally
            {
                dst.UnlockBits(d2);
                bigNoise.UnlockBits(dn);
            }
            src.Dispose();            src.Dispose();
            bigNoise.Dispose();
            return dst;
        }

        /// <summary>盖章渲染实时预览：对章原图应用当前参数（固定种子 → 斑块分布稳定，仅强度随参数变化）。</summary>
        public static Bitmap RenderTexturePreview(Bitmap src, int seed,
            int brightness, int blob, int gradient, int white, int spot,
            int radial, int cast,
            float kb, float kblob, float kgrad, float kwhite, float kspot,
            float kradial, float kcast)
        {
            return ApplyInkTexture(src, seed, brightness, blob, gradient, white, spot, radial, cast,
                kb, kblob, kgrad, kwhite, kspot, kradial, kcast);
        }

        /// <summary>HSV 色相微偏：模拟印泥色调差异（红章偏橘=暖 / 偏玫红=冷），饱和度与明度不变。
        /// r/g/b 为 0~255 域，dh 为色相偏移角度（正=偏暖、负=偏冷）。</summary>
        private static void HsvShift(ref double r, ref double g, ref double b, double dh)
        {
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;
            double v = max;
            double s = max > 0 ? delta / max : 0.0;
            double h = 0.0;
            if (delta > 0.0001)
            {
                if (max == r) h = 60.0 * (((g - b) / delta) % 6.0);
                else if (max == g) h = 60.0 * ((b - r) / delta + 2.0);
                else h = 60.0 * ((r - g) / delta + 4.0);
                if (h < 0) h += 360.0;
            }
            h += dh;
            h %= 360.0;
            if (h < 0) h += 360.0;
            double c = v * s;
            double x = c * (1.0 - Math.Abs((h / 60.0) % 2.0 - 1.0));
            double m = v - c;
            double r1 = 0, g1 = 0, b1 = 0;
            if (h < 60) { r1 = c; g1 = x; }
            else if (h < 120) { r1 = x; g1 = c; }
            else if (h < 180) { g1 = c; b1 = x; }
            else if (h < 240) { g1 = x; b1 = c; }
            else if (h < 300) { r1 = x; b1 = c; }
            else { r1 = c; b1 = x; }
            r = r1 + m;
            g = g1 + m;
            b = b1 + m;
        }

        /// <summary>判定点是否位于章形状内部（四方向扫描均能遇到章面 alpha>0）。
        /// 用于将内部斑点限制在章内空白处，避免撒到圆章外接方框角落等章外区域。</summary>
        private static bool IsInsideStamp(byte[] b1, int stride, int x, int y,
            int minX, int minY, int maxX, int maxY)
        {
            bool left = false;
            for (int xx = x - 1; xx >= minX; xx--)
            {
                if (b1[y * stride + xx * 4 + 3] != 0) { left = true; break; }
            }
            if (!left) return false;
            bool right = false;
            for (int xx = x + 1; xx <= maxX; xx++)
            {
                if (b1[y * stride + xx * 4 + 3] != 0) { right = true; break; }
            }
            if (!right) return false;
            bool top = false;
            for (int yy = y - 1; yy >= minY; yy--)
            {
                if (b1[yy * stride + x * 4 + 3] != 0) { top = true; break; }
            }
            if (!top) return false;
            bool bottom = false;
            for (int yy = y + 1; yy <= maxY; yy++)
            {
                if (b1[yy * stride + x * 4 + 3] != 0) { bottom = true; break; }
            }
            return bottom;
        }

        /// <summary>灰度噪声 3x3 均值模糊（更柔和的低频斑块）。</summary>
        private static Bitmap BoxBlurGray(Bitmap src)
        {
            int w = src.Width, h = src.Height;
            Bitmap dst = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            System.Drawing.Imaging.BitmapData d1 = src.LockBits(new Rectangle(0, 0, w, h),
                System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            System.Drawing.Imaging.BitmapData d2 = dst.LockBits(new Rectangle(0, 0, w, h),
                System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                byte[] b1 = new byte[d1.Stride * h];
                byte[] b2 = new byte[d2.Stride * h];
                Marshal.Copy(d1.Scan0, b1, 0, b1.Length);
                for (int yy = 0; yy < h; yy++)
                {
                    for (int xx = 0; xx < w; xx++)
                    {
                        int sum = 0, cnt = 0;
                        for (int ky = -1; ky <= 1; ky++)
                        {
                            int ny = yy + ky;
                            if (ny < 0 || ny >= h) continue;
                            for (int kx = -1; kx <= 1; kx++)
                            {
                                int nx = xx + kx;
                                if (nx < 0 || nx >= w) continue;
                                sum += b1[ny * d1.Stride + nx * 4];
                                cnt++;
                            }
                        }
                        byte val = (byte)(sum / cnt);
                        int o = yy * d2.Stride + xx * 4;
                        b2[o] = val;
                        b2[o + 1] = val;
                        b2[o + 2] = val;
                        b2[o + 3] = 255;
                    }
                }
                Marshal.Copy(b2, 0, d2.Scan0, b2.Length);
            }
            finally
            {
                src.UnlockBits(d1);
                dst.UnlockBits(d2);
            }
            src.Dispose();
            return dst;
        }

        private static Bitmap RotateImg(Bitmap bitmap, int angle, bool original = true)
        {
            angle = angle % 360;
            double radian = angle * Math.PI / 180.0;
            double cos = Math.Cos(radian);
            double sin = Math.Sin(radian);
            int w = bitmap.Width;
            int h = bitmap.Height;
            int W = (int)(Math.Max(Math.Abs(w * cos - h * sin), Math.Abs(w * cos + h * sin)));
            int H = (int)(Math.Max(Math.Abs(w * sin - h * cos), Math.Abs(w * sin + h * cos)));

            if (original)
            {
                H = H * w / W;
                W = w;
            }

            Bitmap dsImage = new Bitmap(W, H);
            Graphics g = Graphics.FromImage(dsImage);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            Point Offset = new Point((W - w) / 2, (H - h) / 2);
            Rectangle rect = new Rectangle(Offset.X, Offset.Y, w, h);
            Point center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            g.TranslateTransform(center.X, center.Y);
            g.RotateTransform(angle);
            g.TranslateTransform(-center.X, -center.Y);
            g.DrawImage(bitmap, rect);
            g.ResetTransform();
            g.Dispose();
            return dsImage;
        }

        private static Bitmap[] subImages(Bitmap img, int n)
        {
            if (n < 1) return new Bitmap[0]; // V390：防御 n=0 时 (W-w1)/0 除零（段划分已跳过，此处双保险）
            Bitmap[] nImage = new Bitmap[n];
            int H = img.Height;
            int W = img.Width;
            int w1 = W / 3;
            int w = (W - w1) / n;
            n = n - 1;
            int tmpw = W;
            for (int i = 0; i <= n; i++)
            {
                int sw;
                if (i == n)
                {
                    sw = tmpw;
                }
                else if (i == 0)
                {
                    sw = w1;
                }
                else
                {
                    sw = w;
                }
                Bitmap newbitmap = new Bitmap(sw, H);
                Graphics g = Graphics.FromImage(newbitmap);
                g.DrawImage(img, new Rectangle(0, 0, sw, H), new Rectangle(W - tmpw, 0, sw, H), GraphicsUnit.Pixel);
                g.Dispose();
                nImage[i] = newbitmap;
                tmpw = tmpw - sw;
            }
            return nImage;
        }

        // ===================== 转图 / 签名 / 加密 =====================
        public static void ImageToPDF(Bitmap[] bitmaps, float bl, string trageFullName)
        {
            using (iTextSharp.text.Document document = new iTextSharp.text.Document(new iTextSharp.text.Rectangle(0, 0), 0, 0, 0, 0))
            {
                PdfWriter.GetInstance(document, new FileStream(trageFullName, FileMode.Create, FileAccess.ReadWrite));
                document.Open();
                for (int i = 0; i < bitmaps.Length; i++)
                {
                    iTextSharp.text.Image image = iTextSharp.text.Image.GetInstance(bitmaps[i], ImageFormat.Bmp);
                    float Width = image.Width * bl, Height = image.Height * bl;
                    image.ScaleToFit(Width, Height);
                    document.SetPageSize(new iTextSharp.text.Rectangle(0, 0, Width, Height));
                    document.NewPage();
                    document.Add(image);
                }
            }
        }

        /// <summary>PDF 转图片后重新生成 PDF（"合并"输出模式：盖章不可编辑）。
        /// dpi 由输出清晰度档位决定（极高300/高200/标准150/低96/极低72），默认标准 150。
        /// V2.3.2：移除数字签名/PDF密码后，直接重写原文件即可。</summary>
        public static void PDFToiPDF(string pdfPath, int dpi = 150)
        {
            if (dpi < 72) dpi = 72;
            if (dpi > 600) dpi = 600;
            float bl = 72f / dpi;
            Bitmap[] bitmaps = null;
            string renderPath = pdfPath;
            try
            {
                renderPath = PreviewPdfPreparation.CreateAnnotationFlattenedCopy(pdfPath);
                using (IPdfDocumentRenderer pdfRenderer = PdfiumDocumentRenderer.Open(renderPath))
                {
                    bitmaps = new Bitmap[pdfRenderer.PageCount];
                    for (int i = 0; i < pdfRenderer.PageCount; i++)
                    {
                        bitmaps[i] = pdfRenderer.RenderPage(i, dpi);
                    }
                }

                ImageToPDF(bitmaps, bl, pdfPath);
            }
            finally
            {
                if (bitmaps != null)
                {
                    foreach (Bitmap bitmap in bitmaps)
                    {
                        if (bitmap != null) bitmap.Dispose();
                    }
                }
                if (!string.Equals(renderPath, pdfPath, StringComparison.OrdinalIgnoreCase))
                {
                    PreviewPdfPreparation.TryDelete(renderPath);
                }
            }
        }
    }
}
