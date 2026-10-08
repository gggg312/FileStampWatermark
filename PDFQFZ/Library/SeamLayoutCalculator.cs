using System;
using System.Collections.Generic;
using System.Linq;

namespace PDFQFZ.Library
{
    /// <summary>v2.4.0.51：骑缝章预览布局计算（纯数学、无图像）。
    /// 与 StampEngine.PDFWatermark（PDFToiPDF）生成期切片算法完全一致：
    /// qfzList 构建 → 段划分（ss/sy/sys/syy/pp）→ subImages 切片宽度（首片 W/3、中间等分、末片剩余，自章右向左）
    /// → wzType 位置公式 → 旋转标记。页面坐标与章源坐标均为 0-1 比例。</summary>
    public sealed class SeamSlice
    {
        public int Page;
        public float X, Y, W, H;              // 页面内比例（旋转后目标矩形，相对该页 pt 尺寸）
        public float SrcX, SrcY, SrcW, SrcH;  // 基准章图内比例（源矩形）
        public bool Rotated;                  // wzType 0/1（下/上）：切片旋转 90°
    }

    public static class SeamLayoutCalculator
    {
        /// <summary>计算全部骑缝章页的切片布局。
        /// qfzType：0=每页、1=不加、2=奇数页、3=偶数页、4=已盖章页（stampedPages）；
        /// wzType：0=下、1=上、2=左、3=右（对齐 StampEngine 位置公式）；
        /// wzPercent 0-100；maxSplit 分割数；stampSizeMm 印章尺寸（mm）；
        /// stampW/stampH 基准章图（旋转后）像素尺寸；stampRotationScale 旋转缩放因子（xzbl，无旋转=1）；
        /// pageSize 回调返回指定页 pt 尺寸（null 时用 595×842）；pageRotation 回调返回指定页旋转角
        /// （null 时全部按 0；90/270 = 横向页，骑缝章方向按"横页竖起来盖"映射：竖右→横下、竖下→横左、
        /// 竖左→横上、竖上→横右，显示尺寸与生成端一致交换宽高，保证预览与输出一致）。</summary>
        public static List<SeamSlice> Compute(
            int pageCount,
            int qfzType,
            int wzType,
            int wzPercent,
            int maxSplit,
            float stampSizeMm,
            int stampW,
            int stampH,
            float stampRotationScale,
            IReadOnlyList<int> stampedPages,
            Func<int, (float W, float H)> pageSize = null,
            Func<int, int> pageRotation = null)
        {
            var result = new List<SeamSlice>();
            if (pageCount <= 0 || stampW <= 0 || stampH <= 0) return result;
            if (qfzType == 1) return result;                       // 不加骑缝章
            if (pageCount == 1 && qfzType != 1) return result;     // 单页跳过（SeamStampPolicy.ShouldSkipForSinglePage）

            var qfzList = new List<int>();
            if (qfzType == 0)
            {
                for (int i = 1; i <= pageCount; i++) qfzList.Add(i);
            }
            else if (qfzType == 2)
            {
                for (int i = 1; i <= pageCount; i += 2) qfzList.Add(i);
            }
            else if (qfzType == 3)
            {
                for (int i = 2; i <= pageCount; i += 2) qfzList.Add(i);
            }
            else if (qfzType == 4 && stampedPages != null)
            {
                qfzList.AddRange(stampedPages.Distinct().Where(p => p >= 1 && p <= pageCount).OrderBy(p => p));
            }

            // V390：左/上骑缝（wzType 2/1）页序反向，与 StampEngine 生成侧一致（物理：页1=章最右条）
            if (wzType == 2 || wzType == 1) qfzList.Reverse();

            int qfzPages = qfzList.Count;
            if (qfzPages <= 1) return result;   // 生成期要求 qfzPages > 1 才盖骑缝章

            int max = Math.Max(1, maxSplit);
            int ss = (qfzPages + max - 1) / max;     // 段数向上取整
            int sy = qfzPages - ss * max / 2;
            int sys = sy / ss;
            int syy = sy % ss;
            int pp = max / 2 + sys;

            // 章显示宽 pt = SizeMm * xzbl * 72 / 25.4（sfbl 推导）；每章像素 → pt：k = 章显示宽 / 章像素宽
            float k = (stampSizeMm * Math.Max(0.1f, stampRotationScale) * 72f / 25.4f) / stampW;

            int startIndex = 0;
            for (int seg = 0; seg < ss; seg++)
            {
                int tmp = pp;
                if (seg < syy) tmp++;
                if (tmp <= 0) { startIndex += tmp; continue; }   // 该段无切片（对齐生成期：段内页不盖章）

                int W = stampW, H = stampH;
                /* V1.0.0.68：均分切片（与生成端 subImages 一致——用户要求平均分布，不再首片 W/3 大、末片剩余大、中间小）；
                   超长文档整数除法归 0 时保底 1px，源矩形钳制防越界
                   V1.0.0.69：首页切片保底章宽/20——n≤20 均分天然满足；n>20 首页放大到 W/20 保证首页永远可见骑缝章，
                   其余 n-1 片均分剩余宽（余数均匀分配到前几片，避免末片吸收余数偏大）；n==1 整章 */
                if (tmp <= 1)
                {
                    // 单页段：整章（与生成端 subImages(n=1) 一致）；继续下一段不 return
                    int page1 = qfzList[startIndex];
                    float pW1, pH1;
                    if (pageSize != null) { var ps1 = pageSize(page1); pW1 = ps1.W > 0 ? ps1.W : 595f; pH1 = ps1.H > 0 ? ps1.H : 842f; }
                    else { pW1 = 595f; pH1 = 842f; }
                    int rot1 = pageRotation != null ? Math.Max(0, pageRotation(page1)) : 0;
                    bool rotLand1 = (rot1 == 90 || rot1 == 270);
                    bool land1 = rotLand1 || (!rotLand1 && pW1 > pH1);
                    int eff1 = land1 ? (wzType == 3 ? 0 : wzType == 0 ? 2 : wzType == 2 ? 1 : 3) : wzType;
                    float dW1 = rotLand1 ? pH1 : pW1, dH1 = rotLand1 ? pW1 : pH1;
                    bool rot1f = (eff1 == 0 || eff1 == 1);
                    float sW1 = rot1f ? H * k : W * k;
                    float sH1 = rot1f ? W * k : H * k;
                    float xP1, yP1;
                    if (eff1 == 3) { xP1 = dW1 - sW1; yP1 = (dH1 - sH1) * (100 - wzPercent) / 100f; }
                    else if (eff1 == 2) { xP1 = 0f; yP1 = (dH1 - sH1) * (100 - wzPercent) / 100f; }
                    else if (eff1 == 1) { xP1 = (dW1 - sW1) * wzPercent / 100f; yP1 = 0f; }
                    else { xP1 = (dW1 - sW1) * wzPercent / 100f; yP1 = dH1 - sH1; }
                    result.Add(new SeamSlice { Page = page1, X = Clamp01(xP1 / dW1), Y = Clamp01(yP1 / dH1), W = Clamp01(sW1 / dW1), H = Clamp01(sH1 / dH1), SrcX = 0f, SrcY = 0f, SrcW = 1f, SrcH = 1f, Rotated = rot1f });
                    startIndex += tmp;
                    continue;
                }
                int wAvg = W / tmp; if (wAvg < 1) wAvg = 1;
                int nFirst = W / 20; if (nFirst < 1) nFirst = 1;
                if (nFirst < wAvg) nFirst = wAvg;
                if (nFirst > W) nFirst = W;
                int wBase = (W - nFirst) / (tmp - 1); if (wBase < 1) wBase = 1;
                int rem = (W - nFirst) - wBase * (tmp - 1);
                int tmpw = W;                        // 剩余宽（自章右向左累计）

                for (int y = 0; y < tmp; y++)
                {
                    int page = qfzList[startIndex + y];
                    int sw;
                    if (y == 0) sw = nFirst;
                    else sw = wBase + ((y - 1) < rem ? 1 : 0);
                    if (sw > tmpw) sw = tmpw > 0 ? tmpw : 1;

                    float srcX = (float)(W - tmpw) / W;
                    if (srcX < 0f) srcX = 0f;
                    float srcXMax = (float)(W - sw) / W;
                    if (srcX > srcXMax) srcX = srcXMax;
                    float srcW = (float)sw / W;

                    // 页面 pt 尺寸（生成期每页各自 GetPageSize）
                    float pW, pH;
                    if (pageSize != null)
                    {
                        var ps = pageSize(page);
                        pW = ps.W > 0 ? ps.W : 595f;
                        pH = ps.H > 0 ? ps.H : 842f;
                    }
                    else
                    {
                        pW = 595f; pH = 842f;
                    }

                    // V1.0.0.32：横向页（rotation 90/270）骑缝章方向映射——与生成端 StampEngine 一致：
                    // 竖右→横下、竖下→横左、竖左→横上、竖上→横右；显示尺寸按旋转后交换宽高
                    // V1.0.0.71：横向判定补"真横版"（rotation 0/180 且宽>高，MediaBox 本身就横向的页面），
                    // 只认 rotation 时真横版不映射导致横页骑缝章也盖在右边（用户实测回归）；
                    // 真横版显示宽边即 pW（不交换），旋转横版显示宽边=原始高（交换）
                    int rot = pageRotation != null ? Math.Max(0, pageRotation(page)) : 0;
                    bool rotLand = (rot == 90 || rot == 270);
                    bool landscape = rotLand || (!rotLand && pW > pH);
                    int effWz = landscape ? (wzType == 3 ? 0 : wzType == 0 ? 2 : wzType == 2 ? 1 : 3) : wzType;
                    float dispW = rotLand ? pH : pW;
                    float dispH = rotLand ? pW : pH;

                    // 目标尺寸（pt）：不旋转（effWz 2/3）w=sw*k、h=H*k；旋转（0/1）w=H*k、h=sw*k
                    bool rotated = (effWz == 0 || effWz == 1);
                    float sliceW = rotated ? H * k : sw * k;
                    float sliceH = rotated ? sw * k : H * k;

                    float xPos, yPos;
                    if (effWz == 3)      // 右：贴右缘，纵向位置由 wzPercent
                    {
                        xPos = dispW - sliceW;
                        yPos = (dispH - sliceH) * (100 - wzPercent) / 100f;
                    }
                    else if (effWz == 2) // 左：贴左缘，纵向位置由 wzPercent
                    {
                        xPos = 0f;
                        yPos = (dispH - sliceH) * (100 - wzPercent) / 100f;
                    }
                    else if (effWz == 1) // 上：贴顶，横向位置由 wzPercent
                    {
                        xPos = (dispW - sliceW) * wzPercent / 100f;
                        yPos = 0f;
                    }
                    else                 // 下：贴底，横向位置由 wzPercent
                    {
                        xPos = (dispW - sliceW) * wzPercent / 100f;
                        yPos = dispH - sliceH;
                    }

                    result.Add(new SeamSlice
                    {
                        Page = page,
                        X = Clamp01(xPos / dispW),
                        Y = Clamp01(yPos / dispH),
                        W = Clamp01(sliceW / dispW),
                        H = Clamp01(sliceH / dispH),
                        SrcX = Clamp01(srcX),
                        SrcY = 0f,
                        SrcW = Clamp01(srcW),
                        SrcH = 1f,
                        Rotated = rotated
                    });

                    tmpw -= sw;
                }
                startIndex += tmp;
            }
            return result;
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}
