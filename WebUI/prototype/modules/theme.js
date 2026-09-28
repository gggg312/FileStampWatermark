/* PDFQFZ Web UI 前端模块：主题色切换（V2.4.0.400 新增，V2.4.0.403 修复）
 * 功能：三套主题色板（蓝/紫/绿，Ant Design 5.0 色板同规则派生），收费版（enableTheme=true）右上角下拉切换即时生效并持久化。
 * - 开关：enableTheme=false（免费版无 vip.ini）→ 无切换器、固定蓝；vip.ini enableTheme=1 → 渲染下拉切换器
 * - 色板：主色取 antd -6、hover -5、active -7、soft -1，派生 ring/banner/渐变等配套变量
 * - 应用：themeApply() 覆盖 :root CSS 变量（--primary 系列 + --el-color-primary 系列 + 派生变量），不触碰任何功能逻辑
 * - 持久化：themePick() 写 SetUiConfig（V402 起收费键在 vip.ini，config.ini 不再承担收费键）
 * - V2.4.0.403 关键修复：Vue3 options API 的 methods 选项只保留函数，_themes（对象）经 spread 注入时被丢弃，
 *   this._themes 在组件实例上不存在 → themePick 抛 TypeError → 点击切换无效。改为模块闭包常量 THEMES，方法内直接引用。
 * 注入方式：index.html methods 通过 ...window.PdfqModules.theme 注入，this 指向组件实例（保留绑定）。
 */
window.PdfqModules = window.PdfqModules || {};
(function () {
    /* ---- 三套色板（Ant Design 5.0 官方色板同规则：-6 主 / -5 hover / -7 active / -1 soft） ---- */
    var THEMES = {
        blue:   { primary:'#1677FF', hover:'#4096FF', active:'#0958D9', soft:'#E6F4FF', softHover:'#D6EBFF', border:'#91CAFF', ring:'rgba(22,119,255,.35)', ringStrong:'rgba(22,119,255,.45)', ringSoft:'rgba(22,119,255,.18)', ringSoft2:'rgba(22,119,255,.25)', bannerBg:'rgba(22,119,255,.06)', bannerBg2:'rgba(22,119,255,.08)', bannerBorder:'rgba(22,119,255,.22)', grad:'#5CA8FF', elDark2:'#0958D9', elL3:'#4096FF', elL5:'#69B1FF', elL7:'#91CAFF', elL8:'#BAE0FF', elL9:'#E6F4FF', elBorderHover:'#4096FF' },
        purple: { primary:'#722ED1', hover:'#9254DE', active:'#531DAB', soft:'#F9F0FF', softHover:'#EFDBFF', border:'#D3ADF7', ring:'rgba(114,46,209,.35)', ringStrong:'rgba(114,46,209,.45)', ringSoft:'rgba(114,46,209,.18)', ringSoft2:'rgba(114,46,209,.25)', bannerBg:'rgba(114,46,209,.06)', bannerBg2:'rgba(114,46,209,.08)', bannerBorder:'rgba(114,46,209,.22)', grad:'#B37FEB', elDark2:'#531DAB', elL3:'#9254DE', elL5:'#B37FEB', elL7:'#D3ADF7', elL8:'#EFDBFF', elL9:'#F9F0FF', elBorderHover:'#9254DE' },
        deepGreen:{ primary:'#389E0D', hover:'#52C41A', active:'#237804', soft:'#F6FFED', softHover:'#D9F7BE', border:'#95DE64', ring:'rgba(56,158,13,.35)', ringStrong:'rgba(56,158,13,.45)', ringSoft:'rgba(56,158,13,.18)', ringSoft2:'rgba(56,158,13,.25)', bannerBg:'rgba(56,158,13,.06)', bannerBg2:'rgba(56,158,13,.08)', bannerBorder:'rgba(56,158,13,.22)', grad:'#73D13D', elDark2:'#237804', elL3:'#73D13D', elL5:'#95DE64', elL7:'#B7EB8F', elL8:'#D9F7BE', elL9:'#F6FFED', elBorderHover:'#52C41A' },
        teal:   { primary:'#13C2C2', hover:'#36CFC9', active:'#08979C', soft:'#E6FFFB', softHover:'#B5F5EC', border:'#5CDBD3', ring:'rgba(19,194,194,.35)', ringStrong:'rgba(19,194,194,.45)', ringSoft:'rgba(19,194,194,.18)', ringSoft2:'rgba(19,194,194,.25)', bannerBg:'rgba(19,194,194,.06)', bannerBg2:'rgba(19,194,194,.08)', bannerBorder:'rgba(19,194,194,.22)', grad:'#5CDBD3', elDark2:'#08979C', elL3:'#36CFC9', elL5:'#5CDBD3', elL7:'#87E8DE', elL8:'#B5F5EC', elL9:'#E6FFFB', elBorderHover:'#36CFC9' }
    };

    window.PdfqModules.theme = {
        enableTheme: false,        // 档位开关（vip.ini enableTheme=1 → applyUiConfig 写入）
        themeColor: 'blue',        // 色板名（vip.ini themeColor → applyUiConfig 写入）

        /* ---- 幂等初始化：applyUiConfig 每次配置刷新调用；免费版强制蓝，付费版应用当前色板 ---- */
        themeInit(){
            this.themeApply(this.themeColor);
        },

        /* ---- 应用色板：覆盖 :root 主色系列（不改任何功能逻辑） ---- */
        themeApply(name){
            var th = THEMES[name] || THEMES.blue;
            var root = document.documentElement.style;
            var set = function (k, v) { root.setProperty(k, v); };
            set('--primary', th.primary); set('--primary-hover', th.hover); set('--primary-active', th.active); set('--primary-soft', th.soft);
            set('--primary-soft-hover', th.softHover); set('--primary-border', th.border); set('--primary-ring', th.ring);
            set('--primary-ring-strong', th.ringStrong); set('--primary-ring-soft', th.ringSoft); set('--primary-ring-soft2', th.ringSoft2);
            set('--primary-banner-bg', th.bannerBg); set('--primary-banner-bg2', th.bannerBg2); set('--primary-banner-border', th.bannerBorder);
            set('--primary-grad', th.grad);
            set('--el-color-primary', th.primary); set('--el-color-primary-dark-2', th.elDark2);
            set('--el-color-primary-light-3', th.elL3); set('--el-color-primary-light-5', th.elL5);
            set('--el-color-primary-light-7', th.elL7); set('--el-color-primary-light-8', th.elL8); set('--el-color-primary-light-9', th.elL9);
            set('--el-border-color-hover', th.elBorderHover);
        },

        /* ---- 下拉菜单命令：应用 + 持久化（SetUiConfig 写 vip 相关；config.ini 不承担收费键） ---- */
        themePick(name){
            if (!THEMES[name]) return;
            this.themeColor = name;
            this.themeApply(name);
            if (window.Bridge && window.Bridge.invoke) {
                // V2.4.0.405：主题色持久化改写 vip.ini（收费版），修复重启回蓝（config.ini 自 V402 不再承担收费键）；免费版无 vip.ini 返回 err 忽略
                window.Bridge.invoke('SetVipConfig', JSON.stringify({ themeColor: name })).catch(function () {});
            }
        },
    };
})();
