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
    /// V2.4.0.82：桥按业务拆 partial（本文件：阶段 3 配置/诊断/版本）。
    /// </summary>
    public partial class CSharpBridge
    {
        /// <summary>壳/软件版本号（与 AssemblyInfo 一致）。</summary>
        public string GetVersion()
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return v == null ? "2.4.0.0" : v.ToString(4);
        }
        /// <summary>程序所在目录（EXE 同目录，交付形态的文件都在这）。</summary>
        public string GetAppPath()
        {
            return AppDomain.CurrentDomain.BaseDirectory;
        }
        /// <summary>通道连通性自检：JS 调用返回 pong + 服务器时间戳。</summary>
        public string Ping()
        {
            return "pong:" + DateTime.Now.ToString("HH:mm:ss.fff");
        }
        /// <summary>config.ini 完整路径（WPF 版同口径：EXE 同目录）。</summary>
        public string GetIniPath()
        {
            return AppConfig.IniPath;
        }
        /// <summary>全局 UI 配置 JSON（前端启动时拉取，字段名 camelCase 与前端一致）。</summary>
        public string GetUiConfig()
        {
            var d = new Dictionary<string, object>
            {
                ["wjType"] = AppConfig.WjType,
                ["qfzType"] = AppConfig.QfzType,
                ["yzType"] = AppConfig.YzType,
                ["wzType"] = AppConfig.WzType,
                ["qbflag"] = AppConfig.QbFlag,
                ["size"] = AppConfig.Size,
                ["rotation"] = AppConfig.Rotation,
                ["opacity"] = AppConfig.Opacity,
                ["wz"] = AppConfig.WzPercent,
                ["maxfgs"] = AppConfig.MaxFgs,
                ["outputQualityDpi"] = AppConfig.OutputQualityDpi,
                ["outputNameMark"] = AppConfig.OutputNameMark ?? "",
                ["outputNamePos"] = AppConfig.OutputNamePos,
                ["outputNameSeqType"] = AppConfig.OutputNameSeqType,
                ["outputNamePad"] = AppConfig.OutputNamePad,
                ["outputNameTs"] = AppConfig.OutputNameTs,
                ["outputNameTsFormat"] = AppConfig.OutputNameTsFormat ?? "yyyyMMdd",
                ["contextFilterEnabled"] = AppConfig.ContextFilterEnabled,
                ["contextKeywords"] = AppConfig.ContextKeywords ?? "",
                ["contextRange"] = AppConfig.ContextRange,
                ["contextMatch"] = AppConfig.ContextMatch,
                ["contextExcludeSpaces"] = AppConfig.ContextExcludeSpaces,
                ["outputDir"] = AppConfig.OutputDir ?? "",
                ["outputDirLocked"] = AppConfig.OutputDirLocked,
                ["foldAutoText"] = AppConfig.FoldAutoText,
                ["foldSealParams"] = AppConfig.FoldSealParams,
                ["foldOther"] = AppConfig.FoldOther,
                ["foldDisp"] = AppConfig.FoldDisp,
                ["foldRand"] = AppConfig.FoldRand,
                ["foldRender"] = AppConfig.FoldRender,
                ["foldRidge"] = AppConfig.FoldRidge,
                ["foldWhite"] = AppConfig.FoldWhite,
                ["foldRange"] = AppConfig.FoldRange,
                ["foldText"] = AppConfig.FoldText,
                ["foldMode"] = AppConfig.FoldMode,
                ["foldDpi"] = AppConfig.FoldDpi,
                ["foldName"] = AppConfig.FoldName,
                ["lastStampImagePath"] = AppConfig.LastStampImagePath ?? "",
                ["leftPanelWidth"] = AppConfig.LeftPanelWidth,
                ["badgeDebug"] = AppConfig.BadgeDebug, // V2.4.0.397
                ["enableTheme"] = AppConfig.EnableTheme, // V2.4.0.400
                ["enableCustomTitle"] = AppConfig.EnableCustomTitle, // V2.4.0.407：12.9 档自定义标题开关
                ["version"] = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(), // V1.0.0.14：完整版本号（前端关于/使用说明展示，反馈定位用）
                ["tierActive"] = AppConfig.TierActive,     // V2.4.0.407：授权有效（满 2 台 false=免费效果）
                ["themeColor"] = AppConfig.ThemeColor,      // V2.4.0.400
            };
            return Json(d);
        }
        /// <summary>更新全局配置（部分键即可），写 config.ini。返回 "ok" 或错误消息。</summary>
        public string SetUiConfig(string json)
        {
            try
            {
                var ser = new JavaScriptSerializer();
                var d = ser.Deserialize<Dictionary<string, object>>(json);
                if (d == null) return "err:空配置";

                AppConfig.WjType = GetInt(d, "wjType", AppConfig.WjType);
                AppConfig.QfzType = GetInt(d, "qfzType", AppConfig.QfzType);
                AppConfig.YzType = GetInt(d, "yzType", AppConfig.YzType);
                AppConfig.WzType = GetInt(d, "wzType", AppConfig.WzType);
                AppConfig.QbFlag = GetInt(d, "qbflag", AppConfig.QbFlag);
                AppConfig.Size = GetInt(d, "size", AppConfig.Size);
                AppConfig.Rotation = GetInt(d, "rotation", AppConfig.Rotation);
                AppConfig.Opacity = GetInt(d, "opacity", AppConfig.Opacity);
                AppConfig.WzPercent = GetInt(d, "wz", AppConfig.WzPercent);
                AppConfig.MaxFgs = GetInt(d, "maxfgs", AppConfig.MaxFgs);
                AppConfig.OutputQualityDpi = GetInt(d, "outputQualityDpi", AppConfig.OutputQualityDpi);
                AppConfig.OutputNameMark = GetStr(d, "outputNameMark", AppConfig.OutputNameMark);
                AppConfig.OutputNamePos = GetInt(d, "outputNamePos", AppConfig.OutputNamePos);
                AppConfig.OutputNameSeqType = GetInt(d, "outputNameSeqType", AppConfig.OutputNameSeqType);
                AppConfig.OutputNamePad = GetInt(d, "outputNamePad", AppConfig.OutputNamePad);
                AppConfig.OutputNameTs = GetBool(d, "outputNameTs", AppConfig.OutputNameTs);
                AppConfig.OutputNameTsFormat = GetStr(d, "outputNameTsFormat", AppConfig.OutputNameTsFormat);
                AppConfig.ContextFilterEnabled = GetBool(d, "contextFilterEnabled", AppConfig.ContextFilterEnabled);
                AppConfig.ContextKeywords = GetStr(d, "contextKeywords", AppConfig.ContextKeywords);
                AppConfig.ContextRange = GetInt(d, "contextRange", AppConfig.ContextRange);
                AppConfig.ContextMatch = GetInt(d, "contextMatch", AppConfig.ContextMatch);
                AppConfig.ContextExcludeSpaces = GetBool(d, "contextExcludeSpaces", AppConfig.ContextExcludeSpaces);
                AppConfig.OutputDir = GetStr(d, "outputDir", AppConfig.OutputDir);
                AppConfig.OutputDirLocked = GetInt(d, "outputDirLocked", AppConfig.OutputDirLocked);
                AppConfig.FoldAutoText = GetInt(d, "foldAutoText", AppConfig.FoldAutoText);
                AppConfig.FoldSealParams = GetInt(d, "foldSealParams", AppConfig.FoldSealParams);
                AppConfig.FoldOther = GetInt(d, "foldOther", AppConfig.FoldOther);
                AppConfig.FoldDisp = GetInt(d, "foldDisp", AppConfig.FoldDisp);
                AppConfig.FoldRand = GetInt(d, "foldRand", AppConfig.FoldRand);
                AppConfig.FoldRender = GetInt(d, "foldRender", AppConfig.FoldRender);
                AppConfig.FoldRidge = GetInt(d, "foldRidge", AppConfig.FoldRidge);
                AppConfig.FoldWhite = GetInt(d, "foldWhite", AppConfig.FoldWhite);
                AppConfig.FoldRange = GetInt(d, "foldRange", AppConfig.FoldRange);
                AppConfig.FoldText = GetInt(d, "foldText", AppConfig.FoldText);
                AppConfig.FoldMode = GetInt(d, "foldMode", AppConfig.FoldMode);
                AppConfig.FoldDpi = GetInt(d, "foldDpi", AppConfig.FoldDpi);
                AppConfig.FoldName = GetInt(d, "foldName", AppConfig.FoldName);
                if (d.ContainsKey("leftPanelWidth"))
                {
                    AppConfig.LeftPanelWidth = GetInt(d, "leftPanelWidth", AppConfig.LeftPanelWidth);
                    AppConfig.SaveLeftPanelWidth(); // WPF 版同口径：拖动完即落盘
                }
                AppConfig.SaveUiConfig();
                return "ok";
            }
            catch (Exception ex)
            {
                return "err:" + ex.Message;
            }
        }
        /// <summary>读取某印章参数 JSON（无记录返回默认参数）。key=印章显示名。</summary>
        public string GetStampParams(string stampName)
        {
            try
            {
                var p = AppConfig.LoadStampParams(stampName ?? "");
                var d = new Dictionary<string, object>
                {
                    ["size"] = p.Size,
                    ["rotation"] = p.Rotation,
                    ["rotationHandle"] = p.RotationHandle,
                    ["opacity"] = p.Opacity,
                    ["randomParams"] = p.RandomParams,
                    ["randomRange"] = p.RandomRange,
                    ["randomOffsetXMm"] = p.RandomOffsetXMm,
                    ["randomOffsetYMm"] = p.RandomOffsetYMm,
                    ["removeWhite"] = p.RemoveWhite,
                    ["tolerance"] = p.Tolerance,
                    ["maxSplit"] = p.MaxSplit,
                    ["textureQuality"] = p.TextureQuality,
                    ["textureBrightness"] = p.TextureBrightness,
                    ["textureBlob"] = p.TextureBlob,
                    ["textureGradient"] = p.TextureGradient,
                    ["textureWhite"] = p.TextureWhite,
                    ["textureSpot"] = p.TextureSpot,
                    ["textureRadial"] = p.TextureRadial,
                    ["textureCast"] = p.TextureCast,
                    ["texturePresetIndex"] = p.TexturePresetIndex,
                };
                return Json(d);
            }
            catch (Exception ex)
            {
                return "err:" + ex.Message;
            }
        }
        /// <summary>保存某印章参数（部分键即可）。返回 "ok" 或错误消息。</summary>
        public string SetStampParams(string stampName, string json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(stampName)) return "err:印章名为空";
                var ser = new JavaScriptSerializer();
                var d = ser.Deserialize<Dictionary<string, object>>(json);
                if (d == null) return "err:空参数";

                var p = AppConfig.LoadStampParams(stampName.Trim());
                p.Size = GetInt(d, "size", p.Size);
                p.Rotation = GetInt(d, "rotation", p.Rotation);
                p.RotationHandle = GetInt(d, "rotationHandle", p.RotationHandle);
                p.Opacity = GetInt(d, "opacity", p.Opacity);
                p.RandomParams = GetBool(d, "randomParams", p.RandomParams);
                p.RandomRange = GetInt(d, "randomRange", p.RandomRange);
                p.RandomOffsetXMm = GetInt(d, "randomOffsetXMm", p.RandomOffsetXMm);
                p.RandomOffsetYMm = GetInt(d, "randomOffsetYMm", p.RandomOffsetYMm);
                p.RemoveWhite = GetBool(d, "removeWhite", p.RemoveWhite);
                p.Tolerance = GetInt(d, "tolerance", p.Tolerance);
                p.MaxSplit = GetInt(d, "maxSplit", p.MaxSplit);
                if (p.MaxSplit <= 0) p.MaxSplit = -1; // 自动重置（未手动修改）不写入记忆，对齐 WPF MaxSplit=-1 语义
                p.TextureQuality = GetBool(d, "textureQuality", p.TextureQuality);
                p.TextureBrightness = GetInt(d, "textureBrightness", p.TextureBrightness);
                p.TextureBlob = GetInt(d, "textureBlob", p.TextureBlob);
                p.TextureGradient = GetInt(d, "textureGradient", p.TextureGradient);
                p.TextureWhite = GetInt(d, "textureWhite", p.TextureWhite);
                p.TextureSpot = GetInt(d, "textureSpot", p.TextureSpot);
                p.TextureRadial = GetInt(d, "textureRadial", p.TextureRadial);
                p.TextureCast = GetInt(d, "textureCast", p.TextureCast);
                p.TexturePresetIndex = GetInt(d, "texturePresetIndex", p.TexturePresetIndex);
                AppConfig.SaveStampParams(stampName.Trim(), p);
                return "ok";
            }
            catch (Exception ex)
            {
                return "err:" + ex.Message;
            }
        }
        /// <summary>V2.4.0.35：拖放/加载诊断日志。V1.0.0.14：统一并入 app_log.log（原 pdfqfz_diag.log 停用）。</summary>
        public string DiagLog(string msg)
        {
            AppLog.Write("[DIAG] " + (msg ?? ""));
            return "{\"ok\":true}";
        }
        /// <summary>V2.4.0.405：写收费版 vip.ini 配置（themeColor/titleSuffix 部分键，保留其他键；无 BOM UTF-8 文本写回）。
        /// 调用方：前端 themePick（主题色持久化到 vip.ini，修复重启回蓝）、自定义标题保存。返回 "ok" 或 "err:..."。</summary>
        public string SetVipConfig(string json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json)) return "err:参数为空";
                var d = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string, string>>(json);
                if (d == null) return "err:参数错误";
                if (AppConfig.VipTier <= 0) return "err:未开通收费功能";
                if (!AppConfig.TierActive) return "err:授权超限";
                if (d.ContainsKey("themeColor") && AppConfig.EnableTheme)
                {
                    AppConfig.ThemeColor = d["themeColor"];
                    AppConfig.SaveTierKey("themeColor", d["themeColor"]);
                }
                if (d.ContainsKey("titleSuffix"))
                {
                    if (AppConfig.VipTier < 3) return "err:当前版本不支持自定义标题";
                    AppConfig.TitleSuffix = d["titleSuffix"];
                    AppConfig.SaveTierKey("titleSuffix", d["titleSuffix"]);
                }
                System.Windows.Application.Current.Dispatcher.Invoke(() => { PDFQFZ.WebShell.MainWindow.RefreshTitle(); });
                return "ok";
            }
            catch (Exception ex) { return "err:" + ex.Message; }
        }
    }
}
