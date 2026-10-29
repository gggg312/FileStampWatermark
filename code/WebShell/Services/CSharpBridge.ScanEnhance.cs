using System;
using System.IO;
using System.Web.Script.Serialization;
using PDFQFZ.WPF.Services;

namespace PDFQFZ.WebShell.Services
{
    public partial class CSharpBridge
    {
        /// <summary>
        /// 三功能集成·阶段2：扫描件优化参数同步（作用于当前预览）。
        /// 参数 json 为 ScanEnhanceParams（各强度 0-100）；空/null 或全 0 = 关闭扫描优化（预览恢复原样）。
        /// 更新后并入 _scanEnhanceKey 使 RenderPage 缓存失效。返回 "ok"。
        /// </summary>
        public string ScanEnhanceSyncParams(string json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json))
                {
                    _scanEnhanceParams = null; _scanEnhanceKey = "";
                }
                else
                {
                    ScanEnhanceParams p = new JavaScriptSerializer().Deserialize<ScanEnhanceParams>(json) ?? new ScanEnhanceParams();
                    // V1.2.1.39：Deskew 有效范围 -100~+100，负值（向左纠偏）是有效参数；全零判断须用 ==0 而非 <=0，否则负值纠偏会被误清空导致预览无效果
                    if (p.Deskew == 0 && p.Crop <= 0 && p.DeTone <= 0 && p.Contrast <= 0 && !p.EnhEn)
                    {
                        _scanEnhanceParams = null; _scanEnhanceKey = "";
                    }
                    else
                    {
                        _scanEnhanceParams = p;
                        _scanEnhanceKey = p.Deskew + "|" + p.Crop + "|" + p.DeTone + "|" + p.Contrast + "|" + p.DeToneTh + "|"
                            + p.EnhEn + "|" + p.EnhSmooth + "|" + p.EnhSens + "|" + p.EnhDeep;
                    }
                }
                lock (_renderPageCacheLock) { _renderPageCache.Clear(); }
                return "ok";
            }
            catch (Exception ex) { return "err:" + ex.Message; }
        }

        /// <summary>
        /// 三功能集成·阶段2：扫描件优化生成输出。对当前已加载 PDF 整本应用优化，
        /// 输出位置用 SaveFileDialog 指定。返回 {"ok":true,"out":"..."} 或错误。
        /// </summary>
        public string ScanEnhanceGenerate(string json)
        {
            try
            {
                if (_stampDocPath == null || !File.Exists(_stampDocPath))
                    return "{\"ok\":false,\"error\":\"请先加载 PDF 文件\"}";
                ScanEnhanceParams p = new ScanEnhanceParams();
                if (!string.IsNullOrWhiteSpace(json))
                    p = new JavaScriptSerializer().Deserialize<ScanEnhanceParams>(json) ?? p;
                string outPath;
                using (var dlg = new System.Windows.Forms.SaveFileDialog())
                {
                    dlg.Title = "保存扫描件优化 PDF";
                    dlg.Filter = "PDF 文件|*.pdf";
                    dlg.FileName = Path.GetFileNameWithoutExtension(_stampDocPath) + "_扫描优化.pdf";
                    dlg.InitialDirectory = Path.GetDirectoryName(_stampDocPath);
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return "{\"ok\":false,\"cancel\":true}";
                    outPath = dlg.FileName;
                }
                string res = PdfScanEnhance.ConvertPdfToScanEnhance(_stampDocPath, p, outPath);
                if (res.StartsWith("{\"ok\":true", StringComparison.Ordinal))
                    return "{\"ok\":true,\"out\":" + Json(outPath) + "}";
                return res;
            }
            catch (Exception ex)
            {
                return "{\"ok\":false,\"error\":" + Json("扫描件优化失败：" + ex.Message) + "}";
            }
        }

        /// <summary>
        /// 三功能集成·阶段2：对单个输出 PDF 应用扫描件优化并覆盖原文件（渲染+滤镜+合成）。
        /// 供主界面「生成结果文件」（GenerateFiles）输出后在生成分支内调用。
        /// </summary>
        internal static bool ApplyScanEnhanceToFile(string pdfPath, ScanEnhanceParams p)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath)) return false;
                string tmp = pdfPath + ".setmp";
                string res = PdfScanEnhance.ConvertPdfToScanEnhance(pdfPath, p, tmp);
                if (res.StartsWith("{\"ok\":true", StringComparison.Ordinal))
                {
                    File.Copy(tmp, pdfPath, true);
                    try { File.Delete(tmp); } catch { }
                    return true;
                }
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                return false;
            }
            catch { return false; }
        }
    }
}
