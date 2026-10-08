/* PDFQFZ Web UI 前端模块：等级徽章彩蛋（V2.4.0.397 新增，V2.4.0.403 修复）
 * 功能：本地文档统计 + QQ 风格等级徽章。
 * - 统计：PDF 盖章成功 +1、PDF 水印成功 +1、图片水印成功 +1（导出成功才计；预览/打开/失败/取消不计）
 * - 等级：图标单价递增（星8/月10/日12/金日15/奖杯18/皇冠22）+ 每 4 图标进位；等级数字=图标总数，永不封顶
 * - 存储：localForage(IndexedDB) + Web Crypto AES-GCM（https://app 安全上下文可用），防记事本/开发者工具直改
 * - 开关：enableLevelBadge=false 时后台照常计数不渲染 UI；badgeDebug=1（vip.ini）强制显示徽章
 * - V2.4.0.403：0 级显示灰色空心星星占位（让人看出等级区域）；右键徽章直接三路各 +10（不再弹调试菜单）
 * - V2.4.0.403 关键修复：Vue3 methods 选项只保留函数，_per/_badgeKey/_badgePass 等非函数经 spread 注入时被丢弃，
 *   this._per 不存在 → badgeCalc 抛 TypeError → 徽章图标渲染/计数保存失败。改为模块闭包常量，方法内直接引用。
 * - 导出 PDF 成品零污染：本模块不触碰 PDF 写入链路
 * index.html methods 通过 ...window.PdfqModules.badge 注入，this 指向组件实例（Vue3 options API spread 保留绑定）。
 */
