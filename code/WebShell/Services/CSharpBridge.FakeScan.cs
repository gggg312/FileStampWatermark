using System;
using System.IO;
using System.Web.Script.Serialization;
using PDFQFZ.WPF.Services;

namespace PDFQFZ.WebShell.Services
{
    public partial class CSharpBridge
    {
        /// <summary>
        /// 三功能集成·阶段1：PDF 仿扫描参数同步（作用于当前预览）。
        /// 参数 json 为 FakeScanParams（各强度 0-100）；空/null 或全 0 = 关闭仿扫描（预览恢复原样）。
        /// 更新后并入 _fakeScanKey 使 RenderPage 缓存失效，前端调 renderPage 即见仿扫描效果。返回 "ok"。
        /// </summary>
        public string FakeScanSyncParams(string json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json))
                {
                    _fakeScanParams = null; _fakeScanKey = "";
                }
                else
                {
                    FakeScanParams p = new JavaScriptSerializer().Deserialize<FakeScanParams>(json) ?? new FakeScanParams();
                    if (p.Noise <= 0 && p.Blur <= 0 && p.Warp <= 0 && p.Vignette <= 0 && p.BgTone <= 0 && p.Tilt == 0)
                    {
                        _fakeScanParams = null; _fakeScanKey = "";
                    }
                    else
                    {
                        _fakeScanParams = p;
                        _fakeScanKey = p.Noise + "|" + p.Blur + "|" + p.Warp + "|" + p.Vignette + "|" + p.BgTone + "|" + p.Tilt;
                    }
                }
                lock (_renderPageCacheLock) { _renderPageCache.Clear(); }
                return "ok";
            }
            catch (Exception ex) { return "err:" + ex.Message; }
        }

        /// <summary>
        /// 三功能集成·阶段1：PDF 仿扫描生成输出。对当前已加载 PDF 整本应用滤镜，
        /// 输出位置用 SaveFileDialog 指定。返回 {"ok":true,"out":"..."} 或错误。
        /// </summary>
        public string FakeScanGenerate(string json)
        {
            try
            {
                if (_stampDocPath == null || !File.Exists(_stampDocPath))
                    return "{\"ok\":false,\"error\":\"请先加载 PDF 文件\"}";
                FakeScanParams p = new FakeScanParams();
                if (!string.IsNullOrWhiteSpace(json))
                    p = new JavaScriptSerializer().Deserialize<FakeScanParams>(json) ?? p;
                string outPath;
                using (var dlg = new System.Windows.Forms.SaveFileDialog())
                {
                    dlg.Title = "保存仿扫描 PDF";
                    dlg.Filter = "PDF 文件|*.pdf";
                    dlg.FileName = Path.GetFileNameWithoutExtension(_stampDocPath) + "_仿扫描.pdf";
                    dlg.InitialDirectory = Path.GetDirectoryName(_stampDocPath);
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return "{\"ok\":false,\"cancel\":true}";
                    outPath = dlg.FileName;
                }
                string res = PdfFakeScan.ConvertPdfToFakeScan(_stampDocPath, p, outPath);
                if (res.StartsWith("{\"ok\":true", StringComparison.Ordinal))
                    return "{\"ok\":true,\"out\":" + Json(outPath) + "}";
                return res;
            }
            catch (Exception ex)
            {
                return "{\"ok\":false,\"error\":" + Json("仿扫描失败：" + ex.Message) + "}";
            }
        }
        /// <summary>
        /// 三功能集成·阶段1：对单个输出 PDF 应用仿扫描滤镜并覆盖原文件（渲染+滤镜+合成，图片化扫描件）。
        /// 供主界面「生成结果文件」（GenerateFiles）输出后在生成分支内调用。
        /// </summary>
        internal static bool ApplyFakeScanToFile(string pdfPath, FakeScanParams p)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath)) return false;
                string tmp = pdfPath + ".fktmp";
                string res = PdfFakeScan.ConvertPdfToFakeScan(pdfPath, p, tmp);
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
