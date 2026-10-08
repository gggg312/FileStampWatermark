/* PDFQFZ Web UI 前端模块：图片批量水印（V2.4.0.97 新增，第二步）
 * 功能：拖入图片/图片文件夹自动进入图片模式（用户定稿：不设专门模式 Tab）；
 *       文件夹内图片+PDF 混合时弹窗让用户选择处理方式；
 *       图片工具条（上一张/下一张/缩放/清空/批量输出）；水印框复用卡4同一套设置与交互
 *       （wmBoxesImg 独立数组，wmTextStyle/wmOuterStyle 以图片像素尺寸为页面尺寸，预览=输出）。
 * 交互隔离：图片模式下预览区不触发盖章（stampActions.onStageDown 顶部 return）；
 *       水印框 @mousedown.stop 与 PDF 模式同一套处理。
 * 坐标系：框 X/Y/W/H 相对图片 0~1（Y 向下），与后端 WatermarkBox 一致（图片 DocumentPath、页恒 1）。
 */
window.PdfqModules = window.PdfqModules || {};
/* 注意：data（imgMode/imgQueue/wmBoxesImg 等）在 index.html 组件内定义，本模块仅提供 methods。 */
window.PdfqModules.imageWatermarkActions = {
    /* ---------- methods ---------- */
    /* V300: 回退 C# 算换行，改用 CSS 自动换行（和 PDF 一样） */
    refreshImgWrapLines(b) {
        // 空函数，不再调用 C# 算换行
    },
    /* V300: 图片模式非编辑态显示文本（直接用 b.text，CSS 自动换行） */
    wmImgDisplayText(b) {
        return b.text;
    },
        /* ===================== 拖放主入口（替代 PDF 拖放分发，供 onDropPdf 扩展） ===================== */
        /* WebView2 文件夹真实路径通道（f.path 可用时）：C# 扫描分类——纯图片自动图片模式；
         * 纯 PDF 返回 false 交给原 openDir 逻辑；混合由前端弹窗选择。返回 true=已处理。 */
        imgDropFolderWithPath(dirPath) {
            const self = this;
            window.Bridge.invoke('LoadImageFolder', dirPath).then(function (json) {
                let r = {}; try { r = JSON.parse(json); } catch (e) {}
                if (r.ok) {
                    if (r.total > 0 && r.pdfs > 0) {
                        // 混合：弹窗选择（图片已入队）
                        self.imgMixedInfo = { pdfs: r.pdfs, imgs: r.total, imgFiles: null, pdfFiles: null, folderPath: dirPath };
                        self.dlgImgMixed = true;
                    } else if (r.total > 0) {
                        self.imgShow(0);
                        self.imgRefreshQueue();
                        self.opHint = '已加载 ' + r.total + ' 张图片，自动进入图片模式';
                        self.addLog('已加载图片文件夹：共 ' + r.total + ' 张', 'ok');
                    } else {
                        // 纯 PDF 文件夹：交给原 PDF 逻辑
                        self.opHint = '文件夹内为 PDF 文件，按 PDF 方式加载';
                        self.openDir(dirPath);
                    }
                } else { self.opHint = r.error || '文件夹加载失败'; self.addLog(r.error || '文件夹加载失败', true); }
            }, function (e) { self.opHint = '文件夹加载失败：' + (e && e.message || e); });
            return true;
        },

        /* 返回 true=已处理（图片/混合）；false=交给 PDF 流程。 */
        imgHandleDataTransfer(e) {
            const dt = e.dataTransfer;
            if (!dt) return false;
            const files = Array.from(dt.files || []);
            const items = Array.from(dt.items || []);
            const imgFiles = [];   // 图片 File 对象
            const pdfFiles = [];   // PDF File 对象
            const dirEntries = []; // 目录 entry（webkitGetAsEntry）
            let hasEntry = false;
            for (let i = 0; i < items.length; i++) {
                const it = items[i];
                if (it.webkitGetAsEntry) {
                    const en = it.webkitGetAsEntry();
                    if (en && en.isDirectory) { dirEntries.push(en); hasEntry = true; }
                }
            }
            if (hasEntry) {
                // 拖入含文件夹：递归收集全部文件后统一分类决策
                const self = this;
                let pending = dirEntries.length;
                const collected = [];
                const doneCollect = function () {
                    pending--;
                    if (pending > 0) return;
                    self.imgClassifyAndRoute(collected, true);
                };
                dirEntries.forEach(function (en) {
                    self.imgReadEntry(en, function (fl) { collected.push.apply(collected, fl); doneCollect(); }, doneCollect);
                });
                return true;
            }
            // 无目录：按文件类型分类（多文件拖放）
            for (let i = 0; i < files.length; i++) {
                const f = files[i];
                if (!f || !f.name) continue;
                if (/\.pdf$/i.test(f.name)) pdfFiles.push(f);
                else if (/\.(jpe?g|png|bmp|gif|tiff?)$/i.test(f.name)) imgFiles.push(f);
            }
            if (pdfFiles.length > 0 && imgFiles.length > 0) {
                this.imgMixedInfo = { pdfs: pdfFiles.length, imgs: imgFiles.length, imgFiles: imgFiles, pdfFiles: pdfFiles };
                this.dlgImgMixed = true;
                return true;
            }
            if (imgFiles.length > 0) { this.imgLoadFiles(imgFiles); return true; }
            return false;
        },

        /* 递归读取目录 entry（webkitGetAsEntry），返回 File 列表（容错跳过读取失败项）。 */
        imgReadEntry(entry, cb, errCb) {
            if (!entry) { if (errCb) errCb(); return; }
            const self = this;
            if (entry.isFile) {
                entry.file(function (f) { cb([f]); }, function () { if (errCb) errCb(); });
            } else if (entry.isDirectory) {
                const reader = entry.createReader();
                const all = [];
                const readBatch = function () {
                    reader.readEntries(function (entries) {
                        if (!entries || entries.length === 0) { cb(all); return; }
                        let pend = entries.length;
                        entries.forEach(function (en) {
                            self.imgReadEntry(en, function (fl) { all.push.apply(all, fl); pend--; if (pend === 0) readBatch(); },
                                function () { pend--; if (pend === 0) readBatch(); });
                        });
                    }, function () { if (errCb) errCb(); });
                };
                readBatch();
            } else { if (errCb) errCb(); }
        },

        /* 分类决策：dirFiles 为 File 列表（可能含文件夹）。hasDir=true 时混合类型弹窗选择。 */
        imgClassifyAndRoute(dirFiles, hasDir) {
            const pdfs = [], imgs = [];
            for (let i = 0; i < dirFiles.length; i++) {
                const f = dirFiles[i];
                if (!f || !f.name) continue;
                if (/\.pdf$/i.test(f.name)) pdfs.push(f);
                else if (/\.(jpe?g|png|bmp|gif|tiff?)$/i.test(f.name)) imgs.push(f);
            }
            if (pdfs.length === 0 && imgs.length === 0) {
                this.opHint = '拖入内容中没有 PDF 或支持的图片文件';
                this.addLog('未找到 PDF 或支持的图片（jpg/jpeg/png/bmp/gif/tif/tiff）', true);
                return;
            }
            if (pdfs.length > 0 && imgs.length > 0) {
                this.imgMixedInfo = { pdfs: pdfs.length, imgs: imgs.length, imgFiles: imgs, pdfFiles: pdfs };
                this.dlgImgMixed = true;
                return;
            }
            if (imgs.length > 0) this.imgLoadFiles(imgs);
            else this.opHint = '拖入内容为 PDF，按 PDF 方式加载（请使用原有拖放加载 PDF 功能）';
        },

        /* 混合弹窗选择：0=按图片处理 1=按PDF处理 2=取消 */
        imgMixedChoice(choice) {
            const info = this.imgMixedInfo;
            this.dlgImgMixed = false;
            if (!info) return;
            if (choice === 0) {
                if (info.folderPath) { this.imgShow(0); this.imgRefreshQueue(); this.opHint = '已按图片处理：共 ' + (info.imgs || 0) + ' 张'; this.addLog('已按图片处理：共 ' + (info.imgs || 0) + ' 张', 'ok'); }
                else if (info.imgFiles && info.imgFiles.length > 0) this.imgLoadFiles(info.imgFiles);
                else this.opHint = '没有可加载的图片';
            } else if (choice === 1) {
                if (info.folderPath) {
                    const fp = info.folderPath;
                    if (window.Bridge) { window.Bridge.invoke('ClearImages').then(function(){}, function(){}); }
                    this.imgClearFrontend();
                    this.openDirPdf(fp);
                }
                else if (info.pdfFiles && info.pdfFiles.length > 0) this.pdfLoadFiles(info.pdfFiles);
                else this.opHint = '该内容没有 PDF 文件';
            } else if (choice === 2) {
                if (info.folderPath && window.Bridge) { window.Bridge.invoke('ClearImages').then(function(){}, function(){}); this.imgClearFrontend(); }
            }
            this.imgMixedInfo = null;
        },

        /* 仅清空前端图片态（后端队列另由 ClearImages 负责） */
        imgClearFrontend() {
            this.imgMode = false; this.imgLoaded = false; this.curImgUrl = '';
            this.imgQueue = []; this.curImgIdx = -1; this.imgW = 0; this.imgH = 0;
            this.wmBoxes = []; this.wmSelId = null; this.wmEditingId = null;  // V277：合并到 wmBoxes
        },

        /* 路径列表 → LoadImagePaths 入队进图片模式（壳层拖放/文件选择按钮/openPdf 顶部共用） */
        imgLoadByPaths(paths) {
            if (!window.Bridge) { this.opHint = '浏览器预览模式：请使用壳程序（EXE）'; return; }
            const self = this;
            window.Bridge.invoke('LoadImagePaths', JSON.stringify(paths || [])).then(function (json) {
                let r = {}; try { r = JSON.parse(json); } catch (e) {}
                if (!r || !r.ok) { self.opHint = '图片加载失败：' + ((r && r.error) || ''); self.addLog('图片加载失败：' + ((r && r.error) || ''), true); return; }
                self.imgShow(0); self.imgRefreshQueue();
                self.opHint = '已加载 ' + r.total + ' 张图片，自动进入图片模式';
                self.addLog('已加载图片：共 ' + r.total + ' 张', 'ok');
            }, function (e) { self.opHint = '图片加载失败：' + e.message; });
        },

        /* 壳层/按钮单文件路径统一分流：图片→图片模式；PDF→openPdf */
        openDroppedPath(path) {
            if (!path) return;
            if (/\.(jpe?g|png|bmp|gif|tiff?)$/i.test(String(path))) { this.imgLoadByPaths([path]); return; }
            this.openPdf(path);
        },

        /* 壳层/按钮文件夹路径统一分流：LoadImageFolder 分类 → 纯图片/混合弹窗/纯 PDF 走 openDirPdf */
        openDroppedDir(path) {
            if (!window.Bridge) { this.openDirPdf(path); return; }
            const self = this;
            window.Bridge.invoke('LoadImageFolder', path).then(function (json) {
                let r = {}; try { r = JSON.parse(json); } catch (e) {}
                if (r && r.ok && r.total > 0) {
                    if (r.pdfs > 0) { self.imgMixedInfo = { pdfs: r.pdfs, imgs: r.total, imgFiles: null, pdfFiles: null, folderPath: path }; self.dlgImgMixed = true; return; }
                    self.imgShow(0); self.imgRefreshQueue();
                    self.opHint = '已加载 ' + r.total + ' 张图片，自动进入图片模式';
                    self.addLog('已加载图片文件夹：共 ' + r.total + ' 张', 'ok');
                    return;
                }
                self.openDirPdf(path);
            }, function () { self.openDirPdf(path); });
        },

        /* 图片 File 列表 → base64 通道入队（逐张 AddImageFromBytes；首张进入图片模式） */
        imgLoadFiles(files) {
            if (!window.Bridge || !window.Bridge.invoke) { this.opHint = '浏览器预览模式：请使用壳程序（EXE）'; return; }
            const self = this;
            let first = null;
            const loadNext = function (i) {
                if (i >= files.length) {
                    if (first) self.imgShow(first);
                    self.imgRefreshQueue();
                    if (files.length > 0) self.addLog('已加载图片：共 ' + files.length + ' 张，自动进入图片模式', 'ok');
                    return;
                }
                const f = files[i];
                const rd = new FileReader();
                rd.onload = function () {
                    const b64 = String(rd.result).split(',')[1] || '';
                    window.Bridge.invoke('AddImageFromBytes', b64, f.name).then(function (json) {
                        let r = {}; try { r = JSON.parse(json); } catch (e) {}
                        if (r.ok) {
                            if (first === null) first = r.idx;
                        } else { self.addLog((r.error || '加载失败') + '：' + f.name, true); }
                        loadNext(i + 1);
                    }, function () { loadNext(i + 1); });
                };
                rd.onerror = function () { loadNext(i + 1); };
                rd.readAsDataURL(f);
            };
            loadNext(0);
        },

        /* 拖入 PDF 文件列表（混合选择"按PDF处理"时用；复用现有 PDF 加载逻辑） */
        pdfLoadFiles(files) {
            if (files.length === 0) return;
            const self = this;
            const f = files[0];
            const rd = new FileReader();
            rd.onload = function () {
                const b64 = String(rd.result).split(',')[1] || '';
                window.Bridge.invoke('OpenPdfFromBytes', b64, f.name).then(function (json) {
                    let r = {}; try { r = JSON.parse(json); } catch (e) {}
                    if (r.ok) {
                        self.opHint = '已加载 PDF：《' + f.name + '》'; /* V1.0.0.78：文件名《》包裹 */
                        self.addLog('已加载 PDF：《' + f.name + '》', 'ok');
                    } else { self.addLog(r.error || 'PDF 加载失败', true); }
                }, function () {});
            };
            rd.readAsDataURL(f);
        },

        /* ===================== 图片模式状态 ===================== */
        imgShow(idx) {
            if (!window.Bridge) return;
            if (idx < 0) idx = 0;
            const self = this;
            window.Bridge.invoke('SetCurrentImage', idx).then(function () {
                window.Bridge.invoke('GetImagePreview', idx).then(function (json) {
                    let r = {}; try { r = JSON.parse(json); } catch (e) {}
                    if (r.ok) {
                        self.imgMode = true;
                        self.wmSect.main = true;   // V357：图片模式自动展开文字水印设置（只操作文字水印）
                        if (self._expandCard) self._expandCard('watermark');  // V357：图片模式自动打开文字水印卡（navCardOn 已强制 true）
                        self.curImgIdx = r.idx;
                        self.imgPageInput = String(r.idx + 1);  // V346: 统一工具条页码同步
                        self.curImgUrl = r.url;
                        self.imgW = r.w; self.imgH = r.h;
                        self.imgLoaded = true;
                        // V2.4.0.370：图片模式=文字水印模式——进入即开启文字水印（渲染闸门+导航高亮+C# 状态四者一致；防 V360 删卡内开关后图片模式全新加载加框不显示）
                        self.watermarkEnabled = true;
                        if (window.Bridge && window.Bridge.invoke) window.Bridge.invoke('SetWatermarkEnabled', true);
                        if (window.Bridge && window.Bridge.invoke) window.Bridge.invoke("WriteDebugLog", "[IMG-DIAG] imgShow 成功 idx=" + r.idx + " w=" + r.w + " h=" + r.h + " imgDispW=" + (self.imgDispW ? self.imgDispW() : '?') + " watermarkEnabled=" + self.watermarkEnabled + " 预览分支 imgMode=" + self.imgMode + " imgLoaded=" + self.imgLoaded + " pdfLoaded=" + self.pdfLoaded + " viewMode=" + self.viewMode);
                        self.debugActive = false;  // V278: 清调试页标记
                        self.viewMode = 'single';  // V346/V362: 图片模式恒单页（不支持双页）；V362: 放大视图拖入图片也自动回单页（原只处理 double，zoom 残留导致预览仍放大）
                        self.pdfLoaded = false;  // V283: 图片模式清 pdfLoaded，避免 refreshPageWatermarks 干扰
                        self.wmSelId = null; self.wmEditingId = null;
                        // V283: 填源文件路径 + 自动设保存目录
                        if (r.filePath) { self.curFile = r.filePath.replace(/\\/g, '/'); }
                        if (!self.dirLocked && r.filePath) { var _pp = (r.filePath||'').replace(/\\/g,'/'); var _d = _pp.substring(0, _pp.lastIndexOf('/')); if (_d) self.saveDir = _d; }
                        /* V1.0.0.18：切图后按当前图重算预览字号 + 框高（框随字：FsToS×短边 + hidden div 测高贴合；wrapLines 不碰——输出 perFile 逐图重测） */
                        self.refreshImgWatermarks().then(function () {
                            self.$nextTick(function () {
                                self.imgMeasure();
                                (self.wmBoxes || []).forEach(function (b) {
                                    try { self.wmRefitToPage(b, self.wmSideDisp(b)); } catch (e) {}
                                });
                            });
                        });
                        self.opHint = '图片模式：《' + r.name + '》（' + r.total + ' 张中第 ' + (r.idx + 1) + ' 张）；水印设置与 PDF 一致'; /* V1.0.0.78：文件名《》包裹 */
                    } else { self.opHint = r.error || '图片预览失败'; }
                }, function () {});
            }, function () {});
        },
        imgNext() { if (this.curImgIdx < this.imgQueue.length - 1) this.imgShow(this.curImgIdx + 1); },
        imgPrev() { if (this.curImgIdx > 0) this.imgShow(this.curImgIdx - 1); },
        /* V346：图片模式页码输入跳转（工具条统一后由 barGoPage 分派） */
        imgGoPage() {
            const v = Number(this.imgPageInput);
            if (!this.imgLoaded || !this.imgQueue.length || !v) { this.imgPageInput = String((this.curImgIdx >= 0 ? this.curImgIdx + 1 : 0)); return; }
            this.imgShow(Math.max(0, Math.min(this.imgQueue.length - 1, v - 1)));
        },

        /* 图片实际显示尺寸（wm 层用 px 对齐） */
        imgMeasure() {
            const stage = this.$refs.stage;
            // V285: fit to view（对齐 PDF fitPage）——100% = 适配舞台宽高，不是按图片实际像素
            const sw = (stage && stage.clientWidth) ? (stage.clientWidth - 36) : 780;
            const sh = (stage && stage.clientHeight) ? (stage.clientHeight - 36) : 600;
            const baseW = sw;
            const baseH = Math.max(80, (sh - 52) * (this.imgW || 1) / (this.imgH || 1));
            this.imgFitW = Math.round(Math.min(baseW, baseH));
        },
        /* V346：图片缩放统一走 zoomText（与 PDF 同一套缩放状态，100~300%，imgDispW=imgFitW*zoomText/100） */
        zoomImgIn() { this._zoomAnchor = true; this.zoomText = this.clampZoomText(Number(parseFloat(this.zoomText) || 100) + 25) + '%'; this._fitImg(); },
        zoomImgOut() { this._zoomAnchor = true; this.zoomText = this.clampZoomText(Number(parseFloat(this.zoomText) || 100) - 25) + '%'; this._fitImg(); },
        clampImgZoom() { this._zoomAnchor = true; this.zoomText = this.clampZoomText(parseFloat(this.zoomText) || 100) + '%'; this._fitImg(); },

        imgRefreshQueue() {
            if (!window.Bridge) return;
            const self = this;
            window.Bridge.invoke('GetImageQueueInfo').then(function (json) {
                let r = {}; try { r = JSON.parse(json); } catch (e) {}
                if (r && (r.ok || typeof r.mode === 'boolean')) {
                    self.imgMode = !!r.mode;
                    self.imgQueue = r.list || [];
                    self.curImgIdx = typeof r.idx === 'number' ? r.idx : -1;
                    if (!self.imgMode) { self.imgLoaded = false; self.curImgUrl = ''; self.wmBoxes = []; }
                }
            }, function () {});
        },

        /* 清空图片队列与图片模式状态（切回 PDF 显示） */
        imgClear() {
            if (!window.Bridge) return;
            const self = this;
            window.Bridge.invoke('ClearImages').then(function (r) {
                if (String(r) === 'ok') {
                    self.imgMode = false; self.imgLoaded = false;
                    self.curImgUrl = ''; self.imgQueue = []; self.curImgIdx = -1;
                    self.imgW = 0; self.imgH = 0; self.wmBoxes = [];  // V277：合并到 wmBoxes
                    self.wmSelId = null; self.wmEditingId = null;
                    self.opHint = '已清空图片队列';
                    self.addLog('已清空图片队列', 'ok');
                } else { self.opHint = String(r); }
            }, function () {});
        },

        /* 选择图片文件夹（C# FolderBrowserDialog，递归收集；混合类型前端弹窗） */
        imgChooseFolder() {
            if (!window.Bridge) return;
            const self = this;
            window.Bridge.invoke('OpenImageFolder').then(function (json) {
                let r = {}; try { r = JSON.parse(json); } catch (e) {}
                if (r.ok) {
                    if (r.pdfs > 0) {
                        // 混合：让用户选择（文件夹里的图片已入队；若选按PDF处理需另行加载——这里保留图片处理，提示说明）
                        self.imgShow(0);
                        self.imgRefreshQueue();
                        self.opHint = '已加载 ' + r.total + ' 张图片（文件夹内另有 ' + r.pdfs + ' 个 PDF，如需处理请拖入 PDF）';
                        self.addLog('已加载图片文件夹：共 ' + r.total + ' 张（含 ' + r.pdfs + ' 个 PDF）', 'ok');
                    } else {
                        self.imgShow(0);
                        self.imgRefreshQueue();
                        self.addLog('已加载图片文件夹：共 ' + r.total + ' 张', 'ok');
                    }
                } else if (!r.cancel) { self.opHint = r.error || '加载失败'; self.addLog(r.error || '加载失败', true); }
            }, function () {});
        },

        /* 选择图片文件（OpenFileDialog 多选） */
        imgPickImages() {
            if (!window.Bridge) return;
            const self = this;
            window.Bridge.invoke('PickImages').then(function (json) {
                let r = {}; try { r = JSON.parse(json); } catch (e) {}
                if (r.ok) {
                    self.imgRefreshQueue();
                    if (self.curImgIdx < 0) self.imgShow(0);
                    self.addLog('已追加图片：' + r.total + ' 张', 'ok');
                } else if (!r.cancel) { self.opHint = r.error || '加载失败'; }
            }, function () {});
        },

        /* ===================== 水印框（图片模式，复用卡4交互） ===================== */
        refreshImgWatermarks() {
            if (!window.Bridge || !this.imgLoaded) { this.wmBoxes = []; return; }
            const self = this;
            return window.Bridge.invoke('GetPageWatermarks', 0).then(function (json) {  /* V1.0.0.19：返回 Promise——imgShow 重测链依赖（原未 return 致 .then 接到 undefined 抛 TypeError，跨图重测从未执行） */
                let a = []; try { a = JSON.parse(json); } catch (e) { return; }
                if (Array.isArray(a)) {
                    self.wmBoxes = a;  // V277：合并到 wmBoxes（不再用 wmBoxesImg）
                    if (!a.some(x => x.id === self.wmSelId)) self.wmSelId = null;
                    if (self.wmEditingId && !a.some(x => x.id === self.wmEditingId)) self.wmEditingId = null;
                }
            }, function () {});
        },

        /* 图片模式添加水印框（V277：对齐 PDF addWatermarkBox——align中、初始化字号、测宽调框宽、page=0 批次共享） */
        imgAddWatermarkBox() {
            if (!window.Bridge) { this.opHint = '浏览器预览模式：请使用壳程序（EXE）'; return; }
            if (!this.imgLoaded) { this.opHint = '请先加载图片再添加水印'; return; }
            const self = this;
            // V2.4.0.370 防御：图片模式加框不依赖开关状态——未开则自动开启（防遗漏路径）
            if (!this.watermarkEnabled) { this.watermarkEnabled = true; if (window.Bridge && window.Bridge.invoke) window.Bridge.invoke('SetWatermarkEnabled', true); }
            const x = 0.225, y = 0.42, w = 0.55;
            // V280：框高按图片显示高度动态算，让框高像素和 PDF 一致（约 61px），避免图片模式文字偏小
            var _dispH = this.imgDispW() * (this.imgH||1) / (this.imgW||1);
            var h = Math.max(0.04, Math.min(0.25, 61 / (_dispH || 768)));
            const p = {
                text: '双击编辑文字', fontName: '微软雅黑', fontScale: 0.8, colorArgb: 0xFF1F2329,
                opacity: 40, bold: false, italic: false, underline: false, strike: false,
                letterSpacing: 0, lineSpacing: 0, align: 1, rotation: 35  // V1.0.0.46：新建框默认 35° / 不透明度 40（原 V277 默认对齐=中）
            };
            window.Bridge.invoke('AddWatermarkBox', 0, x, y, w, h, JSON.stringify(p)).then(function (json) {
                let r = {}; try { r = JSON.parse(json); } catch (e) {}
                if (r.ok) {
                    if (window.Bridge && window.Bridge.invoke) window.Bridge.invoke("WriteDebugLog", "[IMG-DIAG] addWM 返回 ok box.id=" + (r.box ? r.box.id : 'null') + " push 后 wmBoxes.length=" + (self.wmBoxes||[]).length + " 渲染分支 imgMode=" + self.imgMode + " imgLoaded=" + self.imgLoaded + " pdfLoaded=" + self.pdfLoaded + " viewMode=" + self.viewMode + " imgW=" + self.imgW + " imgH=" + self.imgH + " imgDispW=" + (self.imgDispW ? self.imgDispW() : '?'));
                    if (r.box) { self.wmBoxes.push(r.box); }
                    self.wmSelId = r.box ? r.box.id : null;
                    // V277：与 PDF 新建框一致——记录 h0/fs0、初始化 wmFsMap、测宽调框宽
                    self.$nextTick(() => {
                        var b = self.wmBoxes.find(x => x.id === self.wmSelId);
                        if (b) {
                            b.h0 = b.h;
                            b.fs0 = b.h * (b.fontScale > 0 ? b.fontScale : 0.8) * 768.4;
                            if (!self.wmFsMap) self.wmFsMap = {};
                            var side = self.wmSideDisp(b);
                            if (side && side.dispW > 0) {
                                var dispH = side.dispW * (side.ptH / side.ptW);
                                self.wmFsMap[b.id] = self.wmCalcFontSize(b, side.dispW, dispH);
                                var fs = self.wmFsMap[b.id] || 50;
                                var measurer = document.createElement("span");
                                measurer.style.cssText = "position:absolute;visibility:hidden;white-space:nowrap;font:" + fs + "px " + (b.fontName || "微软雅黑") + ";";
                                measurer.textContent = b.text;
                                document.body.appendChild(measurer);
                                var textWidth = measurer.offsetWidth;
                                document.body.removeChild(measurer);
                                var cx = b.x + b.w / 2;
                                b.w = Math.min(1.0 - b.x, (textWidth + 10) / side.dispW);
                                b.x = cx - b.w / 2;
                                // V288: 同步 h0 到后端（新建框时 AddWatermarkBox 没传 h0，后端 H0=0 导致输出字号不对）
                                window.Bridge.invoke('UpdateWatermarkBox', b.id, JSON.stringify(self.wmBoxToJson(b)));
                                // V1.0.0.7: 新建框测宽后立即测换行快照（当前图 CSS 断行）；渲染/切图不再自动重测
                                if (typeof self.wmCaptureWrapLines === 'function') { try { self.wmCaptureWrapLines(b); } catch (e) {} }
                                // V1.0.0.42：添加水印框后自动进入编辑状态（无需再双击）
                                self.wmDblEditStart(b);
                            }
                        }
                    });
                    self.opHint = '已添加文字水印：双击编辑文字，拖拽移动，四角缩放，右键删除';
                    self.addLog('已添加文字水印（应用到所有图片）', 'ok');
                } else { self.opHint = r.error || '添加水印失败'; self.addLog(r.error || '添加水印失败', true); }
            }, function (e) { self.addLog('添加水印失败：' + e.message, true); });
        },

        /* 图片模式删除框（V100：前端先删保证即时反馈，C# 删除静默容错——"水印框不存在"不再弹错；C# 成功则后端同步删，输出一致） */
        imgRemoveWatermarkBox(id) {
            // 1. 前端先删（本地状态一致，不阻塞）
            this.wmBoxes = this.wmBoxes.filter(b => b.id !== id);
            if (this.wmSelId === id) this.wmSelId = null;
            if (this.wmEditingId === id) this.wmEditingId = null;
            // 2. 再调 C# 删（C# 找不到时静默，不弹"水印框不存在"）
            if (!window.Bridge || !window.Bridge.invoke) return;
            const self = this;
            window.Bridge.invoke('RemoveWatermarkBox', id).then(function (r) {
                if (String(r) === 'ok') self.addLog('已删除文字水印', 'ok');
                // C# 返回 "err:水印框不存在" 时静默——前端已删，不打扰用户
            }, function () {});
        },

        /* ===================== 批量输出 ===================== */
        imgApplyOutput() {
            if (!window.Bridge) return;
            if (!this.imgLoaded && this.imgQueue.length === 0) { this.opHint = '请先加载图片'; return; }
            const self = this;
            /* V1.0.0.38：输出前统一 flush——编辑态文字/换行快照先写回（原路径连 b.text 都不写回，输出与预览不一致） */
            if (typeof this.wmFlushBeforeGenerate === 'function') { try { this.wmFlushBeforeGenerate(); } catch (e) {} }
            let outDir = String(this.imgOutDir || '').trim();
            if (!outDir) {
                // 默认当前图片所在目录（后端取队列首图目录）
                outDir = '';
                this.opHint = '未指定输出目录，将输出到图片所在目录';
            }
            this.imgBusy = true;
            /* V1.0.0.17：输出前全量同步水印参数到后端（透明度等基础参数；ReplaceAll 失败也继续，不阻塞输出） */
            window.Bridge.invoke('ReplaceAllWatermarks', 0, JSON.stringify((self.wmBoxes || []).map(function (b) { return self.wmBoxToJson(b); }))).then(function(){}, function(){});
            /* V1.0.0.18：输出前逐图测换行快照（用户 v1.0.0.6 方案彻底实施——每图按自己实际像素尺寸 + 输出端字号 FsToS×短边 测行，
               输出 = 该图预览；不再跨图共享同一份快照）。GetImageDims 返回每图 {path,w,h}。 */
            const _doApply = function (perFileJson) {
                window.Bridge.invoke('ApplyImageWatermarks', outDir, perFileJson || '').then(function (json) {
                    self.imgBusy = false;
                    let r = {}; try { r = JSON.parse(json); } catch (e) {}
                    if (r.ok) {
                        if (r.done > 0 && typeof self.badgeAddImgWm === 'function') self.badgeAddImgWm(r.done); // V2.4.0.397：等级徽章计数（图片水印成功 N 张 +N）
                        const outs = r.outputs || [];
                        let msg = '批量输出完成：共 ' + r.total + ' 张，成功 ' + r.done + ' 张' +
                            (r.skipped > 0 ? '，无水印框跳过 ' + r.skipped + ' 张' : '') +
                            (r.failed > 0 ? '，失败 ' + r.failed + ' 张' : '');
                        self.opHint = msg;
                        self.addLog(msg, 'imp'); // V369：输出汇总=重要蓝（对齐 PDF generate-done summary）
                        if (r.skipped > 0) self.addLog('无水印框跳过：' + r.skipped + ' 张', 'warn'); // V369：跳过=警告黄
                        outs.forEach(function (p) { self.addLog('输出：' + String(p).replace(/\\/g, '/'), 'imp'); }); // V369：每文件=重要蓝（对齐 PDF 每文件 imp）
                        if (r.failures && r.failures.length > 0) r.failures.forEach(function (fm) { self.addLog('失败：' + fm, true); });
                        // V2.4.0.407：输出目录放日志最后一条 + 可点击打开（C# 返回真实 outDir，不再从 outputs 推算且不传 dir）
                        const _od = (r.outDir ? String(r.outDir).replace(/\\/g, '/') : '');
                        if (_od) self.addLog('输出目录：' + _od, 'imp', _od);
                    } else { self.imgBusy = false; self.opHint = r.error || '输出失败'; self.addLog(r.error || '输出失败', true); }
                }, function (e) { self.imgBusy = false; self.opHint = '输出失败：' + e.message; self.addLog('输出失败：' + e.message, true); });
            };
            window.Bridge.invoke('GetImageDims').then(function (json) {
                let dims = []; try { dims = JSON.parse(json); } catch (e) {}
                const perFile = {};
                if (dims && dims.length && (self.wmBoxes || []).length) {
                    (self.wmBoxes || []).forEach(function (b) {
                        var fsToS = (b.fsToS > 0.0001 ? b.fsToS : 0);
                        dims.forEach(function (d) {
                            var _w = d.w || 0, _h = d.h || 0;
                            var short = Math.min(_w, _h);
                            var bc = Object.assign({}, b);
                            if (fsToS > 0 && short > 0 && _w > 0) {
                                try { self.wmCaptureWrapLines(bc, true, { dispW: _w, ptW: _w, ptH: _h }, fsToS * short); } catch (e) {}
                            }
                            if (!perFile[d.path]) perFile[d.path] = [];
                            perFile[d.path].push(self.wmBoxToJson(bc));
                        });
                    });
                }
                _doApply(Object.keys(perFile).length ? JSON.stringify(perFile) : '');
            }, function () { _doApply(''); });
        },

        /* 输出目录选择（C# FolderBrowserDialog，复用 PickOutputDir） */
        imgPickOutDir() {
            if (!window.Bridge) return;
            const self = this;
            window.Bridge.invoke('PickOutputDir').then(function (json) {
                let r = {}; try { r = JSON.parse(json); } catch (e) {}
                if (r.ok) self.imgOutDir = r.path;
                else if (!r.cancel) self.opHint = r.error || '选择失败';
            }, function () {});
        },

        /* 返回 PDF 预览（加载过 PDF 时） */
        /* V277：底部「生成结果文件」统一入口——图片模式走批量输出，PDF 模式走原 generateFiles */
        handleGenerate() {
            if (this.imgMode && this.imgLoaded) { this.imgApplyOutput(); return; }
            this.generateFiles();
        },
        imgBackToPdf() {
            if (this.pdfLoaded) {
                this.imgMode = false; this.imgLoaded = false;
                this.curImgUrl = ''; this.wmBoxes = [];
                this.wmSelId = null; this.wmEditingId = null;
                this.opHint = '已切回 PDF 预览（图片队列保留，可再次进入图片模式）';
            } else {
                this.opHint = '当前没有加载 PDF';
            }
        }
};
