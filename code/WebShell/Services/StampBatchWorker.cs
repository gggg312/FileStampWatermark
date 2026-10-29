using System;
using System.Collections.Generic;
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
                        onLog(string.Format("正在处理第 {0}/{1} 个文件：《{2}》", done, total, fileInfo.Name), false); // V1.0.0.79：文件名《》包裹（与 batch-progress 前端日志一致）
                        int curDone = done; int curTotal = total; string curName = fileInfo.Name; /* V1.0.0.78：写盘/图片进度闭包引用（foreach 变量在 lambda 内延迟求值，需局部副本） */
                        bool success = StampEngine.PDFWatermark(request.Options, seamImage, xzbl,
                            source, output, source,
                            msg => onLog(msg, false),
                            (d, t) => onProgress(string.Format("正在盖章中：已完成 {0}/{1} 页（{2}%），文件：《{3}》",
                                d, t, t > 0 ? (int)Math.Round(100.0 * d / t) : 0, fileInfo.Name)), // V1.0.0.78：文件名《》包裹
                            active => { if (active) onProgress(string.Format("文件生成中：第 {0}/{1} 个文件，请勿关闭软件", curDone, curTotal)); }); // V1.0.0.79：去掉文本省略号（动态点点由前端动画），文件夹模式带文件序号（方案B）
                        // V1.0.0.47：输出格式多选分流（pdf/jpg/png 可同选各输出各的；图片文件夹名带格式标识）
                        var fmtsDir = StampEngine.ParseOutFormats(request.OutFormat);
                        bool wantPdfDir = fmtsDir.Contains("pdf");
                        int imgOutCount = -1; var imgLogsDir = new List<string>();
                        if (success)
                        {
                            if (wantPdfDir && request.DjType == 1) StampEngine.PDFToiPDF(output, request.QualityDpi);
                            foreach (var fmt in fmtsDir)
                            {
                                if (fmt == "pdf") continue;
                                var imgs = StampEngine.PDFToImages(output, request.OutDir, Path.GetFileNameWithoutExtension(output), request.QualityDpi, fmt,
                                    (d, t) => onProgress(string.Format("正在输出图片：已完成 {0}/{1} 张（{2}%），文件：《{3}》", d, t, t > 0 ? (int)Math.Round(100.0 * d / t) : 0, curName))); // V1.0.0.78：图片输出逐张百分比（真实可算）+ 文件名《》包裹
                                if (imgs.Count > 0) { imgOutCount = (imgOutCount < 0 ? 0 : imgOutCount) + imgs.Count; imgLogsDir.Add(string.Format("已输出 {0} 张 {1} 图片（文件夹：{2}）", imgs.Count, fmt.ToUpperInvariant(), Path.GetFileNameWithoutExtension(output) + "_" + fmt.ToUpperInvariant())); }
                            }
                            if (imgOutCount >= 0 && !wantPdfDir) { try { File.Delete(output); } catch { } }
                        }
                        if (success)
                        {
                            if (wantPdfDir) onLog(OutputFileNamingPolicy.BuildSuccessMessage(fileInfo.Name, output), false);
                            foreach (var lg in imgLogsDir) onLog(string.Format("成功！《{0}》{1}", fileInfo.Name, lg), false); // V1.0.0.78：文件名《》统一
                        }
                        else
                        {
                            hasFailures = true;
                            onLog("失败！《" + fileInfo.Name + "》盖章失败！", true); // V1.0.0.79：文件名《》统一（原“”中文引号）
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
                        onLog(string.Format("正在处理第 {0}/{1} 个文件：《{2}》", done, total, filename), false); // V1.0.0.79：单文件与文件夹模式日志一致（第 1/1 个文件 + 《》包裹）
                        int lastTotal = 0; /* V1.0.0.79：单文件写盘提示带页数——从盖章进度回调捕获最终页数（写盘发生在盖章完成后，闭包局部副本防 foreach 延迟求值） */
                        bool success = StampEngine.PDFWatermark(request.Options, seamImage, xzbl,
                            file, output, file,
                            msg => onLog(msg, false),
                            (d, t) => { lastTotal = t; onProgress(string.Format("正在盖章中：已完成 {0}/{1} 页（{2}%），文件：《{3}》",
                                d, t, t > 0 ? (int)Math.Round(100.0 * d / t) : 0, filename)); }, // V1.0.0.78：文件名《》包裹
                            active => { if (active) onProgress("正在处理第 1/1 个文件：《" + filename + "》，" + (lastTotal > 0 ? "文件（" + lastTotal + "页）生成中，请勿关闭软件" : "文件生成中，请勿关闭软件")); }); // V1.0.0.81：savingIndicator(true) 恰在盖章100%后、写盘Close前——实时推送「正在处理第 1/1 个文件」+ 写盘防卡死提示合并为一条（动态点点由前端 genMerging 动画）
                        // V1.0.0.47：输出格式多选分流（单文件模式同目录模式一致）
                        var fmtsFile = StampEngine.ParseOutFormats(request.OutFormat);
                        bool wantPdfFile = fmtsFile.Contains("pdf");
                        int imgOutCount = -1; var imgLogsFile = new List<string>();
                        if (success)
                        {
                            if (wantPdfFile && request.DjType == 1) StampEngine.PDFToiPDF(output, request.QualityDpi);
                            foreach (var fmt in fmtsFile)
                            {
                                if (fmt == "pdf") continue;
                                var imgs = StampEngine.PDFToImages(output, request.OutDir, Path.GetFileNameWithoutExtension(output), request.QualityDpi, fmt,
                                    (d, t) => onProgress(string.Format("正在输出图片：已完成 {0}/{1} 张（{2}%），文件：《{3}》", d, t, t > 0 ? (int)Math.Round(100.0 * d / t) : 0, filename))); // V1.0.0.78：图片输出逐张百分比 + 文件名《》包裹
                                if (imgs.Count > 0) { imgOutCount = (imgOutCount < 0 ? 0 : imgOutCount) + imgs.Count; imgLogsFile.Add(string.Format("已输出 {0} 张 {1} 图片（文件夹：{2}）", imgs.Count, fmt.ToUpperInvariant(), Path.GetFileNameWithoutExtension(output) + "_" + fmt.ToUpperInvariant())); }
                            }
                            if (imgOutCount >= 0 && !wantPdfFile) { try { File.Delete(output); } catch { } }
                        }
                        if (success)
                        {
                            if (wantPdfFile) onLog(OutputFileNamingPolicy.BuildSuccessMessage(filename, output), false);
                            foreach (var lg in imgLogsFile) onLog(string.Format("成功！《{0}》{1}", filename, lg), false); // V1.0.0.78：文件名《》统一
                        }
                        else
                        {
                            hasFailures = true;
                            onLog("失败！《" + filename + "》盖章失败！", true); // V1.0.0.79：文件名《》统一
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
