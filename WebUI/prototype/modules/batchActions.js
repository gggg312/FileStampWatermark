/* PDFQFZ Web UI 前端模块：批量预览/批量生成/输出目录（V2.4.0.85 拆分）
 * index.html methods 中通过 ...window.PdfqModules.batchActions 注入，
 * this 指向组件实例（Vue3 options API spread 保留绑定），函数体零改动。
 */
window.PdfqModules = window.PdfqModules || {};
window.PdfqModules.batchActions = {
    onBatchForce(v){
      if(v){ this.batchSect.folder=true; } else { this.batchSect.folder=false; }
      this.opHint=v?'启用批量文件盖章：点“生成结果文件”将按批量规则对文件夹内所有文件盖章（不再提示确认）':'已关闭启用批量文件盖章：未放置印章时点“生成结果文件”会提示确认';
      this.addLog(v?'启用批量文件盖章':'关闭启用批量文件盖章');
    },
    onFolderTitleClick(){
      if(!this.batchForce){ return; }
      this.toggleSect('batchSect','folder');
    },
    doBatchPreview(){
      if(!this.dirMode || !this.curFileList.length){ return; }
      const self=this;
      const f=this.curFileList[this.curIdx];
      if(!f){ return; }
      // v2.4.0.55：前端主防线——参数缺失时明确提示，不发后端请求（V2.4.0.56 修正：真实印章字段是 curSeal，原 currentStamp 不存在导致已选印章仍误报）
      if(!this.curSeal){ this.opHint='请先选择印章'; this.addLog('请先选择印章',true); return; }
      const key=String(f).replace(/\\/g,'/');
      if(this.batchPreviewed.indexOf(key)>=0){ this.opHint='该文件已按批量设置预览'; this._seamSync(); return; }
      const cur=(this.curFile||'').replace(/\\/g,'/');
      const run=function(){
        self.opHint='正在按批量设置预览…';
        window.Bridge.invoke('BatchPreview', f,
          parseInt(self.batchRange)||0, parseInt(self.batchStart)||1, parseInt(self.batchEnd)||1,
          parseInt(self.batchX)||50, parseInt(self.batchY)||50, true,
          (self.saveDir||'').trim()
        ).then(function(json){
          let r={}; try{ r=JSON.parse(json); }catch(e){ self.opHint='预览失败：返回数据异常'; self.addLog('预览失败：返回数据异常',true); return; }
          if(!r.ok){ self.opHint=r.error||'预览失败'; self.addLog((r.error||'预览失败'),true); return; }
          self.batchPreviewed.push(key);
          self.renderPage(1);
          self._seamSync();
          self.opHint='本文件共 '+r.pageCount+' 页，匹配 '+r.count+' 页，跳过 '+(r.skippedCount||0)+' 页';
          self.addLog('批量预览：共 '+r.pageCount+' 页，匹配 '+r.count+' 页，跳过 '+(r.skippedCount||0)+' 页');
        },function(e){ self.opHint='预览失败：'+e.message; self.addLog('预览失败：'+e.message,true); });
      };
      if(self.pdfLoaded && cur===key){ run(); }
      else { self.switchDirFile(run); }
    },
    generateFiles(force){
      // V2.4.0.405：青绿主题（收费版）下连点「生成结果文件」15 次 → 弹自定义标题彩蛋入口（不影响正常生成流程）
      if(this.enableTheme && this.enableCustomTitle && this.themeColor==='teal'){ // V2.4.0.407：仅 12.9 档（enableCustomTitle）触发自定义标题彩蛋
        this._tealClicks=(this._tealClicks||0)+1;
        if(this._tealClicks>=15){ this._tealClicks=0; this.dlgCustomTitle=true; }
      }
      // 生成前：如果有正在编辑的水印框，先从 DOM 读文字更新 b.text
      if (this.wmEditingId) {
          var ref = this.$refs["wmEdit-" + this.wmEditingId];
          var el = Array.isArray(ref) ? ref[ref.length - 1] : ref;
          if (el) {
              var editingBox = null;
              for (var arr of [this.wmBoxes, this.wmBoxesRight]) {
                  var f = (arr||[]).find(function(b){return b.id===this.wmEditingId}.bind(this));
                  if (f) { editingBox = f; break; }
              }
              if (editingBox) { editingBox.text = String(el.innerText).replace(/\n+$/, ""); }
          }
          this.wmEditingId = null;
      }
      this.rangeActive=false;
      // V392：点击生成不再收起任何控件区（撤销 V2.4.0.28 旧需求）
      if(!window.Bridge){ this.opHint='浏览器预览模式：请使用壳程序（EXE）'; return; }
      if(!this.pdfLoaded || this.debugActive){ this.opHint='请先加载 PDF 文件'; return; }
      const self=this;
      this.generating=true; // V2.4.0.60：点击即置 loading；generate-done / batch-done / needConfirm / 失败分支均重置
      const seqMap={num:0,upper:1,lower:2};
      const posMap={after:0,before:1};
      // 生成前：把所有水印框的完整参数传给 C#（ReplaceAllWatermarks）
      var allBoxes = [...(this.wmBoxes||[]), ...(this.wmBoxesRight||[])];
      var jsonArray = JSON.stringify(allBoxes.map(b => this.wmBoxToJson(b)));
      var page = this.wmApplyAllPages ? 0 : (parseInt(this.curPage) || 1);
      window.Bridge.invoke('WriteDebugLog', '[WM-GEN] wmApplyAllPages=' + this.wmApplyAllPages + ' curPage=' + this.curPage + ' page=' + page);
      window.Bridge.invoke('WriteDebugLog', '[WM-GEN] allBoxes=' + allBoxes.length + ' jsonArray=' + jsonArray.substring(0, 300));
      window.Bridge.invoke('ReplaceAllWatermarks', page, jsonArray).then(function() {
        // 替换成功后，再调用 GenerateFiles
        window.Bridge.invoke('GenerateFiles',
          self.saveDir||'', self.outputMode, parseInt(self.dpi)||150,
          self.nameMark, posMap[self.namePos]||0, seqMap[self.seqType]||0, parseInt(self.seqPad)||1,
          !!self.useTs, self.fmtToBack(self.tsFormat),
          (self.watermarkEnabled?1:(parseInt(self.seamType)||0)), ['下','上','左','右'].indexOf(self.sealPos), parseInt(self.posVal)||50, // V392：文字水印模式骑缝章强制「不加」（与“无法盖章”互斥语义一致；纯水印不再需要印章）
          (self.segAuto?0:Math.max(1,parseInt(self.segCount)||20)), !!force, // V367：手动 0 过渡值按 1 生成；V368：文件夹不锁（可手动）
          parseInt(self.batchRange)||0, parseInt(self.batchStart)||1, parseInt(self.batchEnd)||1,
          parseInt(self.batchX)||50, parseInt(self.batchY)||50, true, !!self.batchForce
        ).then(function(json){
          const r=JSON.parse(json);
        // V2.4.0.70：所见即所得，取消无章确认流程（后端不再返回 needConfirm；无章直接生成无章副本）
        if(r.ok){ self.opHint='正在生成文件，请稍候…'; }
        else { self.generating=false; self.opHint=r.error||'生成失败'; self.addLog(r.error||'生成失败', true); } // V2.4.0.388：生成失败原因进系统日志区（此前只显示操作提示，排查无据）
      },function(e){ self.generating=false; self.opHint='生成失败：'+e.message; });
      }); // 关闭 ReplaceAllWatermarks 的 .then
    },
    pickOutDir(){
      if(!window.Bridge){ this.opHint='浏览器预览模式：请使用壳程序（EXE）'; return; }
      const self=this;
      window.Bridge.invoke('PickOutputDir').then(function(p){
        if(p && String(p).indexOf('err:')===0){ self.opHint='选择失败：'+p; }
        else if(p && p!=='cancel' && p.trim()){ self.saveDir=p; }
      },function(e){ self.opHint='选择失败：'+e.message; });
    },
    /* ---- 悬浮工具栏（翻页/缩放/常驻切换） ---- */
    batchDelChoice(choice){
      const b=this.batchDel; if(!b){ return; }
      this.dlgBatchDel=false; this.batchDel=null;
      if(choice===1){
        const self=this;
        // v2.4.0.70：批量放置章（batchId<0）整批次删除走 RemovePreviewBatch（按批次号删所有文件该批章）；范围章（batchId>0）走 DeleteStampBatch
        const fn=b.batchId<0
          ? (function(){ return window.Bridge.invoke('RemovePreviewBatch',b.batchId).then(function(json){ const rr=JSON.parse(json); return rr.ok?('ok'+((rr.removed||0)>0?'':'（当前无批量章）')):(rr.error||'删除失败'); }); })
          : (function(){ return window.Bridge.invoke('DeleteStampBatch',b.batchId); });
        fn().then(function(r){ self.opHint=(String(r)==='ok'||String(r).indexOf('ok')===0)?'已删除整个批次':'删除失败：'+r; if(String(r).indexOf('ok')===0){ self.addLog('已删除整个批次','ok'); } else { self.addLog('删除失败：'+r,true); } self.refreshPageStamps(); },function(e){ self.opHint='删除失败：'+e.message; self.addLog('删除失败：'+e.message,true); });
      } else if(choice===2){
        const self=this;
        // v2.4.0.70：批量章仅删除当前页 → DeletePreviewBatchOnPage(batchId,page)；范围页章 → DeleteStampBatchOnPage
        const fn=b.batchId<0
          ? (function(){ return window.Bridge.invoke('DeletePreviewBatchOnPage',b.batchId,b.page).then(function(json){ const rr=JSON.parse(json); return rr.ok?('ok'+((rr.removed||0)>0?'':'（当前页无批量章）')):(rr.error||'删除失败'); }); })
          : (function(){ return window.Bridge.invoke('DeleteStampBatchOnPage',b.batchId,b.page); });
        fn().then(function(r){ self.opHint=(String(r)==='ok'||String(r).indexOf('ok')===0)?'已删除当前页批次印章':'删除失败：'+r; if(String(r).indexOf('ok')===0){ self.addLog('已删除当前页批次印章','ok'); } else { self.addLog('删除失败：'+r,true); } self.refreshPageStamps(); },function(e){ self.opHint='删除失败：'+e.message; self.addLog('删除失败：'+e.message,true); });
      } else if(choice===4){
        // V2.4.0.88：仅删除当前印章（按文字章多枚场景）——按 id 删单个
        const self=this;
        window.Bridge.invoke('DeleteStampById',b.id).then(function(r){ self.opHint=(r==='ok')?'已删除当前印章':'删除失败：'+r; if(r==='ok'){ self.addLog('已删除当前印章','ok'); } else { self.addLog('删除失败：'+r,true); } self.refreshPageStamps(); },function(e){ self.opHint='删除失败：'+e.message; self.addLog('删除失败：'+e.message,true); });
      }
    },
    /* ---- 阶段6：手动盖章叠加 ---- */
};
