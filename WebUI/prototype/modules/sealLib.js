/* PDFQFZ Web UI 前端模块：印章库管理/章样式/纹理预设（V2.4.0.84 拆分）
 * index.html methods 中通过 ...window.PdfqModules.sealLib 注入，
 * this 指向组件实例（Vue3 options API spread 保留绑定），函数体零改动。
 */
window.PdfqModules = window.PdfqModules || {};
window.PdfqModules.sealLib = {
    syncTex(){
      if(!window.Bridge || !this.pdfLoaded){ return; }
      const self=this;
      // v2.4.0.52：防请求堆积——上一次 SyncTextureParams 未返回时丢弃本次（不排队），后端渲染完成即解锁
      if(this._texSyncLock){ return; }
      this._texSyncLock=true;
      window.Bridge.invoke('SyncTextureParams',JSON.stringify({
        textureBrightness:this.tex.brightness, textureBlob:this.tex.blobSize,
        textureGradient:this.tex.gradient, textureWhite:this.tex.white,
        textureSpot:this.tex.blob, textureRadial:this.tex.pressure, textureCast:this.tex.hue
      })).then(function(r){ if(r==='ok'){ self.refreshPageStamps(); } self._texSyncLock=false; },function(){ self._texSyncLock=false; });
    },
    /* v2.4.0.52：调参重绘「节流+防抖」——saveStampParams 保持即时（拖动中途关闭软件不丢参数）；
       syncTex+refreshPageStamps：throttle 100ms 提供中间帧预览（~10fps 跟手）+ debounce 150ms 收尾精确刷新
       （throttle 的最后一次与 debounce 收尾合并为最终 trailing，保证停止后最终值一定刷新）；防堆积由 syncTex 内锁承担 */
    _texSyncDebounce(){
      const now=Date.now();
      if(!this._texSyncLast || now-this._texSyncLast>=100){
        this._texSyncLast=now;
        this.syncTex();
      }
      clearTimeout(this._texSyncTimer);
      this._texSyncTimer=setTimeout(()=>{
        this._texSyncLast=Date.now();
        this.syncTex();
      },150);
    },
    stampStyle(s,dispW){
      const pts=this.pagePts||595;
      const scale=dispW/pts;
      const w=s.sizeMm*72/25.4*scale;
      const h=w*(s.imgH/s.imgW);
      const ox=s.offsetX*72/25.4*scale;
      const oy=s.offsetY*72/25.4*scale;
      const dispH=dispW*((this.pageH||this.pageW||1)/(this.pageW||1));
      // v2.4.0.36（章位置根治）：完全对齐 WPF PositionPreviewOverlay（2498-2521 行）——
      // ① 基础章左/章上按 CenterRatio 区分（手动=章中心比例 s.x×dispW-w/2；按文字=左上区间 (dispW-w)×s.x）
      // ② 随机位移 mm→px 直接加在位置上（不再用 transform translate）
      // ③ 章小于页面时出界自动移回页内（对齐 WPF clamp）
      // ④ 去掉 translate(-50%,-50%)——上一版 left 已按"章左"算、transform 又左移半章 → 固定偏左上半个章（用户"都偏中心左上"根因）
      // ⑤ 旋转只在 randomRotation 时显示层叠加（非随机旋转 PNG 已含旋转，StampEngine 416-420；对齐 WPF 2493-2496 仅 RandomRotation RenderTransform）
      // V2.4.0.70：内容中心 + 旋转后 bbox 钳制（与输出端 StampAnchorClamp.ClampCenterByBBox 同一套公式）——
      // 中心比例/左上区间 → 随机位移 → 按 bbox 钳中心 → 章左/章上（CSS 旋转 origin center center 不改变布局位置）。
      // 无旋转时 bbox=原尺寸，与原逻辑等价（文字章 randomRotation=false 零变化）。
      let cx = s.centerRatio ? (s.x*dispW) : ((dispW-w)*s.x + w/2);
      let cy = s.centerRatio ? (s.y*dispH) : ((dispH-h)*s.y + h/2);
      cx += ox; cy += oy;
      const A=(s.randomRotation&&s.rotation)?s.rotation:0;
      const rad=A*Math.PI/180, cA=Math.cos(rad), sA=Math.sin(rad);
      const bw=w*Math.abs(cA)+h*Math.abs(sA);
      const bh=w*Math.abs(sA)+h*Math.abs(cA);
      if(bw<=dispW){ cx=Math.max(bw/2, Math.min(dispW-bw/2, cx)); }
      if(bh<=dispH){ cy=Math.max(bh/2, Math.min(dispH-bh/2, cy)); }
      const left=cx - w/2, top=cy - h/2;
      const tf=(s.randomRotation&&s.rotation)?('rotate('+s.rotation+'deg)'):'';
      return {left:left+'px',top:top+'px',width:w+'px',height:h+'px',
              transform:tf,'transform-origin':'center center'};
    },
    delSeal(name){
      if(!window.Bridge||!window.Bridge.invoke){
        const i=this.seals.indexOf(name);
        if(i>-1) this.seals.splice(i,1);
        if(this.curSeal===name) this.curSeal=this.seals[0]||'';
        this.opHint='已删除印章：'+name;
        this.addLog('已删除印章：'+name,'ok');
        return;
      }
      window.Bridge.invoke('DeleteStamp',name).then(r=>{
        if(r==='ok'){
          const i=this.seals.indexOf(name);
          if(i>-1) this.seals.splice(i,1);
          if(this.curSeal===name) this.curSeal=this.seals[0]||'';
          this.opHint='已删除印章：'+name;
          this.addLog('已删除印章：'+name,'ok');
        } else { this.opHint='删除失败：'+r; this.addLog('删除失败：'+r,true); } /* V2.4.0.396：修复删除成功时仍写红色"删除失败"日志（else 只包 opHint 导致 addLog 无条件执行） */
      });
    },
    /* 从 C# 印章库加载印章列表 */
    loadStamps(){
      if(!window.Bridge||!window.Bridge.invoke){ if(this.seals.length===0){ this.seals=['公章','法人章','合同专用章']; this.curSeal='公章'; } return; }
      const self=this;
      window.Bridge.invoke('GetStampList').then(r=>{
        try{
          const list=JSON.parse(r);
          if(Array.isArray(list)&&list.length){
            this.seals=list.map(x=>x.name);
            // V2.4.0.388：印章路径无效（文件缺失/config 编码损坏）时明确提示，不再静默
            const invalid=list.filter(x=>x.valid===false);
            if(invalid.length){ const nm=invalid.map(x=>x.name).join('、'); this.opHint='印章图片不存在或配置损坏：'+nm+'，请重新导入印章'; this.addLog('印章图片不存在或配置损坏：'+nm+'，请重新导入印章', true); }
            if(!list.some(x=>x.name===this.curSeal)) this.curSeal=list[0].name;
          } else {
            this.seals=[]; this.curSeal='';
          }
        }catch(e){ this.opHint='印章列表解析失败：'+e.message; }
      }).catch(e=>{ this.opHint='印章列表加载失败：'+e.message; });
    },
    /* 重命名印章 */
    renameSeal(name){
      if(!window.Bridge||!window.Bridge.invoke){ this.opHint='原型模式不支持重命名'; return; }
      this.renameVal=name;
      this.renameSrc=name;
      this.dlgRename=true;
    },
    /* V2.4.0.90：重命名弹窗确定（替代原 window.prompt 逻辑，弹窗 el-dialog 化） */
    confirmRename(){
      const nn=String(this.renameVal||'').trim();
      if(!nn){ this.opHint='名称不能为空'; return; }
      const name=this.renameSrc;
      if(!name){ this.dlgRename=false; return; }
      window.Bridge.invoke('RenameStamp',name,nn).then(r=>{
        if(r==='ok'){
          const i=this.seals.indexOf(name);
          if(i>-1) this.seals[i]=nn;
          if(this.curSeal===name) this.curSeal=nn;
          this.opHint='已重命名：'+name+' → '+nn;
          this.addLog('印章已重命名：'+name+' → '+nn,'ok');
          this.dlgRename=false;
        } else { this.opHint='重命名失败：'+r; this.addLog('重命名失败：'+r,true); }
      });
    },
    /* ---- 盖章渲染方案 1~4：下拉菜单保存/加载（对齐 WPF） ---- */
    presetCmd(i,cmd){
      if(!window.Bridge||!window.Bridge.invoke){ this.opHint='原型模式不支持方案存取'; return; }
      if(cmd==='save'){
        const v=[Number(this.tex.brightness)||0, Number(this.tex.blobSize)||0, Number(this.tex.gradient)||0, Number(this.tex.white)||0, Number(this.tex.blob)||0, Number(this.tex.pressure)||0, Number(this.tex.hue)||0];
        window.Bridge.invoke('SetTexPreset', i, JSON.stringify({values:v})).then(r=>{
          if(r==='ok'){ this.preset=i; this.presetExists[i]=true; this.opHint='已保存渲染参数'+i; this.addLog('已保存渲染参数'+i,'ok'); }
          else { this.opHint='保存失败：'+r; this.addLog('保存失败：'+r,true); }
        });
      } else {
        window.Bridge.invoke('GetTexPreset', i).then(r=>{
          try{
            const o=JSON.parse(r);
            if(o.exists){
              this.tex.brightness=o.values[0]; this.tex.blobSize=o.values[1]; this.tex.gradient=o.values[2];
              this.tex.white=o.values[3]; this.tex.blob=o.values[4]; this.tex.pressure=o.values[5]; this.tex.hue=o.values[6];
              if(this.tex.blob<=0){ this.tex.blobSize=0; } /* V45：对齐 WPF 内部斑点=0 联动置 0 */
              this.preset=i; this.opHint='已加载渲染参数'+i; this.addLog('已加载渲染参数'+i,'ok');
            } else this.opHint='参数'+i+'尚未保存';
          }catch(e){}
        });
      }
    },
    /* 打开渲染弹窗：拉取方案存在性 + 当前值匹配自动选中（对齐 WPF DetectTexturePresetFromCurrentValues） */
    refreshPresetState(){
      if(!window.Bridge||!window.Bridge.invoke) return;
      const cur=[Number(this.tex.brightness)||0, Number(this.tex.blobSize)||0, Number(this.tex.gradient)||0, Number(this.tex.white)||0, Number(this.tex.blob)||0, Number(this.tex.pressure)||0, Number(this.tex.hue)||0];
      let auto=0;
      for(let i=1;i<=4;i++){
        window.Bridge.invoke('GetTexPreset', i).then(r=>{
          try{
            const o=JSON.parse(r);
            this.presetExists[i]=!!o.exists;
            if(o.exists&&!auto){
              const v=o.values.map(Number);
              if(v.length===7&&v.every((x,k)=>x===cur[k])){ auto=i; this.preset=i; }
            }
          }catch(e){}
        });
      }
    },
    /* 恢复默认：7 参数全 0 + 回到自定义（对齐 WPF btnReset） */
    presetReset(){
      this.tex.brightness=0; this.tex.blobSize=0; this.tex.gradient=0;
      this.tex.white=0; this.tex.blob=0; this.tex.pressure=0; this.tex.hue=0;
      this.preset=0;
      this.opHint='渲染参数已恢复默认';
      this.addLog('渲染参数已恢复默认','ok');
    },
    /* ---- 选择印章文件（原型阶段 mock：文件选择后加入印章列表） ---- */
    pickSealFile(){
      if(!window.Bridge||!window.Bridge.invoke){
        const input=document.createElement('input');
        input.type='file'; input.accept='image/png,image/jpeg,.png,.jpg,.jpeg';
        input.onchange=()=>{
          const f=input.files&&input.files[0];
          if(!f) return;
          const name=f.name;
          if(!this.seals.includes(name)) this.seals.push(name);
          this.curSeal=name;
          this.opHint='已选择印章文件：'+name+'（原型 mock）';
          this.addLog('已选择印章文件：'+name,'ok');
        };
        input.click();
        return;
      }
      window.Bridge.invoke('PickStamps').then(r=>{
        try{
          const o=JSON.parse(r);
          if(o.ok&&o.names&&o.names.length){
            for(const nm of o.names){ if(!this.seals.includes(nm)) this.seals.push(nm); }
            this.curSeal=o.names[0];
            this.opHint='已导入印章：'+o.names.join('、')+(o.errors&&o.errors.length?('；失败：'+o.errors.join('、')):'');
            this.addLog('已导入印章：'+o.names.join('、')+(o.errors&&o.errors.length?('；失败：'+o.errors.join('、')):''),'ok');
          } else if(!o.cancel){
            this.opHint='导入失败：'+(o.error||'未导入任何印章');
            this.addLog('导入失败：'+(o.error||'未导入任何印章'),true);
          }
        }catch(e){ this.opHint='导入结果解析失败'; this.addLog('导入结果解析失败',true); }
      });
    },
    /* ---- 阶段3：与 C# 配置对接（壳内生效，纯浏览器降级保持演示数据） ---- */
    loadFromShell(){
      if(!window.Bridge || !window.Bridge.invoke) return;
      const self=this;
      window.Bridge.invoke('GetUiConfig').then(json=>{
        try{ self.applyUiConfig(JSON.parse(json)); }catch(e){}
      }).catch(()=>{});
      window.Bridge.invoke('GetStampParams', this.curSeal).then(json=>{
        try{ self.applyStampParams(JSON.parse(json)); }catch(e){}
      }).catch(()=>{});
    },
};
