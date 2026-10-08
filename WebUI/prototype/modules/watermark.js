/* PDFQFZ Web UI 前端模块：文字水印（V2.4.0.96 新增）
 * 功能：预览区水印框（添加/选中/拖拽移动/四角等比缩放/左右竖线宽向拉伸/旋转按钮/移动按钮/
 *       双击编辑文字/右键删除）、参数面板联动（选中框即时编辑）、方案保存/加载/删除、
 *       翻页重取、开关门控（关闭=预览隐藏+生成不输出，框数据保留）。
 * 交互隔离：水印框 @mousedown.stop（不触发盖章）；点击空白取消选中（stampActions.onStageDown 挂钩）。
 * 坐标系：框 X/Y/W/H 相对页面 0~1（Y 向下），与后端 WatermarkBox 一致；字号 = 框高 × fontScale（超宽按最长行收窄），
 *         旋转绕框中心（屏幕系顺时针正，与输出端 iText 取反对齐——两端方向一致）。
 */
window.PdfqModules = window.PdfqModules || {};
/* 注意：data（watermarkEnabled/wmBoxes/wmSelId 等）与 computed（wmSel/wmColorHex）在 index.html 组件内定义，
   本模块仅提供 methods（spread 进 methods 注入）。 */
window.PdfqModules.watermarkActions = {
    /* ---------- methods ---------- */
        /* ---- 开关 ---- */
        onWatermarkEnable(v) {
            /* V1.0.0.54：watermarkEnabled=水印显示+输出开关——开启后常驻，导航不再关闭（导航只切 wmEditMode/wmSect.main）；关闭分支保留给内部/未来入口 */
            const on = !!v;
            this.watermarkEnabled = on;
            if (window.Bridge && window.Bridge.invoke) { window.Bridge.invoke('SetWatermarkEnabled', on).then(function(){}, function(){}); }
            if (on) { this.wmSect.main = true; this.wmEditMode = true; if(this.viewMode==='grid4'||this.viewMode==='grid8'){ this.setView('single'); } /* V1.0.0.46 需求1：网格仅浏览，开开关即切单页 */ this.refreshPageWatermarks(); }
            else { this.wmSect.main = false; this.wmEditMode = false; this.wmSelId = null; this.wmEditingId = null; }
            this.opHint = on ? '文字水印已启用：水印可编辑，不可盖章；点提示条「退出水印模式」后可盖章' : '文字水印已关闭（已放置的水印框保留，生成时不再输出）';
            this.addLog(on ? '文字水印已启用' : '文字水印已关闭');
        },

        /* ---- V1.0.0.54：提示条按钮——编辑/只读切换（不碰控件区开合；进入时自动打开控件区） ---- */
        toggleWmEditMode() {
            if (this.wmEditMode) {
                /* 退出编辑：正在编辑的框先 blur 保存 */
                if (this.wmEditingId) { const ref=this.$refs['wmEdit-'+this.wmEditingId]; const el=Array.isArray(ref)?ref[ref.length-1]:ref; if(el) el.blur(); }
                this.wmSelId = null; this.wmEditingId = null; this.wmEditMode = false; this.wmSect.main = false; /* V1.0.0.55：退出水印模式→水印卡整卡消失（含添加按钮，避免加了框却无法编辑） */
                this.opHint = '已退出水印模式：可以盖章，水印只读';
                this.addLog('已退出水印模式（可以盖章，水印只读）');
            } else {
                this.wmEditMode = true; this.wmSect.main = true; /* 进入：自动打开控件区 */
                if (this.navState && this.navState.watermark) { this.navState.watermark.on = true; }
                if (this._expandCard) { this._expandCard('watermark'); }
                if (this._flashNav) { this._flashNav('watermark'); }
                if (this.viewMode==='grid4'||this.viewMode==='grid8'){ this.setView('single'); } /* 网格仅浏览：进入编辑自动切单页 */
                this.opHint = '已进入水印模式：水印可编辑，不可盖章';
                this.addLog('已进入水印模式');
            }
        },

        /* ---- V1.0.0.57：模式提示条移动（与水印框移动按钮同款交互：按住拖动、相对位移、预览区内钳制；位置持久化，重启恢复） ---- */
        /* V1.0.0.57b：offX/offY 相对提示条当前实际 rect（默认右上角锚定或已持久化位置），拖动起点无跳动 */
        /* V1.0.0.59：拖动重构——mousedown 时动态创建箭头闭包监听（不依赖 methods 自动绑定，消除事件层一切潜在失效）；坐标系基于 banner 实际定位祖先（.preview 容器 rect），与 CSS absolute 定位基准一致；banner 本体也支持按住拖动（自动排除内部控件）；全链路 _diag 打点便于日志定位 */
        wmBannerStyle() {
            if (!this.bannerPos) { return {}; }
            return { left: this.bannerPos.x + 'px', top: this.bannerPos.y + 'px', right: 'auto' };
        },
        wmBannerDown(e, fromBody) {
            if (e.button !== 0) return;
            e.preventDefault(); e.stopPropagation();
            if (fromBody) { /* banner 本体拖动：排除内部交互控件（切换按钮行、移动按钮、el-button） */
                try { if (e.target && e.target.closest && e.target.closest('.wm-mode-btnrow,.wm-mode-btn,.wm-banner-move,.el-button')) { return; } } catch (err) {}
            }
            const el = this.$refs.wmBanner;
            if (!el) return;
            const r = el.getBoundingClientRect();
            /* offX/offY 相对提示条当前左上角（默认右上角锚定或已持久化位置），拖动起点即当前位置，无跳动 */
            this._bannerDrag = { offX: e.clientX - r.left, offY: e.clientY - r.top, w: r.width, h: r.height };
            try { if (typeof this._diag === 'function') { this._diag('wmBannerDown: 拖动开始 off=(' + Math.round(this._bannerDrag.offX) + ',' + Math.round(this._bannerDrag.offY) + ') pos=' + JSON.stringify(this.bannerPos || null)); } } catch (err) {}
            const self = this;
            const move = function (ev) { self._wmBannerMove(ev); };
            const up = function () { self._wmBannerMoveEnd(); };
            this._wmBannerCleanup = function () { window.removeEventListener('mousemove', move); window.removeEventListener('mouseup', up); };
            window.addEventListener('mousemove', move);
            window.addEventListener('mouseup', up);
        },
        _wmBannerMove(ev) {
            try {
                if (!this._bannerDrag) return;
                const d = this._bannerDrag;
                const el = this.$refs.wmBanner;
                if (!el) return;
                /* V1.0.0.59：坐标系用 banner 实际定位祖先（.preview 容器）——与 CSS absolute 定位基准一致（stage 在 toolbar 下方，若用 stage 会偏移跳位） */
                const anchor = el.offsetParent || this.$refs.stage;
                if (!anchor) return;
                const ar = anchor.getBoundingClientRect();
                let x = (ev.clientX - ar.left) - d.offX;
                let y = (ev.clientY - ar.top) - d.offY;
                x = Math.max(6, Math.min(ar.width - d.w - 6, x));
                y = Math.max(6, Math.min(ar.height - d.h - 6, y));
                this.bannerPos = { x: Math.round(x), y: Math.round(y) };
                try { if (typeof this._diag === 'function') { this._diag('wmBannerMove: pos=' + JSON.stringify(this.bannerPos)); } } catch (err) {}
            } catch (err) {
                try { if (typeof this._diag === 'function') { this._diag('wmBannerMove 异常: ' + (err && err.message || err)); } } catch (err2) {}
            }
        },
        _wmBannerMoveEnd() {
            if (this._wmBannerCleanup) { try { this._wmBannerCleanup(); } catch (err) {} this._wmBannerCleanup = null; }
            if (this._bannerDrag) {
                try { localStorage.setItem('pdfqfz_wmBannerPosV2', JSON.stringify(this.bannerPos)); } catch (err) {}
                this._bannerDrag = null;
            }
        },

        /* ---- 数据拉取 ---- */
        /* 翻页/重取：当前页（双页时含右页）水印框 */
        refreshPageWatermarks() {
            if (!window.Bridge || !this.pdfLoaded) { this.wmBoxes = []; this.wmBoxesRight = []; return; }
            const self = this;
            window.Bridge.invoke('GetPageWatermarks', Number(this.curPage) || 1).then(function (json) {
                let a = []; try { a = JSON.parse(json); } catch (e) { return; }
                if (Array.isArray(a)) {
                    self.wmBoxes = a;
                    if (!a.some(x => x.id === self.wmSelId)) self.wmSelId = null;
                    if (self.wmEditingId && !a.some(x => x.id === self.wmEditingId)) self.wmEditingId = null;
                    /* V1.0.0.18：切页后按当前页重算预览字号 + 框高（框随字；wrapLines 不碰——输出快照不受污染；横竖页混排预览框高贴合文字） */
                    self.$nextTick(function () {
                        (self.wmBoxes || []).forEach(function (b) {
                            try { self.wmRefitToPage(b, self.wmSideDisp(b)); } catch (e) {}
                        });
                    });
                }
            }, function () {});
            if (this.viewMode === 'double' && this.pageRightUrl) {
                window.Bridge.invoke('GetPageWatermarks', (Number(this.curPage) || 1) + 1).then(function (j2) {
                    let b = []; try { b = JSON.parse(j2); } catch (e) { return; }
                    if (Array.isArray(b)) {
                        self.wmBoxesRight = b;
                        /* V1.0.0.18：双页视图右页框同样按右页基准重算预览字号+框高 */
                        self.$nextTick(function () {
                            (b || []).forEach(function (bx) {
                                try { self.wmRefitToPage(bx, self.wmSideDisp(bx)); } catch (e) {}
                            });
                        });
                    }
                }, function () {});
            } else { this.wmBoxesRight = []; }
        },

        /* ---- 样式 ---- */
        /* 框定位（外层）：位置/尺寸/旋转（旋转绕中心） */
        wmOuterStyle(b, dispW, ptW, ptH, tick) { /* V1.0.0.53：第5参 gridCellTick 仅作响应式依赖（resize 触发重渲染），内部不参与计算 */
            const dispH = dispW * ((ptH || 842) / (ptW || 595));
            // V1.0.0.15（方案A）：图片模式与 PDF 统一"框驱动字"——框 = 保存的框尺寸（b.w/b.h），字号随框高二分，
            //           文字在框内换行、超框显示不全（预览=输出）。原 V1.0.0.10 图片模式"框贴字"（框宽/高跟随文字、
            //           中心锚定）已移除——它使拖动角点失去缩放效果（框大小被文字锁死，看起来像移动）。
            const st = {
                left: (b.x * dispW) + 'px',
                top: (b.y * dispH) + 'px',
                width: (b.w * dispW) + 'px',
                height: (b.h * dispH) + 'px',
                transform: 'rotate(' + (b.rotation || 0) + 'deg)',
            };
            return st;
        },
        /* 计算字号：根据框宽和字符数量，计算行数和字号 */
        /* 用给定字号 + 框宽算自动换行行数 */
        wmCalcLines(text, boxWidthPx, fontSize) {
            var totalWidth = 0;
            for (var i = 0; i < text.length; i++) {
                var ch = text.charCodeAt(i);
                totalWidth += (ch >= 0x4e00 && ch <= 0x9fff) ? fontSize : fontSize / 2;
            }
            return Math.max(1, Math.ceil(totalWidth / Math.max(1, boxWidthPx)));
        },
        /* 二分搜索最佳字号：框驱动字 */
        wmCalcFontSize(b, dispW, dispH) {
            var boxWidthPx = Math.max(1, b.w * dispW);
            var boxHeightPx = Math.max(1, b.h * dispH);
            // 编辑模式下，用 wmEditText[b.id]，不是 b.text
            var text = String((this.wmEditText && this.wmEditText[b.id]) || b.text || '');
            // 字号边界：最小 8px，最大 200px
            var minFs = 8;
            var maxFs = 200;
            // 字号初值：从当前字号开始
            var fs = this.wmFsMap[b.id] || (b.h * dispH * 0.8);
            fs = Math.max(minFs, Math.min(maxFs, fs));
            // 二分搜索：最多 10 次
            for (var i = 0; i < 10; i++) {
                var lines = this.wmCalcLines(text, boxWidthPx, fs);
                var estimatedHeight = lines * fs;
                // 误差在 10px 以内，收敛
                if (Math.abs(estimatedHeight - boxHeightPx) <= 10) break;
                // 预估高度 > 框高 → 字号往小试
                if (estimatedHeight > boxHeightPx) {
                    maxFs = fs;
                } else {
                    // 预估高度 < 框高 → 字号往大试
                    minFs = fs;
                }
                fs = (minFs + maxFs) / 2;
            }
            // 应用 fontScale
            fs = fs * (b.fontScale > 0 ? b.fontScale : 0.8);
            if (this._wmDiag && window.Bridge && window.Bridge.invoke) window.Bridge.invoke("WriteDebugLog", "[WM-CALC] b.h=" + b.h + " dispH=" + dispH + " boxHeightPx=" + boxHeightPx + " fs=" + fs); /* V1.0.0.14：高频日志加诊断守卫——拖动每帧/每次输入不再同步写文件（卡顿根因） */
            return fs;
        },
        /* 文字渲染（内层）：字号（含超宽收窄）/行距/字距/对齐/颜色/透明度/样式/装饰——与输出端 StampEngine.DrawTextWatermark 同公式 */
        wmTextStyle(b, dispW, ptW, ptH, fsOverride, tick) { /* V1.0.0.52：第5参 fsOverride——grid 视图按格宽重算字号传入，不污染 wmFsMap；V1.0.0.53：第6参 gridCellTick 仅作响应式依赖（resize 触发重渲染），内部不参与计算 */
            const dispH = dispW * ((ptH || 842) / (ptW || 595));
            const bw = b.w * dispW, bh = b.h * dispH;
            const lines = String(b.text || '').split('\n');
            let maxChars = 0;
            for (let i = 0; i < lines.length; i++) { if (lines[i].length > maxChars) maxChars = lines[i].length; }
            // fs 用 wmFsMap 持久化（跨 refreshPageWatermarks 保留）
            if (!this.wmFsMap) this.wmFsMap = {};
            // 只在调整文本框大小时重新计算字号（不在移动和旋转时重新计算）
            let fs = (fsOverride && fsOverride > 0) ? fsOverride : this.wmFsMap[b.id];
            if (!fs || fs < 2) { var _side = this.wmSideDisp(b); if (_side) { var _dispH = _side.dispW * (_side.ptH / _side.ptW); fs = this.wmCalcFontSize(b, _side.dispW, _dispH); } }
            if (this._wmDiag && window.Bridge && window.Bridge.invoke) window.Bridge.invoke("WriteDebugLog", "[WM-FS-DIAG] b.id=" + b.id + " wmFsMap=" + (this.wmFsMap ? "yes" : "no") + " wmFsMap[b.id]=" + this.wmFsMap[b.id] + " b.h=" + b.h + " fs=" + fs);
            const ls = b.letterSpacing || 0;
            const adv = fs * Math.max(0.2, 1 + ls / 100);
            // 已删除：超宽收窄逻辑（框宽固定后应该换行，不应该缩小字号）
            if (fs < 2) fs = 2;
            // V1.0.0.10: 已按用户决策移除图片模式超宽收窄——跨图时字号保持短边比例，超边界不调字号（显示不全就显示不全）；
            //           框贴字（wmOuterStyle）负责跟随文字，行超图宽时文字可能出图界被裁（与输出一致）
            if (this._wmDiag && window.Bridge && window.Bridge.invoke) window.Bridge.invoke("WriteDebugLog", "[WM-FS] b.w=" + b.w + " b.h=" + b.h + " b.h0=" + b.h0 + " dispH=" + dispH + " fontScale=" + b.fontScale + " bw=" + bw + " bh=" + bh + " maxChars=" + maxChars + " fs=" + fs + " text=" + (b.text||"").substring(0,20));

            // V303: 快照方案——测量当前水印框的换行结果（每行的文字），保存到 b.wrapLines
            // V1.0.0.7: 仅编辑态防抖测快照（渲染/切图不再自动重测——图片/PDF 文件夹批次共享框时，快照不得被跨图/跨文件覆盖）
            if (this.wmEditingId === b.id) { this.wmCaptureWrapLinesDebounced(b); }
            const deco = [];
            if (b.underline) deco.push('underline');
            if (b.strike) deco.push('line-through');
            const o = {
                opacity: Math.max(0.01, Math.min(1, (b.opacity === undefined ? 100 : b.opacity) / 100)),
                color: this.wmArgbToHex(b.colorArgb),
                fontSize: fs + 'px',
                lineHeight: ((this.wmEditingId === b.id ? fs : fs * Math.max(0.5, 1 + (b.lineSpacing || 0) / 100))) + 'px',  // V271 编辑态行高=字号（选区背景与文字同高，多行不重叠）
                letterSpacing: (fs * ls / 100) + 'px',
                textAlign: ['left', 'center', 'right'][b.align || 0],
                fontStyle: b.italic ? 'italic' : 'normal',
                fontWeight: b.bold ? 'bold' : 'normal',
                textDecoration: deco.join(' '),
                fontFamily: (b.fontName || '微软雅黑') + ",Microsoft YaHei,'Segoe UI',sans-serif",
                whiteSpace: 'pre-wrap',
                wordBreak: 'break-all',
            };
            return o;
        },
        /* V310: 快照方案——创建隐藏 div，复制水印框样式，用 Range 逐字符测量换行结果 */
        /* V1.0.0.16: 新增 skipUpdate 参数——拖动中实时测量时跳过 UpdateWatermarkBox（C# 往返），纯前端更新快照；松手后补测全量保存 */
        /* V1.0.0.18: 新增 sideOpt/fsOpt 参数——输出前逐图模拟测量（图片模式用每图实际像素尺寸 + 输出端字号 FsToS×短边），
           不依赖当前显示图；预览路径不传则沿用 wmSideDisp(b) 与 wmFsMap */
        wmCaptureWrapLines(b, skipUpdate, sideOpt, fsOpt) {
            try {
                if (this._wmDiag && window.Bridge && window.Bridge.invoke) {
                    // V2.4.0.348 诊断：记录测量基准（页面模式/页面 pt 尺寸/框宽 px/字号），用于判断快照是按哪个页面测的
                    var _sideDiag = sideOpt || (this.wmSideDisp ? this.wmSideDisp(b) : null);
                    var _modeDiag = this.imgMode ? 'img' : 'pdf';
                    window.Bridge.invoke('WriteDebugLog', '[WM-CAPTURE-ENTER] b.id=' + b.id + ' mode=' + _modeDiag
                        + ' ptW=' + (_sideDiag ? _sideDiag.ptW : 0) + ' ptH=' + (_sideDiag ? _sideDiag.ptH : 0)
                        + ' dispW=' + (_sideDiag ? Math.round(_sideDiag.dispW) : 0)
                        + ' b.w=' + (b.w||0) + ' b.h=' + (b.h||0) + ' boxWpx=' + Math.round((_sideDiag ? _sideDiag.dispW : 0) * (b.w||0))
                        + ' fs=' + (this.wmFsMap[b.id] || 0)
                        + ' text=' + (b.text||'').substring(0,30));
                }

                // 1. 创建一个隐藏的 div，直接用 b 的参数设置样式（不用找 srcEl，避免编辑态找不到）
                var hiddenDiv = document.createElement('div');
                hiddenDiv.style.position = 'absolute';
                hiddenDiv.style.left = '-9999px';
                hiddenDiv.style.top = '-9999px';
                hiddenDiv.style.visibility = 'hidden';
                hiddenDiv.style.whiteSpace = 'pre-wrap';
                hiddenDiv.style.wordBreak = 'break-all';
                // 用 b 的参数设置样式（V1.0.0.18：支持传入 sideOpt/fsOpt 指定测量基准，不依赖当前显示图）
                var fs = fsOpt || this.wmFsMap[b.id] || 40;
                var dispW = (sideOpt && sideOpt.dispW) ? sideOpt.dispW : this.wmSideDisp(b).dispW;
                var boxWpx = dispW * b.w; // 框宽（像素）
                hiddenDiv.style.width = boxWpx + 'px';
                hiddenDiv.style.fontSize = fs + 'px';
                hiddenDiv.style.fontFamily = (b.fontName || '微软雅黑') + ",Microsoft YaHei,sans-serif";
                hiddenDiv.style.fontWeight = b.bold ? 'bold' : 'normal';
                hiddenDiv.style.fontStyle = b.italic ? 'italic' : 'normal';
                hiddenDiv.style.letterSpacing = (fs * (b.letterSpacing || 0) / 100) + 'px';
                hiddenDiv.style.lineHeight = (this.wmEditingId === b.id ? fs : fs * Math.max(0.5, 1 + (b.lineSpacing || 0) / 100)) + 'px';
                hiddenDiv.textContent = b.text || '';
                document.body.appendChild(hiddenDiv);

                if (this._wmDiag && window.Bridge && window.Bridge.invoke) {
                    window.Bridge.invoke('WriteDebugLog', '[WM-CAPTURE-HIDDEN] width=' + hiddenDiv.style.width + ' fontSize=' + hiddenDiv.style.fontSize);
                }

                // 3. 逐字符测量每个字符的中心 y 坐标，判断属于哪一行
                var textNode = hiddenDiv.firstChild;
                if (!textNode || textNode.nodeType !== Node.TEXT_NODE) {
                    document.body.removeChild(hiddenDiv);
                    if (this._wmDiag && window.Bridge && window.Bridge.invoke) {
                        window.Bridge.invoke('WriteDebugLog', '[WM-CAPTURE-NO-TEXTNODE]');
                    }
                    return;
                }

                var text = b.text || '';
                var lines = [];
                var lineRanges = []; // V315: 记录每行在原始文本中的起始和结束索引
                var curLine = '';
                var curStart = 0;
                var curY = null;
                var lineTolerance = 5; // 同一行的 y 坐标误差容忍度（像素）

                for (var i = 0; i < text.length; i++) {
                    var range = document.createRange();
                    range.setStart(textNode, i);
                    range.setEnd(textNode, i + 1);
                    var rect = range.getBoundingClientRect();
                    // 用中心 y 坐标（不用 top，因为不同字符的包围盒高度不一样）
                    var y = rect.top + rect.height / 2;

                    if (curY === null) {
                        curY = y;
                        curLine = text[i];
                        curStart = i;
                    } else if (Math.abs(y - curY) < lineTolerance) {
                        // 同一行
                        curLine += text[i];
                    } else {
                        // 新的一行
                        lines.push(curLine);
                        lineRanges.push({ start: curStart, end: i - 1 });
                        curLine = text[i];
                        curStart = i;
                        curY = y;
                    }
                }
                if (curLine) {
                    lines.push(curLine);
                    lineRanges.push({ start: curStart, end: text.length - 1 });
                }

                // V315: 测量每行的行宽比例（行宽 / 框宽），传给后端，保证水平方向完全一致
                var widths = [];
                var topRatios = [];
                var boxWidth = hiddenDiv.getBoundingClientRect().width; // 框宽（像素）
                var boxHeight = hiddenDiv.getBoundingClientRect().height; // 框高（像素）
                for (var li = 0; li < lineRanges.length; li++) {
                    var start = lineRanges[li].start;
                    var end = lineRanges[li].end;
                    if (start > end) { widths.push(0); continue; }
                    // 第一个字符的左边缘
                    var firstRange = document.createRange();
                    firstRange.setStart(textNode, start);
                    firstRange.setEnd(textNode, start + 1);
                    var firstRect = firstRange.getBoundingClientRect();
                    // 最后一个字符的右边缘
                    var lastRange = document.createRange();
                    lastRange.setStart(textNode, end);
                    lastRange.setEnd(textNode, end + 1);
                    var lastRect = lastRange.getBoundingClientRect();
                    var lineWidth = lastRect.right - firstRect.left;
                    // 行宽比例 = 行宽 / 框宽
                    var widthRatio = boxWidth > 0 ? lineWidth / boxWidth : 0;
                    widths.push(widthRatio);
                    // V323: 测量行起始y坐标比例（每行顶部距离框顶部的比例），传给后端
                    var boxTop = hiddenDiv.getBoundingClientRect().top;
                    var lineTopY = firstRect.top - boxTop;
                    var topRatio = boxHeight > 0 ? lineTopY / boxHeight : 0;
                    topRatios.push(topRatio);
                }

                // 删除隐藏的 div
                document.body.removeChild(hiddenDiv);

                // 保存到水印框
                b.wrapLines = lines;
                b.wrapLineWidths = widths;
                b.wrapLineTopRatios = topRatios;
                if (this._wmDiag && window.Bridge && window.Bridge.invoke) {
                    var log = '[WM-CAPTURE-OK] b.id=' + b.id + ' lines=' + lines.length;
                    for (var li = 0; li < Math.min(lines.length, 10); li++) {
                        log += ' L' + li + '=[' + lines[li].substring(0,20) + '] w=' + widths[li].toFixed(3) + ' top=' + topRatios[li].toFixed(3);
                    }
                    window.Bridge.invoke('WriteDebugLog', log);
                }

                // V309: 测完后立即更新到后端（V1.0.0.16: skipUpdate=true 时跳过——拖动中每帧测量不往返 C#，松手后补测全量保存）
                if (!skipUpdate && window.Bridge && window.Bridge.invoke) {
                    window.Bridge.invoke('UpdateWatermarkBox', b.id, JSON.stringify(this.wmBoxToJson(b))).then(function(){}, function(){});
                }
            } catch (e) {
                if (this._wmDiag && window.Bridge && window.Bridge.invoke) {
                    window.Bridge.invoke('WriteDebugLog', '[WM-CAPTURE-ERR] ' + e.message + ' stack=' + e.stack);
                }
            }
        },
        /* V1.0.0.18：跨图/跨页后框随字——hidden div 按指定基准测渲染高度（不写 wrapLines，预览渲染用 CSS 换行，输出快照不受污染） */
        wmMeasureBoxHeight(b, fs, side) {
            if (!side || side.dispW <= 0) return 0;
            /* V1.0.0.20：与预览渲染一致——按快照行（wrapLines，nowrap 单行）测高，不按 CSS 自动换行。
               预览 .wm-wl-line 为 white-space:nowrap 单行渲染（行数恒=快照行数），故总高 = 单行高 × 行数；
               旧实现用 pre-wrap+break-all 自动换行测高，长行被折成多行导致框高虚高（与渲染不一致）。 */
            var lines = (b.wrapLines && b.wrapLines.length) ? b.wrapLines : String(b.text || '').split('\n');
            var lineCount = Math.max(1, lines.length);
            var singleDiv = document.createElement('div');
            singleDiv.style.position = 'absolute';
            singleDiv.style.left = '-9999px';
            singleDiv.style.top = '-9999px';
            singleDiv.style.visibility = 'hidden';
            singleDiv.style.whiteSpace = 'nowrap';
            singleDiv.style.fontSize = fs + 'px';
            singleDiv.style.fontFamily = (b.fontName || '微软雅黑') + ",Microsoft YaHei,sans-serif";
            singleDiv.style.fontWeight = b.bold ? 'bold' : 'normal';
            singleDiv.style.fontStyle = b.italic ? 'italic' : 'normal';
            singleDiv.style.letterSpacing = (fs * (b.letterSpacing || 0) / 100) + 'px';
            singleDiv.style.lineHeight = (fs * Math.max(0.5, 1 + (b.lineSpacing || 0) / 100)) + 'px';
            singleDiv.textContent = '测';
            document.body.appendChild(singleDiv);
            var singleH = singleDiv.getBoundingClientRect().height;
            document.body.removeChild(singleDiv);
            return singleH * lineCount;
        },
        /* V1.0.0.19：PDF 翻页/切视图后重测全部预览框（左页+右页）；V1.0.0.20：图片模式（切图/缩放/resize）同样重测（wmSideDisp img 分支返回图片基准） */
        wmRefitAllPages() {
            const self = this;
            this.$nextTick(function () {
                (self.wmBoxes || []).forEach(function (b) { try { self.wmRefitToPage(b, self.wmSideDisp(b)); } catch (e) {} });
                if (self.wmBoxesRight && self.wmBoxesRight.length) {
                    (self.wmBoxesRight || []).forEach(function (bx) { try { self.wmRefitToPage(bx, self.wmSideDisp(bx)); } catch (e) {} });
                }
            });
        },
        /* V1.0.0.18：跨图/跨页后重算预览字号 + 框高（框随字：上下 10px 边距贴合文字；w/位置不动；wrapLines 不碰——输出快照不受污染）。
           字号基准：FsToS×显示短边（预览=输出——图片 DrawBox / PDF DrawTextWatermark 均为 FsToS×短边）；无 FsToS 时框高二分兜底 */
        wmRefitToPage(b, side) {
            if (!side || side.dispW <= 0) return;
            var dispH = side.dispW * (side.ptH || 1) / (side.ptW || 1);
            if (dispH <= 0) return;
            if (!this.wmFsMap) this.wmFsMap = {};
            var shortPt = Math.min(side.ptW || 1, side.ptH || 1);
            var shortDisp = side.dispW * shortPt / (side.ptW || 1);
            var fs = (b.fsToS > 0 && shortDisp > 0) ? b.fsToS * shortDisp : this.wmCalcFontSize(b, side.dispW, dispH);
            fs = Math.max(8, Math.min(200, fs));
            this.wmFsMap[b.id] = fs;
            var h = this.wmMeasureBoxHeight(b, fs, side);
            if (h > 0) { b.h = Math.min(0.9, (h + 20) / dispH); }
            if (window.Bridge && window.Bridge.invoke) { /* V1.0.0.19：诊断——每次跨图/跨页重测记录基准与结果 */
                window.Bridge.invoke('WriteDebugLog', '[WM-REFIT] b.id=' + b.id + ' mode=' + (this.imgMode ? 'img' : 'pdf')
                    + ' ptW=' + (side.ptW || 0) + ' ptH=' + (side.ptH || 0) + ' dispW=' + Math.round(side.dispW || 0)
                    + ' shortDisp=' + Math.round(shortDisp) + ' fsToS=' + (b.fsToS || 0) + ' fs=' + fs.toFixed(1)
                    + ' boxH_px=' + h.toFixed(0) + ' dispH=' + dispH.toFixed(0) + ' b.h_old=' + (b.h || 0).toFixed(3) + ' b.h_new=' + (Math.min(0.9, (h + 20) / dispH)).toFixed(3)
                    + ' text=' + (b.text || '').substring(0, 20));
            }
        },
        /* V2.4.0.356: 编辑态换行测量防抖（400ms 空闲才执行 wmCaptureWrapLines），消除连续输入时逐字符 Range 布局卡顿；
         * 非编辑态（拖拽/缩放/切页/退出编辑后的重渲染）立即同步测量，保证输出快照实时准确 */
        wmCaptureWrapLinesDebounced(b) {
            if (this._wmSizing) return; /* V2.4.0.396: 拖拽/缩放/旋转期间跳过换行测量(每帧全量测量是拖动卡顿根因); 松手后 _wmDragUpHandler 补测一次 */
            if (this.wmEditingId !== b.id) { this.wmCaptureWrapLines(b); return; }
            this._wmWrapPending = b;
            if (this._wmWrapTimer) clearTimeout(this._wmWrapTimer);
            const self = this;
            this._wmWrapTimer = setTimeout(function () {
                self._wmWrapTimer = null;
                const pb = self._wmWrapPending;
                self._wmWrapPending = null;
                if (pb) self.wmCaptureWrapLines(pb);
            }, 400);
        },
        /* V1.0.0.17：非编辑态渲染兜底——wrapLines 缺失时渲染后补测一次快照（不阻塞当前渲染）；返回快照行或按 \n 手动分行（不实时断行，移动/选中永不重排） */
        wmEnsureSnap(b) {
            if (this.wmEditingId !== b.id && (!b.wrapLines || !b.wrapLines.length)) {
                const self = this;
                this.$nextTick(function () { try { self.wmCaptureWrapLines(b); } catch (e) {} });
            }
            if (b.wrapLines && b.wrapLines.length) return b.wrapLines;
            return String(b.text || '').split('\n');
        },
        /* V2.4.0.356: 取消挂起的防抖测量（退出/取消编辑时调用，避免重复测量；退出后的重渲染会同步计算一次） */
        _wmCancelWrapTimer() {
            if (this._wmWrapTimer) { clearTimeout(this._wmWrapTimer); this._wmWrapTimer = null; }
            this._wmWrapPending = null;
        },
        wmArgbToHex(argb) {
            const rgb = (argb || 0) & 0xFFFFFF;
            return '#' + ('000000' + rgb.toString(16)).slice(-6);
        },
        wmHexToArgb(hex) {
            let h = String(hex || '#000000').replace('#', '');
            if (h.length === 3) h = h.split('').map(c => c + c).join('');
            return parseInt(h || '000000', 16) | 0xFF000000;
        },
        wmClamp(v, lo, hi) { return v < lo ? lo : (v > hi ? hi : v); },

        /* ---- 添加/删除 ---- */
        addWatermarkBox() {
            if (this.imgMode && this.imgLoaded) { return this.imgAddWatermarkBox(); }
            // V1.0.0.6: PDF 双页视图下添加文字水印 → 自动切回单页视图（双页下无法精确定位/编辑水印框，须先切单页再放框）
            // V1.0.0.46 需求1：4/8 页网格仅浏览，同样切单页后再放框
            if (this.viewMode === 'double' || this.viewMode === 'grid4' || this.viewMode === 'grid8') { this.setView('single'); }
            if (!window.Bridge) { this.opHint = '浏览器预览模式：请使用壳程序（EXE）'; return; }
            if (!this.pdfLoaded || this.debugActive) { this.opHint = '请先加载 PDF 文件再添加水印'; return; }
            const self = this;
            const x = 0.225, y = 0.42, w = 0.55, h = 0.08;
            const p = {
                text: '双击编辑文字', fontName: '微软雅黑', fontScale: 0.8, colorArgb: 0xFF1F2329,
                opacity: 40, bold: false, italic: false, underline: false, strike: false,
                letterSpacing: 0, lineSpacing: 0, align: 1, rotation: 35  // V1.0.0.46：新建框默认 35° / 不透明度 40
            };
            var page = self.wmApplyAllPages ? 0 : (Number(self.curPage) || 1); window.Bridge.invoke('AddWatermarkBox', page, x, y, w, h, JSON.stringify(p)).then(function (json) {
                let r = {}; try { r = JSON.parse(json); } catch (e) {}
                if (r.ok) {
                    // V2.4.0.164：不调用 refreshPageWatermarks，直接把新框加到 wmBoxes
                    if (r.box) {
                        self.wmBoxes.push(r.box);
                    }
                    self.wmSelId = r.box ? r.box.id : null;
                    // 新建框后：记录初始框高 b.h0，测量默认文字宽度调整框宽
                    self.$nextTick(() => {
                        var b = self.wmBoxes.find(x => x.id === self.wmSelId);
                        if (b) {
                            b.h0 = b.h;  // 记录初始框高（Vue 3 直接赋值）
                            b.fs0 = b.h * (b.fontScale > 0 ? b.fontScale : 0.8) * 768.4;  // 记录初始字高（像素）
                            // 初始化 wmFsMap[b.id]
                            if (!self.wmFsMap) self.wmFsMap = {};
                            var sideInit = self.wmSideDisp(b);
                            if (sideInit && sideInit.dispW > 0) {
                                var dispHInit = sideInit.dispW * (sideInit.ptH / sideInit.ptW);
                                self.wmFsMap[b.id] = self.wmCalcFontSize(b, sideInit.dispW, dispHInit);
                            }
                            if (window.Bridge && window.Bridge.invoke) window.Bridge.invoke("WriteDebugLog", "[WM-INIT] b.h0=" + b.h0 + " b.fs0=" + b.fs0);
if (window.Bridge && window.Bridge.invoke) window.Bridge.invoke("WriteDebugLog", "[WM-NEW] h0 set: b.id=" + b.id + " b.h=" + b.h + " b.h0=" + b.h0)
                            var side = self.wmSideDisp(b);
                            if (window.Bridge && window.Bridge.invoke) window.Bridge.invoke("WriteDebugLog", "[WM-NEW] side=" + JSON.stringify(side));
                            if (side && side.dispW > 0) {
                                var dispH = side.dispW * (side.ptH / side.ptW);
                                // V275：测量文字宽度用实际字号（wmFsMap 已初始化），不是固定50px，避免大窗口下框宽偏窄导致换行溢出
                                var fs = self.wmFsMap[b.id] || 50;
                                var measurer = document.createElement("span");
                                measurer.style.cssText = "position:absolute;visibility:hidden;white-space:nowrap;font:" + fs + "px " + (b.fontName || "微软雅黑") + ";";
            measurer.textContent = b.text;
                            if (window.Bridge && window.Bridge.invoke) window.Bridge.invoke("WriteDebugLog", "[WM-NEW] text=" + b.text + " fs=" + fs);
                                document.body.appendChild(measurer);
                                var textWidth = measurer.offsetWidth;
                                document.body.removeChild(measurer);
                                var cx = b.x + b.w / 2;
                                // 框宽 = 文字宽度 + 10px（留余量）
                                b.w = Math.min(1.0 - b.x, (textWidth + 10) / side.dispW);
                                b.x = cx - b.w / 2;
                            if (window.Bridge && window.Bridge.invoke) window.Bridge.invoke("WriteDebugLog", "[WM-NEW] text=" + b.text + " textWidth=" + textWidth + " b.w_old=" + b.w + " b.w_new=" + (Math.min(1.0 - b.x, textWidth / side.dispW)));
                            }
                            // V1.0.0.7: 新建框测宽调框宽后立即测换行快照（当前页 CSS 断行）；渲染/切图不再自动重测
                            if (typeof self.wmCaptureWrapLines === 'function') { try { self.wmCaptureWrapLines(b); } catch (e) {} }
                            // V1.0.0.42：添加水印框后自动进入编辑状态（无需再双击）
                            self.wmDblEditStart(b);
                        }
                    });
                    self.opHint = '已添加文字水印：双击水印框编辑文字；拖拽移动，四角缩放，下方按钮旋转/移动；右键删除';
                    self.addLog('第 ' + (Number(self.curPage) || 1) + ' 页已添加文字水印', 'ok');
                } else { self.opHint = r.error || '添加水印失败'; self.addLog(r.error || '添加水印失败', true); }
            }, function (e) { self.opHint = '添加水印失败：' + e.message; self.addLog('添加水印失败：' + e.message, true); });
        },
        removeWatermarkBox(id) {
            if (!window.Bridge) { return; }
            const self = this;
            window.Bridge.invoke('RemoveWatermarkBox', id).then(function (r) {
                if (String(r) === 'ok') {
                    self.wmBoxes = self.wmBoxes.filter(b => b.id !== id);
                    self.wmBoxesRight = self.wmBoxesRight.filter(b => b.id !== id);
                    if (self.wmSelId === id) self.wmSelId = null;
                    if (self.wmEditingId === id) self.wmEditingId = null;
                    self.addLog('已删除文字水印', 'ok');
                } else if (String(r).indexOf('不存在') >= 0) {
                    // V271 容错：C# 端无此框（历史遗留粘贴框）时前端兜底删除
                    self.wmBoxes = self.wmBoxes.filter(b => b.id !== id);
                    self.wmBoxesRight = self.wmBoxesRight.filter(b => b.id !== id);
                    if (self.wmSelId === id) self.wmSelId = null;
                    if (self.wmEditingId === id) self.wmEditingId = null;
                    self.addLog('已删除文字水印', 'ok');
                } else { self.opHint = String(r); self.addLog(String(r), true); }
            }, function (e) { self.addLog('删除水印失败：' + e.message, true); });
        },
        removePageWatermarks() {
            if (!window.Bridge) { return; }
            const self = this;
            const page = Number(this.curPage) || 1;
            window.Bridge.invoke('RemovePageWatermarks', page).then(function (json) {
                let r = {}; try { r = JSON.parse(json); } catch (e) {}
                if (r.ok) {
                    self.wmBoxes = []; self.wmSelId = null; self.wmEditingId = null;
                    self.opHint = '已清空第 ' + page + ' 页全部水印框（' + (r.removed || 0) + ' 个）';
                    self.addLog('已清空第 ' + page + ' 页全部水印框', 'ok');
                } else { self.opHint = r.error || '清空失败'; }
            }, function (e) { self.opHint = '清空失败：' + e.message; });
        },
        clearAllWatermarks() {
            if (!window.Bridge) { return; }
            const self = this;
            window.Bridge.invoke('ClearWatermarks').then(function (json) {
                let r = {}; try { r = JSON.parse(json); } catch (e) {}
                if (r.ok) {
                    self.wmBoxes = []; self.wmBoxesRight = []; self.wmSelId = null; self.wmEditingId = null;
                    self.opHint = '已清空当前文档全部水印框（' + (r.removed || 0) + ' 个）';
                    self.addLog('已清空当前文档全部水印框', 'ok');
                } else { self.opHint = r.error || '清空失败'; }
            }, function (e) { self.opHint = '清空失败：' + e.message; });
        },

        /* ---- 交互：命中 + 拖拽 ---- */
        wmDown(e, b, hit) {
            if (!this.wmEditMode && !this.imgMode) { return; } /* V1.0.0.54：PDF 非编辑模式水印框只读 */
            if (e.button !== 0) return;
            e.preventDefault(); e.stopPropagation();
            this.wmSelId = b.id;
            if (hit === 'move') {
                // V100 对齐 WPF MainWindow.xaml.cs L3295-3304：移动按钮相对位移（X=X0+(relX-rel0X)），不瞬移框中心
                this._wmMoveBox = b;
                this._wmMoveDisp = this.wmSideDisp(b);
                this._wmMoveX0 = b.x; this._wmMoveY0 = b.y;
                this._wmMoveRel0 = this._mouseToPageRel(e);
                this._wmSizing = true; /* V2.4.0.396: 移动按钮拖动期间跳过换行测量 */
                window.addEventListener('mousemove', this._wmMoveHandler);
                window.addEventListener('mouseup', this._wmMoveUp);
                return;
            }
            const rect = e.currentTarget.getBoundingClientRect();
            let rot0 = 0;
            if (hit === 'rot') {
                const cx = rect.left + rect.width / 2, cy = rect.top + rect.height / 2;
                rot0 = Math.atan2(e.clientY - cy, e.clientX - cx) * 180 / Math.PI;
            }
            this.wmDrag = {
                hit: hit, box: b, startX: e.clientX, startY: e.clientY,
                ox: b.x, oy: b.y, ow: b.w, oh: b.h, orot: b.rotation || 0,
                disp: this.wmSideDisp(b), rect: rect, rot0: rot0
            };
            this._wmSizing = true; /* V2.4.0.396: 拖拽/缩放/旋转期间跳过换行测量 */
            window.addEventListener('mousemove', this._wmDragHandler);
            window.addEventListener('mouseup', this._wmDragUpHandler);
        },
        /* 框所在视图尺寸（单页/双页右页） */
        wmSideDisp(b) {
            if (this.imgMode && this.imgLoaded) {
                return { dispW: this.imgDispW() || this.imgW || 600, ptW: this.imgW || 1, ptH: this.imgH || 1 };
            }
            if (this.wmBoxesRight && this.wmBoxesRight.some(x => x.id === b.id)) {
                return { dispW: this.imgW2 || this.imgW || 600, ptW: this.pageRightW || this.pageW || 595, ptH: this.pageRightH || this.pageH || 842 };
            }
            if (this.viewMode === 'double') { /* V1.0.0.19：双页左页显示宽=imgW2（双页 fitPage 只更新 imgW2，imgW 是单页残留） */
                return { dispW: this.imgW2 || this.imgW || 600, ptW: this.pageW || 595, ptH: this.pageH || 842 };
            }
            return { dispW: this.imgW || 600, ptW: this.pageW || 595, ptH: this.pageH || 842 };
        },
        /* 命中测试装饰区（四角 12px、左右竖线 中部 10×24、框内） */
        wmHitTest(mx, my, rw, rh) {
            const h = 12;
            if (mx <= h && my <= h) return 'tl';
            if (mx >= rw - h && my <= h) return 'tr';
            if (mx <= h && my >= rh - h) return 'bl';
            if (mx >= rw - h && my >= rh - h) return 'br';
            if (mx <= 6 && my >= rh / 2 - 12 && my <= rh / 2 + 12) return 'el';
            if (mx >= rw - 6 && my >= rh / 2 - 12 && my <= rh / 2 + 12) return 'er';
            return 'body';
        },
        _wmDragHandler(ev) {
            const g = this.wmDrag;
            if (!g) return;
            const b = g.box;
            const disp = g.disp;
            const dispH = disp.dispW * disp.ptH / disp.ptW;
            let sx = (ev.clientX - g.startX) / disp.dispW;
            let sy = (ev.clientY - g.startY) / dispH;
            // V100 对齐 WPF L3388-3395：旋转态下角点/竖线鼠标位移先逆旋转到框本地系（rad=-Rotation）
            if (g.hit !== 'body' && g.hit !== 'rot' && (b.rotation || 0) !== 0) {
                const rad = -(b.rotation || 0) * Math.PI / 180;
                const cc = Math.cos(rad), ss = Math.sin(rad);
                const rx = sx * cc - sy * ss;
                const ry = sx * ss + sy * cc;
                sx = rx; sy = ry;
            }
            if (g.hit === 'body') {
                b.x = this.wmClamp(g.ox + sx, 0, 1 - b.w);
                b.y = this.wmClamp(g.oy + sy, 0, 1 - b.h);
            } else if (g.hit === 'tl' || g.hit === 'tr' || g.hit === 'bl' || g.hit === 'br') {
                this.wmCornerDrag(b, g.ox, g.oy, g.ow, g.oh, sx, sy, g.hit);
            } else if (g.hit === 'el' || g.hit === 'er') {
                // V100 对齐 WPF L3433-3439：EdgeL 右边固定 nw=W0-dx, nx=X0+(W0-nw)；EdgeR 左边固定 nw=W0+dx
                let nw, nx;
                if (g.hit === 'el') { nw = Math.max(0.02, g.ow - sx); nx = g.ox + (g.ow - nw); }
                else { nw = Math.max(0.02, g.ow + sx); nx = g.ox; }
                b.w = this.wmClamp(nw, 0.02, 1 - Math.max(0, g.ox));
                b.x = this.wmClamp(nx, 0, 1 - b.w);
            } else if (g.hit === 'rot') {
                // 线性旋转：鼠标水平移动距离 = 旋转角度（1px = 0.5°）
                const dx = ev.clientX - g.startX;
                let r = g.orot - dx * 0.5;  // 往左移动（dx<0）→ 逆时针旋转（角度减小）→ 左高右低
                r = r > 180 ? r - 360 : (r < -180 ? r + 360 : r);
                b.rotation = Math.round(r * 10) / 10;
            }
        },
        /* V100 对齐 WPF L3403-3432：四角等比缩放——角点方向主导（TL/TR/BR 用 dx，BL 用 dy），等比 nh=nw*(H0/W0)，用按下初始值 */
        wmCornerDrag(b, ox, oy, ow, oh, sx, sy, corner) {
            const right = ox + ow, bottom = oy + oh;
            let nx, ny, nw, nh;
            // V276：标准 resize——拖哪个角哪个角跟随鼠标，对角固定（clamp 一次性算好）
            if (corner === 'tl') {
                // 左上跟随；右 right、下 bottom 固定
                nx = Math.min(Math.max(ox + sx, 0), right - 0.02);
                ny = Math.min(Math.max(oy + sy, 0), bottom - 0.02);
                nw = right - nx;
                nh = bottom - ny;
            } else if (corner === 'tr') {
                // 右、上跟随；左 ox、下 bottom 固定
                nx = ox;
                ny = Math.min(Math.max(oy + sy, 0), bottom - 0.02);
                nw = Math.max(0.02, Math.min(ow + sx, 1 - ox));
                nh = bottom - ny;
            } else if (corner === 'bl') {
                // 左、下跟随；右 right、上 oy 固定
                nx = Math.min(Math.max(ox + sx, 0), right - 0.02);
                ny = oy;
                nw = right - nx;
                nh = Math.max(0.02, Math.min(oh + sy, 1 - oy));
            } else { // br
                // 右、下跟随；左 ox、上 oy 固定
                nx = ox; ny = oy;
                nw = Math.max(0.02, Math.min(ow + sx, 1 - ox));
                nh = Math.max(0.02, Math.min(oh + sy, 1 - oy));
            }
            b.w = nw; b.h = nh;
            b.x = nx; b.y = ny;
            /* V1.0.0.15（方案A）：恢复拖动中实时更新字号——框驱动字（wmCalcFontSize 纯计算无 DOM 测量），拖动中框大小/文字大小实时跟随；
               原 V1.0.0.14 "拖动中固定字号" 使图片模式（当时贴字）框被文字锁死、看起来像移动；贴字已删，无每帧 DOM 测量，不颤不卡 */
            var _sb = this.wmSideDisp(b);
            if (_sb && _sb.dispW > 0) { var _dh2 = _sb.dispW * _sb.ptH / _sb.ptW; this.wmFsMap[b.id] = this.wmCalcFontSize(b, _sb.dispW, _dh2); }
            /* V1.0.0.18：拖动中字号实时优先（用户确认——实时看到字大小最重要），换行拖动中不测（快照固定，不打架）；
               松手后 _wmDragUpHandler 按最终几何重算字号 + 补测快照（V1.0.0.16 的每帧换行实时已移除） */
        },
        _wmDragUpHandler(e) {
            window.removeEventListener('mousemove', this._wmDragHandler);
            window.removeEventListener('mouseup', this._wmDragUpHandler);
            if (this.wmDrag) {
                var _savedByCapture = false; /* V1.0.0.38：缩放/旋转补测快照已全量保存时跳过 wmParamSave（两次 Update 合并为一次） */
                /* V2.4.0.396: 先补测一次换行快照(拖拽中已跳过, 用最终几何/字号), 再保存——wmCaptureWrapLines 内部已全量 Update, wmParamSave 为几何兜底
                   V1.0.0.9: 单击选中（无拖动位移≤3px）不重测快照——应用方案后单击会触发重测覆盖方案保存的快照（换行/字号变化）；
                   仅真实拖动/缩放/旋转（鼠标位移>3px）才补测 */
                if (this._wmSizing) {
                    this._wmSizing = false;
                    var _dx = Math.abs(((e && e.clientX) || 0) - ((this.wmDrag && this.wmDrag.startX) || 0));
                    var _dy = Math.abs(((e && e.clientY) || 0) - ((this.wmDrag && this.wmDrag.startY) || 0));
                    if (_dx + _dy > 3 && this.wmDrag && this.wmDrag.hit !== 'body') {
                        /* V1.0.0.18：移动（body）不改文字布局，松手不补测换行（移动永不触发换行变化）；
                           缩放/旋转松手按最终几何重算一次字号，再补测快照（输出与预览一致） */
                        var _bb = this.wmDrag.box;
                        var _side = this.wmSideDisp(_bb);
                        if (_side && _side.dispW > 0) { var _dh = _side.dispW * _side.ptH / _side.ptW; this.wmFsMap[_bb.id] = this.wmCalcFontSize(_bb, _side.dispW, _dh); }
                        this.wmCaptureWrapLines(_bb); /* V1.0.0.38：补测内部已全量 UpdateWatermarkBox——标记跳过随后的 wmParamSave */
                        _savedByCapture = true;
                    }
                }
                if (!_savedByCapture) this.wmParamSave();  // V1.0.0.38：缩放/旋转已由补测保存；移动/单击仍走几何兜底
                this.wmDrag = null;
            }
        },
        /* V100：鼠标位置 → 页面归一化坐标（0~1），供移动按钮相对位移用 */
        _mouseToPageRel(e) {
            const stage = this.$refs.stage;
            if (!stage) return { x: 0, y: 0 };
            const wraps = stage.querySelectorAll('.page-wrap');
            if (!wraps.length) return { x: 0, y: 0 };
            const el = wraps[0];
            const r = el.getBoundingClientRect();
            return { x: (e.clientX - r.left) / r.width, y: (e.clientY - r.top) / r.height };
        },
        _wmMoveHandler(ev) {
            const b = this._wmMoveBox;
            if (!b) return;
            // V100 对齐 WPF L3295-3304：相对位移 b.x=Clamp(X0+(relX-rel0X),0,1-W)
            const rel = this._mouseToPageRel(ev);
            b.x = this.wmClamp(this._wmMoveX0 + (rel.x - this._wmMoveRel0.x), 0, 1 - b.w);
            b.y = this.wmClamp(this._wmMoveY0 + (rel.y - this._wmMoveRel0.y), 0, 1 - b.h);
        },
        _wmMoveUp() {
            window.removeEventListener('mousemove', this._wmMoveHandler);
            window.removeEventListener('mouseup', this._wmMoveUp);
            if (this._wmMoveBox) { this._wmMoveBox = null; this._wmSizing = false; /* V2.4.0.396: 清除拖拽标记; 移动不改文字布局无需补测 */ }
        },
        /* 双击编辑：contenteditable */
        wmDblEditStart(b) {
            if (!this.wmEditMode && !this.imgMode) { return; } /* V1.0.0.54：PDF 非编辑模式禁双击编辑 */
            if (this.wmEditText) delete this.wmEditText[b.id]; /* V1.0.0.45：进入编辑清残留编辑缓冲（上次 Esc 取消可能残留），防旧文字被误当本次输入 */
            this.wmSelId = b.id;
            this.wmEditingId = b.id;
            const self = this;
            this.$nextTick(() => {
                const ref = self.$refs['wmEdit-' + b.id];
                const el = Array.isArray(ref) ? ref[ref.length - 1] : ref;
                if (el) { el.focus(); try { const r = document.createRange(); r.selectNodeContents(el); const s = window.getSelection(); s.removeAllRanges(); s.addRange(r); } catch (e) {} }
            });
        },
        wmImeStart(b, e) { this.isComposing = true; if(e && e.target) { e.target.classList.add("ime-composing"); e.target.style.overflow="visible"; var side=this.wmSideDisp(b); var dispH=side?side.dispW*(side.ptH/side.ptW):0; var ch=b.h*dispH; e.target.style.height=ch+"px"; if(this._wmDiag&&window.Bridge&&window.Bridge.invoke) window.Bridge.invoke("WriteDebugLog","[WM-H] IME-start setH="+ch.toFixed(0)+" clientH="+e.target.clientHeight+" scrollH="+e.target.scrollHeight+" computedH="+getComputedStyle(e.target).height); } },
        wmImeEnd(b, e) { this.isComposing = false; var el=e?e.target:null; if(el) { el.classList.remove("ime-composing"); el.style.overflow="hidden"; var self=this; this.$nextTick(function(){ el.style.height=""; }); } this.wmEditInput(b); },
        /* 编辑中实时调整框宽（compositionend 后触发） */
        wmEditInput(b) {
            // V2.4.0.356: 输入路径唯一保留的汇总日志（确认输入链路正常）——V1.0.0.14 加诊断守卫（非诊断模式每次按键不写文件）
            if(this._wmDiag&&window.Bridge&&window.Bridge.invoke) window.Bridge.invoke("WriteDebugLog","[WM-EDIT] enter isComposing="+this.isComposing+" b.id="+b.id);
            if (this.isComposing) return;
            const ref = this.$refs["wmEdit-" + b.id];
            const el = Array.isArray(ref) ? ref[ref.length - 1] : ref;
            if (!el) return;
            var text = el.innerText;
            // 编辑模式下，保存新文字到 wmEditText，不更新 b.text（避免触发 Vue 重渲染）
            if (!this.wmEditText) this.wmEditText = {};
            this.wmEditText[b.id] = text;
            // V1.0.0.14：文字立即可见
            // V1.0.0.16：输入实时——去掉 nowrap（恢复 pre-wrap 实时换行，已换行内容不再跳回一行、文字不超图片边界）；框尺寸由 rAF 每帧跟随
            el.style.overflow = "visible";
            this._wmEditResizeDebounced(b);
        },
        /* V1.0.0.16：编辑态框尺寸自适应——rAF 每帧执行（连续输入每帧测最长行宽→更新框宽、测渲染高度→更新框高），框宽实时跟随输入；
           只做轻量 offsetWidth/scrollHeight 测量（不调逐字符 Range 快照），输入中偶尔卡顿为可接受代价（用户拍板） */
        _wmEditResizeDebounced(b) {
            this._wmEditResizePending = b;
            if (this._wmEditResizeTimer) { cancelAnimationFrame(this._wmEditResizeTimer); this._wmEditResizeTimer = null; }
            const self = this;
            this._wmEditResizeTimer = requestAnimationFrame(function () {
                self._wmEditResizeTimer = null;
                const pb = self._wmEditResizePending;
                self._wmEditResizePending = null;
                if (pb) self._wmEditResize(pb, false);
            });
        },
        _wmEditResize(b, syncHeight) {
            var side = this.wmSideDisp(b);
            if (!side || side.dispW <= 0) return;
            const ref = this.$refs["wmEdit-" + b.id];
            const el = Array.isArray(ref) ? ref[ref.length - 1] : ref;
            if (!el) return;
            var text = (this.wmEditText && this.wmEditText[b.id]) || b.text || '';
            // 隐藏测量元素测文字真实宽度
            var dispH = side.dispW * (side.ptH / side.ptW);
            var fs = this.wmFsMap[b.id] || (b.h * (b.fontScale > 0 ? b.fontScale : 0.8) * dispH);
            var measurer = document.createElement("span");
            measurer.style.cssText = "position:absolute;visibility:hidden;white-space:nowrap;font:" + fs + "px " + (b.fontName || "微软雅黑") + ";font-weight:" + (b.bold ? "bold" : "normal") + ";";
            document.body.appendChild(measurer);
            // V275：按最长行测宽（不是所有字总宽），多行时框宽跟随最长行
            var _lines = String(text).split("\n");
            var textWidth = 0;
            for (var _li = 0; _li < _lines.length; _li++) {
                measurer.textContent = _lines[_li];
                var _lw = measurer.offsetWidth;
                if (_lw > textWidth) textWidth = _lw;
            }
            document.body.removeChild(measurer);
            // 编辑态：字高不变，框宽随文字增长，框高随行数变化
            var textW = (textWidth + 10) / side.dispW;  // 留 10px 余量
            if (b.x > 0.1 && b.x + b.w < 0.9) {
                // 阶段 1：中心固定，向两边扩展
                var cx = b.x + b.w / 2;
                b.w = Math.min(1.0 - b.x, textW);  // 上限：不超过右边缘
                b.x = cx - b.w / 2;
            } else {
                // 阶段 2/3：已接近边缘，框宽固定
                b.w = Math.min(1.0 - b.x, b.w);  // 框宽不超过右边缘
            }
            // V273：无论阶段1/2/3，编辑态框高都随行数变化（修复换行后框高不变、文字上下被裁切）
            if (syncHeight) {
                // V1.0.0.14：退出编辑路径——同步读渲染高度（保证后续快照按最终尺寸测量；退出时一次同步布局可接受）
                var renderedHeight = el.scrollHeight;
                b.h = Math.min(0.9, (renderedHeight * 1.05) / dispH);
            } else if (!this._wmHeightMeasuring) {
                this._wmHeightMeasuring = true;
                requestAnimationFrame(() => {
                    var renderedHeight = el.scrollHeight;
                    var dispH2 = side.dispW * (side.ptH / side.ptW);
                    // 编辑态：字高不变，框高随行数变化
                    b.h = Math.min(0.9, (renderedHeight * 1.05) / dispH2);
                    this._wmHeightMeasuring = false;
                });
            }
        },
        /* V1.0.0.14：退出编辑前立即应用挂起的框尺寸自适应（同步测高度），保证快照按最终尺寸测量 */
        _wmFlushEditResize() {
            if (this._wmEditResizeTimer) { cancelAnimationFrame(this._wmEditResizeTimer); this._wmEditResizeTimer = null; }
            const pb = this._wmEditResizePending;
            this._wmEditResizePending = null;
            if (pb) this._wmEditResize(pb, true);
        },
        wmDblEditEnd(b) {
            this._wmCancelWrapTimer(); // V2.4.0.356: 退出编辑先取消挂起的防抖测量（退出后的重渲染会同步计算一次 wrapLines）
            if (this.isComposing) { return; } /* V1.0.0.14：输入法组合中失焦不退出编辑（卡顿/输入法焦点抖动导致的误退兜底） */
            if (this._wmDiag && window.Bridge && window.Bridge.invoke) window.Bridge.invoke("WriteDebugLog", "[WM-EDIT-END] b.id=" + (b && b.id) + " hasRefresh=" + (typeof this.refreshImgWrapLines) + " imgMode=" + this.imgMode);
            const ref = this.$refs["wmEdit-" + b.id];
            const el = Array.isArray(ref) ? ref[ref.length - 1] : ref;
            /* V1.0.0.45：退出编辑读取改双源——优先 wmEditText[b.id]（输入时实时同步的最新文字；DOM 可能被 Vue 重渲染
               用 {{b.text}} 旧值/空值覆盖，只读 DOM 会误判空 → 误删有内容的框），DOM innerText 兜底；两源都空才删框 */
            var editText = this.wmEditText && this.wmEditText[b.id];
            if (editText !== undefined && editText !== null) {
                b.text = String(editText).replace(/\n+$/, "");
            } else if (el && el.innerText !== undefined && el.innerText !== null) {
                b.text = String(el.innerText).replace(/\n+$/, "");
            }
            if (this.wmEditText) delete this.wmEditText[b.id]; /* V1.0.0.45：退出即清编辑缓冲——防下次"清空文字退出"读到旧文字而不删空框（保持空框自动删除设计） */
            this._wmFlushEditResize(); /* V1.0.0.14：退出前立即应用挂起的框尺寸自适应（同步测高度），保证快照按最终尺寸测量 */
            this.wmEditingId = null;
            // 空文字 → 删除框（V1.0.0.8：原 wmRemoveBox 未定义会抛异常导致框残留；改前端即时移除+后端静默同步删除，
            //           并清空快照避免按快照渲染显示旧文字；与 imgRemoveWatermarkBox 同模式）
            if (!b.text || b.text.trim().length === 0) {
                b.wrapLines = []; b.wrapLineWidths = []; b.wrapLineTopRatios = [];
                this.wmBoxes = this.wmBoxes.filter(x => x.id !== b.id);
                this.wmBoxesRight = this.wmBoxesRight.filter(x => x.id !== b.id);
                if (this.wmSelId === b.id) this.wmSelId = null;
                if (this.wmEditingId === b.id) this.wmEditingId = null;
                if (window.Bridge && window.Bridge.invoke) { window.Bridge.invoke('RemoveWatermarkBox', b.id).then(function(){}, function(){}); }
                return;
            }
            if (el) el.style.overflow = "hidden";
            // 框宽已在 wmEditInput 里算过，watch 会自动保存，不需要手动调
            // V1.0.0.7: 编辑结束立即测换行快照（原 refreshImgWrapLines 为空函数不测）；渲染/切图不再自动重测
            if (typeof this.wmCaptureWrapLines === 'function') { try { this.wmCaptureWrapLines(b); } catch (e) {} }
        },
        wmDblEditCancel(b) {
            this._wmCancelWrapTimer(); // V2.4.0.356: 取消挂起的防抖测量
            if (this.wmEditText) delete this.wmEditText[b.id]; /* V1.0.0.45：Esc 取消即清编辑缓冲——防残留文字被下次退出编辑误当本次输入 */
            this.wmEditingId = null;
        },

        /* ---- 参数持久化 ---- */
        wmBoxToJson(b) {
            var _fsToS = 0;
            try {
                var _fs = this.wmFsMap[b.id] || 0;
                var _side = this.wmSideDisp(b);
                var _dispW = _side && _side.dispW > 0 ? _side.dispW : 0; var _ptW = _side && _side.ptW > 0 ? _side.ptW : 0; var _ptH = _side && _side.ptH > 0 ? _side.ptH : 0; var _shortEdge = _ptW > 0 && _ptH > 0 ? Math.min(_ptW, _ptH) : 0;
                if (_fs > 0 && _dispW > 0 && _shortEdge > 0) _fsToS = _fs * _ptW / (_dispW * _shortEdge);
                if (_fsToS > 0) b.fsToS = _fsToS; /* V1.0.0.18：跨图/跨页重测基准（预览=输出 FsToS×短边） */
            } catch (e) {}
            var _j = {
                id: b.id,  // V2.4.0.349: 输出 id，供 ReplaceAllWatermarks 保留（生成前全量同步时 id 不丢失）
                x: b.x, y: b.y, w: b.w, h: b.h, rotation: b.rotation || 0,
                text: b.text || '', fontName: b.fontName || '微软雅黑',
                colorArgb: b.colorArgb === undefined ? 0xFF1F2329 : b.colorArgb,
                opacity: b.opacity === undefined ? 100 : b.opacity,
                bold: !!b.bold, italic: !!b.italic, underline: !!b.underline, strike: !!b.strike,
                letterSpacing: b.letterSpacing || 0, lineSpacing: b.lineSpacing || 0,
                align: b.align || 0, fontScale: b.fontScale > 0 ? b.fontScale : 0.8,
                h0: (function(){var fs=this.wmFsMap[b.id]; if(!fs) return b.h0||0; var side=this.wmSideDisp(b); if(!side) return b.h0||0; var dispHpx=side.dispW*(side.ptH/side.ptW); return fs/(dispHpx*(b.fontScale>0?b.fontScale:0.8));}.call(this)),
                fsToS: _fsToS,
                wrapLines: b.wrapLines || []
            };
            if (window.Bridge && window.Bridge.invoke) {
                var wl = _j.wrapLines || [];
                window.Bridge.invoke('WriteDebugLog', '[WM-JSON] b.id=' + b.id + ' h0=' + _j.h0 + ' fsToS=' + _fsToS + ' fs=' + (this.wmFsMap[b.id]||0) + ' text=' + (b.text||'').substring(0,20) + ' wrapLines=' + wl.length);
            }
            return _j;
        },
        wmBoxToJsonOld(b) {
            return {
                x: b.x, y: b.y, w: b.w, h: b.h, rotation: b.rotation || 0,
                text: b.text || '', fontName: b.fontName || '微软雅黑',
                colorArgb: b.colorArgb === undefined ? 0xFF1F2329 : b.colorArgb,
                opacity: b.opacity === undefined ? 100 : b.opacity,
                bold: !!b.bold, italic: !!b.italic, underline: !!b.underline, strike: !!b.strike,
                letterSpacing: b.letterSpacing || 0, lineSpacing: b.lineSpacing || 0,
                align: b.align || 0, fontScale: b.fontScale > 0 ? b.fontScale : 0.8, h0: (function(){var fs=this.wmFsMap[b.id]; if(!fs) return b.h0||0; var side=this.wmSideDisp(b); if(!side) return b.h0||0; var dispHpx=side.dispW*(side.ptH/side.ptW); return fs/(dispHpx*(b.fontScale>0?b.fontScale:0.8));}.call(this))
            };
        },
        wmParamSave() {
            const b = (this.wmDrag && this.wmDrag.box) || this.wmSel;
            if (!b || !window.Bridge || !window.Bridge.invoke) return;
            const self = this;
            window.Bridge.invoke('UpdateWatermarkBox', b.id, JSON.stringify(this.wmBoxToJson(b))).then(function (r) {
                if (String(r) !== 'ok' && r.indexOf('err') === 0) { self.opHint = String(r); }
            }, function () {});
        },
        wmParamSaveThrottled() {
            if (this._wmSaveTimer) return;
            const self = this;
            this._wmSaveTimer = setTimeout(function () { self._wmSaveTimer = null; self.wmParamSave(); }, 150);
        },
        wmFlushSave() {
            if (this._wmSaveTimer) { clearTimeout(this._wmSaveTimer); this._wmSaveTimer = null; }
            this.wmParamSave();
        },
        /* V1.0.0.38：生成入口统一 flush——取消挂起换行防抖；若仍在编辑态，读 DOM/wmEditText 文字写回 b.text 并同步补测换行快照
           （wmCaptureWrapLines 同步更新 b.wrapLines 三件套），确保生成时输出与当前预览一致（防 400ms 防抖窗口内旧文字/旧快照）。
           PDF 端 generateFiles 与图片端 imgApplyOutput 在生成前调用；空文字写回后由后端按无框跳过处理（不在此删框）。 */
        wmFlushBeforeGenerate() {
            if (typeof this._wmCancelWrapTimer === 'function') { try { this._wmCancelWrapTimer(); } catch (e) {} }
            if (!this.wmEditingId) return;
            const ref = this.$refs["wmEdit-" + this.wmEditingId];
            const el = Array.isArray(ref) ? ref[ref.length - 1] : ref;
            let editingBox = null;
            for (const arr of [this.wmBoxes, this.wmBoxesRight]) {
                const f = (arr || []).find(function (b) { return b.id === this.wmEditingId; }.bind(this));
                if (f) { editingBox = f; break; }
            }
            if (editingBox) {
                const txt = (this.wmEditText && this.wmEditText[editingBox.id]) || (el ? String(el.innerText) : '');
                editingBox.text = String(txt).replace(/\n+$/, '');
                try { this.wmCaptureWrapLines(editingBox); } catch (e) {}
            }
            this.wmEditingId = null;
        },

        /* ---- 方案管理 ---- */
        loadWmSchemes() {
            if (!window.Bridge) { return; }
            const self = this;
            window.Bridge.invoke('GetWatermarkSchemes').then(function (json) {
                let a = []; try { a = JSON.parse(json); } catch (e) {}
                if(window.Bridge&&window.Bridge.invoke) window.Bridge.invoke('WriteDebugLog','[WM-SCHEMES] GetWatermarkSchemes result=' + json);
                if (Array.isArray(a)) {
                    self.wmSchemes = a;
                    if(window.Bridge&&window.Bridge.invoke) window.Bridge.invoke('WriteDebugLog','[WM-SCHEMES] self.wmSchemes updated=' + JSON.stringify(self.wmSchemes));
                    if (self.wmSchemeCur && a.indexOf(self.wmSchemeCur) < 0) self.wmSchemeCur = '';
                }
                if(window.Bridge&&window.Bridge.invoke) window.Bridge.invoke('WriteDebugLog','[WM-SCHEMES] parsed=' + JSON.stringify(a) + ' isArray=' + Array.isArray(a));
            }, function () {});
        },
        wmSaveScheme() {
            const name = String(this.wmDlgSaveName || '').trim();
            if (!name) { this.opHint = '请输入水印框名称'; return; }
            // 获取当前选中的水印框
            const b = this.wmBoxes.find(x => x.id === this.wmCtxMenu.boxId);
            if (!b) { this.opHint = '没有选中的水印框'; return; }
            // 直接把水印框的参数传给 C# 端
            const boxJson = JSON.stringify([this.wmBoxToJson(b)]);
            const self = this;
            window.Bridge.invoke('SaveWatermarkScheme', name, boxJson).then(function (json) {
                let r = {}; try { r = JSON.parse(json); } catch (e) {}
                if (r.ok) {
                    self.wmDlgSave = false; self.wmDlgSaveName = '';
                    self.loadWmSchemes();
                    self.opHint = '水印框「' + name + '」已保存'; /* 水印框名非文件名，保持「」引用 */
                    self.addLog('水印框「' + name + '」已保存', 'ok');
                } else { self.opHint = r.error || '保存失败'; self.addLog(r.error || '保存失败', true); }
            }, function (e) { self.opHint = '保存失败：' + e.message; });
        },
        wmApplyScheme() {
            if (!this.wmSchemeCur) { this.opHint = '请先选择要应用的水印框'; return; }
            const self = this;
            window.Bridge.invoke('ApplyWatermarkScheme', this.wmSchemeCur).then(function (json) {
                let r = {}; try { r = JSON.parse(json); } catch (e) {}
                if (r.ok) {
                    if (self.imgMode) { self.refreshImgWatermarks(); } else { self.refreshPageWatermarks(); }
                    self.opHint = '方案「' + self.wmSchemeCur + '」已应用到当前页（新增 ' + (r.count || 0) + ' 个框）'; /* 方案名非文件名，保持「」引用 */
                    self.addLog('方案「' + self.wmSchemeCur + '」已应用到当前页', 'ok');
                } else { self.opHint = r.error || '应用失败'; self.addLog(r.error || '应用失败', true); }
            }, function (e) { self.opHint = '应用失败：' + e.message; });
        },
        wmDelScheme() {
            if (!this.wmSchemeCur) { this.opHint = '请先选择要删除的水印框'; return; }
            const self = this;
            const name = this.wmSchemeCur;
            window.Bridge.invoke('DeleteWatermarkScheme', name).then(function (r) {
                if (String(r) === 'ok') {
                    self.wmSchemes = self.wmSchemes.filter(s => s !== name);
                    self.wmSchemeCur = '';
                    self.opHint = '水印框「' + name + '」已删除'; /* 水印框名非文件名，保持「」引用 */
                    self.addLog('水印框「' + name + '」已删除', 'ok');
                } else { self.opHint = String(r); self.addLog(String(r), true); }
            }, function (e) { self.opHint = '删除失败：' + e.message; });
        },
        /* V1.0.0.42：下拉项内嵌删除（按名称删除，不依赖当前选中） */
        wmDelSchemeByName(name) {
            if (!name) return;
            const self = this;
            ElementPlus.ElMessageBox.confirm('确定删除水印框「' + name + '」吗？删除后不可恢复。', '删除水印框', {
                confirmButtonText: '删除',
                cancelButtonText: '取消',
                type: 'warning'
            }).then(function () {
                window.Bridge.invoke('DeleteWatermarkScheme', name).then(function (r) {
                    if (String(r) === 'ok') {
                        self.wmSchemes = self.wmSchemes.filter(s => s !== name);
                        if (self.wmSchemeCur === name) self.wmSchemeCur = '';
                        self.opHint = '水印框「' + name + '」已删除'; /* 水印框名非文件名，保持「」引用 */
                        self.addLog('水印框「' + name + '」已删除', 'ok');
                    } else { self.opHint = String(r); self.addLog(String(r), true); }
                }, function (e) { self.opHint = '删除失败：' + e.message; });
            }).catch(function () { /* 用户取消：不做任何操作 */ });
        },

        /* ---- 右键菜单 ---- */
        /* V1.0.0.7: 右键菜单延迟关闭（防鼠标略移出菜单即消失）：移出 250ms 后关、移入取消 */
        wmCtxScheduleClose() {
            if (this._wmCtxTimer) { clearTimeout(this._wmCtxTimer); this._wmCtxTimer = null; }
            const self = this;
            this._wmCtxTimer = setTimeout(function () { self._wmCtxTimer = null; self.wmCtxMenu.show = false; }, 250);
        },
        wmCtxCancelClose() {
            if (this._wmCtxTimer) { clearTimeout(this._wmCtxTimer); this._wmCtxTimer = null; }
        },
        wmShowCtxMenu(e, boxId) {
            if (!this.wmEditMode && !this.imgMode) { return; } /* V1.0.0.54：PDF 非编辑模式禁右键菜单 */
            window._wmVueInstance = this;
            this.wmCtxCancelClose();  // V1.0.0.7: 打开新菜单前取消挂起的延迟关闭
            // V271：纯状态驱动（菜单模板 v-if 渲染）；删除历史 DOM 直操/cloneNode/每次弹菜单重复挂监听
            this.wmCtxMenu.show = true;
            this.wmCtxMenu.x = e.clientX;
            this.wmCtxMenu.y = e.clientY;
            this.wmCtxMenu.boxId = boxId;
            this.wmCtxMenu.px = null;
            this.wmCtxMenu.py = null;
            this.wmSelId = boxId;
        },
        /* V271：页面空白处右键 → 只显示"粘贴"菜单；点击后框中心对准鼠标位置粘贴 */
        wmStageCtx(e) {
            if (!this.wmClipboard) return;                 // 无剪贴板内容不弹
            if (this.wmEditingId) return;                  // 文字编辑态不弹（保留浏览器文本菜单）
            this.wmCtxCancelClose();  // V1.0.0.7: 打开新菜单前取消挂起的延迟关闭
            // V277：图片模式也弹粘贴菜单（水印框已合并到 wmBoxes）
            var el = document.querySelector('.page-wrap');
            var rect = el ? el.getBoundingClientRect() : null;
            var px = null, py = null;
            if (rect && rect.width > 0 && rect.height > 0) {
                px = (e.clientX - rect.left) / rect.width;
                py = (e.clientY - rect.top) / rect.height;
            }
            this.wmCtxMenu.show = true;
            this.wmCtxMenu.x = e.clientX;
            this.wmCtxMenu.y = e.clientY;
            this.wmCtxMenu.boxId = null;
            this.wmCtxMenu.px = px;
            this.wmCtxMenu.py = py;
        },
        wmCtxCopy() {
            if(window.Bridge&&window.Bridge.invoke) window.Bridge.invoke('WriteDebugLog','[WM-CTX] wmCtxCopy clicked');
            var id = (this.wmCtxMenu && this.wmCtxMenu.boxId != null) ? this.wmCtxMenu.boxId : this.wmSelId;  // V271 兼容 ctrl+c
            var b = this.wmBoxes.find(x => x.id === id);
            if (b) {
                this.wmClipboard = JSON.parse(JSON.stringify(b));
                this.opHint = '已复制水印框';
            }
            this.wmCtxMenu.show = false;
        },
        /* V271：粘贴走 C# AddWatermarkBox 注册（前后端一致：Del 可删、输出包含、字号正常）；cx/cy 为页面 0-1 坐标（框中心），缺省用剪贴板位置+偏移 */
        wmCtxPaste(cx, cy) {
            if(window.Bridge&&window.Bridge.invoke) window.Bridge.invoke('WriteDebugLog','[WM-CTX] wmCtxPaste clicked');
            if (!this.wmClipboard) return;
            const self = this;
            const c = this.wmClipboard;
            var nx, ny;
            if (cx != null && cy != null) {
                nx = cx - c.w / 2;
                ny = cy - c.h / 2;
            } else {
                nx = c.x + 0.02;
                ny = c.y + 0.02;
            }
            nx = Math.max(0, Math.min(1 - c.w, nx));
            ny = Math.max(0, Math.min(1 - c.h, ny));
            const p = {
                text: c.text, fontName: c.fontName, fontScale: c.fontScale, colorArgb: c.colorArgb,
                opacity: c.opacity, bold: c.bold, italic: c.italic, underline: c.underline, strike: c.strike,
                letterSpacing: c.letterSpacing, lineSpacing: c.lineSpacing, align: c.align, rotation: c.rotation,
                h0: c.h0 != null ? c.h0 : 0,
                fsToS: c.fsToS || 0,
                wrapLines: c.wrapLines || []
            };
            // V277：图片模式强制 page=0（批次共享）；PDF 模式按 wmApplyAllPages
            const page = (self.imgMode && self.imgLoaded) ? 0 : (self.wmApplyAllPages ? 0 : (Number(self.curPage) || 1));
            window.Bridge.invoke('AddWatermarkBox', page, nx, ny, c.w, c.h, JSON.stringify(p)).then(function (json) {
                let r = {}; try { r = JSON.parse(json); } catch (e) {}
                if (r.ok && r.box) {
                    var nb = r.box;
                    nb.h0 = (c.h0 != null) ? c.h0 : nb.h;
                    nb.fs0 = (c.fs0 != null) ? c.fs0 : (nb.h * (nb.fontScale > 0 ? nb.fontScale : 0.8) * 768.4);
                    nb.wrapLines = c.wrapLines || [];
                    nb.fsToS = c.fsToS || 0;
                    if (!self.wmFsMap) self.wmFsMap = {};
                    var srcFs = self.wmFsMap[c.id];
                    if (srcFs != null) {
                        self.wmFsMap[nb.id] = srcFs;   // 框尺寸/字体一致 → 字号一致
                    } else {
                        var side = self.wmSideDisp(nb);
                        if (side && side.dispW > 0) {
                            var dispH = side.dispW * (side.ptH / side.ptW);
                            self.wmFsMap[nb.id] = self.wmCalcFontSize(nb, side.dispW, dispH);
                        } else {
                            self.wmFsMap[nb.id] = nb.h * 768.4 * (nb.fontScale > 0 ? nb.fontScale : 0.8);
                        }
                    }
                    self.wmBoxes.push(nb);
                    self.wmSelId = nb.id;
                    self.opHint = '已粘贴水印框';
                } else {
                    self.opHint = r.error || '粘贴水印失败';
                    self.addLog(r.error || '粘贴水印失败', true);
                }
            }, function (e) {
                self.opHint = '粘贴失败：' + e.message;
                self.addLog('粘贴水印失败：' + e.message, true);
            });
            this.wmCtxMenu.show = false;
        },
        wmCtxDelete() {
            // V392：右键删除走后端 RemoveWatermarkBox（此前只删前端数组→切文件/翻页后从后端读回“复活”）；删除成功才更新前端
            if(window.Bridge&&window.Bridge.invoke) window.Bridge.invoke('WriteDebugLog','[WM-CTX] wmCtxDelete id=' + this.wmCtxMenu.boxId);
            var id = this.wmCtxMenu.boxId;
            var self = this;
            this.wmCtxMenu.show = false;
            if (id == null) { return; }
            if (!window.Bridge) {
                this.wmBoxes = this.wmBoxes.filter(x => x.id !== id);
                if (this.wmSelId === id) this.wmSelId = null;
                return;
            }
            window.Bridge.invoke('RemoveWatermarkBox', id).then(function (r) {
                if (String(r) === 'ok') {
                    self.wmBoxes = self.wmBoxes.filter(b => b.id !== id);
                    self.wmBoxesRight = self.wmBoxesRight.filter(b => b.id !== id);
                    if (self.wmSelId === id) self.wmSelId = null;
                    if (self.wmEditingId === id) self.wmEditingId = null;
                    self.opHint = '已删除水印框';
                    self.addLog('已删除文字水印', 'ok');
                } else if (String(r).indexOf('不存在') >= 0) {
                    self.wmBoxes = self.wmBoxes.filter(b => b.id !== id);
                    self.wmBoxesRight = self.wmBoxesRight.filter(b => b.id !== id);
                    if (self.wmSelId === id) self.wmSelId = null;
                    if (self.wmEditingId === id) self.wmEditingId = null;
                    self.opHint = '已删除水印框';
                    self.addLog('已删除文字水印', 'ok');
                } else { self.opHint = String(r); self.addLog(String(r), true); }
            }, function (e) { self.addLog('删除水印失败：' + e.message, true); });
        },
        wmCtxSaveScheme() {
            if(window.Bridge&&window.Bridge.invoke) window.Bridge.invoke('WriteDebugLog','[WM-CTX] wmCtxSaveScheme clicked');
            // 打开统一样式的弹窗
            this.wmDlgSaveName = '';
            this.wmDlgSave = true;
            this.wmCtxMenu.show = false;
        }
};
