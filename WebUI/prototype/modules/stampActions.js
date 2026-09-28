/* PDFQFZ Web UI 前端模块：盖章/放置/范围/骑缝/预览（V2.4.0.84 拆分）
 * index.html methods 中通过 ...window.PdfqModules.stampActions 注入，
 * this 指向组件实例（Vue3 options API spread 保留绑定），函数体零改动。
 */
window.PdfqModules = window.PdfqModules || {};
window.PdfqModules.stampActions = {
    onRandEnable(v){
      this.opHint=v?'随机角度位移已启用：盖章将进行随机的角度和位移变化；点击名称按钮调节参数':'随机角度位移已关闭';
      this.addLog(v?'随机角度位移已启用':'随机角度位移已关闭');
    },
    onRenderEnable(v){
      this.opHint=v?'盖章渲染已启用：模拟真实盖章效果；点击名称按钮调节参数':'盖章渲染已关闭';
      this.addLog(v?'盖章渲染已启用':'盖章渲染已关闭');
    },
    /* ---- v2.4.0.41：浮动面板（非模态）打开/拖拽/位置记忆（相对左栏坐标，localStorage） ---- */
    onWhiteEnable(v){
      if(v) this.stampSect.white=true;
      this.opHint=v?'图章去白已启用：盖章时去除印章图片的白色背景，仅保留红色章体；容差越大去除越彻底':'图章去白已关闭';
      this.addLog(v?'图章去白已启用':'图章去白已关闭');
    },
    applyRange(){
      const pc=this.pageCount||29;
      const f=Math.max(1,this.rangeFromN), t=Math.min(pc,Math.max(f,this.rangeToN));
      this.rangeFromN=f; this.rangeToN=t;
      this.rangeActive=true;
      this.dlgRange=false;
      // V2.4.0.13：确认后跳转到范围结束页并渲染（对齐 WPF：结束页=批量盖章触发点）。
      // 原实现只改 curPage=起始页 不重新渲染 → 预览显示停留原页、curPage 状态错位，
      // 点击盖章盖到 curPage 而非显示页（用户实测"只有3、6有章"根因）。
      this.renderPage(t);
      this.opHint='范围模式：已跳转到结束页（第 '+t+' 页），点击结束页自动为范围内每页盖章，点其他页单页盖章；翻页限定范围内';
    },
    exitRange(){
      this.rangeActive=false;
      this.opHint='已退出范围模式，恢复手动点击盖章';
    },
    placeTextStamps(){
      this.rangeActive=false;
      if(!window.Bridge){ this.opHint='浏览器预览模式：请使用壳程序（EXE）'; return; }
      if(!this.pdfLoaded || this.debugActive){ this.opHint='请先加载 PDF 文件再放置印章'; return; }
      if(!this.searchText || !String(this.searchText).trim()){ this.opHint='请输入要识别的盖章文字'; return; }
      const self=this;
      if(this.dirMode && this.curFileList.length>1){
        // v2.4.0.70：文件夹模式直接对全部文件放置（去掉 confirm 弹窗——V60 该弹窗在 WebView 下偶发挂起，导致点击后无任何提示/章不出现）
        this.doTextStampDir();
        return;
      }
      this.doTextStampSingle();
    },
    doTextStampSingle(){
      this.rangeActive=false;
      if(!window.Bridge){ this.opHint='浏览器预览模式：请使用壳程序（EXE）'; return; }
      const self=this;
      window.Bridge.invoke('AutoStamp',
        String(this.searchText).trim(),
        this.keywordEnabled ? (this.keywords||'') : '',
        Number(this.ctxRange)||10,
        (this.matchMode||'all')==='all',
        !!this.ignoreSpace,
        !!this.offsetEnabled,
        Number(this.off.h)||0,
        Number(this.off.v)||0).then(function(json){
        const r=JSON.parse(json);
        if(r.ok){
          const _n=r.total||0,_c=r.count||0;
          self.opHint='按文字盖章完成：共找到 '+_n+' 处，新增 '+_c+' 个'+(r.contextFiltered?'（上下文过滤）':'')+'，详见下方日志';
          self.addLog('按文字盖章完成：关键词「'+String(self.searchText).trim()+'」，共找到 '+_n+' 处，新增 '+_c+' 个'+(r.contextFiltered?'（上下文过滤）':''),'ok');
          if(r.count>0){ self.refreshPageStamps(); }
          window.Bridge.invoke('GetAutoStampHistory').then(function(j){ try{ const a=JSON.parse(j); if(Array.isArray(a)){ self.searchHistory=a; } }catch(e){} },function(){});
        } else { self.opHint=r.error||'按文字盖章失败'; self.addLog(r.error||'按文字盖章失败',true); }
      },function(e){ self.opHint='按文字盖章失败：'+e.message; self.addLog('按文字盖章失败：'+e.message,true); });
    },
    doTextStampDir(){
      this.rangeActive=false;
      if(!window.Bridge){ this.opHint='浏览器预览模式：请使用壳程序（EXE）'; return; }
      const self=this;
      this.opHint='正在按文字盖章全部文件…';
      window.Bridge.invoke('AutoStampDir',
        String(this.searchText).trim(),
        this.keywordEnabled ? (this.keywords||'') : '',
        Number(this.ctxRange)||10,
        (this.matchMode||'all')==='all',
        !!this.ignoreSpace,
        !!this.offsetEnabled,
        Number(this.off.h)||0,
        Number(this.off.v)||0).then(function(json){
        const r=JSON.parse(json);
        if(r.ok){
          let okN=0; const errs=[];
          (r.results||[]).forEach(function(x){ if(x.ok&&x.count>0){okN++;} else if(x.error){errs.push(x.file+'：'+x.error);} });
          self.opHint='按文字盖章完成：'+okN+' 个文件已盖章'+(errs.length?('；失败 '+errs.length+' 个，如 '+errs.slice(0,2).join('；')):'')+'，详见下方日志';
          self.addLog('按文字盖章（全部文件）完成：'+okN+' 个文件已放置'+(errs.length?('；失败 '+errs.length+' 个：'+errs.join('；')):''),'ok');
          self.refreshPageStamps();
        } else { self.opHint='按文字盖章失败：'+(r.error||''); self.addLog('按文字盖章失败：'+(r.error||''),true); }
      },function(e){ self.opHint='按文字盖章失败：'+e.message; self.addLog('按文字盖章失败：'+e.message,true); });
    },
    undoTextStamp(){
      this.rangeActive=false;
      if(!window.Bridge){ this.opHint='浏览器预览模式：请使用壳程序（EXE）'; return; }
      const self=this;
      window.Bridge.invoke('UndoAutoStamp').then(function(json){
        const r=JSON.parse(json);
        if(r.ok){ self.opHint=r.removed>0?('已撤销放置：'+r.keyword+' 的印章已移除（'+r.removed+' 个）'):(r.note||'当前文件无按文字印章可撤销'); if(r.removed>0){ self.addLog('已撤销放置：「'+r.keyword+'」的印章已移除（'+r.removed+' 个）','ok'); } self.refreshPageStamps(); }
        else { self.opHint=r.error||'撤销失败'; }
      },function(e){ self.opHint='撤销失败：'+e.message; });
    },
    /* 关键词变化 → 加载该词中心偏移记忆（对齐 WPF comboAutoKeyword 变化联动） */
    loadCenterOffsetForSearch(){
      if(!window.Bridge || !this.searchText){ return; }
      const self=this;
      window.Bridge.invoke('GetCenterOffsetForKeyword',String(this.searchText).trim()).then(function(json){
        const r=JSON.parse(json);
        if(r && typeof r.enabled==='boolean'){ self.off.h=r.x||0; self.off.v=r.y||0; }
      },function(){});
    },
    /* ---- 阶段8：生成文件与输出 ---- */
    /* ===================== V2.4.0.53 文件夹批量处理 ===================== */
    onTextForce(v){
      if(v){ this.batchSect.text=true; } else { this.batchSect.text=false; }
      this.opHint=v?'在 PDF 中搜索指定文字，在匹配位置自动盖章。填写需要盖章的文字后，点击「按文字放置印章」':'已关闭按文字批量盖章';
      this.addLog(v?'启用按文字批量盖章':'关闭按文字批量盖章');
    },
    onTextTitleClick(){
      if(!this.textForce){ return; }
      this.toggleSect('batchSect','text');
    },
    /* V2.4.0.70：撤销放置（整批次）——清除所有文件上的批量放置章（batchId=-1），可再次点击批量放置重新放置 */
    undoBatchStamp(){
      this.rangeActive=false;
      if(!window.Bridge){ this.opHint='浏览器预览模式：请使用壳程序（EXE）'; return; }
      const self=this;
      window.Bridge.invoke('RemoveBatchPreviewAll').then(function(json){
        const r=JSON.parse(json);
        if(r.ok){
          if(r.removed>0){
            const left=r.remaining>0?('，剩余 '+r.remaining+' 枚'):'';
            self.opHint='已撤销最近一批批量放置：移除 '+r.removed+' 枚'+left;
            self.addLog('已撤销最近一批批量放置：移除 '+r.removed+' 枚'+left,'ok');
          }
          else { self.opHint='当前无批量放置的章可撤销'; }
          // V2.4.0.70：撤销后保留 batchPreviewed（"已按批量设置预览"标记），
          // 避免切换文件触发 doBatchPreview 重算把已撤销的批次重新生成（V2.4.0.70 修复）
          self.refreshPageStamps();
          self._seamSync();
        }
        else { self.opHint=r.error||'撤销失败'; }
      },function(e){ self.opHint='撤销失败：'+e.message; });
    },
    /* V2.4.0.70：批量放置印章（所有文件）——一次按批量参数把章放入文件夹内全部文件（后端 BatchPreviewAll，batchId=-1）；
       放置只能由本按钮触发，参数改动不自动放置；撤销为整批次撤销。 */
    previewBatchStamp(){
      this.rangeActive=false;
      if(!window.Bridge){ this.opHint='浏览器预览模式：请在壳程序（EXE）中使用'; return; }
      if(!this.dirMode || !this.curFileList.length){ this.opHint='请先在【源文件】选择文件夹'; return; }
      if(!this.curSeal){ this.opHint='请先选择印章'; this.addLog('请先选择印章',true); return; }
      const self=this;
      this.batchPreviewMode=true;
      this.opHint='正在按批量设置放置所有文件…';
      // V2.4.0.81：BatchPreviewAll 已异步化（Task.Run）——invoke 立即返回 {ok,started}，results 走 batch-preview-done 事件收尾
      window.Bridge.invoke('BatchPreviewAll',
        parseInt(this.batchRange)||0, parseInt(this.batchStart)||1, parseInt(this.batchEnd)||1,
        parseInt(this.batchX)||50, parseInt(this.batchY)||50, true,
        (this.saveDir||'').trim()
      ).then(function(json){
        let r={}; try{ r=JSON.parse(json); }catch(e){ self.batchPreviewMode=false; self.opHint='放置失败：返回数据异常'; self.addLog('放置失败：返回数据异常',true); return; }
        if(!r.ok){ self.batchPreviewMode=false; self.opHint=r.error||'放置失败'; self.addLog(r.error||'放置失败',true); return; }
        if(!r.started){ self.batchPreviewMode=false; self.opHint=r.error||'放置失败'; return; }
        // started=true：后台执行中，batch-preview-done 事件到达后完成收尾（防 loading 卡死：失败/异常分支已重置）
      },function(e){ self.batchPreviewMode=false; self.opHint='放置失败：'+e.message; self.addLog('放置失败：'+e.message,true); });
    },
    /* V2.4.0.54：批量预览——按批量设置（页码范围/X/Y 百分比/钳制）在当前文件上显示盖章效果（内存预览不写文件）；
       骑缝章切片由 _seamSync 按印章参数卡设置显示；已预览过的文件直接复用，切换文件自动预览。 */
    setSeamType(v){ const t=String(v); if(this.pageCount<=1 && t!=='1'){ this.opHint='单页文档无需骑缝章'; return; } /* V368：单页强制不加 */ this.seamType=t;
      if(t==='2'){ this.opHint='单页骑缝章：类似于双面打印正向盖章的效果，奇数页（1、3、5...）有骑缝章'; }
      else if(t==='3'){ this.opHint='双页骑缝章：类似于双面打印反向盖章的效果，偶数页（2、4、6...）有骑缝章'; }
      if(t!=='1' && !this.dirMode){ this.resetSegFromPage(); }
      this.saveUiConfig();
    },
    resetSegFromPage(){ if(this.pdfLoaded && this.pageCount>0){ this._segModified=false; this.segCount=this.pageCount; this.segAuto=true; } }, /* V368：选骑缝章类型后分割数默认自动 */
    onSegInput(){ this._segModified=true; },
    /* V367：分割数一体控件（自动/数字）▲▼ 步进；PDF 文件夹模式（dirMode）锁死自动不可切换 */
    segStepUp(){ if(this.pageCount<=1) return; /* V368：单页锁；文件夹不再锁（可切手动） */ this._segModified=true; if(this.segAuto){ this.segAuto=false; this.segCount=0; } else { this.segCount=Math.min(500, (parseInt(this.segCount)||0)+1); } },
    segStepDown(){ if(this.pageCount<=1) return; /* V368：单页锁；文件夹不再锁 */ this._segModified=true; if(!this.segAuto){ if((parseInt(this.segCount)||0)<=0){ this.segAuto=true; } else { this.segCount=Math.max(0,(parseInt(this.segCount)||0)-1); } } },
    searchHistorySuggest(queryString, cb){
      const q=(queryString||'').trim();
      cb(this.searchHistory.filter(h=>!q||h.indexOf(q)>=0).map(v=>({value:v})));
    },
    removeSearchHistory(kw){
      const i=this.searchHistory.indexOf(kw);
      if(i>-1) this.searchHistory.splice(i,1);
      if(window.Bridge&&window.Bridge.invoke){ window.Bridge.invoke('RemoveAutoStampKeyword',kw).then(function(){},function(){}); }
    },
    removeStamp(id){
      this.rangeActive=false;
      const self=this;
      let hit=null, inRight=false;
      for(const s of this.pageStamps){ if(s.id===id){ hit=s; break; } }
      if(!hit){ for(const s of this.pageStampsRight){ if(s.id===id){ hit=s; inRight=true; break; } } }
      if(!hit){ return; }
      // V2.4.0.88：章类型数据模型——type 权威区分（manual/range/batch/text），不靠 batchId 猜类型；旧缓存无 type 时按 batchId 兜底
      const type=hit.type || (hit.batchId===0?'manual':(hit.batchId<0?'batch':'range'));
      if(type==='manual'){
        // 手动章：无弹窗直接删除（保持现状，用户单击盖的不需确认）
        window.Bridge.invoke('DeleteStampById',id).then(function(r){ self.opHint=(r==='ok')?'已删除印章':'删除失败：'+r; if(r==='ok'){ self.addLog('已删除印章','ok'); } else { self.addLog('删除失败：'+r,true); } self.refreshPageStamps(); },function(e){ self.opHint='删除失败：'+e.message; self.addLog('删除失败：'+e.message,true); });
        return;
      }
      // range/batch/text → 弹窗；按文字章（text）当前页该批次多枚时才显示"仅删除当前印章"
      let showSingle=false;
      if(type==='text'){
        const arr=inRight?this.pageStampsRight:this.pageStamps;
        const cnt=arr.filter(s=>s.batchId===hit.batchId).length;
        showSingle=cnt>1;
      }
      this.batchDelShowSingle=showSingle;
      this.batchDel={id:id,page:Number(this.curPage),batchId:hit.batchId,type:type};
      this.dlgBatchDel=true;
    },
    /* 批量删除弹窗选项：1=删除整个批次 2=删除当前页批次 3=取消 4=仅删除当前印章（按文字章多枚时显示） */
    refreshPageStamps(){
      if(!window.Bridge || !this.pdfLoaded){ return; }
      const self=this;
      // v2.4.0.52：后端章图 url 固定（stamp_{id}.png），SyncTextureParams 后 PNG 已重渲染但 img src 不变不重载 →
      // cache-bust 时间戳强制重载；Date.now()+随机数保证连续调参时 url 唯一。仅 refreshPageStamps 触发时加，不污染常渲染路径。
      const bust=function(url){ const t=Date.now()+'-'+(Math.random()*1e6|0); return url + (url.indexOf('?')>=0 ? '&' : '?') + 't=' + t; };
      window.Bridge.invoke('GetPageStamps',Number(this.curPage)||1).then(function(json){
        let a=[]; try{ a=JSON.parse(json); }catch(e){ return; }
        if(Array.isArray(a)){ self._diag('refreshPageStamps: 请求page='+(Number(self.curPage)||1)+' 后端返回章数='+a.length); for(let i=0;i<a.length;i++){ if(a[i]&&a[i].url) a[i].url=bust(a[i].url); } }
        self.pageStamps=Array.isArray(a)?a:[];
        if(self.viewMode==='double' && self.pageRightUrl){
          window.Bridge.invoke('GetPageStamps',Number(self.curPage)+1).then(function(j2){
            let b=[]; try{ b=JSON.parse(j2); }catch(e){ return; }
            if(Array.isArray(b)){ for(let i=0;i<b.length;i++){ if(b[i]&&b[i].url) b[i].url=bust(b[i].url); } }
            self.pageStampsRight=Array.isArray(b)?b:[];
          },function(){});
        } else { self.pageStampsRight=[]; }
      },function(){});
    },
    onStageDown(e,side){
      if(e.button===2){ return; }
      if(!window.Bridge || (!this.pdfLoaded && !this.imgMode)){ return; }
      // V100 问题1：文字水印与点击盖章互斥——水印开关开时，点预览区空白只取消水印选中，不盖章（水印框本身 @mousedown.stop 已隔离，点框正常选中）
      // V101：不清 wmEditingId——让 contenteditable blur 正常触发 wmDblEditEnd 保存（清了会导致元素销毁，ref 不存在，不保存）
      // V2.4.0.362：文启用时仅拦截"点击"（不盖章），放大视图拖拽（pan）照常——把拦截从 mousedown 移到 up 的未拖动分支
      if(this.watermarkEnabled && this.wmEditingId){
        // 主动退出编辑状态（触发 blur 保存内容）
        var ref = this.$refs["wmEdit-" + this.wmEditingId];
        var el = Array.isArray(ref) ? ref[ref.length-1] : ref;
        if(el) el.blur();
      }
      // v2.4.0.36：点击悬浮工具栏（含目录文件下拉 el-select）不触发盖章——事件穿透防御（对齐 WPF：工具栏与预览区互不干扰）
      if(e.target && e.target.closest && e.target.closest('.float-wrap,.float-bar,.el-select,.el-popper,.preview-toolbar')){ return; }
      // V2.4.0.6：只有放大视图可拖拽（对齐 WPF Scroll 模式滚动条 Auto 可拖，单页/双页滚动条 Disabled 拖不动）；
      // 不再看 zoomText——双页视图即使残留 125% 缩放值也不允许拖拽
      const dragEnabled = this.viewMode==='zoom';
      // V2.4.0.362：拖拽改为滚动条驱动——base 用 stage.scrollLeft/scrollTop（页面不再 transform 平移，内容由 preview-stage 滚动条滚动，拖拽/滚轮/缩放统一同步滚动条，滚动条实时联动）
      const stage=this.$refs.stage;
      const g={side:e.side||side, el:e.currentTarget, startX:e.clientX, startY:e.clientY, baseX:stage?stage.scrollLeft:this.dragOfs.x, baseY:stage?stage.scrollTop:this.dragOfs.y, downX:e.clientX, downY:e.clientY, dragging:false};
      this._gesture=g;
      const self=this;
      const move=ev=>{
        if(!g.dragging && (Math.abs(ev.clientX-g.downX)>4 || Math.abs(ev.clientY-g.downY)>4)){ g.dragging=true; }
        if(g.dragging && dragEnabled){
          // V2.4.0.6：拖动偏移钳制在页面边缘对齐视口边缘（对齐 WPF ScrollViewer 滚动钳制），页面不能拖出可视区
          const sw=(stage&&stage.clientWidth)?(stage.clientWidth-36):0;
          const sh=(stage&&stage.clientHeight)?(stage.clientHeight-52):0;
          const iw=self.imgMode?(self.imgDispW()||0):(self.imgW||0);
          const ih=self.imgMode?(self.imgDispH()||0):(iw*((self.pageH||self.pageW||1)/(self.pageW||1)));
          const maxX=Math.max(0,iw-sw), maxY=Math.max(0,ih-sh);
          // V2.4.0.9：拖拽方向与滚轮方向分离——拖右/拖下→offset 减→内容右/下移→看左/上面，对齐 WPF ScrollViewer（拖拽 offset 变化方向与滚轮相反）
          const nx=g.baseX-(ev.clientX-g.startX), ny=g.baseY-(ev.clientY-g.startY);
          const ox=Math.max(0,Math.min(maxX,nx)), oy=Math.max(0,Math.min(maxY,ny));
          self.dragOfs={x:ox, y:oy};
          // V2.4.0.362：同步滚动条——内容由滚动条滚动，拖拽即滚动
          if(stage){ stage.scrollLeft=ox; stage.scrollTop=oy; }
        }
      };
      const up=ev=>{
        window.removeEventListener('mousemove',move);
        window.removeEventListener('mouseup',up);
        if(self._gesture!==g){ return; }
        self._gesture=null;
        if(g.dragging){ return; } // V2.4.0.6：只要拖动过就不触发点击（双页禁拖且不误盖章，对齐 WPF 拖动防误盖章）
        // V2.4.0.362：文启用时点击空白 → 取消水印选中，不盖章（拖拽已在上方放行）
        if(self.watermarkEnabled){ self.wmSelId=null; self.wmEditingId=null; return; }
        // V2.4.0.96：点击空白处若已选中文字水印框 → 先取消选中（不触发盖章）；水印框 mousedown.stop 已隔离互不干扰
        if(self.wmSelId){ self.wmSelId=null; self.wmEditingId=null; return; }
        // V2.4.0.15：放大视图也允许点击盖章（原实现 dragEnabled 时直接 return，导致放大视图点不了章）；
        // 拖动与点击仍由 g.dragging（位移>4px）区分——未拖动即点击，触发盖章
        self.onPreviewClick({clientX:g.downX, clientY:g.downY, currentTarget:g.el}, g.side);
      };
      window.addEventListener('mousemove',move);
      window.addEventListener('mouseup',up);
    },
    onPreviewClick(e,side){
      if(!window.Bridge || !this.pdfLoaded){ return; }
      // v2.4.0.52：盖章前校验已选印章（对齐原版"请先选择印章！"）
      if(!this.seals || !this.seals.length || !this.curSeal){ this.opHint='请先选择印章'; return; }
      const el=e.currentTarget;
      const rect=el.getBoundingClientRect();
      let x=(e.clientX-rect.left)/rect.width;
      let y=(e.clientY-rect.top)/rect.height;
      // V2.4.0.6：章体不越界——章中心钳制在页面内（章小于页面时），章大于页面允许越界
      const pageWpx=this.imgW||rect.width;
      const pageHpx=pageWpx*((this.pageH||this.pageW||1)/(this.pageW||1));
      const szMm=Number(this.sealSize)||20;
      const scale=pageWpx/(this.pageW||1);
      const sw=szMm*72/25.4*scale;
      const cx=x*pageWpx, cy=y*pageHpx;
      let cx2, cy2;
      if(sw<pageWpx){ cx2=Math.max(sw/2,Math.min(pageWpx-sw/2,cx)); } else { cx2=cx; }
      if(sw<pageHpx){ cy2=Math.max(sw/2,Math.min(pageHpx-sw/2,cy)); } else { cy2=cy; }
      x=Math.max(0,Math.min(1,cx2/pageWpx));
      y=Math.max(0,Math.min(1,cy2/pageHpx));
      const page=side===2?(Number(this.curPage)+1):(Number(this.curPage)||1);
      const self=this;
      if(this.rangeActive){
        if(page===this.rangeToN){
          // 结束页：触发全范围批量盖章（对齐 WPF）
          window.Bridge.invoke('AddRangeStamps',x,y,this.rangeFromN,this.rangeToN).then(function(json){
            const r=JSON.parse(json);
            if(r.ok){ self.rangeActive=false; self.opHint='已在第 '+self.rangeFromN+'–'+self.rangeToN+' 页放置印章'; self.addLog('已在第 '+self.rangeFromN+'–'+self.rangeToN+' 页放置印章','ok'); self.refreshPageStamps(); }
            else { self.opHint=r.error||'范围盖章失败'; self.addLog(r.error||'范围盖章失败',true); }
          },function(err){ self.opHint='范围盖章失败：'+err.message; self.addLog('范围盖章失败：'+err.message,true); });
        } else {
          // 范围内非结束页：单页盖章（V2.4.0.58：盖章成功后退出范围模式——"盖章算退出"）
          window.Bridge.invoke('AddManualStamp',x,y,page).then(function(json){
            const r=JSON.parse(json); if(r.ok){ self.rangeActive=false; self.addLog('第 '+page+' 页已放置印章','ok'); self.refreshPageStamps(); } else { self.opHint=r.error||'盖章失败'; self.addLog(r.error||'盖章失败',true); }
          },function(err){ self.opHint='盖章失败：'+err.message; self.addLog('盖章失败：'+err.message,true); });
        }
        return;
      }
      window.Bridge.invoke('AddManualStamp',x,y,page).then(function(json){
        const r=JSON.parse(json); if(r.ok){ self.addLog('第 '+page+' 页已放置印章','ok'); self.refreshPageStamps(); } else { self.opHint=r.error||'盖章失败'; self.addLog(r.error||'盖章失败',true); }
      },function(err){ self.opHint='盖章失败：'+err.message; self.addLog('盖章失败：'+err.message,true); });
    },
    /* v2.4.0.52：拖拽已盖章实时移动——位移≥4px 判定拖拽（实时改本地 x/y，mouseup 调桥 MoveStamp 持久化）；
       位移<4px 判定单击 → 在章中心叠加盖章（保持"单击已有印章可叠加"原行为）。
       side=1 左页/单页、2 右页（双页视图）。章坐标按各自 centerRatio 语义换算，随机位移 offset 保持不动。 */
    stampDragStart(e,s,side){
      this.rangeActive=false;
      e.preventDefault(); e.stopPropagation();
      if(e.button!==0){ return; }
      const self=this;
      const startX=e.clientX, startY=e.clientY;
      let dragging=false;
      const dispW=(side===2?(this.imgW2||this.imgW):(this.imgW||600));
      const dispH=dispW*((this.pageH||this.pageW||1)/(this.pageW||1));
      // V2.4.0.54：按下时快照章位置（sx0/sy0），move 用快照+累计位移——修复"dx 累计 + s.x 每帧更新"导致位移重复累加、章飞快移动的 bug
      const sx0=s.x, sy0=s.y;
      // 章显示尺寸（页内钳制用）
      const scale=dispW/(this.pagePts||595);
      const w=s.sizeMm*72/25.4*scale;
      const h=w*(s.imgH/s.imgW);
      const clampV=(v,lo,hi)=>Math.max(lo,Math.min(hi,v));
      const move=ev=>{
        const dx=ev.clientX-startX, dy=ev.clientY-startY;
        if(!dragging && (Math.abs(dx)>4 || Math.abs(dy)>4)){ dragging=true; }
        if(!dragging){ return; }
        let nx, ny;
        if(s.centerRatio){
          nx=sx0+dx/dispW; ny=sy0+dy/dispH;
        } else {
          nx=(dispW-w>0)?(sx0+dx/(dispW-w)):sx0;
          ny=(dispH-h>0)?(sy0+dy/(dispH-h)):sy0;
        }
        // V2.4.0.70：拖拽边界与渲染端同一套 bbox 钳制（旋转后章完整页内；无旋转时 bbox=原尺寸，文字章零变化）
        const A=(s.randomRotation&&s.rotation)?s.rotation:0;
        const rad=A*Math.PI/180, cA=Math.cos(rad), sA=Math.sin(rad);
        const bw=w*Math.abs(cA)+h*Math.abs(sA);
        const bh=w*Math.abs(sA)+h*Math.abs(cA);
        if(s.centerRatio){
          if(bw<dispW){ nx=clampV(nx, bw/(2*dispW), 1-bw/(2*dispW)); } else { nx=clampV(nx,0,1); }
          if(bh<dispH){ ny=clampV(ny, bh/(2*dispH), 1-bh/(2*dispH)); } else { ny=clampV(ny,0,1); }
        } else {
          if(bw<dispW){
            let cxN=(dispW-w>0)?((dispW-w)*nx+w/2)/dispW:0.5;
            cxN=clampV(cxN, bw/(2*dispW), 1-bw/(2*dispW));
            nx=(dispW-w>0)?(cxN*dispW-w/2)/(dispW-w):nx;
          } else { nx=clampV(nx,0,1); }
          if(bh<dispH){
            let cyN=(dispH-h>0)?((dispH-h)*ny+h/2)/dispH:0.5;
            cyN=clampV(cyN, bh/(2*dispH), 1-bh/(2*dispH));
            ny=(dispH-h>0)?(cyN*dispH-h/2)/(dispH-h):ny;
          } else { ny=clampV(ny,0,1); }
        }
        const arr=side===2?self.pageStampsRight:self.pageStamps;
        const it=arr.find(t=>t.id===s.id);
        if(it){ it.x=nx; it.y=ny; }
      };
      const up=ev=>{
        window.removeEventListener('mousemove',move);
        window.removeEventListener('mouseup',up);
        if(dragging){
          const arr=side===2?self.pageStampsRight:self.pageStamps;
          const it=arr.find(t=>t.id===s.id);
          if(it && window.Bridge && window.Bridge.invoke){
            window.Bridge.invoke('MoveStamp', it.id, Number(it.x.toFixed(4)), Number(it.y.toFixed(4))).then(function(json){
              let r={}; try{ r=JSON.parse(json); }catch(e){}
              if(r.ok){ self.opHint='已移动印章'; }
              else { self.opHint=r.error||'移动失败'; self.addLog('移动印章失败：'+(r.error||''),true); self.refreshPageStamps(); }
            },function(){ self.addLog('移动印章失败：桥调用失败',true); self.refreshPageStamps(); });
          }
        } else {
          // 单击：在章中心叠加盖章（含随机位移显示位置）
          if(!window.Bridge || !window.Bridge.invoke || !self.pdfLoaded){ return; }
          const page=side===2?(Number(self.curPage)+1):(Number(self.curPage)||1);
          const scale=dispW/(self.pagePts||595);
          const w=s.sizeMm*72/25.4*scale;
          const h=w*(s.imgH/s.imgW);
          const oxPx=s.offsetX*72/25.4*scale;
          const oyPx=s.offsetY*72/25.4*scale;
          let cx, cy;
          if(s.centerRatio){
            cx=(s.x*dispW+oxPx)/dispW; cy=(s.y*dispH+oyPx)/dispH;
          } else {
            cx=((dispW-w)*s.x+oxPx+w/2)/dispW;
            cy=((dispH-h)*s.y+oyPx+h/2)/dispH;
          }
          cx=Math.max(0,Math.min(1,cx)); cy=Math.max(0,Math.min(1,cy));
          window.Bridge.invoke('AddManualStamp', Number(cx.toFixed(4)), Number(cy.toFixed(4)), page).then(function(json){
            const r=JSON.parse(json);
            if(r.ok){ self.addLog('第 '+page+' 页已放置印章','ok'); self.refreshPageStamps(); }
            else { self.opHint=r.error||'盖章失败'; self.addLog(r.error||'盖章失败',true); }
          },function(err){ self.opHint='盖章失败：'+err.message; self.addLog('盖章失败：'+err.message,true); });
        }
      };
      window.addEventListener('mousemove',move);
      window.addEventListener('mouseup',up);
    },
    /* v2.4.0.52：骑缝章切片样式——按后端 GetSeamPreview 布局（页面比例 + 章图源矩形）用基准章图裁剪渲染。
       不旋转：div 直接按目标矩形；旋转（wzType 0/1 下/上）：div 先按旋转前尺寸（宽=目标高、高=目标宽）
       居中定位后 rotate(90deg)（GDI+ 与 CSS 同顺时针）。背景图按源矩形像素裁剪。 */
    seamStyle(sl,dispW){
      if(!this.seamBase.url){ return {display:'none'}; }
      const dispH=dispW*((this.pageH||842)/(this.pageW||595));
      const w=sl.w*dispW, h=sl.h*dispH;
      let left, top, width, height, transform;
      if(sl.rotated){
        const ww=h, hh=w;              // 旋转前尺寸（竖条：宽=目标高、高=目标宽）
        left=(sl.x*dispW + w/2) - ww/2;
        top=(sl.y*dispH + h/2) - hh/2;
        width=ww; height=hh;
        transform='rotate(90deg)';
      } else {
        left=sl.x*dispW; top=sl.y*dispH;
        width=w; height=h;
        transform='none';
      }
      const dw=width, dh=height;
      const bsw=dw/sl.srcW, bsh=dh/sl.srcH;
      return {
        left:left+'px', top:top+'px', width:width+'px', height:height+'px',
        transform:transform, transformOrigin:'center center',
        backgroundImage:'url('+this.seamBase.url+')',
        backgroundSize:bsw+'px '+bsh+'px',
        backgroundPosition:(-sl.srcX*bsw)+'px '+(-sl.srcY*bsh)+'px'
      };
    },
    /* v2.4.0.52：骑缝章预览同步——防抖 150ms，参数/印章/渲染页 pt 变化时重拉布局。
       基准图 URL 加 cache-bust（?t=Date.now()-随机）强制重载（后端每次重生成 seam_base.png）。 */
    _seamSync(){
      const self=this;
      if(!this.pdfLoaded || String(this.seamType)==='1'){ this.seamAll=[]; this.seamBase={url:'',w:0,h:0}; this._seamLastKey=''; return; } // V2.4.0.92：清空同时重置去重键
      if(!window.Bridge || !window.Bridge.invoke){ return; }
      const qfz=parseInt(this.seamType)||0;
      const wz=['下','上','左','右'].indexOf(this.sealPos);
      const wv=parseInt(this.posVal)||50;
      const ms=this.segAuto?0:Math.max(1,parseInt(this.segCount)||20); // v2.4.0.70：自动=传 0；V367：手动 0 过渡值按 1 生成；V368：文件夹不锁（可手动）
      const ptW=this.pagePts||595;
      const ptH=Math.round(ptW*(this.pageH||842)/(this.pageW||595));
      // V2.4.0.80：参数去重——参数组合（总页/类型/位置/百分比/分割数/页 pt）未变且已有结果时跳过，
      // 翻页/折叠/切视图等重复触发不再重拉 GetSeamPreview（后端每次重生成基准图+重算布局）；
      // 前端按 curPage 过滤的切片 computed 不受影响（布局与当前页无关，翻页无需重拉）。
      const key=[this.curFile||'',this.pageCount||1,qfz,wz,wv,ms,ptW,ptH].join('|'); // V2.4.0.92：key 加文档标识——换文档（含内容相同页数相同）强制重拉，修复骑缝章预览不刷新
      if(this._seamLastKey===key){ return; }
      this._seamLastKey=key;
      clearTimeout(this._seamTimer);
      this._seamTimer=setTimeout(()=>{
        if(!this.pdfLoaded || String(this.seamType)==='1'){ this.seamAll=[]; this.seamBase={url:'',w:0,h:0}; this._seamLastKey=''; return; } // V2.4.0.92：清空同时重置去重键
        const seq=++this._seamSeq;
        window.Bridge.invoke('GetSeamPreview', this.pageCount||1, qfz, wz, wv, ms, ptW, ptH).then(function(json){
          if(seq!==self._seamSeq){ return; }
          let r={}; try{ r=JSON.parse(json); }catch(e){}
          if(!r.ok){ self.seamAll=[]; self._seamLastKey=''; return; } // V2.4.0.92：失败清空同时重置去重键，下次参数变化重试
          self.seamBase={url:r.baseUrl+'?t='+Date.now()+'-'+Math.floor(Math.random()*1e6), w:r.baseW||0, h:r.baseH||0};
          self.seamAll=r.layout||[];
        },function(){ self.seamAll=[]; self._seamLastKey=''; }); // V2.4.0.92：invoke 失败清空同时重置去重键
      },150);
    },
    /* ---- 左栏 ---- */
    toggleCard(k){
      this.cards[k]=!this.cards[k];
    },
    expandAndGoto(key){
      this.leftCollapsed=false;
      this.cards[key]=true;
    },
    /* v2.4.0.36：左栏最小宽度 = 显示参数行（印章尺寸输入框右缘）+ 规范列间距 48px（动态测量，不硬编码数值）。
       v2.4.0.78：原"旋转处理"下拉已删除，改测"印章尺寸"行 cell 右缘（label 文本精确匹配；行内含 el-input-number 根为 span rect 恒 0，测 cell 右边缘=输入框右边缘）。
       测量时机：mounted $nextTick / 字体就绪 / 窗口 resize；折叠态或控件隐藏时不更新（保持当前值）。 */
    /* v2.4.0.57：二级折叠统一切换——切换后 nextTick 触发左栏宽度重测 + 窗口 resize 重算，解决折叠状态恢复后展开控件挤压 */
    toggleSect(sect,key){
      this[sect][key]=!this[sect][key];
      this.$nextTick(()=>{
        if(this.measureLeftMin) this.measureLeftMin();
        window.dispatchEvent(new Event('resize'));
      });
    },
};
