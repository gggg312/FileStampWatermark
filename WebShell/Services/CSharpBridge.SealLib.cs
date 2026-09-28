using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Drawing;
using PDFQFZ.Library;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using iTextSharp.text.pdf;
using PDFQFZ.WPF.Services;

namespace PDFQFZ.WebShell.Services
{
    /// <summary>
    /// C# → JS 桥对象（AddHostObjectToScript 注入，JS 端以 window.CSharpBridge 访问）。
    /// 方法返回简单类型（string/int/bool），JS 端调用得到 Promise。
    /// V2.4.0.83：桥按业务拆 partial（本文件：阶段 7 印章库管理）。
    /// </summary>
    public partial class CSharpBridge
    {
        /// <summary>盖章渲染参数变化：同步更新当前文档所有章的 7 个纹理参数上限（种子/强度系数不变，对齐 WPF OnTextureSettingsChanged）。返回 "ok"。</summary>
        public string SyncTextureParams(string json)
        {
            try
            {
                var ser = new JavaScriptSerializer();
                var d = ser.Deserialize<Dictionary<string, object>>(json);
                if (d == null) return "err:空参数";
                foreach (var page in _stampPlacements.DistinctPages(_stampDocPath))
                {
                    foreach (var p in _stampPlacements.ForPage(_stampDocPath, page))
                    {
                        p.TextureBrightness = GetInt(d, "textureBrightness", p.TextureBrightness);
                        p.TextureBlob = GetInt(d, "textureBlob", p.TextureBlob);
                        p.TextureGradient = GetInt(d, "textureGradient", p.TextureGradient);
                        p.TextureWhite = GetInt(d, "textureWhite", p.TextureWhite);
                        p.TextureSpot = GetInt(d, "textureSpot", p.TextureSpot);
                        p.TextureRadial = GetInt(d, "textureRadial", p.TextureRadial);
                        p.TextureCast = GetInt(d, "textureCast", p.TextureCast);
                    }
                }
                return "ok";
            }
            catch (Exception ex) { return "err:" + ex.Message; }
        }
        /// <summary>印章列表 JSON：[{name,path}]，按显示名排序。</summary>
        public string GetStampList()
        {
            try
            {
                var entries = AppConfig.LoadStampEntries()
                    .OrderBy(e => e.DisplayName, StringComparer.CurrentCulture)
                    .ToList();
                var list = new List<object>();
                foreach (var e in entries)
                {
                    // V2.4.0.388：返回路径有效性标记（config 编码损坏/章文件被删时前端明确提示，不再静默失败）
                    list.Add(new Dictionary<string, object> { ["name"] = e.DisplayName, ["path"] = e.Path, ["valid"] = System.IO.File.Exists(e.Path) });
                }
                return Json(list);
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }
        /// <summary>弹多选文件对话框导入印章图片到印章库。返回 {ok,cancel,names:[...],errors:[...]}。</summary>
        public string PickStamps()
        {
            try
            {
                using (var dlg = new System.Windows.Forms.OpenFileDialog())
                {
                    dlg.Filter = "印章图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*";
                    dlg.Title = "选择印章图片（可多选）";
                    dlg.Multiselect = true;
                    dlg.CheckFileExists = true;
                    dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); // V2.4.0.407：选择印章默认打开桌面目录
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return "{\"ok\":false,\"cancel\":true}";
                    var names = new List<string>();
                    var errors = new List<string>();
                    foreach (string file in dlg.FileNames)
                    {
                        try
                        {
                            if (!File.Exists(file)) continue;
                            string defaultName = Path.GetFileNameWithoutExtension(file);
                            if (defaultName.Length > 30) defaultName = defaultName.Substring(0, 30);
                            string finalName = defaultName;
                            if (AppConfig.StampDisplayNameExists(finalName))
                            {
                                finalName = BuildUniqueStampName(finalName);
                            }
                            string libPath = AppConfig.ImportStampToLibrary(file);
                            AppConfig.AppendStampEntry(finalName, libPath);
                            names.Add(finalName);
                        }
                        catch (Exception ex)
                        {
                            errors.Add(Path.GetFileName(file) + "：" + ex.Message);
                        }
                    }
                    return "{\"ok\":true,\"names\":" + Json(names) + ",\"errors\":" + Json(errors) + "}";
                }
            }
            catch (Exception ex) { return "{\"ok\":false,\"error\":" + Json(ex.Message) + "}"; }
        }
        /// <summary>重名自动生成唯一名（WPF 弹窗确认，Web 版自动加序号，后续可手动重命名）。</summary>
        private static string BuildUniqueStampName(string baseName)
        {
            string candidate = baseName;
            int n = 2;
            while (AppConfig.StampDisplayNameExists(candidate))
            {
                candidate = baseName + "_" + n.ToString();
                n++;
            }
            return candidate;
        }
        /// <summary>删除印章（按显示名；库内图片一并删除）。返回 "ok" 或 "err:..."。</summary>
        public string DeleteStamp(string name)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name)) return "err:名称为空";
                string key = name.Trim();
                AppConfig.RemoveStampEntry(key);
                return "ok";
            }
            catch (Exception ex) { return "err:" + ex.Message; }
        }
        /// <summary>重命名印章（参数记忆/勾选集合同步迁移）。返回 "ok" 或 "err:..."。</summary>
        public string RenameStamp(string oldName, string newName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(oldName)) return "err:旧名称为空";
                string newKey = (newName ?? "").Trim();
                if (newKey.Length == 0) return "err:新名称为空";
                if (newKey.Length > 30) return "err:名称最长 30 字符";
                if (newKey.IndexOf('|') >= 0 || newKey.IndexOf(';') >= 0) return "err:名称不能包含 | 或 ;";
                if (!string.Equals(oldName.Trim(), newKey, StringComparison.OrdinalIgnoreCase)
                    && AppConfig.StampDisplayNameExists(newKey)) return "err:已存在同名印章";
                AppConfig.RenameStampEntry(oldName.Trim(), newKey);
                return "ok";
            }
            catch (Exception ex) { return "err:" + ex.Message; }
        }
        /// <summary>记忆当前选中印章（写 config.ini lastSelectedStampNames）。返回 "ok"。</summary>
        public string SelectStamp(string name)
        {
            try
            {
                AppConfig.LastSelectedStampNames.Clear();
                if (!string.IsNullOrWhiteSpace(name)) AppConfig.LastSelectedStampNames.Add(name.Trim());
                AppConfig.SaveUiConfig();
                return "ok";
            }
            catch (Exception ex) { return "err:" + ex.Message; }
        }
        /// <summary>读取纹理方案（idx 1~4）。存在返回 {exists:true,values:[7]}，未保存返回 {exists:false}。</summary>
        public string GetTexPreset(int idx)
        {
            int[] v = AppConfig.GetTexPreset(idx);
            if (v == null) return "{\"exists\":false}";
            return "{\"exists\":true,\"values\":" + Json(v) + "}";
        }
        /// <summary>保存纹理方案（idx 1~4，json 传 values 数组 7 元素）。返回 "ok" 或 "err:..."。</summary>
        public string SetTexPreset(int idx, string json)
        {
            try
            {
                var ser = new JavaScriptSerializer();
                var d = ser.Deserialize<Dictionary<string, object>>(json);
                if (d == null || !d.TryGetValue("values", out object vArr) || vArr == null) return "err:缺少 values";
                var arr = (System.Collections.ArrayList)vArr;
                if (arr.Count != 7) return "err:values 必须 7 个元素";
                int[] v = new int[7];
                for (int k = 0; k < 7; k++) v[k] = Convert.ToInt32(arr[k]);
                AppConfig.SetTexPreset(idx, v);
                return "ok";
            }
            catch (Exception ex) { return "err:" + ex.Message; }
        }
    }
}
