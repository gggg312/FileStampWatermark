using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using System.Text;
using PDFQFZ.Library;

namespace PDFQFZ.WebShell.Services
{
    /// <summary>
    /// 文字水印方案持久化（V2.4.0.207）：直接读写 config.ini 文件，不依赖 Windows API。
    /// [WatermarkSchemes] 节下面，每行一个 键=值，键=方案名，值=水印框数组 JSON。
    /// </summary>
    internal static class WatermarkSchemeStore
    {
        private const string Section = "WatermarkSchemes";
        private const int MaxNameLen = 30;
        private const int MaxSchemes = 50;

        private static string IniPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "运行组件", "config.ini"); }
        }

        /// <summary>方案名合法化：去首尾空白、长度 1~30、非法字符替换。</summary>
        private static string SanitizeName(string name)
        {
            string n = (name ?? "").Trim();
            if (n.Length == 0) return "";
            if (n.Length > MaxNameLen) n = n.Substring(0, MaxNameLen);
            char[] bad = new char[] { '=', ';', '[', ']', '\r', '\n', '\0' };
            foreach (char c in bad) n = n.Replace(c.ToString(), "");
            n = n.Trim();
            return n;
        }

        /// <summary>读取所有方案（键=方案名，值=JSON）。处理 JSON 折行的情况。</summary>
        private static Dictionary<string, string> ReadAllSchemes()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(IniPath)) return result;

            string[] lines = File.ReadAllLines(IniPath, Encoding.GetEncoding(936));
            bool inSection = false;
            string currentKey = null;
            StringBuilder currentVal = null;

            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("["))
                {
                    // 遇到节，先保存上一个方案
                    if (currentKey != null && currentVal != null)
                    {
                        result[currentKey] = currentVal.ToString();
                        currentKey = null;
                        currentVal = null;
                    }
                    inSection = (trimmed == "[" + Section + "]");
                    continue;
                }
                if (!inSection) continue;

                int eqIdx = line.IndexOf('=');
                if (eqIdx > 0)
                {
                    // 新的键值对，先保存上一个
                    if (currentKey != null && currentVal != null)
                    {
                        result[currentKey] = currentVal.ToString();
                    }
                    string key = line.Substring(0, eqIdx).Trim();
                    currentKey = key;
                    currentVal = new StringBuilder(line.Substring(eqIdx + 1));
                }
                else
                {
                    // 折行的 JSON，合并到当前 val
                    if (currentKey != null && currentVal != null)
                    {
                        currentVal.Append(line);
                    }
                }
            }

            // 保存最后一个方案
            if (currentKey != null && currentVal != null)
            {
                result[currentKey] = currentVal.ToString();
            }

            CSharpBridge.WriteLog("[WM-SCHEMES-STORE] ReadAllSchemes count=" + result.Count + " names=" + string.Join(",", result.Keys));
            return result;
        }

        /// <summary>写入所有方案。</summary>
        private static void WriteAllSchemes(Dictionary<string, string> schemes)
        {
            // 读全部文件内容
            string[] lines = File.Exists(IniPath) ? File.ReadAllLines(IniPath, Encoding.GetEncoding(936)) : new string[0];
            var result = new List<string>();
            bool inSection = false;
            bool foundSection = false;

            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("["))
                {
                    // 遇到节
                    if (inSection)
                    {
                        // 刚结束 [WatermarkSchemes] 节，先写入所有方案
                        foreach (var kv in schemes)
                        {
                            result.Add(kv.Key + "=" + kv.Value);
                        }
                        inSection = false;
                    }
                    if (trimmed == "[" + Section + "]")
                    {
                        inSection = true;
                        foundSection = true;
                        result.Add(line);
                        continue;
                    }
                    result.Add(line);
                    continue;
                }
                if (inSection)
                {
                    // 跳过旧的方案行
                    continue;
                }
                result.Add(line);
            }

            // 如果最后是 [WatermarkSchemes] 节，写入所有方案
            if (inSection)
            {
                foreach (var kv in schemes)
                {
                    result.Add(kv.Key + "=" + kv.Value);
                }
            }

            // 如果文件里没有 [WatermarkSchemes] 节，追加到末尾
            if (!foundSection)
            {
                result.Add("[" + Section + "]");
                foreach (var kv in schemes)
                {
                    result.Add(kv.Key + "=" + kv.Value);
                }
            }

            File.WriteAllLines(IniPath, result, Encoding.GetEncoding(936)); // V1.0.0.43：config.ini 由 IniFileHelper(Windows API/ANSI=GBK) 维护，此处 UTF8 读写会把全文件中文字段（印章路径/输出命名等）写乱导致印章失效
            CSharpBridge.WriteLog("[WM-SCHEMES-STORE] WriteAllSchemes count=" + schemes.Count + " names=" + string.Join(",", schemes.Keys));
        }

        /// <summary>保存方案（覆盖同名）。boxes 为空返回 false。</summary>
        public static bool Save(string name, List<WatermarkBox> boxes)
        {
            name = SanitizeName(name);
            if (name.Length == 0 || boxes == null || boxes.Count == 0) return false;
            try
            {
                CSharpBridge.WriteLog("[WM-SCHEMES-STORE] Save name=" + name + " boxes=" + boxes.Count + " iniPath=" + IniPath);
                var list = new List<Dictionary<string, object>>();
                foreach (var b in boxes)
                {
                    list.Add(BoxToDict(b));
                }
                string json = new JavaScriptSerializer().Serialize(list);

                var all = ReadAllSchemes();
                if (!all.ContainsKey(name) && all.Count >= MaxSchemes) return false;
                all[name] = json;
                WriteAllSchemes(all);
                CSharpBridge.WriteLog("[WM-SCHEMES-STORE] Save result=true count=" + all.Count);
                return true;
            }
            catch (Exception ex)
            {
                CSharpBridge.WriteLog("[WM-SCHEMES-STORE] Save error=" + ex.Message);
                return false;
            }
        }

        /// <summary>全部方案名。</summary>
        public static List<string> GetNames()
        {
            try
            {
                var all = ReadAllSchemes();
                var names = new List<string>(all.Keys);
                CSharpBridge.WriteLog("[WM-SCHEMES-STORE] GetNames count=" + names.Count + " names=" + string.Join(",", names));
                return names;
            }
            catch (Exception ex)
            {
                CSharpBridge.WriteLog("[WM-SCHEMES-STORE] GetNames error=" + ex.Message);
                return new List<string>();
            }
        }

        public static bool Delete(string name)
        {
            name = SanitizeName(name);
            if (name.Length == 0) return false;
            try
            {
                var all = ReadAllSchemes();
                if (!all.ContainsKey(name)) return false;
                all.Remove(name);
                WriteAllSchemes(all);
                CSharpBridge.WriteLog("[WM-SCHEMES-STORE] Delete name=" + name + " remaining=" + all.Count);
                return true;
            }
            catch (Exception ex)
            {
                CSharpBridge.WriteLog("[WM-SCHEMES-STORE] Delete error=" + ex.Message);
                return false;
            }
        }

        /// <summary>加载方案为水印框列表。</summary>
        public static List<WatermarkBox> Load(string name)
        {
            name = SanitizeName(name);
            if (name.Length == 0) return null;
            try
            {
                var all = ReadAllSchemes();
                if (!all.ContainsKey(name))
                {
                    CSharpBridge.WriteLog("[WM-SCHEMES-STORE] Load name=" + name + " NOT FOUND, available keys=" + string.Join(",", all.Keys));
                    return null;
                }
                string json = all[name];
                CSharpBridge.WriteLog("[WM-SCHEMES-STORE] Load name=" + name + " json=" + json);
                if (string.IsNullOrWhiteSpace(json)) return null;
                var arr = new JavaScriptSerializer().Deserialize<List<Dictionary<string, object>>>(json);
                CSharpBridge.WriteLog("[WM-SCHEMES-STORE] Load name=" + name + " arr.Count=" + arr.Count);
                var result = new List<WatermarkBox>();
                foreach (var d in arr)
                {
                    var box = DictToBox(d);
                    result.Add(box);
                    CSharpBridge.WriteLog("[WM-SCHEMES-STORE] Load box text=" + (box.Text ?? "").Substring(0, Math.Min(20, (box.Text ?? "").Length)));
                }
                CSharpBridge.WriteLog("[WM-SCHEMES-STORE] Load name=" + name + " result.Count=" + result.Count);
                return result;
            }
            catch (Exception ex)
            {
                CSharpBridge.WriteLog("[WM-SCHEMES-STORE] Load error=" + ex.Message);
                return null;
            }
        }

        public static Dictionary<string, object> BoxToDict(WatermarkBox b)
        {
            return new Dictionary<string, object>
            {
                ["id"] = b.Id,
                ["x"] = (double)b.X, ["y"] = (double)b.Y, ["w"] = (double)b.W, ["h"] = (double)b.H,
                ["rotation"] = (double)b.Rotation,
                ["text"] = b.Text ?? "", ["fontName"] = b.FontName ?? "微软雅黑",
                ["colorArgb"] = b.ColorArgb, ["opacity"] = b.Opacity,
                ["bold"] = b.Bold, ["italic"] = b.Italic, ["underline"] = b.Underline, ["strike"] = b.Strike,
                ["letterSpacing"] = b.LetterSpacing, ["lineSpacing"] = b.LineSpacing,
                ["align"] = b.Align, ["fontScale"] = (double)(b.FontScale > 0 ? b.FontScale : 0.8),
                ["h0"] = (double)b.H0,
                ["fsToS"] = (double)b.FsToS,
                ["wrapLines"] = b.WrapLines ?? new List<string>()
            };
        }

        public static WatermarkBox DictToBox(Dictionary<string, object> d)
        {
            var b = new WatermarkBox
            {
                X = GetFloat(d, "x", 0.3f), Y = GetFloat(d, "y", 0.3f),
                W = GetFloat(d, "w", 0.3f), H = GetFloat(d, "h", 0.05f),
                Rotation = GetFloat(d, "rotation", 0f),
                Text = GetStr(d, "text", ""), FontName = GetStr(d, "fontName", "微软雅黑"),
                ColorArgb = GetInt(d, "colorArgb", unchecked((int)0xFF1F2329)),
                Opacity = GetInt(d, "opacity", 100),
                Bold = GetBool(d, "bold", false), Italic = GetBool(d, "italic", false),
                Underline = GetBool(d, "underline", false), Strike = GetBool(d, "strike", false),
                LetterSpacing = GetInt(d, "letterSpacing", 0), LineSpacing = GetInt(d, "lineSpacing", 0),
                Align = GetInt(d, "align", 0), FontScale = GetFloat(d, "fontScale", 0.8f),
                H0 = GetFloat(d, "h0", 0f),
                FsToS = Math.Max(0f, GetFloat(d, "fsToS", 0f))
            };
            if (b.W <= 0f || b.W > 1f) b.W = 0.3f;
            if (b.H <= 0f || b.H > 1f) b.H = 0.05f;
            if (b.X < 0f || b.X > 1f) b.X = 0.3f;
            if (b.Y < 0f || b.Y > 1f) b.Y = 0.3f;
            // V319: 解析 wrapLines 和 wrapLineWidths
            if (d != null)
            {
                if (d.TryGetValue("wrapLines", out object wl) && wl is System.Collections.IEnumerable wlEnum && !(wl is string))
                {
                    var list = new System.Collections.Generic.List<string>();
                    foreach (var item in wlEnum) list.Add(item?.ToString() ?? "");
                    b.WrapLines = list;
                }
                // V323: 解析 wrapLineWidths（处理字符串情况，和 UpdateWatermarkBox 一样）
                if (d.TryGetValue("wrapLineWidths", out object wlw))
                {
                    var widths = new System.Collections.Generic.List<float>();
                    if (wlw is string wlwStr)
                    {
                        string s = wlwStr.Trim();
                        if (s.StartsWith("["))
                        {
                            int pos = 1;
                            while (pos < s.Length)
                            {
                                while (pos < s.Length && (s[pos] == ' ' || s[pos] == ',')) pos++;
                                if (pos >= s.Length || s[pos] == ']') break;
                                int start = pos;
                                while (pos < s.Length && s[pos] != ',' && s[pos] != ']') pos++;
                                string numStr = s.Substring(start, pos - start).Trim();
                                if (float.TryParse(numStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float w))
                                {
                                    widths.Add(w);
                                }
                            }
                        }
                    }
                    else if (wlw is System.Collections.IEnumerable wlwEnum)
                    {
                        foreach (var item in wlwEnum)
                        {
                            if (item is double db) widths.Add((float)db);
                            else if (float.TryParse(item?.ToString(), out float r)) widths.Add(r);
                        }
                    }
                    b.WrapLineWidths = widths;
                }
                // V323: 解析 wrapLineTopRatios（处理字符串情况，和 UpdateWatermarkBox 一样）
                if (d.TryGetValue("wrapLineTopRatios", out object wlh))
                {
                    var topRatios = new System.Collections.Generic.List<float>();
                    if (wlh is string wlhStr)
                    {
                        string s = wlhStr.Trim();
                        if (s.StartsWith("["))
                        {
                            int pos = 1;
                            while (pos < s.Length)
                            {
                                while (pos < s.Length && (s[pos] == ' ' || s[pos] == ',')) pos++;
                                if (pos >= s.Length || s[pos] == ']') break;
                                int start = pos;
                                while (pos < s.Length && s[pos] != ',' && s[pos] != ']') pos++;
                                string numStr = s.Substring(start, pos - start).Trim();
                                if (float.TryParse(numStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float h))
                                {
                                    topRatios.Add(h);
                                }
                            }
                        }
                    }
                    else if (wlh is System.Collections.IEnumerable wlhEnum)
                    {
                        foreach (var item in wlhEnum)
                        {
                            if (item is double db) topRatios.Add((float)db);
                            else if (float.TryParse(item?.ToString(), out float r)) topRatios.Add(r);
                        }
                    }
                    b.WrapLineTopRatios = topRatios;
                    try { AppLog.Write("wrapLineTopRatios read (DictToBox): count=" + topRatios.Count); } catch {}
                }
            }
            return b;
        }

        private static int GetInt(Dictionary<string, object> d, string k, int def)
        {
            if (d != null && d.TryGetValue(k, out object v) && v != null)
            {
                if (v is int i) return i;
                if (v is double db) return (int)Math.Round(db);
                if (int.TryParse(v.ToString(), out int r)) return r;
            }
            return def;
        }

        private static float GetFloat(Dictionary<string, object> d, string k, float def)
        {
            if (d != null && d.TryGetValue(k, out object v) && v != null)
            {
                if (v is double db) return (float)db;
                if (float.TryParse(v.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float r)) return r;
            }
            return def;
        }

        private static bool GetBool(Dictionary<string, object> d, string k, bool def)
        {
            if (d != null && d.TryGetValue(k, out object v) && v != null)
            {
                if (v is bool b) return b;
                return v.ToString() == "True" || v.ToString() == "true" || v.ToString() == "1";
            }
            return def;
        }

        private static string GetStr(Dictionary<string, object> d, string k, string def)
        {
            if (d != null && d.TryGetValue(k, out object v) && v != null) return v.ToString();
            return def;
        }
    }
}
