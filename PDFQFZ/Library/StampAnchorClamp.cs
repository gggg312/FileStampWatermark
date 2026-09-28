using System;

namespace PDFQFZ.Library
{
    /// <summary>
    /// V2.4.0.57：随机旋转中心补偿后锚点（章中心）钳制。
    /// 输出端 PDFWatermark 流程为：CalculateStampPosition（中心比例→左下角）→ 随机位移 → 出界钳制（左下角页内）
    /// → 随机旋转中心补偿（把左下角推出页面）→ SetAbsolutePosition。
    /// 预览端 rotate 为 transform，不改变布局位置（始终页内）。为对齐两端，补偿后需再次钳制章中心：
    /// 章不超页时中心始终在页内；章比页面大时保持原语义不钳（中心出界裁剪）。
    /// x/y 为章左下角（iText 坐标系，y 向上）。
    /// </summary>
    public static class StampAnchorClamp
    {
        /// <summary>
        /// 【已废弃 V2.4.0.64】输出端改为“内容中心 + 旋转后 bbox 钳制”（ClampCenterByBBox + AnchorFromCenter），
        /// 本方法不再被产品代码调用，仅保留供测试追溯。
        /// </summary>
        [Obsolete("V2.4.0.64 起输出端改用 ClampCenterByBBox + AnchorFromCenter（bbox 钳制），本方法仅保留供测试追溯")]
        public static void ClampAnchorAfterRotate(ref float x, ref float y, float width, float height, float pageW, float pageH)
        {
            if (width <= pageW && width > 0f)
            {
                float halfW = width / 2f;
                float cx = x + halfW;
                x = Math.Min(Math.Max(cx, halfW), pageW - halfW) - halfW;
            }
            if (height <= pageH && height > 0f)
            {
                float halfH = height / 2f;
                float cy = y + halfH;
                y = Math.Min(Math.Max(cy, halfH), pageH - halfH) - halfH;
            }
        }

        /// <summary>V2.4.0.64：旋转后包围盒（bbox）钳制章中心——预览端 stampStyle/拖拽边界与输出端 iText 共用同一套公式，
        /// 两端位置完全一致、旋转后章完整在页内。
        /// bbox：bw = w·|cosA| + h·|sinA|，bh = w·|sinA| + h·|cosA|。
        /// bbox 不大于页面时中心钳在 [bw/2, pageW−bw/2]×[bh/2, pageH−bh/2]；bbox 大于页面（章超页）时不钳（保持原语义：中心出界裁剪）。
        /// 坐标系：钳制公式与方向无关，调用方保证 pageW/pageH 与中心坐标同基准（输出端 iText pt / 预览端 CSS px）。</summary>
        public static void ClampCenterByBBox(ref float cx, ref float cy, float width, float height, float rotationDeg, float pageW, float pageH)
        {
            double rad = rotationDeg * Math.PI / 180.0;
            double cosA = Math.Cos(rad);
            double sinA = Math.Sin(rad);
            float bw = (float)(width * Math.Abs(cosA) + height * Math.Abs(sinA));
            float bh = (float)(width * Math.Abs(sinA) + height * Math.Abs(cosA));
            if (bw <= pageW && bw > 0f)
            {
                cx = Math.Min(Math.Max(cx, bw / 2f), pageW - bw / 2f);
            }
            if (bh <= pageH && bh > 0f)
            {
                cy = Math.Min(Math.Max(cy, bh / 2f), pageH - bh / 2f);
            }
        }

        /// <summary>V2.4.0.64：由章内容中心反推 iText 左下角锚点（iText 旋转绕图像左下角，y 向上）：
        /// 左下角 = 中心 − R·(w/2, h/2)，R 为旋转矩阵（y 向上逆时针）。无旋转时 = 中心 − (w/2, h/2)。
        /// 该公式与 V2.3.2.12 的“补偿保持内容中心”数学等价，供两端（输出端定位/预览端左上换算）共用。
        /// 【V2.4.0.70 已废弃】实证（ItextRotationCenterTests 渲染实测 A=0/90/30）：iTextSharp 的
        /// SetAbsolutePosition + RotationDegrees 实际是“旋转后 bbox 左下角落在锚点”（Image.GetMatrix 按象限
        /// 算 CX/CY、AddImage 用 AbsoluteX−CX 调整），不是“旋转绕未旋转图像左下角”。旧公式在 A≠0 时反推
        /// 错误，输出中心偏移（与预览 CSS 绕中心旋转不一致）。改用 AnchorFromBBoxCenter。</summary>
        [Obsolete("V2.4.0.70 起输出端改用 AnchorFromBBoxCenter（iText 旋转后 bbox 左下角落在锚点），本方法仅保留供测试追溯")]
        public static void AnchorFromCenter(float cx, float cy, float width, float height, float rotationDeg, out float x, out float y)
        {
            double rad = rotationDeg * Math.PI / 180.0;
            double cosA = Math.Cos(rad);
            double sinA = Math.Sin(rad);
            x = cx - (float)(width / 2.0 * cosA - height / 2.0 * sinA);
            y = cy - (float)(width / 2.0 * sinA + height / 2.0 * cosA);
        }

        /// <summary>V2.4.0.70：由章内容中心反推 iText 锚点——实证 iTextSharp SetAbsolutePosition(x,y)+RotationDegrees(A)
        /// 的语义是“旋转后 bbox 左下角落在 (x,y)”（见 ItextRotationCenterTests 渲染实测 + Image.GetMatrix 源码）。
        /// 锚点 = 中心 − (bw/2, bh/2)，bw/bh 为旋转后 bbox 尺寸（与 ClampCenterByBBox 同一公式，角度无关的绝对值）。
        /// A=0 时 bw=w、bh=h，与旧 AnchorFromCenter 逐位一致（零回归）。供输出端（iText 放置）定位使用。</summary>
        public static void AnchorFromBBoxCenter(float cx, float cy, float width, float height, float rotationDeg, out float x, out float y)
        {
            double rad = rotationDeg * Math.PI / 180.0;
            double cosA = Math.Cos(rad);
            double sinA = Math.Sin(rad);
            float bw = (float)(width * Math.Abs(cosA) + height * Math.Abs(sinA));
            float bh = (float)(width * Math.Abs(sinA) + height * Math.Abs(cosA));
            x = cx - bw / 2f;
            y = cy - bh / 2f;
        }
    }
}
