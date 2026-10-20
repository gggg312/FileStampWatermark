// V2.4.0.21：Win32 子类化 OLE 拖放——在 WebView2 的 HWND 上注册自己的 IDropTarget（替换 WebView2 内部拖放目标），
// drop 时从 IDataObject 解析 CF_HDROP 拿真实系统路径（文件+文件夹），彻底解决"预览区拖入拿不到路径"。
// 设计：只接受文件/文件夹拖放（CF_HDROP），其他类型（文字/链接/图片等 OLE 格式）DragEnter 返回 DROPEFFECT_NONE 拒绝——
// 用户场景只需 PDF 文件与含 PDF 的文件夹（未来图片也是文件，HDROP 通道天然支持），无需转发给 WebView2 原拖放目标。
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace PDFQFZ.WebShell.Services
{
    // ============ Win32 OLE 接口与结构 ============
    // 采用 [ComImport] 接口 + ComVisible 类（WinForms 官方 OleDropTarget 同款方案，CLR 原生支持暴露，无需类型库注册）。
    // 注意：接口必须 public（CCW 需对 COM 暴露）；普通 ComVisible 接口的 CCW 暴露需要类型库注册，否则
    // RegisterDragDrop 返回 CLASS_E_CLASSNOTAVAILABLE (0x80040101)。
    [ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDropTarget
    {
        [PreserveSig] int DragEnter([In] IntPtr pDataObj, [In] uint grfKeyState, [In] POINTL pt, [Out] out int pdwEffect);
        [PreserveSig] int DragOver([In] uint grfKeyState, [In] POINTL pt, [Out] out int pdwEffect);
        [PreserveSig] int DragLeave();
        [PreserveSig] int Drop([In] IntPtr pDataObj, [In] uint grfKeyState, [In] POINTL pt, [Out] out int pdwEffect);
    }

    [ComImport, Guid("0000010E-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDataObject
    {
        [PreserveSig] int GetData(ref FORMATETC pFormatetc, out STGMEDIUM pMedium);
        [PreserveSig] int GetDataHere(ref FORMATETC pFormatetc, ref STGMEDIUM pMedium);
        [PreserveSig] int QueryGetData(ref FORMATETC pFormatetc);
        [PreserveSig] int GetCanonicalFormatEtc(ref FORMATETC pformatetc, out FORMATETC pformatetcOut);
        [PreserveSig] int SetData(ref FORMATETC pformatetc, ref STGMEDIUM pmedium, int fRelease);
        [PreserveSig] int EnumFormatEtc(uint dwDirection, out IntPtr ppenumFormatEtc);
        [PreserveSig] int DAdvise(ref FORMATETC pformatetc, uint advf, IntPtr pAdvSink, out uint pdwConnection);
        [PreserveSig] int DUnadvise(uint dwConnection);
        [PreserveSig] int EnumDAdvise(out IntPtr ppenumAdvise);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINTL
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FORMATETC
    {
        public short cfFormat;
        public IntPtr ptd;
        public uint dwAspect;
        public int lindex;
        public uint tymed;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct STGMEDIUM
    {
        public uint tymed;
        public IntPtr hGlobal;
        public IntPtr pUnkForRelease;
    }

    /// <summary>V2.4.0.21：OLE 拖放目标（IDropTarget 实现）。只接受文件/文件夹（CF_HDROP），Drop 时解析真实路径回调。</summary>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public class OleDropTarget : IDropTarget
    {
        private const int CF_HDROP = 15;
        private const uint DVASPECT_CONTENT = 1;
        private const uint TYMED_HGLOBAL = 1;
        private const uint DROPEFFECT_NONE = 0;
        private const uint DROPEFFECT_COPY = 1;

        private readonly Action<string[]> _onDrop;
        private readonly Action _onDragEnter;
        private readonly Action _onDragLeave;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder lpszFile, uint cch);
        [DllImport("ole32.dll")]
        private static extern int ReleaseStgMedium(ref STGMEDIUM pmedium);

        public OleDropTarget(Action<string[]> onDrop, Action onDragEnter = null, Action onDragLeave = null)
        {
            _onDrop = onDrop ?? throw new ArgumentNullException(nameof(onDrop));
            _onDragEnter = onDragEnter;
            _onDragLeave = onDragLeave;
        }

        private static bool HasFiles(IntPtr pDataObj)
        {
            if (pDataObj == IntPtr.Zero) return false;
            try
            {
                var obj = (IDataObject)Marshal.GetObjectForIUnknown(pDataObj);
                var fe = new FORMATETC { cfFormat = CF_HDROP, dwAspect = DVASPECT_CONTENT, lindex = -1, tymed = TYMED_HGLOBAL };
                return obj.QueryGetData(ref fe) == 0;
            }
            catch { return false; }
        }

        private static string[] ExtractFiles(IntPtr pDataObj)
        {
            if (pDataObj == IntPtr.Zero) return null;
            STGMEDIUM med;
            try
            {
                var obj = (IDataObject)Marshal.GetObjectForIUnknown(pDataObj);
                var fe = new FORMATETC { cfFormat = CF_HDROP, dwAspect = DVASPECT_CONTENT, lindex = -1, tymed = TYMED_HGLOBAL };
                int hr = obj.GetData(ref fe, out med);
                if (hr != 0 || med.hGlobal == IntPtr.Zero) return null;
            }
            catch { return null; }
            var list = new List<string>();
            try
            {
                IntPtr hDrop = med.hGlobal;
                uint n = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
                for (uint i = 0; i < n; i++)
                {
                    uint len = DragQueryFile(hDrop, i, null, 0);
                    if (len == 0) continue;
                    var sb = new StringBuilder((int)len + 1);
                    DragQueryFile(hDrop, i, sb, len + 1);
                    list.Add(sb.ToString());
                }
            }
            catch { }
            finally
            {
                try { ReleaseStgMedium(ref med); } catch { }
            }
            return list.Count > 0 ? list.ToArray() : null;
        }

        public int DragEnter(IntPtr pDataObj, uint grfKeyState, POINTL pt, out int pdwEffect)
        {
            bool ok = HasFiles(pDataObj);
            pdwEffect = ok ? (int)DROPEFFECT_COPY : (int)DROPEFFECT_NONE;
            if (ok) { try { _onDragEnter?.Invoke(); } catch { } }
            return 0;
        }

        public int DragOver(uint grfKeyState, POINTL pt, out int pdwEffect)
        {
            // 文件拖放期间持续显示"复制"光标（只有能 drop 时才显示，先按 COPY——因 DragEnter 已确认是文件）
            pdwEffect = (int)DROPEFFECT_COPY;
            return 0;
        }

        public int DragLeave()
        {
            try { _onDragLeave?.Invoke(); } catch { }
            return 0;
        }

        public int Drop(IntPtr pDataObj, uint grfKeyState, POINTL pt, out int pdwEffect)
        {
            pdwEffect = (int)DROPEFFECT_COPY;
            try
            {
                string[] files = ExtractFiles(pDataObj);
                if (files != null && files.Length > 0) _onDrop(files);
            }
            catch { }
            try { _onDragLeave?.Invoke(); } catch { }
            return 0;
        }
    }

    /// <summary>V2.4.0.21：OLE 拖放注册辅助。注意：OLE 没有"查询当前注册拖放目标"的公开 API（OleGetDragDrop 不存在），
    /// 注册成功与否以 RegisterDragDrop 返回 hr==0 为准；"导航后重注册"为幂等自愈（成功即替换原目标）。
    /// WPF 应用不会自动 OleInitialize（WinForms 才自动）——OLE 拖放系统未初始化时 RegisterDragDrop 返回
    /// REGDB_E_IIDNOTREG (0x80040155)；静态构造里初始化一次（幂等，S_FALSE=已初始化）。
    /// V2.4.0.21b：WebView2 内容实际渲染在 host 的子窗口（Chromium render widget），拖放落在子窗口时 OLE 命中
    /// 子窗口的 WebView2 内部目标——必须枚举整棵后代窗口树全部 Revoke+Register（完全接管）。</summary>
    public static class OleDragDrop
    {
        [DllImport("ole32.dll")]
        private static extern int OleInitialize(IntPtr pvReserved);
        [DllImport("ole32.dll")]
        private static extern void OleUninitialize();
        [DllImport("ole32.dll")]
        private static extern int RegisterDragDrop(IntPtr hwnd, IDropTarget pDropTarget);
        [DllImport("ole32.dll")]
        private static extern int RevokeDragDrop(IntPtr hwnd);
        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        static OleDragDrop()
        {
            try { OleInitialize(IntPtr.Zero); } catch { }
        }

        /// <summary>OleInitialize 结果（S_OK=0 首次成功；S_FALSE=1 已初始化；负数=失败）。</summary>
        public static int InitializeResult()
        {
            try { return OleInitialize(IntPtr.Zero); }
            catch { return -1; }
        }

        /// <summary>注册单个窗口（完全接管）：先 RevokeDragDrop 移除现有目标，再注册我们的。
        /// 实测：对已注册拖放目标的窗口直接 RegisterDragDrop 返回 DRAGDROP_E_INVALIDHWND (0x80040101)；
        /// RevokeDragDrop 无注册时返回 DRAGDROP_E_NOTREGISTERED (0x80040103)，忽略。</summary>
        public static int Register(IntPtr hwnd, OleDropTarget target)
        {
            if (hwnd == IntPtr.Zero || target == null) return -1;
            try { RevokeDragDrop(hwnd); } catch { }
            return RegisterDragDrop(hwnd, target);
        }

        /// <summary>注册窗口及其全部后代子窗口（WebView2 内容区在子窗口，需整树接管）。返回注册的窗口数。</summary>
        public static int RegisterTree(IntPtr rootHwnd, OleDropTarget target)
        {
            var wins = new System.Collections.Generic.List<IntPtr>();
            if (rootHwnd != IntPtr.Zero)
            {
                wins.Add(rootHwnd);
                var queue = new System.Collections.Generic.Queue<IntPtr>();
                queue.Enqueue(rootHwnd);
                var seen = new System.Collections.Generic.HashSet<IntPtr>();
                seen.Add(rootHwnd);
                while (queue.Count > 0)
                {
                    IntPtr cur = queue.Dequeue();
                    EnumChildWindows(cur, (h, l) =>
                    {
                        if (h != IntPtr.Zero && seen.Add(h)) { wins.Add(h); queue.Enqueue(h); }
                        return true;
                    }, IntPtr.Zero);
                }
            }
            int ok = 0;
            foreach (var w in wins)
            {
                if (Register(w, target) == 0) ok++;
            }
            return ok;
        }

        public static int Revoke(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return -1;
            return RevokeDragDrop(hwnd);
        }
    }
}
