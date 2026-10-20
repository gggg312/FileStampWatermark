using System;
using System.Collections.Generic;
using System.Drawing;

namespace PDFQFZ.Library
{
    /// <summary>
    /// 批量处理（简化版 V1）纯计算：页码范围交集 + 百分比定位钳制。
    /// 与 PDFWatermark 放置口径保持一致（页尺寸 = iTextSharp GetPageSize + GetPageRotation 交换后的宽高）。
    /// </summary>
    public static class BatchRangeCalculator
    {
        /// <summary>
        /// 计算普通章实际盖章页列表（1-based）。
        /// rangeMode：0=所有页 1=首页 2=尾页 3=自定义（起始/结束取交集：start=max(1,rangeStart)，end=min(rangeEnd,pageCount)，start>end 返回空=跳过）。
        /// </summary>
        public static List<int> ComputePages(int rangeMode, int rangeStart, int rangeEnd, int pageCount)
        {
            var pages = new List<int>();
            if (pageCount <= 0) return pages;
            int s, e;
            switch (rangeMode)
            {
                case 1: s = 1; e = 1; break;
                case 2: s = pageCount; e = pageCount; break;
                case 3:
                    s = Math.Max(1, rangeStart);
                    e = Math.Min(rangeEnd, pageCount);
                    break;
                default: s = 1; e = pageCount; break;
            }
            if (s > e) return pages;
            for (int p = s; p <= e; p++) pages.Add(p);
            return pages;
        }

        /// <summary>
        /// 百分比定位 + 钳制：返回章中心的比例坐标（0-1）。
        /// clampInside=true：章中心钳制到 [半宽, 页面宽-半宽]×[半高, 页面高-半高]；
        ///   若章尺寸超过页面（宽或高任一大于页面）返回 null（调用方跳过该文件并记录"印章尺寸超过页面"）。
        /// clampInside=false：返回原始百分比（允许部分超出页面）。
        /// </summary>
        public static PointF? ClampCenter(float xPct, float yPct, bool clampInside,
            float imgWpt, float imgHpt, float pageW, float pageH)
        {
            if (clampInside)
            {
                if (imgWpt > pageW || imgHpt > pageH) return null;
                float halfW = imgWpt / 2f, halfH = imgHpt / 2f;
                float minX = halfW / pageW, maxX = 1f - halfW / pageW;
                float minY = halfH / pageH, maxY = 1f - halfH / pageH;
                float x = Math.Max(minX, Math.Min(maxX, xPct / 100f));
                float y = Math.Max(minY, Math.Min(maxY, yPct / 100f));
                return new PointF(x, y);
            }
            return new PointF(xPct / 100f, yPct / 100f);
        }
    }
}