window.PdfqModules = window.PdfqModules || {};
(function () {
    /* ---- 图标单价（星/月/日/金日/奖杯/皇冠）；皇冠之后继续用 22 循环（变体叠加，永不封顶） ---- */
    var PER = [8, 10, 12, 15, 18, 22];
    var BADGE_KEY = 'pdfqfz_badge_stat_v1';
    var BADGE_PASS = 'pdfqfz-badge-local-v1';   // AES 密钥（源码内置：防普通用户记事本/开发者工具直改；开源特性本身不防开发者）

    window.PdfqModules.badge = {
        // ---- 开关（vip.ini badgeDebug=1 → GetUiConfig → applyUiConfig 写入 badgeDebug） ----
        enableLevelBadge: false,
        badgeDebug: false,
        // ---- 数据（data 区镜像；计数/等级显示） ----
        badgeData: { totalStampCount: 0, totalWatermarkCount: 0, totalImageWatermarkCount: 0, lastLevel: 0 },
        _badgeInited: false,

        /* ---- 内联 SVG 图标（办公稳重风：星星/月亮/日/金日/奖杯/皇冠；star-empty=0 级灰色空心星星占位） ---- */
        badgeSvg(id) {
            // V2.4.0.404：方案 A（用户选定）——QQ 经典立体金：渐变金(#FFE58F→#F7BA1E→#D48806) + 顶部高光 + 深金描边(#AD6800)
            var m = {
                'star-empty': '<svg viewBox="0 0 16 16" width="14" height="14"><path fill="none" stroke="#C0C4CC" stroke-width="1.5" d="M8 1.6l1.9 3.9 4.3.6-3.1 3 .7 4.2L8 11.5l-3.8 2 .7-4.2-3.1-3 4.3-.6z"/></svg>',
                star: '<svg viewBox="0 0 24 24" width="16" height="16"><defs><linearGradient id="bstar" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#FFE58F"/><stop offset=".5" stop-color="#F7BA1E"/><stop offset="1" stop-color="#D48806"/></linearGradient></defs><path fill="url(#bstar)" stroke="#AD6800" stroke-width=".8" d="M12 2.5l2.85 5.8 6.4.9-4.6 4.5 1.1 6.4-5.75-3-5.75 3 1.1-6.4-4.6-4.5 6.4-.9z"/><ellipse cx="9.5" cy="7" rx="3.4" ry="1.9" fill="#fff" opacity=".45"/></svg>',
                moon: '<svg viewBox="0 0 24 24" width="16" height="16"><defs><linearGradient id="bmoon" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#FFE58F"/><stop offset=".5" stop-color="#F7BA1E"/><stop offset="1" stop-color="#D48806"/></linearGradient></defs><path fill="url(#bmoon)" stroke="#AD6800" stroke-width=".8" d="M15.3 2.2a9.3 9.3 0 1 0 6.5 6.5 7.4 7.4 0 0 1-6.5-6.5z"/><ellipse cx="16.4" cy="6.2" rx="2.6" ry="1.5" fill="#fff" opacity=".4"/></svg>',
                sun: '<svg viewBox="0 0 24 24" width="16" height="16"><defs><radialGradient id="bsun" cx=".5" cy=".35" r=".8"><stop offset="0" stop-color="#FFF3B8"/><stop offset=".55" stop-color="#F7BA1E"/><stop offset="1" stop-color="#D48806"/></radialGradient></defs><g stroke="#AD6800" stroke-width="1"><circle cx="12" cy="12" r="4.6" fill="url(#bsun)"/><path stroke-linecap="round" d="M12 2.2v2.6M12 19.2v2.6M2.2 12h2.6M19.2 12h2.6M4.9 4.9l1.8 1.8M17.3 17.3l1.8 1.8M19.1 4.9l-1.8 1.8M6.7 17.3l-1.8 1.8"/></g></svg>',
                goldsun: '<svg viewBox="0 0 24 24" width="16" height="16"><defs><radialGradient id="bgoldsun" cx=".5" cy=".35" r=".8"><stop offset="0" stop-color="#FFF3B8"/><stop offset=".55" stop-color="#F7BA1E"/><stop offset="1" stop-color="#D48806"/></radialGradient></defs><g stroke="#AD6800" stroke-width="1"><circle cx="12" cy="12" r="3.8" fill="url(#bgoldsun)"/><path stroke-linecap="round" d="M12 2.2v2M12 19.8v2M2.2 12h2M19.8 12h2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M19.1 4.9l-1.4 1.4M6.3 17.7l-1.4 1.4"/></g><circle cx="12" cy="12" r="6.6" fill="none" stroke="#AD6800" stroke-width=".8" opacity=".6"/></svg>',
                trophy: '<svg viewBox="0 0 24 24" width="16" height="16"><defs><linearGradient id="btrophy" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#FFE58F"/><stop offset=".5" stop-color="#F7BA1E"/><stop offset="1" stop-color="#D48806"/></linearGradient></defs><path fill="url(#btrophy)" stroke="#AD6800" stroke-width=".8" d="M6.8 2.6h10.4v4.6c0 3.2-2.2 5.4-5.2 5.4S6.8 10.4 6.8 7.2zM2.6 4h3v1.2c0 1.7.6 3.2 1.5 4.4-.6 1-2.4 1.6-4.5 1.6zM21.4 4h-3v1.2c0 1.7-.6 3.2-1.5 4.4.6 1 2.4 1.6 4.5 1.6zM9.6 16.6h4.8v2H9.6zM7.6 19.4h8.8v1.6H7.6z"/><ellipse cx="10.4" cy="5.6" rx="3.6" ry="1.7" fill="#fff" opacity=".4"/></svg>',
                crown: '<svg viewBox="0 0 24 24" width="16" height="16"><defs><linearGradient id="bcrown" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#FFE58F"/><stop offset=".5" stop-color="#F7BA1E"/><stop offset="1" stop-color="#D48806"/></linearGradient></defs><path fill="url(#bcrown)" stroke="#AD6800" stroke-width=".8" d="M2.6 6.8l5.2 3.6 4.2-5 4.2 5 5.2-3.6v10.4H2.6z"/><path fill="url(#bcrown)" stroke="#AD6800" stroke-width=".8" d="M5.8 16.4h12.4v2.2H5.8z"/><ellipse cx="8.8" cy="9.6" rx="3.2" ry="1.4" fill="#fff" opacity=".4"/></svg>'
            };
            return m[id] || m.star;
        },

        /* ---- 等级计算：等级数字=累计图标总数（每 4 图标进位显示），永不封顶
         * 返回 { level, html, needNext }
         * - level：图标总数（连续递增）
         * - html：最高两档图标 HTML（QQ 式 4 进制拆分）；0 级显示灰色空心星星占位（V2.4.0.403）
         * - needNext：距下一等级还差份数 */
        badgeCalc() {
            var total = this.badgeTotal();
            var k = 0, used = 0;
            for (var i = 0; i < 500; i++) {
                var p = PER[Math.min(i, PER.length - 1)];
                if (used + p > total) break;
                used += p; k++;
            }
            var needNext = used + PER[Math.min(k, PER.length - 1)] - total;
            // QQ 进制拆分（每 4 图标进位）：counts[i] = 第 i 档图标数（0~3）
            var counts = [];
            var v = k;
            for (var i2 = 0; i2 < 12; i2++) { counts.push(v % 4); v = Math.floor(v / 4); if (v === 0) break; }
            var iconIds = ['star', 'moon', 'sun', 'goldsun', 'trophy', 'crown'];
            var top = [];
            for (var i3 = counts.length - 1; i3 >= 0; i3--) { if (counts[i3] > 0) top.push({ idx: i3, n: counts[i3] }); }
            var html = '';
            top.slice(0, 2).forEach(function (t) {
                var id = iconIds[t.idx % iconIds.length];
                for (var n = 0; n < t.n; n++) html += this.badgeSvg(id);
            }, this);
            if (html === '' && k === 0) html = this.badgeSvg('star-empty'); // V2.4.0.403：0 级占位空心星星
            return { level: k, html: html, needNext: Math.max(0, needNext) };
        },
        badgeTotal() {
            return (this.badgeData.totalStampCount || 0) + (this.badgeData.totalWatermarkCount || 0) + (this.badgeData.totalImageWatermarkCount || 0);
        },

        /* ---- AES-GCM 加密存取（Web Crypto；https://app 安全上下文可用） ---- */
        async _badgeEncrypt(obj) {
            var enc = new TextEncoder();
            var key = await crypto.subtle.importKey('raw', enc.encode(BADGE_PASS.padEnd(32, 'x').slice(0, 32)), { name: 'AES-GCM' }, false, ['encrypt', 'decrypt']);
            var iv = crypto.getRandomValues(new Uint8Array(12));
            var ct = await crypto.subtle.encrypt({ name: 'AES-GCM', iv: iv }, key, enc.encode(JSON.stringify(obj)));
            var buf = new Uint8Array(12 + ct.byteLength);
            buf.set(iv, 0); buf.set(new Uint8Array(ct), 12);
            var bin = ''; buf.forEach(function (b) { bin += String.fromCharCode(b); });
            return btoa(bin);
        },
        async _badgeDecrypt(b64) {
            try {
                var bin = atob(b64); var buf = new Uint8Array(bin.length);
                for (var i = 0; i < bin.length; i++) buf[i] = bin.charCodeAt(i);
                var key = await crypto.subtle.importKey('raw', new TextEncoder().encode(BADGE_PASS.padEnd(32, 'x').slice(0, 32)), { name: 'AES-GCM' }, false, ['encrypt', 'decrypt']);
                var pt = await crypto.subtle.decrypt({ name: 'AES-GCM', iv: buf.slice(0, 12) }, key, buf.slice(12));
                return JSON.parse(new TextDecoder().decode(pt));
            } catch (e) { return null; }
        },
        async _badgeLoad() {
            try {
                if (!window.localforage) return;
                var raw = await window.localforage.getItem(BADGE_KEY);
                if (!raw) return;
                var d = await this._badgeDecrypt(raw);
                if (d && typeof d === 'object') Object.assign(this.badgeData, d);
            } catch (e) {}
        },
        async _badgeSave() {
            try {
                if (!window.localforage) return;
                var enc = await this._badgeEncrypt(this.badgeData);
                await window.localforage.setItem(BADGE_KEY, enc);
            } catch (e) {}
        },

        /* ---- 初始化（applyUiConfig 末尾调用；幂等） ---- */
        async badgeInit() {
            if (this._badgeInited) return;
            this._badgeInited = true;
            this.badgeEnabled = this.enableLevelBadge || this.badgeDebug; // V2.4.0.402：enableLevelBadge 由 vip.ini badgeDebug 经 applyUiConfig 写入，不再读 index.html 常量
            await this._badgeLoad();
            this._badgeRender();
        },
        _badgeRender() {
            var calc = this.badgeCalc();
            this.badgeLevel = calc.level;
            this.badgeIconsHtml = calc.html;
            this.badgeNeedNext = calc.needNext;
        },

        /* ---- 计数入口（导出成功才调用；n=本次成功文件数） ---- */
        async badgeAddStamp(n) { if (n > 0) { this.badgeData.totalStampCount += n; await this._badgeAfterAdd(); } },
        async badgeAddPdfWm(n) { if (n > 0) { this.badgeData.totalWatermarkCount += n; await this._badgeAfterAdd(); } },
        async badgeAddImgWm(n) { if (n > 0) { this.badgeData.totalImageWatermarkCount += n; await this._badgeAfterAdd(); } },
        async _badgeAfterAdd() {
            var prev = this.badgeData.lastLevel || 0;
            var cur = this.badgeCalc().level;
            this._badgeRender();
            await this._badgeSave();
            if (cur > prev) {
                this.badgeData.lastLevel = cur;
                await this._badgeSave();
                this.badgeUpgradeAnim(cur);
            }
        },

        /* ---- 升级动画（游戏式：出现 → 飞向徽章 → 缩小消失；只触发一次） ---- */
        badgeUpgradeAnim(level) {
            if (!this.badgeEnabled) return;
            var el = document.getElementById('badge-upgrade-float');
            if (!el) return;
            el.innerHTML = '🎉 恭喜升级！等级 ' + level;
            el.style.display = 'block';
            el.classList.remove('bu-show', 'bu-fly');
            void el.offsetWidth;
            el.classList.add('bu-show');
            var target = document.querySelector('.tb-badge');
            setTimeout(function () {
                if (!target) { el.style.display = 'none'; return; }
                var r = target.getBoundingClientRect();
                // V2.4.0.404：弹窗初始位于 left:50%/top:55%（translate(-50%,-50%)），终点用相对初始位置的差值位移，飞到徽章处（右上角）
                var dx = r.left + r.width / 2 - window.innerWidth * 0.5;
                var dy = r.top + r.height / 2 - window.innerHeight * 0.55;
                el.style.transform = 'translate(' + dx + 'px,' + dy + 'px) scale(0.4)';
                el.style.opacity = '0';
                setTimeout(function () { el.style.display = 'none'; el.style.transform = ''; el.style.opacity = ''; }, 700);
            }, 900);
        },

        /* V1.0.0.12：已移除右键徽章调试口（badgeDebugAdd，三路各+10）——徽章仅展示计数，不再提供右键加数 */
    };
})();