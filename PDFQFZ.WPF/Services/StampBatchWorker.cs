using System;
using System.Drawing;
using System.IO;
using System.Linq;
using PDFQFZ.Library;

namespace PDFQFZ.WPF.Services
{
    /// <summary>
    /// 后台批量盖章执行器（V2.3.2.11 独立化）。
    /// 规则：本类运行在 Task.Run 后台线程，禁止引用任何 WPF 控件（Visual/UIElement/DispatcherObject 属性），
    /// 全部输入通过 StampBatchRequest 快照传入，输出通过回调上抛，从编译层面杜绝跨线程访问 UI。
    /// </summary>
    internal static class StampBatchWorker
    {
        public static bool Run(StampBatchRequest request, Bitmap seamImage, float xzbl,
            Action<string, bool> onLog, Action<string> onProgress, Action<bool> onSaving)
        {
            bool hasFailures = false;
            try
            {
                if (request.DirMode)
                {
                    DirectoryInfo dir = new DirectoryInfo(request.Source);
                    var targets = dir.GetFiles("*.pdf", SearchOption.AllDirectories)
                        .Where(f => !string.Equals(f.DirectoryName, request.OutDir, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    int total = targets.Count;
                    int done = 0;
                    foreach (var fileInfo in targets)
                    {
                        done++;
                        string source = fileInfo.FullName;
                        string output = OutputFileNamingPolicy.GetNextOutputPath(request.OutDir, source, request.NamingOptions);
                        onLog(string.Format("正在处理第 {0}/{1} 个文件：{2}", done, total, fileInfo.Name), false);
                        bool success = StampEngine.PDFWatermark(request.Options, seamImage, xzbl,
                            source, output, source,
                            msg => onLog(msg, false),
                            (d, t) => onProgress(string.Format("正在盖章中：已完成 {0}/{1} 页（{2}%），文件：{3}",
                                d, t, t > 0 ? (int)Math.Round(100.0 * d / t) : 0, fileInfo.Name)),
                            active => { if (active) onSaving(true); });
                        if (success && request.DjType == 1)
                        {
                            StampEngine.PDFToiPDF(output, request.QualityDpi);
                        }
                        if (success)
                        {
                            onLog(OutputFileNamingPolicy.BuildSuccessMessage(fileInfo.Name, output), false);
                        }
                        else
                        {
                            hasFailures = true;
                            onLog("失败！“" + fileInfo.Name + "”盖章失败！", true);
                        }
                    }
                }
                else
                {
                    string[] fileArray = request.Source.Split(',');
                    int total = fileArray.Length;
                    int done = 0;
                    foreach (string file in fileArray)
                    {
                        done++;
                        string filename = Path.GetFileName(file);
                        string output = OutputFileNamingPolicy.GetNextOutputPath(request.OutDir, file, request.NamingOptions);
                        onLog(string.Format("正在处理第 {0}/{1} 个文件：{2}", done, total, filename), false);
                        bool success = StampEngine.PDFWatermark(request.Options, seamImage, xzbl,
                            file, output, file,
                            msg => onLog(msg, false),
                            (d, t) => onProgress(string.Format("正在盖章中：已完成 {0}/{1} 页（{2}%），文件：{3}",
                                d, t, t > 0 ? (int)Math.Round(100.0 * d / t) : 0, filename)),
                            active => { if (active) onSaving(true); });
                        if (success)
                        {
                            if (request.DjType == 1)
                            {
                                StampEngine.PDFToiPDF(output, request.QualityDpi);
                            }
                            onLog(OutputFileNamingPolicy.BuildSuccessMessage(filename, output), false);
                        }
                        else
                        {
                            hasFailures = true;
                            onLog("失败！“" + filename + "”盖章失败！", true);
                        }
                    }
                }
                return hasFailures;
            }
            catch (Exception ex)
            {
                onLog("处理过程中发生错误：" + ex.Message, true);
                // V2.3.2.11：后台异常写独立诊断日志，便于现场排查（与界面日志/布局日志分离）
                try
                {
                    File.AppendAllText(
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "worker_diag.log"),
                        "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + ex + Environment.NewLine);
                }
                catch { }
                return true;
            }
        }
    }
}
