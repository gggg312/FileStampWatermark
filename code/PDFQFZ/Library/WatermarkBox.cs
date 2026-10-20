using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PDFQFZ.Library
{
    /// <summary>
    /// 文字水印框模型（V2.4.0.96 新增，移植自 WPF「GG印章水印助手」Library\WatermarkBoxModel.cs）。
    /// 语义对齐 WPF：X/Y 为框左上角相对页面比例（0~1，Y 向下为正、屏幕系），W/H 为框宽高比例；
    /// 字号 = 框高 × FontScale（默认 0.8，超宽按最长行收窄）；Rotation 屏幕系顺时针为正（输出端取反，见 StampEngine）。
    /// 文本多行用 '\n' 分隔；Align 0=左 / 1=中 / 2=右；LetterSpacing/LineSpacing 为百分比（-150~300）。
    /// </summary>
    public sealed class WatermarkBox
    {
        public long Id;
        public string DocumentPath;   // 所属文档完整路径（按文档隔离，目录模式逐文件独立）
        public int Page;              // 1-based 页号

        // ---- 几何（相对页面 0~1，Y 向下） ----
        public float X, Y, W, H;      // 框左上角 + 宽高
        public float Rotation;        // -180~180，屏幕系顺时针为正

        // ---- 文字参数（对齐 WPF WatermarkBoxModel） ----
        public string Text;           // 水印文字（支持 \n 多行）
        public string FontName;       // 字体名（微软雅黑 / 宋体 / 黑体 / 楷体 等）
        public int ColorArgb;         // 0xAARRGGBB
        public int Opacity;           // 0~100
        public bool Bold;
        public bool Italic;
        public bool Underline;
        public bool Strike;
        public int LetterSpacing;     // %（-150~300）
        public int LineSpacing;       // %（-150~300）
        public int Align;             // 0 左 / 1 中 / 2 右
        public float FontScale;       // 字号 = 框高 × FontScale（WPF 默认 0.8）
        public float H0;              // 初始框高（相对值），>0 时 fs = H0*pageH*FontScale，否则用 H（V339 起仅作 fallback）
        public float FsToS;           // V344: 字号/图片短边比例（前端预览字号px ÷ 图片短边px）——图片水印字号唯一缩放源，fs = FsToS*min(imgW,imgH)
        public List<string> WrapLines; // V303: 快照方案——前端测量的换行结果（每行的文字）
        public List<float> WrapLineWidths; // V315: 前端测量的每行宽度比例（行宽/框宽）——保证水平方向完全一致
        public List<float> WrapLineTopRatios; // V323: 前端测量的每行高度比例（行高/框高）——保证垂直方向完全一致

        public WatermarkBox Clone()
        {
            return new WatermarkBox
            {
                Id = Id,
                DocumentPath = DocumentPath,
                Page = Page,
                X = X, Y = Y, W = W, H = H,
                Rotation = Rotation,
                Text = Text,
                FontName = FontName,
                ColorArgb = ColorArgb,
                Opacity = Opacity,
                Bold = Bold,
                Italic = Italic,
                Underline = Underline,
                Strike = Strike,
                LetterSpacing = LetterSpacing,
                LineSpacing = LineSpacing,
                Align = Align,
                FontScale = FontScale,
                H0 = H0,
                FsToS = FsToS,
                WrapLines = WrapLines == null ? null : new List<string>(WrapLines),
                WrapLineWidths = WrapLineWidths == null ? null : new List<float>(WrapLineWidths),
                WrapLineTopRatios = WrapLineTopRatios == null ? null : new List<float>(WrapLineTopRatios)
            };
        }

        public void MoveTo(float x, float y) { X = x; Y = y; }

        /// <summary>几何更新（拖拽/缩放/旋转统一入口，对齐 StampPlacement.MoveTo/ResizeTo 语义）。</summary>
        public void UpdateGeometry(float x, float y, float w, float h, float rotation)
        {
            X = x; Y = y; W = w; H = h; Rotation = rotation;
        }
    }

    /// <summary>
    /// 文字水印集合（V2.4.0.96 新增）：按 文档路径+页 隔离存储（对齐 StampPlacementCollection 模式）。
    /// 线程安全（生成后台 Task 与 UI 操作并发快照/遍历）。
    /// </summary>
    public sealed class WatermarkBoxCollection
    {
        private readonly object _lock = new object();
        private readonly List<WatermarkBox> _list = new List<WatermarkBox>();
        private long _nextId = 1;

        public int Count { get { lock (_lock) { return _list.Count; } } }

        /// <summary>文档规范化路径比较（大小写/分隔符统一，与章集合同口径，防目录模式重复加载污染）。</summary>
        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            try { return Path.GetFullPath(path); }
            catch { return path; }
        }

        /// <summary>新增水印框（默认参数由桥层传入，此处只分配 Id）。</summary>
        public WatermarkBox Add(WatermarkBox box)
        {
            if (box == null) return null;
            lock (_lock)
            {
                box.Id = _nextId++;
                box.DocumentPath = Normalize(box.DocumentPath);
                _list.Add(box);
                return box;
            }
        }

        public WatermarkBox Find(long id)
        {
            lock (_lock) { return _list.FirstOrDefault(b => b.Id == id); }
        }

        public IEnumerable<WatermarkBox> ForPage(string docPath, int page)
        {
            string dp = Normalize(docPath);
            lock (_lock)
            {
                return _list.Where(b => string.Equals(b.DocumentPath, dp, StringComparison.OrdinalIgnoreCase)
                                        && b.Page == page).Select(b => b.Clone()).ToList();
            }
        }

        public IEnumerable<WatermarkBox> ForDocument(string docPath)
        {
            string dp = Normalize(docPath);
            lock (_lock)
            {
                return _list.Where(b => string.Equals(b.DocumentPath, dp, StringComparison.OrdinalIgnoreCase))
                            .Select(b => b.Clone()).ToList();
            }
        }

        /// <summary>锁内更新（几何/参数统一入口；mutate 对副本执行后替换，避免外部对象跨线程暴露）。</summary>
        public bool Update(long id, Action<WatermarkBox> mutate)
        {
            lock (_lock)
            {
                int i = _list.FindIndex(b => b.Id == id);
                if (i < 0) return false;
                var clone = _list[i].Clone();
                mutate(clone);
                clone.DocumentPath = Normalize(clone.DocumentPath);
                _list[i] = clone;
                return true;
            }
        }

        public bool Remove(long id)
        {
            lock (_lock) { return _list.RemoveAll(b => b.Id == id) > 0; }
        }

        public int RemovePage(string docPath, int page)
        {
            string dp = Normalize(docPath);
            lock (_lock) { return _list.RemoveAll(b => string.Equals(b.DocumentPath, dp, StringComparison.OrdinalIgnoreCase) && b.Page == page); }
        }

        /// <summary>清空指定文档全部水印（重新加载文件夹/换文档时调用）。</summary>
        public int Clear(string docPath)
        {
            string dp = Normalize(docPath);
            lock (_lock) { return _list.RemoveAll(b => string.Equals(b.DocumentPath, dp, StringComparison.OrdinalIgnoreCase)); }
        }

        public int ClearAll()
        {
            lock (_lock) { int n = _list.Count; _list.Clear(); return n; }
        }

        /// <summary>全量快照（生成后台 Task 只读快照；副本与文档解耦）。</summary>
        public List<WatermarkBox> Snapshot()
        {
            lock (_lock) { return _list.Select(b => b.Clone()).ToList(); }
        }

        /// <summary>从快照重建（方案应用/加载用：新 Id、保留文档路径与页）。</summary>
        public void Import(IEnumerable<WatermarkBox> boxes)
        {
            if (boxes == null) return;
            lock (_lock)
            {
                foreach (var b in boxes)
                {
                    var c = b.Clone();
                    c.Id = _nextId++;
                    c.DocumentPath = Normalize(c.DocumentPath);
                    _list.Add(c);
                }
            }
        }
    }
}
