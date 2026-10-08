using System;
using System.IO;
using System.Text;

namespace PDFQFZ.WebShell.Services
{
    /// <summary>
    /// 统一运行日志 app_log.log（EXE 同目录）。
    /// 文件内固定保留最近两次运行：一段「上次运行」+ 一段「本次运行」；
    /// 每次启动轮转一次——丢弃最早那段（即覆盖「最早的那一次」），把旧「本次」段升格为「上次」段，再开启新的「本次」段。
    /// 目的：用户复现 BUG 后只需提供这一个文件；重启软件后上一份仍保留在文件内，不会丢失；文件永远只有两段，不会越积越大。
    /// </summary>
    public static class AppLog
    {
        public const string FileName = "app_log.log";
        private static readonly object _lock = new object();
        private static string _path = "";

        public static string LogPath
        {
            get
            {
                lock (_lock)
                {
                    if (string.IsNullOrEmpty(_path))
                        _path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
                    return _path;
                }
            }
        }

        /// <summary>启动时调用（须尽可能早）：轮转旧文件并开启新「本次运行」段。</summary>
        public static void Init()
        {
            lock (_lock)
            {
                _path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
                string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                try
                {
                    if (File.Exists(_path))
                    {
                        string all = File.ReadAllText(_path, Encoding.UTF8);
                        string prevTime; string prevContent;
                        SplitLatestRun(all, out prevTime, out prevContent);
                        if (string.IsNullOrEmpty(prevTime)) prevTime = now;
                        string head = "===== 上次运行 " + prevTime + " =====" + Environment.NewLine
                                    + (prevContent.Length > 0 ? prevContent + Environment.NewLine : "")
                                    + "===== 本次运行 " + now + " =====" + Environment.NewLine;
                        File.WriteAllText(_path, head, Encoding.UTF8);
                        return;
                    }
                }
                catch { /* 读/轮转失败则直接重建新段 */ }
                try
                {
                    File.WriteAllText(_path, "===== 本次运行 " + now + " =====" + Environment.NewLine, Encoding.UTF8);
                }
                catch { }
            }
        }

        /// <summary>从旧文件全文中提取「最后一次运行」段（标记时间 + 内容），更早的段全部丢弃。</summary>
        private static void SplitLatestRun(string all, out string prevTime, out string prevContent)
        {
            prevTime = ""; prevContent = "";
            if (string.IsNullOrEmpty(all)) return;
            // 段标记行：===== 本次运行 yyyy-MM-dd HH:mm:ss ===== 或 ===== 上次运行 ... =====
            int idx = 0, lastStart = -1;
            while (idx < all.Length)
            {
                int i = all.IndexOf("===== ", idx, StringComparison.Ordinal);
                if (i < 0) break;
                int j = all.IndexOf("运行 ", i, StringComparison.Ordinal);
                if (j < 0) { idx = i + 5; continue; }
                int lineEnd = all.IndexOf('\n', j);
                if (lineEnd < 0) break;
                string line = all.Substring(i, lineEnd - i).Trim();
                if (line.StartsWith("===== 本次运行 ") || line.StartsWith("===== 上次运行 "))
                {
                    lastStart = i;
                    prevTime = line.Substring(line.IndexOf(' ') + 1).TrimEnd('=', ' ', '\r');
                    prevTime = prevTime.Trim();
                }
                idx = lineEnd + 1;
            }
            if (lastStart < 0) { prevContent = all.TrimEnd(); return; }
            int nl = all.IndexOf('\n', lastStart);
            prevContent = nl >= 0 ? all.Substring(nl + 1) : "";
            prevContent = prevContent.TrimEnd('\r', '\n');
        }

        /// <summary>追加一行日志（线程安全；FileShare.ReadWrite 防前端/壳层并发写丢行）。</summary>
        public static void Write(string msg)
        {
            lock (_lock)
            {
                try
                {
                    using (var fs = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var sw = new StreamWriter(fs, new UTF8Encoding(false)))
                    {
                        sw.Write(DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + Environment.NewLine);
                    }
                }
                catch { }
            }
        }
    }
}
