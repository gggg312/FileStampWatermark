/* PDFQFZ Web UI 前端模块：PDF 仿扫描（三功能集成·阶段1）
 * 交互对齐「盖章渲染」：标题栏开关启用 + 参数调整实时预览（预览区显示仿扫描效果）；
 * 确认效果后由主界面「生成结果文件」统一输出（输出时后端套仿扫描滤镜）。
 * 三档预设（轻微/适中/明显）内置默认值，首次打开可直接加载；参数可保存覆盖（localStorage）。
 */
window.PdfqModules = window.PdfqModules || {};
window.PdfqModules.fakescan = (function(){
  /* 内置三档预设（扭曲 0-100，100 = 原 100 档两倍；整体倾斜 -100~100，0=不倾斜，正=顺时针） */
  var PRESETS = {
    1: { bgTone:15, noise:10, vignette:15, warp:16, blur:5,  tilt:5 },
    2: { bgTone:40, noise:30, vignette:40, warp:40, blur:12, tilt:10 },
    3: { bgTone:60, noise:50, vignette:60, warp:64, blur:20, tilt:20 }
  };
  function paramsOf(self){ return {
    noise: Number(self.fakescanParams.noise) || 0,
    blur: Number(self.fakescanParams.blur) || 0,
    warp: Number(self.fakescanParams.warp) || 0,
    vignette: Number(self.fakescanParams.vignette) || 0,
    bgTone: Number(self.fakescanParams.bgTone) || 0,
    tilt: Number(self.fakescanParams.tilt) || 0
  }; }

  return {
    /* 面板打开（复用 uiLayout._openFloat 浮动定位，data-key=fakescan） */
    openFakeScanDlg(){ this._openFloat('fakescan'); },

    /* 标题栏开关：开=应用仿扫描预览，关=恢复原预览（开关改单向 model-value，此处手动同步状态） */
    onFakescanEnable(v){ this.fakescanEnabled = !!v; this.syncFakescanParams(); },

    /* 同步参数到 C# 并刷新预览（renderPage 重渲染，C# RenderPage 在激活时应用滤镜） */
    syncFakescanParams(){
      if(!window.Bridge || !window.Bridge.invoke){ return; }
      const self = this;
      if(!this.fakescanEnabled || !this.pdfLoaded){
        window.Bridge.invoke('FakeScanSyncParams','').then(function(){ if(self.pdfLoaded){ self.renderPage(self.curPage||1); } });
        return;
      }
      window.Bridge.invoke('FakeScanSyncParams', JSON.stringify(paramsOf(this))).then(function(r){
        if(String(r)==='ok'){ self.renderPage(self.curPage||1); }
      });
    },
    /* 调参重绘「节流+防抖」（对齐盖章渲染 _texSyncDebounce） */
    _fakescanSyncDebounce(){
      const now = Date.now();
      if(!this._fakeSyncLast || now - this._fakeSyncLast >= 100){ this._fakeSyncLast = now; this.syncFakescanParams(); }
      clearTimeout(this._fakeSyncTimer);
      this._fakeSyncTimer = setTimeout(()=>{ this._fakeSyncLast = Date.now(); this.syncFakescanParams(); }, 150);
    },

    /* 预设 1~3：下拉保存/加载。首次加载（无本地保存）用内置默认档；改参数后由 watch 自动触发预览刷新 */
    fakescanPresetCmd(i, cmd){
      const key = 'fakescan_preset_' + i;
      if(cmd === 'save'){
        // 三功能集成·阶段1：保存覆盖前弹窗确认，防误操作覆盖（与盖章渲染 presetCmd 一致）
        const self = this;
        ElementPlus.ElMessageBox.confirm('确定用当前参数覆盖「'+['轻微','适中','明显'][i-1]+'」已保存的参数吗？','保存确认',{confirmButtonText:'确定覆盖',cancelButtonText:'取消',type:'warning'}).then(()=>{
          try{
            localStorage.setItem(key, JSON.stringify(self.fakescanParams));
            self.fakescanPreset = i; self.fakescanPresetExists[i] = true;
            self.opHint = '已保存仿扫描参数' + i; self.addLog('已保存仿扫描参数' + i, 'ok');
          }catch(e){}
        }).catch(()=>{});
      } else {
        try{
          const s = localStorage.getItem(key);
          const val = s ? JSON.parse(s) : Object.assign({}, PRESETS[i]);
          Object.assign(this.fakescanParams, val);
          this.fakescanPreset = i;
          this.opHint = '已加载仿扫描参数' + i; this.addLog('已加载仿扫描参数' + i, 'ok');
        }catch(e){}
      }
    },
    /* 恢复默认：内置「适中」档 */
    fakescanPresetReset(){
      this.fakescanParams = Object.assign({}, PRESETS[2]);
      this.fakescanPreset = 0;
      this.opHint = '仿扫描参数已恢复默认'; this.addLog('仿扫描参数已恢复默认', 'ok');
    }
  };
})();
