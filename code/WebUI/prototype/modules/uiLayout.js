/* PDFQFZ Web UI 前端模块：视图/浮窗/缩放/翻页/文件打开/拖放/日志/桥调用（V2.4.0.85 拆分）
 * index.html methods 中通过 ...window.PdfqModules.uiLayout 注入，
 * this 指向组件实例（Vue3 options API spread 保留绑定），函数体零改动。
 */
window.PdfqModules = window.PdfqModules || {};
window.PdfqModules.uiLayout = {
    /* ---- V346：统一工具条入口（图片/PDF 共用同一套模板，方法内按模式分派） ---- */
    barFileName(f){ return this.imgMode ? (f&&f.name||'') : this.dispName(f); },
    barSwitchFile(){
      if(this.imgMode){ this.imgShow(this.barFileIdx); }
      else { this.switchDirFile(); }
    },
    barPrev(){
      if(this.viewMode==='grid4'||this.viewMode==='grid8'){ const el=this.$refs.stage; if(el){ el.scrollTop=Math.max(0,el.scrollTop-320); this.gridOnScroll(); } return; } /* V1.0.0.46 需求1：网格滚轮替代翻页 */
      if(this.imgMode){ if(this.curImgIdx>0){ this.imgShow(this.curImgIdx-1); } }
      else { this.prevPage(); }
    },
    barNext(){
      if(this.viewMode==='grid4'||this.viewMode==='grid8'){ const el=this.$refs.stage; if(el){ el.scrollTop=Math.min(el.scrollHeight,el.scrollTop+320); this.gridOnScroll(); } return; } /* V1.0.0.46 需求1：网格滚轮替代翻页 */
      if(this.imgMode){ if(this.curImgIdx<this.imgQueue.length-1){ this.imgShow(this.curImgIdx+1); } }
      else { this.nextPage(); }
    },
    barGoPage(){
      if(this.viewMode==='grid4'||this.viewMode==='grid8'){ /* V1.0.0.56：网格页码跳转=目标页所在行整行置顶（已加载直接滚，未加载清空从行首重载）；页码以格子左上角页码为准
        V1.0.0.61：滚动不再自动选中左上角页，跳转需显式 curPage=目标页（选中框跟随）；未加载分支设置 _gridScrollToPage 由 _gridDone 锚定定位（V60 加占位后原实现漏定位，目标行被占位压到视口下方） */
        const v=Number(this.curPageInput); const total=this.pageCount||0;
        if(!v||!total){ this.curPageInput=String(this.curPage||1); return; }
        const t=Math.min(total,Math.max(1,v));
        const cols=this.viewMode==='grid4'?4:8;
        const rowStart=Math.floor((t-1)/cols)*cols+1; /* 目标页所在行行首：输入 20 → 17（20 保持行内原位置） */
        const el=this.$refs.stage;
        if(el && this.gridPages.some(function(g){return g.page===rowStart;})){ /* 目标行已加载：_gridLocate 定位（V1.0.0.63：非第一行居中，与切视图跳转一致） */
          this.curPage=String(t); this.curPageInput=String(t); /* V1.0.0.61：显式选中目标页 */
          this._gridJustLocated=true; setTimeout(function(){ this._gridJustLocated=false; }.bind(this),300); /* 跳转后 300ms 内 rAF 不覆盖页码输入框 */
          this._gridLocate(rowStart);
          this.gridOnScroll();
          return;
        }
        /* 目标行未加载：清空重载（换新数组引用防旧链回写污染），从行首起首批即含目标行，滚回顶部；_gridScrollToPage 由 _gridDone 锚定定位 */
        this.gridLoading=false; this.gridPages=[]; this.gridStart=1;
        this.curPage=String(t); this.curPageInput=String(t); this._gridScrollToPage=rowStart;
        this.gridLoad(rowStart);
        if(el){ el.scrollTop=0; }
        return;
      }
      if(this.imgMode){
        const v=Number(this.imgPageInput);
        if(!this.imgLoaded || !this.imgQueue.length || !v){ this.imgPageInput=String((this.curImgIdx>=0?this.curImgIdx+1:0)); return; }
        this.imgShow(Math.max(0,Math.min(this.imgQueue.length-1,v-1)));
      } else { this.goPage(); }
    },
    /* V1.0.0.57b：网格单击选中——选中框跟随单击页（curPage 同步，切回单页即该页；双击前先触发的 click 也设同页，无副作用） */
    gridSelect(g){
      if(!g || !g.page || g.failed){ return; }
      if(this.viewMode!=='grid4'&&this.viewMode!=='grid8'){ return; }
      const p=Math.max(1,Math.min(this.pageCount,Number(g.page)||1));
      this.curPage=String(p); this.curPageInput=String(p);
    },
    /* V1.0.0.57b：切回网格时刷新已加载页的水印预览（新加/修改的水印框在缓存页面上不可见问题）
       V1.0.0.64：收敛刷新范围——只刷新当前选中页 curPage 与所在行（≤8 页），其余页水印由 _gridLoadOverlays 滚动懒加载刷新；
       修复 4↔8 视图来回切换时对全部已加载页（≈64 页）无条件 GetPageWatermarks 调用、桥接队列积压致界面越切越卡 */
    _gridRefreshWms(){
      const self=this;
      if(this.viewMode!=='grid4'&&this.viewMode!=='grid8'){ return; }
      const cols=this.viewMode==='grid4'?4:8;
      const cur=Math.max(1,Number(this.curPage)||1);
      const rowStart=Math.floor((cur-1)/cols)*cols+1; /* 当前选中页所在行行首 */
      const todo=[];
      const gps=this.gridPages||[];
      for(let k=0;k<gps.length;k++){
        const g=gps[k]; if(!g||g.failed){ continue; }
        if(g.page===cur || (g.page>=rowStart && g.page<rowStart+cols)){ todo.push(g); } /* 当前页 + 所在行（≤8 页） */
      }
      if(!todo.length){ return; }
      self._diag('gridRefreshWms: 收敛刷新 '+todo.length+' 页（cur='+cur+' row='+rowStart+'）');
      todo.forEach(function(g){
        try{
          window.Bridge.invoke('GetPageWatermarks',g.page).then(function(j){
            if(self.viewMode!=='grid4'&&self.viewMode!=='grid8'){ return; }
            let a=[]; try{ a=JSON.parse(j); }catch(e){}
            const cur2=self.gridPages.find(function(x){ return x.page===g.page; });
            if(cur2){ cur2.wms=a; }
          });
        }catch(e){}
      });
    },
    /* V1.0.0.56：网格双击格子 → 单页视图定位该页（与工具条"单页"按钮一致）
       V1.0.0.57：先同步 curPage 再切视图——viewMode watch 会异步 renderPage(curPage) 且 renderPage 有"最新请求覆盖"守卫，
       若双击后手动 renderPage(g.page) 会被 watch 后发的 renderPage(curPage=滚动同步的视口顶部页) 覆盖（双击跳 13 页根因）；
       curPage 先设为目标页后，watch 渲染的即正确页 */
    gridGoSingle(g){
      if(!g || !g.page || !this.pdfLoaded){ return; }
      if(this.viewMode!=='grid4'&&this.viewMode!=='grid8'){ return; }
      const p=Math.max(1,Math.min(this.pageCount,Number(g.page)||1));
      this.curPage=String(p); this.curPageInput=String(p);
      this.setView('single');
    },
    barZoomIn(){
      if(this.viewMode==='double'||this.viewMode==='grid4'||this.viewMode==='grid8'){ return; }  // v1.0.0.5：双页/4页/8页视图固定100%，禁止缩放
      this._zoomAnchor=true;
      this.zoomText=this.clampZoomText(Number(parseFloat(this.zoomText)||100)+25)+'%';
      if(this.imgMode){ this._fitImg(); } else { this.fitPage(); }
    },
    barZoomOut(){
      if(this.viewMode==='double'||this.viewMode==='grid4'||this.viewMode==='grid8'){ return; }  // v1.0.0.5：双页/4页/8页视图固定100%，禁止缩放
      this._zoomAnchor=true;
      this.zoomText=this.clampZoomText(Number(parseFloat(this.zoomText)||100)-25)+'%';
      if(this.imgMode){ this._fitImg(); } else { this.fitPage(); }
    },
    barClampZoom(){
      if(this.viewMode==='double'||this.viewMode==='grid4'||this.viewMode==='grid8'){ return; }  // v1.0.0.5：双页/4页/8页视图固定100%，禁止缩放
      this._zoomAnchor=true;
      this.zoomText=this.clampZoomText(parseFloat(this.zoomText)||100)+'%';
      if(this.imgMode){ this._fitImg(); } else { this.fitPage(); }
    },
    /* 图片模式：缩放后重算适配宽（imgFitW 保持 fit-to-view 基准，imgDispW 按 zoomText 现算） */
    _fitImg(){
      if(!this.imgLoaded){ return; }
      const self = this;
      this.$nextTick(function(){ self.imgMeasure(); if (typeof self.wmRefitAllPages === 'function') { try { self.wmRefitAllPages(); } catch (e) {} } }); /* V1.0.0.20：图片缩放/resize 后重测预览字号+框高（文字随页面缩放比例一致） */
    },
    setView(v){
      this.rangeActive=false;
      // V346：图片模式不支持双页（按钮置灰 + 逻辑防御）；V1.0.0.46 需求1：多页下拉含双页/4页/8页，grid 同受 imgMode/debug 限制
      const multi=v==='double'||v==='grid4'||v==='grid8';
      if(multi && this.imgMode){ return; }
      if(multi && this.debugActive){ this.opHint='请先加载 PDF 文件后再进行多页视图'; return; }
      if(v==='grid4'||v==='grid8'){ /* V1.0.0.57：终止旧加载链+复位 loading，保留 gridPages/gridStart 缓存（返回网格可回看前面页，由 viewMode watch 定位当前行）；V1.0.0.60：同时终止向前补载链；V1.0.0.65：gridLoadingPrev（原名 _gridLoadingPrev 带 _ 前缀 Vue3 不代理） */ this._gridSeq++; this.gridLoading=false; this._gridSeqPrev++; this.gridLoadingPrev=false; this._prevItems=[]; this._gridObsInit(); this._gridResizeInit(); }
      else { this._gridObsDestroy(); this._gridResizeDestroy(); }
      this.viewMode=v;
    },
    /* V1.0.0.46 需求1：多页下拉命令入口 */
    gridViewPick(c){ this.setView(c); },
    /* V1.0.0.49：4/8 页网格懒加载（列数×2 页/批，串行渲染防乱序；滚到接近底部自动加载下一批）
       49 修复：①批尺寸降半降低首屏渲染压力（原列数×4，8页=32页/批易卡死）；②invoke 超时兜底（WebView2 调用卡住时强制跳页，
       防 gridLoading 永真白屏）；③全程 _diag 打点（复现时读日志定位卡点/异常）；④按页拉水印框（GetPageWatermarks，与章一致，不再复制当前页框） */
    gridLoad(start){
      if(!this.pdfLoaded || (this.viewMode!=='grid4'&&this.viewMode!=='grid8')){ return; }
      if(this.gridLoading){ return; }
      const total=this.pageCount||0; if(!total){ return; }
      const batch=32; /* V1.0.0.61：批量渲染 32 页/批（RenderGridPageBatch 一次 invoke，WebView2 往返 32→1，千页滚动连续加载不露底） */
      const raw=Number(start)||1; /* V1.0.0.51：原始页码超界直接返回——修复 Math.min 钳制致越界拦截永不生效（第9页无限重复加载） */
      if(raw>total){ return; }
      const s=Math.max(1,Math.min(total,raw));
      const self=this; const seq=++this._gridSeq; this.gridLoading=true; self._gridRetry=[]; /* V1.0.0.51：本批失败页重试列表 */
self._gridEnd=Math.min(total,s+batch-1); /* V1.0.0.52：记录本批末页——批次收尾用 end+1 续载，修复 _gridDone 推到 pageCount+1 致后续批次永不触发（4页视图10页缺9/10） */
      self._diag('gridLoad: view='+this.viewMode+' s='+s+' batch='+batch+' total='+total+' seq='+seq);
      /* V1.0.0.49：invoke 超时兜底——卡住/慢返回时标记超时并只 resolve 一次（正常传结果、异常/超时传 null），外层统一走一次 next，避免双重 next 跳页错乱 */
      const withTimeout=function(promise,ms,mark){
        return new Promise(function(resolve){
          let done=false;
          const timer=setTimeout(function(){ if(!done){ done=true; mark.timedOut=true; } resolve(null); },ms);
          promise.then(function(v){ if(!done){ done=true; clearTimeout(timer); } resolve(v); },
            function(){ if(!done){ done=true; clearTimeout(timer); } resolve(null); });
        });
      };
      /* V1.0.0.61：批量渲染——一次 invoke 32 页；整批超时/失败 → 整批 failed 占位（不逐页重试，避免 32×2 串行卡死）；单页失败 → 进 _gridRetry 单页重试 */
      const m1={timedOut:false};
      const pushFailed=function(a,b){ for(let k=a;k<=b;k++){ if(!self.gridPages.some(function(x){return x.page===k;})){ self.gridPages.push({page:k,url:'',stamps:[],wms:[],ptW:0,ptH:0,failed:true,ovLoaded:true,ovStampsLoaded:true}); } } };
      try{
        withTimeout(window.Bridge.invoke('RenderGridPageBatch',s-1,batch,72),15000,m1).then(function(json){
          if(seq!==self._gridSeq){ self._diag('gridLoad: 批量返回但链已失效，终止'); return; }
          if(m1.timedOut){ self._diag('gridLoad: 批量渲染超时，'+batch+' 页整批占位'); pushFailed(s,self._gridEnd); self._gridDone(seq); return; }
          let r={}; try{ r=JSON.parse(json); }catch(e){ r={}; self._diag('gridLoad: 批量 JSON 解析失败，整批占位'); }
          if(!r || !r.ok){ self._diag('gridLoad: 批量渲染失败：'+(r&&r.error?r.error:'未知')+'，整批占位'); pushFailed(s,self._gridEnd); self._gridDone(seq); return; }
          const items=r.items||[];
          self._diag('gridLoad: 批量返回 '+items.length+' 页');
          for(let k=0;k<items.length;k++){
            const it=items[k]; if(!it){ continue; }
            const pg=Number(it.page)||0; if(pg<1){ continue; }
            if(self.gridPages.some(function(x){return x.page===pg;})){ continue; } /* V1.0.0.51：按页去重防御 */
            if(!it.ok){ self._diag('gridLoad: 页'+pg+' 渲染失败：'+(it.error||'未知')+'，待单页重试'); self._gridRetry.push(pg); continue; }
            self.gridPages.push({page:pg,url:it.url+'?v='+(++self._imgSeq),stamps:[],wms:[],ptW:it.ptW||(it.w/2),ptH:it.ptH||(it.h/2),ovLoaded:false,ovStampsLoaded:false}); /* V1.0.0.60：ovLoaded 标记浮层已拉取（视口懒加载去重） */
          }
          self._diag('gridLoad: 批量完成，实入 '+(items.length-(self._gridRetry.length))+' 页');
          if(self._gridRetry.length){ self._diag('gridLoad: 批次完成，待补 '+self._gridRetry.length+' 页'); self._gridRetryNext(seq); return; }
          self._gridDone(seq);
        },function(){
          if(seq!==self._gridSeq){ return; }
          self._diag('gridLoad: 批量 invoke 异常，整批占位');
          pushFailed(s,self._gridEnd); self._gridDone(seq);
        });
      }catch(e){ if(seq!==self._gridSeq){ return; } self._diag('gridLoad: 批量链异常：'+e.message+'，整批占位'); pushFailed(s,self._gridEnd); self._gridDone(seq); }
    },
    /* V1.0.0.51：批次收尾——失败页已补齐或无需补齐时置完成态并打渲染校验点 */
    _gridDone(seq){
      const self=this; if(seq!==this._gridSeq){ self._diag('gridDone: 链已失效，终止'); return; } self.gridLoading=false; self.gridStart=(self._gridEnd||0)+1; /* V1.0.0.52：按本批末页续载（原 pageCount+1 跳过未加载批次——4页视图10页缺9/10 根因） */
      self._diag('gridLoad: 批次完成 gridLoading=false gridStart='+self.gridStart);
      self.$nextTick(function(){
        try{ var _n=self.$refs.stage?self.$refs.stage.querySelectorAll('.grid-cell').length:0; self._diag('gridLoad: 界面已渲染 grid-cell='+_n+' / gridPages='+self.gridPages.length); }catch(e){ self._diag('gridLoad: 渲染检查异常：'+e.message); }
        /* V1.0.0.53：批次完成后若网格未填满视口——哨兵在视口内不越界不回调、gridOnScroll 需滚动到底才触发，首批后无人续载（4页视图10页只显示前8页根因）；主动续载直到填满视口或全部加载完。上百页首批 2 行通常已超视口高度，不触发，滚动懒加载保留 */
        if(self.gridStart<=self.pageCount){
          try{
            const _st=self.$refs.stage;
            if(!_st || _st.scrollHeight<=_st.clientHeight+10){ self._diag('gridLoad: 网格未填满视口，主动续载 gridStart='+self.gridStart); self.gridTryLoadNext(); }
          }catch(e){ self._diag('gridLoad: 续载检查异常：'+e.message); }
        }
        /* V1.0.0.57：切回网格补载目标行完成后滚动定位（viewMode watch 设置 _gridScrollToPage）；未找到且未加载完→主动续载下一批等定位（修复任意窗口高度下目标行滚不到顶、选中框停留首页）；已全部加载完仍找不到则放弃，避免死循环
           V1.0.0.60：定位后 _gridJustLocated 锁定 curPage（选中框停留当前页，滚动同步 300ms 内不覆盖到视口顶部页）；并触发视口懒加载补章/水印
           V1.0.0.61：定位改 _gridLocate——锚定场景（目标行=当前加载段第一行）直接滚到 gridTopPad（占位高度=目标行前偏移，绝对可靠，不受差值法 scrollTop 旧值/异步重排影响），150ms 二次校正；定位后主动向前补载一批（用户向上滚立即可见前面页，不再是大片空白占位） */
        if(self._gridScrollToPage>0){
          try{
            const locPage=self._gridScrollToPage;
            if(typeof self._gridLocate==='function'){ self._gridLocate(locPage); }
            self._gridScrollToPage=0;
            if(typeof self.gridOnScroll==='function'){ self.gridOnScroll(); }
            if(typeof self._gridLoadOverlays==='function'){ self._gridLoadOverlays(locPage); } /* V1.0.0.65：锚定行 ±32 页预载章/水印（选中行上下先渲染一部分） */
            if(typeof self.gridLoadPrev==='function'){ self.gridLoadPrev(); } /* V1.0.0.61：锚定后主动补前面 32 页（批量一次），向前滚动不露白 */
          }catch(e){ self._diag('gridLoad: 定位滚动异常：'+e.message); self._gridScrollToPage=0; }
        }
        /* V1.0.0.77：批次完成后统一刷新虚拟化窗口（数据更新→占位替换为真实格；窗口内未加载区触发向下/向前补载判定） */
        try{ if(typeof self.gridOnScroll==='function'){ self.gridOnScroll(); } }catch(e){ self._diag('gridLoad: 刷新窗口异常：'+e.message); }
      });
    },
    /* V1.0.0.63：网格目标行两步定位——①纯计算粗定位（V1.0.0.77：虚拟化后容器高度恒定=全文档，目标行绝对偏移=targetRow×行高，不再依赖 gridTopPad/相对行偏移）；
       ②布局稳定+目标格图片就绪后 getBoundingClientRect 差值精校准；居中规则：非文档第一行时目标行在视口垂直居中（第一行保持置顶）；
       _gridJustLocated 锁 curPage 300ms，150ms 二次校正防异步重排 */
    _gridLocate(locPage){
      const self=this;
      const el=self.$refs.stage; if(!el){ return; }
      const cols=self.viewMode==='grid4'?4:8;
      const gp=self.gridPages||[]; if(!gp.length){ return; }
      const targetRow=Math.floor((locPage-1)/cols);
      /* 第一步：纯计算粗定位（同步）——行高统一估算（A4 高宽比+gap），精确且不依赖 DOM/图片加载 */
      const rowPitch=self._gridRowH(); /* V1.0.0.77：统一行高（原 gridTopPad+相对行偏移——虚拟化后直接 targetRow×rowH） */
      const vh=el.clientHeight||0;
      let top=Math.max(0, targetRow*rowPitch);
      if(targetRow>0 && vh>rowPitch){ top=Math.max(0, top-(vh-rowPitch)/2); } /* 居中（文档第一行保持置顶） */
      el.scrollTop=Math.max(0,top);
      if(typeof self.gridOnScroll==='function'){ try{ self.gridOnScroll(); }catch(e){} } /* V1.0.0.77：程序化设置 scrollTop 不触发 @scroll，主动刷新虚拟化窗口使目标行渲染（第二步精校准依赖 DOM） */
      /* 第二步：布局稳定+目标格图片就绪后差值精校准（固定格高下微调估算偏差） */
      self.$nextTick(function(){
        const el2=self.$refs.stage; if(!el2){ return; }
        const c=el2.querySelector('.grid-cell[data-page="'+locPage+'"]');
        if(!c){ return; }
        const refine=function(){
          const el3=self.$refs.stage; if(!el3){ return; }
          const sr=el3.getBoundingClientRect(), cr=c.getBoundingClientRect();
          const vh2=el3.clientHeight||0, rh=c.offsetHeight||0;
          let d=cr.top-sr.top;
          if(targetRow>0 && vh2>rh){ d-=(vh2-rh)/2; }
          el3.scrollTop=Math.max(0,el3.scrollTop+d);
        };
        const img=c.querySelector('img.page-img');
        if(img && !img.complete){
          const timer=setTimeout(function(){ clearTimeout(timer); refine(); },500); /* 图片超时兜底 */
          img.addEventListener('load',function(){ clearTimeout(timer); refine(); },{once:true});
        } else { refine(); }
      });
      self._gridJustLocated=true;
      setTimeout(function(){ self._gridJustLocated=false; },300);
      setTimeout(function(){
        if(!self._gridJustLocated){ return; } /* 用户已手动滚动，不拉回视口 */
        const el4=self.$refs.stage; if(!el4){ return; }
        const c4=el4.querySelector('.grid-cell[data-page="'+locPage+'"]');
        if(c4){
          const sr4=el4.getBoundingClientRect(), cr4=c4.getBoundingClientRect();
          const vh4=el4.clientHeight||0, rh4=c4.offsetHeight||0;
          let d4=cr4.top-sr4.top;
          if(targetRow>0 && vh4>rh4){ d4-=(vh4-rh4)/2; }
          d4=Math.round(d4);
          if(d4>1||d4<-1){ el4.scrollTop=Math.max(0,el4.scrollTop+d4); }
        }
      },150);
    },
    /* V1.0.0.51：失败/超时页自动重试（最多 2 次，5s 超时），仍失败 push '加载失败' 占位格——修复 grid 首开少渲染一页 */
    _gridRetryNext(seq){
      const self=this;
      const list=self._gridRetry.slice(); self._gridRetry=[];
      if(!list.length){ self._gridDone(seq); return; }
      self._diag('gridLoad: 重试失败页 '+list.join(',')+'（共 '+list.length+' 页）');
      const withTimeout=function(promise,ms,mark){ return new Promise(function(resolve){ let done=false; const timer=setTimeout(function(){ if(!done){ done=true; mark.timedOut=true; } resolve(null); },ms); promise.then(function(v){ if(!done){ done=true; clearTimeout(timer); } resolve(v); }, function(){ if(!done){ done=true; clearTimeout(timer); } resolve(null); }); }); };
      const one=function(i,remain,next){
        if(seq!==self._gridSeq){ return; }
        if(self.gridPages.some(function(x){return x.page===i;})){ next(); return; }
        self._diag('gridLoad: 重试第 '+i+' 页开始');
        const m1={timedOut:false};
        try{
          withTimeout(window.Bridge.invoke('RenderGridPage',i-1,72),5000,m1).then(function(json){
            if(m1.timedOut){ if(seq!==self._gridSeq){ return; } if(remain>1){ self._diag('gridLoad: 页'+i+' 重试超时，再试'); one(i,remain-1,next); } else { self._diag('gridLoad: 页'+i+' 重试仍超时，占位'); self.gridPages.push({page:i,url:'',stamps:[],wms:[],ptW:0,ptH:0,failed:true,ovLoaded:true,ovStampsLoaded:true}); next(); } return; }
            if(seq!==self._gridSeq){ return; }
            let r={}; try{ r=JSON.parse(json); }catch(e){ r={}; }
            if(!r||!r.ok){ if(seq!==self._gridSeq){ return; } if(remain>1){ self._diag('gridLoad: 页'+i+' 重试失败，再试'); one(i,remain-1,next); } else { self._diag('gridLoad: 页'+i+' 重试仍失败，占位'); self.gridPages.push({page:i,url:'',stamps:[],wms:[],ptW:0,ptH:0,failed:true,ovLoaded:true,ovStampsLoaded:true}); next(); } return; }
            const item={page:i,url:r.url+'?v='+(++self._imgSeq),stamps:[],wms:[],ptW:r.ptW||(r.w/2),ptH:r.ptH||(r.h/2),ovLoaded:false,ovStampsLoaded:false}; /* V1.0.0.60：浮层由 _gridLoadOverlays 懒加载 */
            self.gridPages.push(item); self._diag('gridLoad: 重试页'+i+' 图片完成');
            next();
          },function(){ if(seq!==self._gridSeq){ return; } if(remain>1){ one(i,remain-1,next); } else { self.gridPages.push({page:i,url:'',stamps:[],wms:[],ptW:0,ptH:0,failed:true,ovLoaded:true,ovStampsLoaded:true}); next(); } });
        }catch(e){ if(remain>1){ one(i,remain-1,next); } else { self.gridPages.push({page:i,url:'',stamps:[],wms:[],ptW:0,ptH:0,failed:true,ovLoaded:true,ovStampsLoaded:true}); next(); } }
      };
      const go=function(k){ if(k>=list.length){ self._gridDone(); return; } one(list[k],2,function(){ go(k+1); }); };
      go(0);
    },
    /* V1.0.0.50：grid 单元格图片加载失败打点（定位 grid 白屏的图片加载路径） */
    gridImgErr(ev,g){ try{ this._diag('gridImgErr: 页'+(g&&g.page)+' 图片加载失败 url='+String((g&&g.url)||'').substring(0,80)); }catch(e){} },
    /* V1.0.0.51：IntersectionObserver 哨兵无限滚动（浏览器原生标准无限滚动模式）——替代 scroll 高频计算，根治已加载完仍反复触发 */
    gridTryLoadNext(){
      if(this.viewMode!=='grid4'&&this.viewMode!=='grid8'){ return; }
      if(this.gridLoading){ return; }
      if(this.gridStart>this.pageCount){ return; }
      this.gridLoad(this.gridStart);
    },
    /* V1.0.0.77：哨兵 IntersectionObserver 移除——页级虚拟化后向下/向前补载由 gridOnScroll 窗口计算精确触发（不依赖底部哨兵 DOM 位置）；保留空壳防 setView 引用报错 */
    _gridObsInit(){},
    _gridObsDestroy(){},
    /* V1.0.0.53：grid 视图窗口/分隔条改变预览区尺寸——浮层样式调 gridCellW() 直接读 DOM 格宽（非响应式），resize 不触发 Vue 重渲染，章/水印/骑缝章停留在旧格宽状态；用 ResizeObserver 观察 stage 递增响应式 gridCellTick 驱动浮层重渲染（对齐单/双页 imgW/imgW2 响应式链，浏览器原生 API 不自写算法） */
    _gridResizeInit(){
      const self=this;
      if(self._gridRo || typeof ResizeObserver==='undefined'){ return; }
      const stage=self.$refs.stage; if(!stage){ return; }
      try{
        self._gridRo=new ResizeObserver(function(){ self.gridCellTick=(self.gridCellTick||0)+1; self._gridResizeLocate(); });
        self._gridRo.observe(stage);
        self._diag('gridResize: stage 尺寸监听已建立');
      }catch(e){ self._diag('gridResize init 异常：'+e.message); }
    },
    /* V1.0.0.65：窗口/预览区 resize 后选中页所在行重新居中（格宽变化致行高变化、选中行漂移出视口；与切视图同 _gridLocate 两步定位；150ms 防抖） */
    _gridResizeLocate(){
      const self=this;
      if(this.viewMode!=='grid4'&&this.viewMode!=='grid8'){ return; }
      const cur=Math.max(1,Number(this.curPage)||1);
      const cols=this.viewMode==='grid4'?4:8;
      const rowStart=Math.floor((cur-1)/cols)*cols+1;
      if(!this.gridPages.some(function(g){ return g.page===rowStart; })){ return; } /* 选中行未加载不定位（首次进入等 gridLoad 完成由 _gridDone 定位） */
      if(self._gridResizeTimer){ clearTimeout(self._gridResizeTimer); }
      self._gridResizeTimer=setTimeout(function(){ self._gridResizeTimer=0; self._gridLocate(rowStart); },150);
    },
    _gridResizeDestroy(){ if(this._gridRo){ try{ this._gridRo.disconnect(); }catch(e){} this._gridRo=null; } },
    /* V1.0.0.77：网格滚动主逻辑（A+B+C 重构）——
       ① C 窗口更新：gridWinTopRow/gridWinRows 响应式驱动模板只渲染视口±2 屏行（页级虚拟化，DOM 恒定 ≤120）；
       ② A 向前补载判定修正：窗口顶行（±2 屏缓冲）触及 gridPages[0] 所在行才补——原 cr.top-sr.top<600 对上方远处格恒成立
         （格子顶<容器顶差值恒负）→ 75 秒一口气补完千页 DOM 爆炸根因；守卫 page>1 + gridLoadingPrev 锁；
       ③ 向下补载：窗口底行+2 屏进入未加载区（gridStart 范围内）即 gridLoad（替代哨兵，更精确）；
       ④ B 章子/水印补载与滚动解耦：滚动停止 300ms 后才补视口±1 屏（不再滚动中逐批追着加载，插入由 Vue 随行渲染）；
       滚动事件 rAF 节流保留 */
    gridOnScroll(){
      if(this.viewMode!=='grid4'&&this.viewMode!=='grid8'){ return; }
      const el=this.$refs.stage; if(!el){ return; }
      const self=this;
      /* B：滚动停止 300ms 后补章/水印（防抖；切视图/锚定/加载完成直接调用 _gridLoadOverlays 不走此路） */
      if(self._gridOvTimer){ clearTimeout(self._gridOvTimer); }
      self._gridOvTimer=setTimeout(function(){
        self._gridOvTimer=0;
        if(self.viewMode==='grid4'||self.viewMode==='grid8'){ try{ self._gridLoadOverlays(); }catch(e){} }
      },300);
      if(!self._gridTopRaf){
        self._gridTopRaf=requestAnimationFrame(function(){
          self._gridTopRaf=0;
          try{
            const cols=self.viewMode==='grid4'?4:8;
            const rowH=self._gridRowH();
            const st=el.scrollTop||0, vh=el.clientHeight||0;
            const topRow=Math.max(0, Math.floor(st/rowH));
            const visRows=Math.max(1, Math.ceil(vh/rowH));
            /* C：窗口更新——滚动位置/尺寸变化时只重渲染窗口±2 屏行（范围外 DOM 由 Vue 移除，gridPages 数据保留） */
            if(self.gridWinTopRow!==topRow || self.gridWinRows!==visRows){ self.gridWinTopRow=topRow; self.gridWinRows=visRows; }
            const gp=self.gridPages||[];
            /* A：向前补载（修正判定）——gridPages[0] 所在行进入窗口顶行-2 屏内才补；补载锁防递归（完成后由 _finishPrev 延迟 100ms 复查） */
            if(gp.length && gp[0].page>1 && !self.gridLoadingPrev){
              const row0=Math.floor((gp[0].page-1)/cols);
              if(topRow-2<=row0){ self.gridLoadPrev(); }
            }
            /* 向下补载：窗口底行+2 屏进入未加载区（gridStart 范围内）即加载——提前 2 屏预载，连续滚动不露底 */
            if(self.gridStart>0 && self.gridStart<=self.pageCount && !self.gridLoading){
              const needPage=Math.min(self.pageCount, (topRow+visRows+3)*cols);
              if(self.gridStart<=needPage){ self.gridLoad(self.gridStart); }
            }
          }catch(e){}
        });
      }
    },
    /* V1.0.0.77：网格章/水印浮层补载——虚拟化后视口外无 DOM（不能 querySelector cell rect），改按页码窗口判定（视口±1 屏）；
       B：滚动停止 300ms 后由 gridOnScroll 防抖触发（不再滚动中逐批追着加载）；切视图/锚定/加载完成场景直接调用（preloadAnchor=锚定行±32 页）；
       数据写入 g.stamps/g.wms 后由 Vue 随行容器自动渲染/回收（与页同步，无需手动插 DOM） */
    /* V1.1.0.2：切回网格时重置可视区章"已加载"标记（stamps 数据保留立即显示，随后由 _gridLoadOverlays 后台重拉最新章）——
       覆盖"先切网格（拉空/被切走丢弃）→ 盖章 → 再切回网格"与"进放大视图后回网格"两类章丢失场景 */
    _gridResetVisibleStamps(){
      if(this.viewMode!=='grid4'&&this.viewMode!=='grid8'){ return; }
      const el=this.$refs.stage; if(!el){ return; }
      const cols=this.viewMode==='grid4'?4:8;
      const rowH=this._gridRowH();
      const st=el.scrollTop||0, vh=el.clientHeight||0;
      const topRow=Math.max(0, Math.floor(st/rowH));
      const visRows=Math.max(1, Math.ceil(vh/rowH));
      const fromPage=Math.max(1,(topRow-1)*cols+1);
      const toPage=Math.min(this.pageCount||0,(topRow+visRows+2)*cols);
      const gps=this.gridPages||[];
      for(let k=0;k<gps.length;k++){
        const g=gps[k];
        if(g && !g.failed && g.page>=fromPage && g.page<=toPage){ g.ovStampsLoaded=false; }
      }
    },
    _gridLoadOverlays(preloadAnchor){
      const self=this;
      if(this.viewMode!=='grid4'&&this.viewMode!=='grid8'){ return; }
      const el=this.$refs.stage; if(!el){ return; }
      const cols=this.viewMode==='grid4'?4:8;
      const rowH=this._gridRowH();
      const st=el.scrollTop||0, vh=el.clientHeight||0;
      const topRow=Math.max(0, Math.floor(st/rowH));
      const visRows=Math.max(1, Math.ceil(vh/rowH));
      const fromPage=Math.max(1, (topRow-1)*cols+1); /* 视口上 1 屏 */
      const toPage=Math.min(this.pageCount||0, (topRow+visRows+2)*cols); /* 视口下 1 屏 */
      const gps=this.gridPages||[];
      const todo=[];
      for(let k=0;k<gps.length;k++){
        const g=gps[k];
        if(!g || g.failed || (g.ovLoaded && g.ovStampsLoaded)){ continue; }
        if(preloadAnchor){ /* 预载模式：锚定行 ±32 页 */
          if(Math.abs(g.page-preloadAnchor)>32){ continue; }
        } else {
          if(g.page<fromPage || g.page>toPage){ continue; }
        }
        todo.push(g);
      }
      if(!todo.length){ return; }
      self._diag('gridOverlays: '+(preloadAnchor?'锚定预载':'视口内')+'补章/水印 '+todo.length+' 页（'+todo[0].page+'..'+todo[todo.length-1].page+'）');
      /* V1.0.0.70：章批量一次桥接（GetPageStampsBatch，small=1 网格专用小图 + 同批内容指纹复用），不再逐页 invoke；
         水印数据是纯内存 JSON（轻量），保留逐页 8 并发 pump */
      const stampsTodo=todo.filter(function(g){ return !g.ovStampsLoaded && !g._stampFetching; });
      if(stampsTodo.length){
        const pages=stampsTodo.map(function(g){ return g.page; });
        /* V1.1.0.2：修复"切走视图后回网格章永久缺失"——不再"发起即置已加载"；
           改用 _stampFetching 防并发；成功写入 g.stamps 后才置 ovStampsLoaded；
           切走视图/失败/解析失败只复位 _stampFetching，下次切回网格或滚动到时自然重拉 */
        stampsTodo.forEach(function(g){ g._stampFetching=true; });
        const releaseFetch=function(){ for(let k=0;k<stampsTodo.length;k++){ stampsTodo[k]._stampFetching=false; } };
        try{
          window.Bridge.invoke('GetPageStampsBatch', JSON.stringify(pages), 1).then(function(j2){
            if(self.viewMode!=='grid4'&&self.viewMode!=='grid8'){ releaseFetch(); return; }
            let arr=[]; try{ arr=JSON.parse(j2); }catch(e){}
            if(Array.isArray(arr)){
              for(let k=0;k<arr.length;k++){
                const it=arr[k]; if(!it) continue;
                const g=gps.find(function(x){ return x.page===it.page; });
                if(!g) continue;
                const a=it.stamps||[];
                for(let m=0;m<a.length;m++){ if(a[m]&&a[m].url){ a[m].url=a[m].url+'?v='+(++self._imgSeq); } }
                g.stamps=a;
                g.ovStampsLoaded=true; /* 成功写入后才算已加载 */
              }
            }
            releaseFetch();
          }).catch(function(){ releaseFetch(); });
        }catch(e){ releaseFetch(); }
      }
      const wmTodo=todo.filter(function(g){ return !g.ovLoaded; });
      if(wmTodo.length){
        const MAXC=8; let idx=0;
        const pump=function(){
          if(idx>=wmTodo.length){ return; }
          const g=wmTodo[idx++];
          g.ovLoaded=true; /* 先置位防重复 */
          try{
            window.Bridge.invoke('GetPageWatermarks',g.page).then(function(j3){
              if(self.viewMode!=='grid4'&&self.viewMode!=='grid8'){ return; }
              let w=[]; try{ w=JSON.parse(j3); }catch(e){}
              if(Array.isArray(w)){ g.wms=w; }
            }).catch(function(){}).finally(function(){ pump(); });
          }catch(e){ pump(); }
        };
        for(let c=0;c<MAXC;c++){ pump(); }
      }
    },
    /* V1.0.0.60：向前补载——网格顶部占位区向上滚动时加载 gridPages 首页之前的一批（start..end 升序，收集后统一 unshift 插入头部保序）；
       独立 _gridSeqPrev 链防与向下加载互扰；setView/切文档时终止
       V1.0.0.61：改批量渲染——一次 invoke 32 页（RenderGridPageBatch），顶部补载不再逐页往返；锚定后由 _gridDone 主动触发一次，用户向上滚立即可见前面页 */
    gridLoadPrev(fromPage){
      if(this.viewMode!=='grid4'&&this.viewMode!=='grid8'){ return; }
      if(this.gridLoadingPrev){ return; }
      const batch=32;
      let start,end,P=0; /* V1.0.0.66：P 提升到分支外声明——fromPage 分支原先只在 else 分支 const P，打点行引用 P 抛 ReferenceError（curPage>1 切网格必现），致 watch 中断、前段页永不补载且 gridLoadingPrev 恒 true */
      if(fromPage){ /* V1.0.0.65：显式锚定起点（切视图未加载分支并行调用）——不依赖 gridPages[0]（首次加载时 gridPages 尚空，旧入口直接 return 致并行补载空转） */
        P=fromPage; start=Math.max(1,fromPage-batch); end=fromPage-1;
        if(end<start){ return; }
      } else {
        const gp=this.gridPages||[]; if(!gp.length){ return; }
        P=gp[0].page||1; if(P<=1){ return; }
        start=Math.max(1,P-batch), end=P-1;
      }
      const self=this; const seq=++this._gridSeqPrev; this.gridLoadingPrev=true; this._prevItems=[];
      self._diag('gridLoadPrev: 向前补载 '+start+'..'+end+'（当前首页 '+P+'） seq='+seq);
      const withTimeout=function(promise,ms,mark){ return new Promise(function(resolve){ let done=false; const timer=setTimeout(function(){ if(!done){ done=true; mark.timedOut=true; } resolve(null); },ms); promise.then(function(v){ if(!done){ done=true; clearTimeout(timer); } resolve(v); }, function(){ if(!done){ done=true; clearTimeout(timer); } resolve(null); }); }); };
      const pushFailed=function(a,b){ for(let k=a;k<=b;k++){ if(!self.gridPages.some(function(x){return x.page===k;})){ self._prevItems.push({page:k,url:'',stamps:[],wms:[],ptW:0,ptH:0,failed:true,ovLoaded:true,ovStampsLoaded:true}); } } };
      const m1={timedOut:false};
      try{
        withTimeout(window.Bridge.invoke('RenderGridPageBatch',start-1,batch,72),15000,m1).then(function(json){
          if(seq!==self._gridSeqPrev){ return; }
          if(m1.timedOut){ self._diag('gridLoadPrev: 批量超时，全部占位'); pushFailed(start,end); self._finishPrev(seq); return; }
          let r={}; try{ r=JSON.parse(json); }catch(e){ r={}; self._diag('gridLoadPrev: 批量 JSON 解析失败，全部占位'); }
          if(!r||!r.ok){ self._diag('gridLoadPrev: 批量失败：'+(r&&r.error?r.error:'未知')+'，全部占位'); pushFailed(start,end); self._finishPrev(seq); return; }
          const items=r.items||[];
          self._diag('gridLoadPrev: 批量返回 '+items.length+' 页');
          for(let k=0;k<items.length;k++){
            const it=items[k]; if(!it){ continue; }
            const pg=Number(it.page)||0; if(pg<1){ continue; }
            if(self.gridPages.some(function(x){return x.page===pg;})){ continue; } /* 与已加载重叠防御 */
            if(!it.ok){ self._diag('gridLoadPrev: 页'+pg+' 失败，占位'); self._prevItems.push({page:pg,url:'',stamps:[],wms:[],ptW:0,ptH:0,failed:true,ovLoaded:true,ovStampsLoaded:true}); continue; }
            self._prevItems.push({page:pg,url:it.url+'?v='+(++self._imgSeq),stamps:[],wms:[],ptW:it.ptW||(it.w/2),ptH:it.ptH||(it.h/2),ovLoaded:false,ovStampsLoaded:false}); /* 浮层待滚动到时懒加载 */
          }
          self._finishPrev(seq);
        },function(){ if(seq!==self._gridSeqPrev){ return; } self._diag('gridLoadPrev: invoke 异常，全部占位'); pushFailed(start,end); self._finishPrev(seq); });
      }catch(e){ if(seq!==self._gridSeqPrev){ return; } self._diag('gridLoadPrev: 链异常：'+e.message+'，全部占位'); pushFailed(start,end); self._finishPrev(seq); }
    },
    /* V1.0.0.60：向前补载收尾——按页码升序合并去重后统一插入头部（保序）
       V1.0.0.61：不再补偿 scrollTop——插入头部页数与 gridTopPad 占位同步减少量相等（净高度不变），视口天然稳定；原补偿使视口向下跳变（V60 实测"本行在中间/向前滚动跳动"相关根因） */
    _finishPrev(seq){
      const self=this;
      if(seq!==self._gridSeqPrev){ return; }
      self.gridLoadingPrev=false;      const items=self._prevItems||[]; self._prevItems=[];
      if(!items.length){ return; }
      items.sort(function(a,b){ return a.page-b.page; });
      const merged=items.concat(self.gridPages||[]);
      const seen={}; const clean=[];
      for(let k=0;k<merged.length;k++){ const p=merged[k].page; if(!seen[p]){ seen[p]=true; clean.push(merged[k]); } }
      self.gridPages=clean;
      self._diag('gridLoadPrev: 完成，插入 '+items.length+' 页（'+items[0].page+'..'+items[items.length-1].page+'）');
      /* V1.0.0.77：①不再立即补章（B 解耦——新插入页 ovLoaded/ovStampsLoaded 保持 false，滚动停止 300ms 由 gridOnScroll 统一补视口±1 屏）；
         ②延迟 100ms 复查滚动状态（A 补载完成后的延迟检查——gridLoadingPrev 已复位，若窗口仍触及边界则继续补载，防同步递归/漏补） */
      setTimeout(function(){ try{ if(self.viewMode==='grid4'||self.viewMode==='grid8'){ self.gridOnScroll(); } }catch(e){} },100);
    },
    /* V1.0.0.48：网格单元格实际显示宽——章/水印浮层按格宽缩放（读首个已渲染格，失败回退与 gridCellStyle 相同的 CSS calc 公式） */
    gridCellW(){
      try{ const el=this.$refs.stage?this.$refs.stage.querySelector('.grid-cell'):null; if(el&&el.clientWidth>0){ return el.clientWidth; } }catch(e){}
      const stage=this.$refs.stage; const sw=(stage&&stage.clientWidth)?(stage.clientWidth-48):780; /* V1.0.0.49：回退公式对齐 gridCellStyle CSS calc（含 .page-grid padding 12px + gap） */
      return this.viewMode==='grid8' ? Math.max(40,(sw-96)/8) : Math.max(40,(sw-48)/4);
    },
    /* V1.0.0.72：纯计算格宽（不读 DOM）——gridCellStyle/gridTopPad 在 Vue 渲染期间同步调用，此时 DOM 仍是上一视图的格子宽（4↔8 互切时 gridCellW() 读到旧宽 → 高用旧宽×比例 → 4 页格子横向只显示上半部、8 页格子超长像两张拼；ResizeObserver 只监听 stage 不监听格宽，错误持续）。与 CSS calc 同款公式，宽高同源。 */
    gridCellWCalc(){
      const stage=this.$refs.stage;
      const sw=(stage&&stage.clientWidth)?(stage.clientWidth-48):780;
      return this.viewMode==='grid8' ? Math.max(40,(sw-96)/8) : Math.max(40,(sw-48)/4);
    },
    /* V1.0.0.77：网格统一行高（A4 高宽比×格宽+gap）——绝对定位行容器高度基准；gridTotalH/行定位/窗口计算同源，估算恒定（超高页 cap 在行内居中） */
    _gridRowH(){
      const w=this.gridCellWCalc();
      return Math.round(w*1.414)+12;
    },
    /* V1.0.0.77：行容器绝对定位样式——top=行 index×行高，height=行高（cell 行内垂直居中，超高页不溢出） */
    gridRowStyle(row){
      const rh=this._gridRowH();
      return {top:(Math.max(0,Number(row&&row.row)||0)*rh)+'px', height:rh+'px'};
    },
    /* V1.0.0.52：grid 水印字号按格宽重算（与预览同公式 wmCalcFontSize）——grid 浮层不能复用 wmFsMap 绝对 px 字号（相对格宽失真，4/8页视图水印大小与页面比例不符） */
    gridWmFs(b, dispW, ptW, ptH) {
      const dispH = dispW * ((ptH || 842) / (ptW || 595));
      return this.wmCalcFontSize(b, dispW, dispH);
    },
    /* ---- 指定范围页（临时范围模式） / 按文字批量盖章 ---- */
    openRandDlg(){ this._openFloat('rand'); },
    openRenderDlg(){ this._openFloat('render'); },
    _openFloat(key){
      // v2.4.0.52：先算位置写入 CSS 变量再打开 → el-dialog 首帧渲染即目标位置（消除"先 left:0 再跳位"闪跳）
      const pos=this._floatPos(key);
      const rect=this._leftRect();
      const doc=document.documentElement;
      doc.style.setProperty('--float-x',(rect.left+pos.x)+'px');
      doc.style.setProperty('--float-y',(rect.top+pos.y)+'px');
      this[key+'Dlg']=true;
      const self=this;
      // teleport 到 body 是异步渲染，nextTick 时 DOM 可能未就绪，延后定位（inline 覆盖 CSS 变量，同值无跳变）
      this.$nextTick(()=>setTimeout(()=>{
        const dlg=self._floatDlgEl(key); if(!dlg) return;
        // v2.4.0.52：EP 2.7.8 UMD 的 overlay-class 未透传，全屏 overlay 仍拦截预览区 → JS 兜底直接置 pointer-events:none
        const ov=dlg.parentElement && dlg.parentElement.parentElement;
        if(ov) ov.style.pointerEvents='none';
        self._applyFloatStyle(dlg, rect.left+pos.x, rect.top+pos.y);
      },150));
    },
    _applyFloatStyle(dlg,l,t){
      const st=dlg.style;
      st.position='fixed'; st.margin='0';
      st.left=l+'px'; st.top=t+'px';
      st.width='360px'; st.boxShadow='0 8px 24px rgba(0,0,0,.15)';
      st.border='1px solid #C6CAD2'; st.borderRadius='10px'; st.overflow='hidden'; st.background='#FFFFFF';
    },
    _floatDlgEl(key){
      // el-dialog 是 teleport 组件，$el 非普通 DOM（无 querySelector），改用模板内 float-hd 标记定位 dialog 元素
      const hd=document.querySelector('.float-hd[data-key="'+key+'"]');
      return hd ? hd.closest('.el-dialog') : null;
    },
    _leftRect(){
      // 左栏固定在内容区最左（top 为自绘标题栏高约 40）；rect 异常时回退默认，防定位错乱
      const el=this.$refs.leftPanel;
      let left=0, top=40;
      if(el){ const r=el.getBoundingClientRect();
        if(r.left>-2 && r.left<12) left=Math.round(r.left);
        if(r.top>=0 && r.top<120) top=Math.round(r.top);
      }
      return {left, top};
    },
    _floatPos(key){
      // 相对左栏坐标：默认水平居中（略偏左），垂直在标题栏下方；记忆值优先，越界 clamp 到可视区
      let x=Math.max(4, Math.round((this.leftWidth-360)/2)), y=150;
      try{ const s=localStorage.getItem('pdfqfz_'+key+'DlgPosV2'); if(s){ const p=JSON.parse(s); if(typeof p.x==='number'&&typeof p.y==='number'){ x=p.x; y=p.y; } } }catch(e){}
      const maxX=Math.max(4, window.innerWidth-364), maxY=Math.max(4, window.innerHeight-64);
      x=Math.min(Math.max(4,x),maxX); y=Math.min(Math.max(4,y),maxY);
      return {x,y};
    },
    onDlgDragStart(e,key){
      if(e.button!==0) return;
      const dlg=this._floatDlgEl(key); if(!dlg) return;
      const rect=dlg.getBoundingClientRect();
      const self=this;
      // 若面板尚未完成定位（inline 位置缺失，延时期间被拖拽），先按当前视觉位置校准，避免起点错乱跳变
      if(dlg.style.left==='' || dlg.style.top===''){ this._applyFloatStyle(dlg, rect.left, rect.top); }
      this._drag={key, sx:e.clientX, sy:e.clientY, lx:rect.left, ty:rect.top, w:rect.width, h:rect.height};
      const mv=function(ev){ if(!self._drag) return; const d=self._drag;
        const nl=Math.max(4, Math.min(d.lx+ev.clientX-d.sx, window.innerWidth-d.w-4));
        const nt=Math.max(4, Math.min(d.ty+ev.clientY-d.sy, window.innerHeight-d.h-4));
        dlg.style.left=nl+'px'; dlg.style.top=nt+'px'; };
      const up=function(){ document.removeEventListener('mousemove',mv); document.removeEventListener('mouseup',up);
        if(self._drag){ try{ const lr=self._leftRect(); const cl=parseFloat(dlg.style.left)||rect.left, ct=parseFloat(dlg.style.top)||rect.top;
          localStorage.setItem('pdfqfz_'+self._drag.key+'DlgPosV2', JSON.stringify({x:Math.round(cl-lr.left), y:Math.round(ct-lr.top)})); }catch(err){} self._drag=null; } };
      document.addEventListener('mousemove',mv); document.addEventListener('mouseup',up);
      e.preventDefault();
    },
    openRangeDlg(){
      if(this.debugActive){ this.opHint='请先加载 PDF 文件后再进行范围盖章'; return; }
      if(!this.seals || !this.seals.length){ if(typeof this.noSealTip==='function'){ this.noSealTip('指定范围页盖章需要先加载印章'); } return; } /* V1.0.0.57：无章弹窗拦截 */
      /* V1.0.0.70：多页视图（4/8）下点范围页盖章先切回单页视图——范围盖章需在单页拖章定位，多页视图不可交互 */
      if(this.viewMode==='grid4'||this.viewMode==='grid8'){ this.setView('single'); }
      this.rangeFromN=1; this.rangeToN=this.pageCount||29;
      this.dlgRange=true;
      this.opHint='指定范围页盖章：输入起止页码，确定后进入范围模式';
    },
    prevPage(){ if(!this.pdfLoaded){ return; } const step=this.viewMode==='double'?2:1; let lo=1, hi=this.pageCount; if(this.rangeActive){ lo=this.rangeFromN; hi=this.rangeToN; } const p=Math.max(lo,Number(this.curPage)-step); this.renderPage(p); },
    nextPage(){ if(!this.pdfLoaded){ return; } const step=this.viewMode==='double'?2:1; let lo=1, hi=this.pageCount; if(this.rangeActive){ lo=this.rangeFromN; hi=this.rangeToN; } const p=Math.min(hi,Number(this.curPage)+step); this.renderPage(p); },
    /* ---- 阶段4：真实 PDF 渲染预览 ---- */
    pickPdf(){ if(!window.Bridge){ this.opHint='浏览器预览模式：无法打开文件选择，请在壳程序（EXE）中使用'; return; } const self=this; window.Bridge.invoke('PickPdf').then(function(path){ if(!path || path==='cancel' || !path.trim()){ return; } self.openDroppedPath(path); window.Bridge.invoke('ResetDirMode'); },function(e){ self.opHint='选择失败：'+e.message; }); },
    pickDir(){ if(!window.Bridge){ this.opHint='浏览器预览模式：无法打开文件夹选择，请在壳程序（EXE）中使用'; return; } const self=this; window.Bridge.invoke('PickSourceDir').then(function(json){ const r=JSON.parse(json); if(!r.ok){ if(!r.cancel){ self.opHint='打开文件夹失败：'+(r.error||''); self.addLog('打开文件夹失败：'+(r.error||''),true); } return; } if(r.path){ self.openDir(r.path); } else { self.applyDir(r); } },function(e){ self.opHint='选择文件夹失败：'+e.message; self.addLog('选择文件夹失败：'+e.message,true); }); },
    applyDir(r){ const self=this; this._diag('applyDir入口: 清空前 pageStamps='+this.pageStamps.length+' pageStampsRight='+this.pageStampsRight.length+' seamAll='+this.seamAll.length+' seamBase='+(this.seamBase?this.seamBase.length:0)+' rangeActive='+this.rangeActive+' curFile='+this.curFile); this._userFileSeq++; this._userFileLoaded=true; this.dirMode=true; this.curFileList=(r.files||[]); this.curIdx=0; this.pageCount=r.pageCount||0; this.curFile=r.current||''; this.pdfLoaded=true; this.debugActive=false; this.viewMode='single'; this.batchPreviewMode=false; this.batchPreviewed=[]; this.pageStamps=[]; this.pageStampsRight=[]; this.seamAll=[]; this.seamBase=''; this._seamLastKey=''; this.curPage='1'; this.curPageInput='1'; this.imgMode=false; this.imgLoaded=false; this.curImgUrl=''; this.wmBoxesImg=[]; this.wmSelId=null; this.wmEditingId=null; this.gridPages=[]; this.gridStart=1; this._gridScrollToPage=0; this._gridSeq++; this._gridSeqPrev++; this.gridLoadingPrev=false; this._prevItems=[]; this.gridLoading=false; /* V1.0.0.72：换文档同时终止向前补载链并复位 loading——旧文件补载结果不再插入新文件网格（上一文件含横向页时，其 ptW/ptH 残留会导致全竖版新文件格子按横向/错乱比例显示） */ // V2.4.0.347：加载 PDF 文件夹退出图片模式（对齐 openPdf L173 清理，修复先加载图片文件夹再拖入 PDF 文件夹时预览区仍显示图片） // V2.4.0.92：清空 seamAll 同步重置 _seamLastKey（换文档/重开强制重拉） v2.4.0.55：重新选文件夹清空预览态章/骑缝章切片/页码（后端 OpenDirectory 同步清章） // v2.4.0.54：重新选文件夹重置批量预览状态 // v2.4.0.36：新加载文件夹回单页视图
        if(!this.dirLocked){ let _src=(r.source||'').replace(/\\/g,'/'); if(!_src){ _src=(this.curFileList[0]||'').replace(/\\/g,'/'); _src=_src.substring(0,_src.lastIndexOf('/')); } if(_src){ this.saveDir=_src+'/已处理'; } } // V2.4.0.351：saveDir 锚定拖入目录(r.source) + 默认输出目录统一为已处理(与后端 V2.4.0.16 起一致,不再生成旧目录) this.segAuto=true; // v2.4.0.70：文件夹模式分割数默认「自动」（按每文件页数计算）
        this.renderPage(1); this.refreshPageWatermarks(); const _c0=(r.current||'').replace(/\\/g,'/').split('/').pop(); this._diag('applyDir: 已清空预览态; 新文件='+(r.current||'')+' 共'+(r.files||[]).length+'个, 调renderPage(1)'); this.opHint='已加载文件夹：共 '+(r.files||[]).length+' 个 PDF，当前文件《'+_c0+'》'; this.addLog('已加载文件夹：共 '+(r.files||[]).length+' 个 PDF，当前《'+_c0+'》','ok'); this._seamSync(); // v2.4.0.70：加载文件夹后刷新骑缝章预览（V60 只清不刷，直接加载文件夹无骑缝章） /* V1.0.0.78：文件名《》包裹 */
      },
    switchDirFile(cb){
      this.rangeActive=false; const f=this.curFileList[this.curIdx]; if(f){ this.openPdf(f,true,cb); } },
    /* v2.4.0.36：多文件加载（Win32 OLE 拖放多文件，对齐 WPF LoadSourceFiles(files)）——非目录模式：文件列表可切换（工具栏下拉），saveDir=第一个文件所在目录 */
    applyFiles(r){ const self=this; this._userFileSeq++; this._userFileLoaded=true; this.dirMode=false; this.curFileList=(r.files||[]); this.curIdx=0; this.pageCount=r.pageCount||0; this.curFile=r.current||''; this.pdfLoaded=true; this.debugActive=false; this.viewMode='single'; this.imgMode=false; this.imgLoaded=false; this.curImgUrl=''; this.wmBoxesImg=[]; this.wmSelId=null; this.wmEditingId=null; this.gridPages=[]; this.gridStart=1; this._gridScrollToPage=0; this._gridSeq++; this._gridSeqPrev++; this.gridLoadingPrev=false; this._prevItems=[]; this.gridLoading=false; /* V1.0.0.72：同 applyDir——换文档终止向前补载链并复位 loading */
        if(!this.dirLocked){ const pp=(r.current||'').replace(/\\/g,'/'); const d=pp.substring(0,pp.lastIndexOf('/')); if(d){ this.saveDir=d; } } this.renderPage(1); this.refreshPageWatermarks(); this.opHint='已加载 '+(r.files||[]).length+' 个 PDF，当前文件《'+(r.current||'').replace(/\\/g,'/').split('/').pop()+'》'; this.addLog('已加载 '+(r.files||[]).length+' 个 PDF','ok'); }, /* V1.0.0.79：文件名《》包裹 */
    openFiles(paths){ if(!window.Bridge){ this.opHint='浏览器预览模式：无法加载多文件，请在壳程序（EXE）中使用'; return; } const self=this; this.opHint='正在加载 '+(paths||[]).length+' 个文件…'; window.Bridge.invoke('OpenFiles',JSON.stringify(paths||[])).then(function(json){ let r={}; try{ r=JSON.parse(json); }catch(e){} if(r&&r.ok){ self.applyFiles(r); } else { self.opHint='打开文件失败：'+(r.error||''); self.addLog('打开文件失败：'+(r.error||''),true); } },function(e){ self.opHint='打开文件失败：'+e.message; self.addLog('打开文件失败：'+e.message,true); }); },
    /* v2.4.0.36：文件夹加载（选择文件夹/壳层拖入文件夹/file-dropped-dir 通道共用） */
    /* V2.4.0.98：文件夹含图片时按类型分流（纯图片→图片模式；混合→弹窗；纯 PDF→openDirPdf 原逻辑） */
    openDir(path){ if(!window.Bridge){ this.opHint='浏览器预览模式：无法加载文件夹，请在壳程序（EXE）中使用'; return; } const self=this; this._diag('openDir入口: path='+path); this.opHint='正在加载文件夹…'; window.Bridge.invoke('LoadImageFolder',path).then(function(j){ let rr={}; try{ rr=JSON.parse(j); }catch(e){} if(rr&&rr.ok&&rr.total>0){ if(rr.pdfs>0){ self.imgMixedInfo={pdfs:rr.pdfs, imgs:rr.total, imgFiles:null, pdfFiles:null, folderPath:path}; self.dlgImgMixed=true; return; } self.imgShow(0); self.imgRefreshQueue(); self.opHint='已加载 '+rr.total+' 张图片，自动进入图片模式'; self.addLog('加载文件夹：'+rr.total+' 张图片','ok'); return; } self.openDirPdf(path); }, function(){ self.openDirPdf(path); }); },
    openDirPdf(path){ if(!window.Bridge){ this.opHint='浏览器预览模式：无法加载文件夹，请在壳程序（EXE）中使用'; return; } const self=this; this._diag('openDirPdf入口: path='+path); this.opHint='正在加载文件夹…'; window.Bridge.invoke('OpenDirectory',path).then(function(json){ self._diag('openDirPdf返回: raw='+String(json).substring(0,300)); let r={}; try{ r=JSON.parse(json); }catch(e){} if(r&&r.ok){ self._diag('openDirPdf ok: count='+(r.files||[]).length+' current='+r.current); self.applyDir(r); } else { self._diag('openDirPdf ok=false: '+(r.error||'')); self.opHint='打开文件夹失败：'+(r.error||''); self.addLog('打开文件夹失败：'+(r.error||''),true); } },function(e){ self._diag('openDirPdf reject: '+e.message); self.opHint='打开文件夹失败：'+e.message; self.addLog('打开文件夹失败：'+e.message,true); }); },
    dispName(f){ return String(f||'').replace(/\\/g,'/').split('/').pop()||f||''; },
    /* 调试页（对齐 WPF ShowBlankDebugPage）：无用户文件时预览区显示内置 A4 调试页，可手动盖章调试渲染参数；范围/文字/生成在后端与前端双重拦截 */
    initDebugPage(){
      if(!window.Bridge || !window.Bridge.invoke){ return; }
      const self=this;
      window.Bridge.invoke('OpenDebugPage').then(function(json){
        let r={}; try{ r=JSON.parse(json); }catch(e){ return; }
        // V2.4.0.9（对齐用户方案"调试页=普通内部PDF，放新文件即替换"）：只要用户发起过打开文件
        // （openPdf/applyDir/onDropPdf 请求发出即 _userFileSeq++，无论响应是否已回），调试页响应一律忽略，
        // 彻底消除"调试页响应晚到覆盖用户文件"竞态——用户文件打开请求本身就是"替换旧文件链接"
        if(r.ok && self._userFileSeq===0){
          self.pageCount=r.pageCount||1; self.curFile='请加载PDF文件/图片'; self.pdfLoaded=true; self.debugActive=true;
          self.renderPage(1);
        }
      },function(){});
    },
    openPdf(path,isSwitch,cb){ if(!window.Bridge){ return; } const self=this; if(!isSwitch && /\.(jpe?g|png|bmp|gif|tiff?)$/i.test(String(path||''))){ this.imgLoadByPaths([path]); return; } this._diag('openPdf入口: path='+path+' isSwitch='+(isSwitch?'T':'F')); this._userFileSeq++; // v2.4.0.36：openPdf 全链路诊断+超时重试（invoke 挂起 15s 超时自动重试一次，不再静默）
        if(!isSwitch){ this.curFileList=[]; this.curIdx=0; this.dirMode=false; this.segAuto=true; this.batchPreviewMode=false; /* V368：单文件默认自动 */ this.batchPreviewed=[]; this.pageStamps=[]; this.pageStampsRight=[]; this.seamAll=[]; this.seamBase=''; this._seamLastKey=''; this.rangeActive=false; } // V2.4.0.92：清空 seamAll 同步重置 _seamLastKey（换文档/重开强制重拉） v2.4.0.78：非切换打开文件清空预览态章/骑缝章切片/范围模式（对齐 applyDir 四态清空，修复重新导入同一 PDF 预览不刷新；后端 OpenPdf 已同步清章） this._diag('openPdf inv调用: '+(isSwitch?'OpenPdfKeep':'OpenPdf'));
        const doOpen=function(){ return self._callBridge(isSwitch?'OpenPdfKeep':'OpenPdf',[path],15000).then(function(json){ // v2.4.0.36：目录模式内切换保留各文件章（对齐 WPF LoadPdf keepStampData:true）
          self._diag('openPdf invoke返回: raw='+String(json).substring(0,300));
          let r={}; try{ r=JSON.parse(json); }catch(e){ self.opHint='打开失败：返回数据异常'; self.addLog('打开失败：返回数据异常',true); self._diag('openPdf JSON解析失败: '+e.message+' raw='+String(json).substring(0,300)); throw e; }
          if(!r.ok){ self.opHint='打开失败：'+(r.error||''); self.addLog('打开失败：'+(r.error||''),true); self._diag('openPdf ok=false: '+(r.error||'')); return; }
          self._diag('openPdf ok=true pageCount='+r.pageCount);
          self._userFileLoaded=true; self.pageCount=r.pageCount; self.curFile=path.replace(/\\/g,'/'); self.pdfLoaded=true; self.debugActive=false; self.viewMode='single';
        self.imgMode=false; self.imgLoaded=false; self.curImgUrl=''; self.wmBoxesImg=[]; self.wmSelId=null; self.wmEditingId=null; self.gridPages=[]; self.gridStart=1; self._gridScrollToPage=0; self._gridSeq++; self._gridSeqPrev++; self.gridLoadingPrev=false; self._prevItems=[]; self.gridLoading=false; /* V1.0.0.72：同 applyDir——换文档（含目录内切换文件）终止向前补载链并复位 loading */ // V2.4.0.97：加载 PDF 退出图片模式 // v2.4.0.36：新打开文件回单页视图（对齐用户"新拖入/打开文件应单页视图"）
        // 骑缝章分割数随文档页数自动调整（对齐 WPF UpdateMaxSplitFromPdf：非目录模式且非"不加"时）
        if(!self.dirMode && String(self.seamType)!=='1' && r.pageCount>0){ self._segModified=false; self.segCount=r.pageCount; } if(!self.dirMode && !self.dirLocked){ const pp=path.replace(/\\/g,'/'); const dir=pp.substring(0,pp.lastIndexOf('/')); if(dir){ self.saveDir=dir; } } self.renderPage(1); self._seamSync(); if(typeof self.refreshPageWatermarks === "function"){ self.refreshPageWatermarks(); } else { console.error("[WM-FATAL] refreshPageWatermarks 未注册！watermark.js 模块解析失败，请检查语法。"); self.addLog("[WM-FATAL] 水印模块解析失败，请刷新重试", true); } // V2.4.0.92：打开/切换文件后显式刷新骑缝章（修复换内容相同页数相同文件、同文件重开时 watcher 不触发导致预览不刷新）；V2.4.0.96：加载/切换文件后刷新水印框（后端已清空旧文档框） const _fn=(path.replace(/\\/g,'/').split('/').pop()||''); self.opHint='已加载 《'+_fn+'》'; self.addLog('已加载文件：《'+_fn+'》（'+(r.pageCount||'?')+' 页）','ok'); /* V1.0.0.78：文件名《》包裹 */ self._diag('openPdf 成功渲染 page=1'); if(typeof cb==='function'){ try{ cb(); }catch(_e){ self._diag('openPdf cb异常: '+_e.message); } } // V2.4.0.54：批量预览模式下切换文件后自动按批量设置预览
        if(self.batchPreviewMode && self.dirMode){ self.doBatchPreview(); } }); };
        doOpen().catch(function(e){ self._diag('openPdf 超时/失败: '+String(e&&e.message||e)+' 重试一次'); self.opHint='打开超时，重试中…'; return doOpen(); }).catch(function(e2){ self.opHint='打开失败：'+String(e2&&e2.message||e2); self.addLog('打开失败：'+String(e2&&e2.message||e2),true); self._diag('openPdf 重试仍失败: '+String(e2&&e2.message||e2)); }); },
    renderPage(n){
      if(!this.pdfLoaded){ return; }
      const self=this; const idx=Math.max(1,Math.min(this.pageCount,Number(n)||1));
      const isDouble=this.viewMode==='double';
      // V2.4.0.6：渲染请求序号守卫——只应用最新一次 RenderPage 的响应，
      // 防止调试页初始化/翻页等慢响应晚到后覆盖当前页面（对齐 WPF 单线程渲染时序）
      const seq=++this._renderSeq;
      window.Bridge.invoke('RenderPage',idx-1).then(function(json){
        if(seq!==self._renderSeq){ return; }
        const r=JSON.parse(json);
        if(!r.ok){ self.opHint='渲染失败：'+(r.error||''); self.addLog('渲染失败：'+(r.error||''),true); return; }
        self.curPage=String(idx); self.curPageInput=String(idx);
        self.pageW=r.ptW||(r.w/2); self.pageH=r.ptH||(r.h/2); // v2.4.0.36：页面显示基准=页面真实 pt（对齐 WPF displayWidth 语义；渲染 PNG 是 2×pt，若用 PNG 宽当页面宽，印章/布局换算全错）
        // V2.4.0.12（关键）：渲染 URL 加唯一 ?v= 参数强制 img 重新加载——否则打开用户 PDF 后
        // curPageUrl 与启动调试页时完全相同（https://cache/page_0.png），Vue :src 值不变不触发更新，
        // 浏览器保留启动时加载的调试页图片 → 用户看到"预览页/第一页未变"（历版状态已对、显示层一直未修）
        self.curPageUrl=r.url+'?v='+(++self._imgSeq); self.pagePts=r.ptW||(r.w/2); self._diag('renderPage响应: idx='+idx+' url='+r.url+' v='+self._imgSeq);
        self.refreshPageStamps();
        if(isDouble){ const nxt=idx+1<=self.pageCount?idx+1:0; if(nxt>0){
          window.Bridge.invoke('RenderPage',nxt-1).then(function(j2){
            if(seq!==self._renderSeq){ return; }
            const r2=JSON.parse(j2);
            if(r2.ok){ self.pageRightUrl=r2.url+'?v='+(++self._imgSeq); self.pageRightW=r2.ptW||(r2.w/2); self.pageRightH=r2.ptH||(r2.h/2); }
            /* V1.0.0.49：右页基准就绪后重拉水印框——切双页时 viewMode 同步触发的 refreshPageWatermarks 早于右页异步渲染完成，
               pageRightUrl 门控导致右页框丢失（仅左页有框）；此处右页 url 已就绪，重拉后右页框按页填充 */
            if (typeof self.refreshPageWatermarks === 'function') { try { self.refreshPageWatermarks(); } catch (e) {} }
            /* V1.0.0.53：右页 URL 就绪后补拉右页章——此前 L355 refreshPageStamps 调用时 pageRightUrl 未就绪、双页分支被门控跳过，右页章空（首次切双页右页只有水印缺章根因）；补调后右页分支并行拉取（见 stampActions.refreshPageStamps） */
            if (typeof self.refreshPageStamps === 'function') { try { self.refreshPageStamps(); } catch (e) {} }
            self.fitPage();
            if (typeof self.wmRefitAllPages === 'function') { try { self.wmRefitAllPages(); } catch (e) {} } /* V1.0.0.19：右页基准就绪后整体重测 */
          });
        } else { self.pageRightUrl=''; self.fitPage(); } }
        else { self.fitPage(); }
        if (typeof self.wmRefitAllPages === 'function') { try { self.wmRefitAllPages(); } catch (e) {} } /* V1.0.0.19：PDF 翻页后按新页基准重测预览框（框随字） */
      },function(e){ self.opHint='渲染失败：'+e.message; self.addLog('渲染失败：'+e.message,true); });
    },
    // v2.4.0.78：单页/双页视图自适应窗口——100% = 适应舞台（拖大放大、拖小缩小）；百分比 = 相对适应基准的倍数
    // v2.4.0.78：缩放中心保持——主动缩放（_zoomAnchor=true，由 zoomIn/zoomOut/输入/Ctrl+滚轮 置位）时按比例换算可视中心，
    //   dragOfs 坐标系 = 页面内容相对视口左上角的偏移（transform: translate(-dragOfs)）；可视中心内容坐标 = (sw/2+dragOfs.x, sh/2+dragOfs.y)；
    //   缩放后 newDragOfs = 旧中心内容坐标×ratio − (sw/2, sh/2)，再钳制到 [0, 内容宽高−视口宽高]（页面小于视口时自然钳到 0 = 居中）。
    //   仅放大视图调整 dragOfs；单页/双页缩放后复位 0（页面居中）；非主动缩放（resize/切页/切视图）不触发。标志每次 fitPage 末尾重置。
    fitPage(){
      if(!this.pdfLoaded){ return; }
      if(this.viewMode==='grid4'||this.viewMode==='grid8'){ return; } /* V1.0.0.46 需求1：网格布局由 gridLoad 自管 */
      const stage=this.$refs.stage;
      const sw=(stage&&stage.clientWidth)?(stage.clientWidth-36):780;
      const stageH=(stage&&stage.clientHeight)?(stage.clientHeight-36):600;
      const oldW=this.imgW, oldX=this.dragOfs.x, oldY=this.dragOfs.y;
      const cx0=sw/2+oldX, cy0=stageH/2+oldY;
      if(this.viewMode==='double'){
        const gap=12; const maxW=(sw-gap)/2; const maxH=(stageH-52)*this.pageW/this.pageH; const w2=Math.min(maxW,maxH);
        this.imgW2=Math.round(Math.max(80,w2));
        if(this._zoomAnchor){ this.dragOfs={x:0,y:0}; this._zoomAnchor=false; }
        if (typeof this.wmRefitAllPages === 'function') { try { this.wmRefitAllPages(); } catch (e) {} } /* V1.0.0.20：双页布局更新后重测（缩放/resize/切页共用） */
        return;
      }
      const baseW=sw; const baseH=Math.max(80,(stageH-52)*this.pageW/this.pageH); const base=Math.min(baseW,baseH);
      const zoom=(parseFloat(this.zoomText)||100)/100;
      this.imgW=Math.round(zoom<=1.01?base:base*zoom);
      if(this._zoomAnchor){
        this._zoomAnchor=false;
        if(oldW>0 && this.viewMode==='zoom'){  /* v1.0.0.4：缩放值未变化（达到上/下限）时 ratio=1 自然保持原视口，不再清零跳回左上角 */
          const ratio=this.imgW/oldW;
          const iw=this.imgW, ih=iw*((this.pageH||this.pageW||1)/(this.pageW||1));
          const maxX=Math.max(0,iw-sw), maxY=Math.max(0,ih-stageH);
          const nx=cx0*ratio-sw/2, ny=cy0*ratio-stageH/2;
          this.dragOfs={x:Math.max(0,Math.min(maxX,nx)), y:Math.max(0,Math.min(maxY,ny))};
        } else {
          this.dragOfs={x:0,y:0};
        }
        this.syncStageScroll(); /* V362：缩放后 dragOfs 变化同步滚动条 */
      }
      if (typeof this.wmRefitAllPages === 'function') { try { this.wmRefitAllPages(); } catch (e) {} } /* V1.0.0.20：缩放/resize/切页后重测预览字号+框高（文字随页面缩放比例一致） */
    },
    goPage(){ const v=Number(this.curPageInput); if(!this.pdfLoaded || !v){ this.curPageInput=this.curPage; return; } this.renderPage(Math.max(1,Math.min(this.pageCount,v))); },
    /* V2.4.0.11：拖放改为 document 级原生监听（不依赖 #app 的 Vue 属性绑定——Vue 3 in-DOM 模板容器
       属性不生成事件监听器，导致 WebView2 外部拖放判定时页面不接受 dragover/drop → 系统回退用默认程序
       （Adobe）打开文件。document 级监听保证任意位置 dragover 都 preventDefault → WebView2 必然接受 →
       drop 触发 onDropPdf） */
    _bindDocDrag(){
      if(this._docDragBound) return; this._docDragBound=true;
      const self=this;
      this._docDragOver=function(e){ e.preventDefault(); self.dragOver=true; };
      this._docDragEnter=function(e){ self._dragDepth=(self._dragDepth||0)+1; self.dragOver=true; };
      this._docDragLeave=function(e){ self._dragDepth=Math.max(0,(self._dragDepth||0)-1); if(self._dragDepth===0){ self.dragOver=false; } };
      this._docDrop=function(e){ e.preventDefault(); self.onDropPdf(e); };
      document.addEventListener('dragover',this._docDragOver);
      document.addEventListener('dragenter',this._docDragEnter);
      document.addEventListener('dragleave',this._docDragLeave);
      document.addEventListener('drop',this._docDrop);
    },
    /* v2.4.0.36：拖放/加载诊断——V1.0.0.14 起经桥 DiagLog 统一写入 app_log.log，真实拖放路径排查用 */
    _diag(msg){ try{ if(window.Bridge && window.Bridge.invoke){ window.Bridge.invoke('DiagLog','[前端] '+msg); } }catch(e){} },
    /* v2.4.0.52：系统日志统一入口——[HH:mm:ss] 时间戳、错误红色（对齐 WPF AppendLog）、保留最近 200 条、logs watcher 自动滚底 */
    addLog(msg,isError,dir){
      if(!msg){ return; }
      const d=new Date();
      const ts=[d.getHours(),d.getMinutes(),d.getSeconds()].map(x=>String(x).padStart(2,'0')).join(':');
      const t=(isError==='ok'||isError==='warn'||isError==='imp')?isError:''; /* V369：新增 warn（跳过/部分失败=黄 #C77800） */ this.logs=this.logs.concat([{ts:ts,msg:String(msg),err:isError===true,t:t,dir:dir||''}]).slice(-200);
      /* V1.0.0.14：系统日志区逐行落盘到统一 app_log.log（经桥 fire-and-forget 不阻塞 UI；复现问题后反馈此文件即可排查） */
      try{ if(window.Bridge && window.Bridge.invoke){ const tag=(isError===true?'[错误]':(isError==='warn'?'[警告]':(isError==='ok'?'[成功]':(isError==='imp'?'[重要]':'[信息]')))); window.Bridge.invoke('LogLine','['+ts+'] '+tag+' '+String(msg)+(dir?'（'+dir+'）':'')); } }catch(_e){}
    },
    /* v2.4.0.36：桥调用超时兜底——invoke 挂起不再静默（openPdf/onDropPdf 防复发：15s 超时 reject，调用方自动重试一次） */
    _callBridge(name,args,timeoutMs){ const self=this; const p=(window.Bridge&&window.Bridge.invoke)?window.Bridge.invoke.apply(window.Bridge,[name].concat(args||[])):Promise.reject(new Error('桥不可用')); let timer=null; const to=new Promise(function(_,reject){ timer=setTimeout(function(){ reject(new Error('调用超时('+name+')')); },timeoutMs||15000); }); return Promise.race([p,to]).then(function(r){ clearTimeout(timer); return r; },function(e){ clearTimeout(timer); throw e; }); },
    onDropPdf(e){ e.preventDefault(); if(this._dropBusy){ this._diag('drop被忽略:_dropBusy=true'); return; } this._dropBusy=true; this.dragOver=false; this._dragDepth=0; const self=this; const item=e.dataTransfer&&e.dataTransfer.items&&e.dataTransfer.items[0]; this._diag('drop触发: items='+(e.dataTransfer&&e.dataTransfer.items?e.dataTransfer.items.length:0)+' files='+(e.dataTransfer&&e.dataTransfer.files?e.dataTransfer.files.length:0)); this._diag('drop判定: webkitGetAsEntry='+(item&&item.webkitGetAsEntry?'可用':'不可用')+(item&&item.webkitGetAsEntry?(' isDirectory='+item.webkitGetAsEntry().isDirectory):'')+' item.type='+(item&&item.type||'')); // V2.4.0.97：图片/文件夹分流——图片或混合内容自动进入图片模式；纯 PDF 继续原逻辑
      const dtAll=Array.from(e.dataTransfer&&e.dataTransfer.items||[]);
      const dirEntry=dtAll.find(function(it){ try{ var en=it.webkitGetAsEntry&&it.webkitGetAsEntry(); return !!en&&en.isDirectory; }catch(err){ return false; } });
      if(dirEntry){ var fd0=e.dataTransfer&&e.dataTransfer.files&&e.dataTransfer.files[0];
        if(fd0&&fd0.path){ this._dropBusy=false; return this.imgDropFolderWithPath(fd0.path); }
        this._dropBusy=false; return this.imgHandleDataTransfer(e); }
      const fls=Array.from(e.dataTransfer&&e.dataTransfer.files||[]);
      const pfs=[], ifs=[];
      fls.forEach(function(f){ if(!f||!f.name)return; if(/\.pdf$/i.test(f.name))pfs.push(f); else if(/\.(jpe?g|png|bmp|gif|tiff?)$/i.test(f.name))ifs.push(f); });
      if(pfs.length===0&&ifs.length===0){ /* 无有效文件：走原逻辑 */ }
      else if(pfs.length>0&&ifs.length>0){ this._dropBusy=false; this.imgMixedInfo={pdfs:pfs.length,imgs:ifs.length,imgFiles:ifs,pdfFiles:pfs}; this.dlgImgMixed=true; return; }
      else if(ifs.length>0){ this._dropBusy=false; this.imgLoadFiles(ifs); return; }
      // 纯 PDF：继续原逻辑
      if(item&&item.webkitGetAsEntry){ try{ if(item.webkitGetAsEntry().isDirectory){ // v2.4.0.36：目录项若带 WebView2 真实路径（f.path）直接走文件夹加载；否则提示走「选择文件夹」
          const fd=e.dataTransfer&&e.dataTransfer.files&&e.dataTransfer.files[0]; const dp=(fd&&fd.path)||''; this._diag('drop=目录 isDirectory, f.path='+(dp||'(空)')); if(dp && /\\|[\/]/.test(dp)){ self._dropBusy=false; self.openDir(dp); return; }
          this._dropBusy=false; this.opHint='拖入文件夹请点击「选择文件夹」按钮选择，或拖到窗口标题栏/边框区域（浏览器内容区无法直接获取文件夹路径）'; return; } }catch(err){} } const f=e.dataTransfer&&e.dataTransfer.files&&e.dataTransfer.files[0]; if(!f){ this._diag('drop失败: files[0]=null'); this._dropBusy=false; return; } this._diag('drop文件: name='+f.name+' path='+(f.path||'(无path)')+' size='+f.size); if(!/.pdf$/i.test(f.name)){ this._diag('drop拒绝: 非PDF'); this._dropBusy=false; this.opHint='仅支持 PDF/图片文件'; return; } /* V1.0.0.83：纯PDF分支拒绝非PDF时文案补「图片」（图片拖入已在上方分流） */ if(f.size>30*1024*1024){ this._diag('drop拒绝: 超30MB'); this._dropBusy=false; this.opHint='文件较大（超过30MB），请用「选择」按钮打开'; return; } this._userFileSeq++; this.opHint='正在读取 《'+f.name+'》…'; /* V1.0.0.79：文件名《》包裹 */
        const doLoad=function(){ return f.arrayBuffer().then(function(buf){ const bytes=new Uint8Array(buf); let bin=''; for(let i=0;i<bytes.length;i+=0x8000){ bin+=String.fromCharCode.apply(null,bytes.subarray(i,i+0x8000)); } const b64=btoa(bin); self._diag('arrayBuffer完成 size='+bytes.length+' 调OpenPdfFromBytes'); return self._callBridge('OpenPdfFromBytes',[b64,f.name],15000); }).then(function(json){ const r=JSON.parse(json); self._diag('OpenPdfFromBytes返回 ok='+r.ok+' pageCount='+r.pageCount+(r.error?(' err='+r.error):'')); if(!r.ok){ self._dropBusy=false; self.opHint='打开失败：'+(r.error||''); self.addLog('打开失败：'+(r.error||''),true); return; } self._userFileLoaded=true; self.pageCount=r.pageCount; self.curFile=(f.path||f.name||'').replace(/\\/g,'/'); self.pdfLoaded=true; self.debugActive=false; self.viewMode='single';
        self.imgMode=false; self.imgLoaded=false; self.curImgUrl=''; self.wmBoxesImg=[]; self.wmSelId=null; self.wmEditingId=null; self.gridPages=[]; self.gridStart=1; self._gridScrollToPage=0; self._gridSeq++; self._gridSeqPrev++; self.gridLoadingPrev=false; self._prevItems=[]; self.gridLoading=false; /* V1.0.0.72：同 applyDir——拖入新文档终止向前补载链并复位 loading */ // V2.4.0.97：加载 PDF 退出图片模式 // v2.4.0.36：拖入新文件回单页视图
        self.segAuto=true; self._segModified=false; self.segCount=r.pageCount||1; // V368：拖入单文件默认分割数自动（切手动初始值=页数）
        // V2.4.0.15：拖入文件后自动填充保存目录为源文件所在目录（f.path 为 WebView2 提供的真实路径；未锁定输出目录时）
        if(!self.dirLocked){ const fp=f.path||''; if(fp){ const pp=fp.replace(/\\/g,'/'); const dir=pp.substring(0,pp.lastIndexOf('/')); if(dir){ self.saveDir=dir; } } }
        self.opHint='已加载 《'+f.name+'》'; self.addLog('已加载文件：《'+f.name+'》（'+(r.pageCount||'?')+' 页）','ok'); /* V1.0.0.79：文件名《》包裹 */ self._diag('加载成功 curFile='+self.curFile+' saveDir='+self.saveDir); self.renderPage(1); if(typeof self.refreshPageWatermarks === "function"){ self.refreshPageWatermarks(); } else { console.error("[WM-FATAL] refreshPageWatermarks 未注册！watermark.js 模块解析失败，请检查语法。"); self.addLog("[WM-FATAL] 水印模块解析失败，请刷新重试", true); } self._dropBusy=false; }); };
        doLoad().catch(function(err){ self._diag('drop超时/失败: '+String(err&&err.message||err)+' 重试一次'); self.opHint='打开超时，重试中…'; return doLoad(); }).catch(function(err){ self._diag('drop重试仍失败: '+String(err&&err.message||err)); self._dropBusy=false; self.opHint='拖入失败：'+err.message; self.addLog('拖入失败：'+err.message,true); }); },
    onStageWheel(e){
      // V346：图片/PDF 共用同一套滚轮逻辑——Ctrl+滚轮缩放、非放大滚轮翻页/翻图、放大视图滚动阅读（滚到底/顶翻下一/上一张）
      // v1.0.0.5：双页视图固定100%，Ctrl+滚轮不缩放（走下方翻页）；V1.0.0.46 需求1：4/8 页网格仅浏览——滚轮原生滚动 stage，不缩放不翻页
      if(this.viewMode==='grid4'||this.viewMode==='grid8'){ return; }
      if(e.ctrlKey && this.viewMode!=='double'){ e.preventDefault(); const dz=e.deltaY<0?25:-25; this._zoomAnchor=true; this.zoomText=this.clampZoomText(Number(parseFloat(this.zoomText)||100)+dz)+'%'; if(this.imgMode){ this._fitImg(); } else { this.fitPage(); } return; }
      if(!this.pdfLoaded && !this.imgLoaded){ return; }
      e.preventDefault();
      const enlarged = this.viewMode==='zoom' || (Number(parseFloat(this.zoomText))>100);
      // 单页/双页视图：滚轮直接翻页/翻图（对齐 WPF：累积翻页）
      if(!enlarged){ if(e.deltaY>0){ this.barNext(); } else { this.barPrev(); } return; }
      // 放大视图连续阅读（对齐 WPF OnPreviewMouseWheel）：先滚动页面，滚到底再翻下一页（新页顶部在顶部）、
      // 滚到顶翻上一页（回到上一页底部）；页面不超高（maxY=0）时滚轮直接翻页
      const iw=this.imgMode?(this.imgDispW()||0):(this.imgW||0);
      const ih=this.imgMode?(this.imgDispH()||0):(iw*((this.pageH||this.pageW||1)/(this.pageW||1)));
      const sh=(this.$refs.stage&&this.$refs.stage.clientHeight)?(this.$refs.stage.clientHeight-52):0;
      const maxY=Math.max(0,ih-sh);
      const edge=2;
      const atBottom=this.dragOfs.y>=maxY-edge;
      const atTop=this.dragOfs.y<=edge;
      const delta=Math.abs(e.deltaY)||40;
      const atLast=this.imgMode?(this.curImgIdx>=this.imgQueue.length-1):(Number(this.curPage)>=this.pageCount);
      const atFirst=this.imgMode?(this.curImgIdx<=0):(Number(this.curPage)<=1);
      if(e.deltaY>0){
        if(atBottom && !atLast){
          this.barNext(); this.dragOfs={x:this.dragOfs.x, y:0};
        } else {
          this.dragOfs={x:this.dragOfs.x, y:Math.min(maxY, this.dragOfs.y+delta)};
        }
      } else {
        if(atTop && !atFirst){
          this.barPrev(); this.dragOfs={x:this.dragOfs.x, y:maxY};
        } else {
          this.dragOfs={x:this.dragOfs.x, y:Math.max(0, this.dragOfs.y-delta)};
        }
      }
      this.syncStageScroll(); /* V1.0.0.39: wheel sync scrollbars */
    },
    applyZoom(){ this._zoomAnchor=true; this.zoomText=this.clampZoomText(parseFloat(this.zoomText)||100)+'%'; this.fitPage(); },
    zoomIn(){ this._zoomAnchor=true; this.zoomText=this.clampZoomText(Number(parseFloat(this.zoomText)||100)+25)+'%'; this.fitPage(); },
    zoomOut(){ this._zoomAnchor=true; this.zoomText=this.clampZoomText(Number(parseFloat(this.zoomText)||100)-25)+'%'; this.fitPage(); },
    clampZoom(){ this._zoomAnchor=true; this.zoomText=this.clampZoomText(parseFloat(this.zoomText)||100)+'%'; this.fitPage(); },
    clampZoomText(v){ return Math.max(100,Math.min(300,Math.round(v))); },
    toggleFloatMode(){
      this.floatAuto=!this.floatAuto;
      if(this.floatAuto){ this.scheduleHideFloat(); } else { this.floatHidden=false; if(this.floatTimer){clearTimeout(this.floatTimer);this.floatTimer=null;} }
      this.opHint=this.floatAuto?'预览工具已设为自动收起：鼠标移走几秒收起，移到顶部把手展开':'预览工具已设为常驻显示';
    },
    scheduleHideFloat(){
      if(this.floatTimer) clearTimeout(this.floatTimer);
      if(!this.floatAuto || this.isFullscreen) return;
      this.floatTimer=setTimeout(()=>{ this.floatHidden=true; },2000);
    },
    cancelHideFloat(){
      if(this.floatTimer){ clearTimeout(this.floatTimer); this.floatTimer=null; }
      this.floatHidden=false;
    },
    showFloat(){ this.cancelHideFloat(); },
    /* ---- 全屏模式 ---- */
    toggleFullscreen(){
      this.isFullscreen=!this.isFullscreen;
      if(!this.isFullscreen && !this.floatAuto){ this.floatHidden=false; }
      this.opHint=this.isFullscreen?'全屏预览模式：鼠标移到顶部可展开预览工具，右上角「恢复」退出全屏':'已退出全屏预览';
      // V2.4.0.13：全屏切换改变预览区尺寸但窗口尺寸不变（resize 监听不触发）→ 主动重算适配
      // （修复双页视图点全屏后页面不自动放大、翻页后才放大的问题）
      // V1.0.0.62：网格视图下全屏切换（左栏隐藏→预览区变宽）主动刷 gridCellTick + 重载章/水印浮层，防"全屏后预览不刷新/内容不显示"
      const self=this;
      this.$nextTick(function(){
        if(self.viewMode==='grid4'||self.viewMode==='grid8'){
          self.gridCellTick=(self.gridCellTick||0)+1;
          if(typeof self._gridRefreshWms==='function'){ try{ self._gridRefreshWms(); }catch(e){} }
          if(typeof self._gridLoadOverlays==='function'){ try{ self._gridLoadOverlays(); }catch(e){} }
        } else if(self.imgLoaded){ self.imgMeasure(); }
        else if(self.pdfLoaded){ self.fitPage(); }
      });
    },
    /* ---- 预览画布 ---- */
    measureLeftMin(){
      const leftEl=this.$refs.leftPanel; if(!leftEl){ return; }
      let target=null; const cells=document.querySelectorAll('.left .row .disp-cell');
      for(let i=0;i<cells.length;i++){ const lb=cells[i].querySelector('label'); if(lb && lb.textContent==='印章尺寸'){ target=cells[i]; break; } }
      if(!target){ return; }
      const lr=leftEl.getBoundingClientRect(), cr=target.getBoundingClientRect();
      if(lr.width<100 || cr.width<10){ return; } // 折叠态/印章参数区收起时不测
      this.leftMinW=Math.max(694,Math.ceil(cr.right-lr.left+48)); // v2.4.0.358：控件区 580→570（导航条 124，694-124=570，用户实测修正）；动态测量为下限
      if(this.leftWidth<this.leftMinW){ this.leftWidth=this.leftMinW; } // v2.4.0.358：实测宽度跟随最小宽（此前仅改 leftMinW 而 leftWidth 停留 680，控件区未达 580）
    },
    startDrag(){
      // v2.4.0.358：用户实测决策——取消分隔条拖动，左栏固定宽度（694=导航条124+控件区570），右栏自动吃剩余；保留函数避免模板引用失效
      return;
    },
    /* ---- V2.4.0.352：左栏功能导航（一级=卡片，二级=卡内分区；选中显隐 + localStorage 持久化） ---- */
    toggleNavCard(key){
      if(this.navDisabled(key)){ return; } /* V357：图片模式印/批置灰不可启动 */
      if(this.imgMode && key==='watermark'){ return; } /* V1.0.0.13：图片模式水印强制打开，重复点击无动作——防打断正在编辑的水印框 */
      const s=this.navState[key]; if(!s) return;
      const g=this._navCardMeta(key);
      if(!s.subs){
        if(g && g.sw){
          if(key==='watermark'){
            /* V1.0.0.55：互斥双模式——导航「文」= 模式开关（与提示条按钮同语义）
               ① 未启用水印（watermarkEnabled=false）：启用 + 进入水印模式（卡体出现+高亮）
               ② 盖章模式（已启用、非编辑）：进入水印模式（卡体出现+高亮+网格自动切单页）
               ③ 水印模式（编辑中）：退出水印模式（卡体消失+取消高亮，水印仍显示仍输出） */
            if(!this.watermarkEnabled){ this.onWatermarkEnable(true); s.on=true; this._expandCard(key); }
            else if(!this.wmEditMode){
              this.wmEditMode=true; this.wmSect.main=true; s.on=true; this._expandCard(key);
              if(this.viewMode==='grid4'||this.viewMode==='grid8'){ this.setView('single'); }
            }
            else { this.wmEditMode=false; this.wmSect.main=false; this.wmSelId=null; this.wmEditingId=null; s.on=false; }
          } else { s.on=!this[g.sw]; this[g.sw]=s.on; }
        }
        else { s.on=!s.on; }
        if(s.on) this._expandCard(key);
      }
      else {
        const keys=Object.keys(s.subs);
        const vals=keys.map(k=>this.navSubOn(key,k));
        if(!vals.some(Boolean)){ keys.forEach(k=>this._setNavSubOn(key,k,true)); }
        else { const all=vals.every(Boolean); keys.forEach(k=>this._setNavSubOn(key,k,!all)); }
        if(!vals.every(Boolean)) this._expandCard(key); /* 部分选中/全不选 → 全选后展开卡体 */
      }
      if(s.on || (this.navState[key]&&this.navState[key].subs&&Object.keys(this.navState[key].subs).some(k=>this.navSubOn(key,k)))) this._flashNav(key); /* V362：选中后滚动到功能卡 + 闪烁 */
      this._afterNavChange();
    },
    toggleNavSub(cardKey,subKey){
      const s=this.navState[cardKey]; if(!s||!s.subs) return;
      const on=!this.navSubOn(cardKey,subKey);
      this._setNavSubOn(cardKey,subKey,on);
      if(on){ this._expandCard(cardKey, subKey); this._flashNav(cardKey, subKey); } /* V362：选中二级滚动到功能卡+闪烁；V363：只闪该二级分区；V363b：一级卡折叠时自动展开（只展开被点分区，其余分区不展开） */
      this._afterNavChange();
    },
    _afterNavChange(){
      this.saveNavState();
      this.$nextTick(()=>{ if(this.measureLeftMin) this.measureLeftMin(); window.dispatchEvent(new Event('resize')); });
    },
    /* V2.4.0.362：拖拽/缩放改为滚动条驱动——dragOfs 与 preview-stage 滚动条同步（页面不再 transform 平移） */
    syncStageScroll(){
      const st=this.$refs.stage; if(!st) return;
      st.scrollLeft=this.dragOfs.x; st.scrollTop=this.dragOfs.y;
    },
    /* V2.4.0.362：导航选中后滚动控件区到对应功能卡 + 背景闪烁（用户建议：功能被压下时自动露出 + 闪烁提示）
       V2.4.0.363：点二级按钮只闪烁该二级分区（navsect-{card}-{sub}），不再整卡闪烁（用户：范围/批量开着时不应被包进闪烁区） */
    _flashNav(key, subKey){
      if(!key) return;
      this.$nextTick(()=>{
        const el=subKey ? document.getElementById('navsect-'+key+'-'+subKey) : document.getElementById('navcard-'+key);
        if(!el) return;
        /* V363b：闪烁区完整可见则不滚；下缘被控件区视口遮挡→滚到底部露出；上缘超出→滚到顶部
           V365：滚动时上下各留 8px 余量（闪烁区底部线条/顶部不贴视口边缘），用 scrollTo 精确控制 */
        const scr=this.$refs&&this.$refs.leftScroll;
        if(scr){
          const r=el.getBoundingClientRect(), sr=scr.getBoundingClientRect();
          const GAP=8;
          if(r.bottom>sr.bottom-GAP){ scr.scrollTo({top: scr.scrollTop+(r.bottom-(sr.bottom-GAP)), behavior:'smooth'}); }
          else if(r.top<sr.top+GAP){ scr.scrollTo({top: scr.scrollTop-((sr.top+GAP)-r.top), behavior:'smooth'}); }
        } else { el.scrollIntoView({block:'nearest', behavior:'smooth'}); }
        el.classList.remove('nav-flash');
        void el.offsetWidth; /* 重置动画 */
        el.classList.add('nav-flash');
        setTimeout(()=>{ el.classList.remove('nav-flash'); }, 1300);
      });
    },
    navCardOn(key){
      if(this.imgMode){ /* V357：图片模式只能操作文字水印——印/批卡隐藏，文卡强制打开 */
        if(key==='stamp'||key==='batch'){ return false; }
        if(key==='watermark'){ return true; }
      }
      const s=this.navState[key]; if(!s) return false;
      const g=this._navCardMeta(key);
      if(!s.subs){
        if(g && g.sw) return !!this[g.sw];
        return !!s.on;
      }
      return !!(s.on && Object.keys(s.subs).some(k=>this.navSubOn(key,k)));
    },
    navSectOn(cardKey,subKey){ return this.navSubOn(cardKey,subKey); },
    /* V355：导航选中态统一读取——有 sw 的项以开关值为准（批量在单文件模式用导航态，开关不可用）；无 sw 项用 navState.subs */
    navSubOn(cardKey,subKey){
      const g=this._navSubMeta(cardKey,subKey);
      if(g && g.sw){
        if(cardKey==='batch' && subKey==='folder'){ return this.dirMode ? !!this.batchForce : !!this.navState.batch.subs.folder; }
        return !!this[g.sw];
      }
      const s=this.navState[cardKey]; return !!(s && s.subs && s.subs[subKey]);
    },
    _navSubMeta(cardKey,subKey){
      const g=this.navGroups.find(x=>x.key===cardKey && x.subs);
      if(!g || !g.subs) return null;
      return g.subs.find(x=>x.key===subKey) || null;
    },
    _navCardMeta(key){
      return this.navGroups.find(x=>x.key===key) || null;
    },
    _setNavSubOn(cardKey,subKey,on){
      const g=this._navSubMeta(cardKey,subKey);
      if(g && g.sw){
        if(cardKey==='batch' && subKey==='folder'){ this.navState.batch.subs.folder=!!on; if(this.dirMode){ this.batchForce=!!on; } }
        else if(cardKey==='watermark'){ this.onWatermarkEnable(!!on); this.navState[cardKey].subs[subKey]=!!on; } /* V359：导航开关同步 C# SetWatermarkEnabled（否则 _watermarkEnabled 恒 false，输出无水印） */
        else { this[g.sw]=!!on; this.navState[cardKey].subs[subKey]=!!on; }
      } else { const s=this.navState[cardKey]; if(s && s.subs){ s.subs[subKey]=!!on; } }
    },
    _expandSect(cardKey,subKey){
      if(cardKey==='stamp'){ this.stampSect[subKey==='param'?'disp':subKey]=true; }
      else if(cardKey==='batch'){ this.batchSect[subKey]=true; }
      else if(cardKey==='output'){ this.outSect[subKey]=true; }
    },
    _expandCard(key, onlySub){
      const g=this._navCardMeta(key);
      this.cards[key]=true;
      if(g && g.subs){ g.subs.forEach(x=>{ if(!onlySub || x.key===onlySub) this._expandSect(key,x.key); }); }
    },
    navDisabled(key){
      /* V357：图片模式只操作文字水印——印章参数/批量盖章置灰不可启动（一级=置灰根） */
      return !!(this.imgMode && (key==='stamp'||key==='batch'));
    },
    navSubDisabled(cardKey,subKey){
      /* V362：二级继承一级置灰——一级 disabled → 该卡所有二级联动 disabled（树形继承，非独立开关）；子级独立条件后续扩展位 */
      if(this.navDisabled(cardKey)) return true;
      return false;
    },
    navPathBlue(key){
      /* V357：竖线蓝色高亮段高度——从一级下缘到最后一个选中二级的垂直中心；无选中=0（全灰） */
      const s=this.navState[key]; if(!s||!s.subs) return 0;
      const keys=Object.keys(s.subs);
      let last=-1;
      for(let i=0;i<keys.length;i++){ if(this.navSubOn(key,keys[i])) last=i; }
      if(last<0) return 0;
      return last*36+18; /* 每项占高 26+10 间距，垂直中心=18+36i */
    },
    navSbsStyle(key){
      /* V357：绑定竖线蓝段高度（CSS 变量 --nav-blue-h） */
      return {'--nav-blue-h': (this.navPathBlue(key)||0)+'px'};
    },
    navInnerStyle(){
      /* V358：左栏 grid 行1 上限=导航区内容高（--nav-h）——窗口升高优先让行1 长高（导航卡完整、卡内滚动消失），之后行2 下方通栏才长；窗口矮时行1 压缩、导航卡内滚动 */
      return this.navH ? {'--nav-h': this.navH+'px'} : {};
    },
    navL1Class(key){
      if(this.imgMode && key==='watermark'){ return 'on'; } /* V362：图片模式文卡强制启用——navCardOn 已强制 true，一级按钮同步高亮（此前显示 off/置灰但控件出现，矛盾） */
      const s=this.navState[key]; if(!s) return 'off';
      const g=this._navCardMeta(key);
      if(!s.subs){
        if(g && g.sw){
          if(key==='watermark'){ return (this.watermarkEnabled && this.wmEditMode)?'on':'off'; } /* V1.0.0.54：文卡高亮=水印已启用且正在编辑（退出编辑即不亮）；图片模式特判在前 */
          return this[g.sw]?'on':'off';
        }
        return s.on?'on':'off';
      }
      const keys=Object.keys(s.subs);
      const vals=keys.map(k=>this.navSubOn(key,k));
      if(!vals.some(Boolean)) return 'off';
      return vals.every(Boolean)?'on':'part';
    },
    loadNavState(){
      try{
        const raw=localStorage.getItem('pdfqfz_nav_v2');
        if(!raw) return;
        const st=JSON.parse(raw);
        if(!st || typeof st!=='object') return;
        for(const k in this.navState){
          if(!st[k]) continue;
          const g=this._navCardMeta(k);
          if(!this.navState[k].subs){
            if(g && g.sw) continue; /* V355：有开关的整卡（文字水印）以开关值为准，忽略存储 */
            if(typeof st[k].on==='boolean') this.navState[k].on=st[k].on;
          }
          else if(st[k].subs){ for(const sk in this.navState[k].subs){
            const sg=this._navSubMeta(k,sk);
            if(sg && sg.sw) continue; /* V355：有开关的二级（去白/文字/批量）以开关值为准，忽略存储 */
            if(typeof st[k].subs[sk]==='boolean') this.navState[k].subs[sk]=st[k].subs[sk];
          } }
        }
      }catch(e){}
    },
    saveNavState(){
      try{ localStorage.setItem('pdfqfz_nav_v2', JSON.stringify(this.navState)); }catch(e){}
    },
};