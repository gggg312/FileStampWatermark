using PDFQFZ.Library;

namespace PDFQFZ.WPF.Services
{
    /// <summary>
    /// 后台盖章批次参数快照（V2.3.2.11 独立化）。全部字段必须在 UI 线程收集完毕，
    /// 传给 StampBatchWorker 后不得再读任何控件；新增输出选项时在此加字段并在 UI 线程赋值。
    /// </summary>
    internal sealed class StampBatchRequest
    {
        public StampOptions Options { get; set; }
        public string Source { get; set; }
        public string OutDir { get; set; }
        /// <summary>合并=1 / 叠加=0。</summary>
        public int DjType { get; set; }
        /// <summary>合并模式输出 DPI（合并转图片栅格化用）。</summary>
        public int QualityDpi { get; set; }
        public OutputNamingOptions NamingOptions { get; set; }
        public bool DirMode { get; set; }
    }
}
