/* PDFQFZ Web UI 前端模块：扫描件优化（三功能集成·阶段2）
 * 交互对齐「盖章渲染」：标题栏开关启用 + 参数调整实时预览；
 * 确认效果后由主界面「生成结果文件」统一输出（输出时后端套扫描优化滤镜）。
 * 参数：纠偏/裁边/去底灰/对比度（均 0-100，默认全 0）+ 去灰阈值(0-255，默认200)
 *      + 文字加强三方法（A 局部对比度 / B 二值化 / C 背景减法，各含开关+强度+辅助参数，多选叠加）
 *      + 4 档预设（参数1~4）。
 */
window.PdfqModules = window.PdfqModules || {};
window.PdfqModules.scanenhance = (function(){
  /* 内置 4 档预设（扫描件优化；纠偏手动双向、预设默认 0；文字加强三方法默认全关，由用户手动选） */
  var PRESETS = {
    1: { deskew:0, crop:20, detone:20, contrast:10, detoneTh:255, enhEn:false, enhSmooth:50, enhSens:50, enhDeep:100 },
    2: { deskew:0, crop:40, detone:40, contrast:20, detoneTh:255, enhEn:false, enhSmooth:50, enhSens:60, enhDeep:100 },
    3: { deskew:0, crop:60, detone:60, contrast:30, detoneTh:255, enhEn:false, enhSmooth:50, enhSens:70, enhDeep:100 },
    4: { deskew:0, crop:80, detone:80, contrast:50, detoneTh:255, enhEn:false, enhSmooth:50, enhSens:80, enhDeep:100 }
  };
  var ZERO = { deskew:0, crop:0, detone:0, contrast:0, detoneTh:255, enhEn:false, enhSmooth:20, enhSens:100, enhDeep:100 };
  function paramsOf(self){ return {
    deskew: Number(self.scanenhanceParams.deskew) || 0,
    crop: Number(self.scanenhanceParams.crop) || 0,
    detone: Number(self.scanenhanceParams.detone) || 0,
    contrast: Number(self.scanenhanceParams.contrast) || 0,
    detoneTh: Number(self.scanenhanceParams.detoneTh) || 200,
    enhEn: !!self.scanenhanceParams.enhEn,
    enhSmooth: Number(self.scanenhanceParams.enhSmooth) || 50,
    enhSens: Number(self.scanenhanceParams.enhSens) || 50,
    enhDeep: Number(self.scanenhanceParams.enhDeep) || 50
  }; }

  return {
    /* 面板打开（复用 uiLayout._openFloat 浮动定位，data-key=scanenhance） */
    openScanEnhanceDlg(){ this._openFloat('scanenhance'); },

    /* 标题栏开关：开=应用扫描优化预览，关=恢复原预览（开关改单向 model-value，此处手动同步状态） */
    onScanEnhanceEnable(v){ this.scanenhanceEnabled = !!v; this.syncScanEnhanceParams(); },

    /* 同步参数到 C# 并刷新预览（renderPage 重渲染，C# RenderPage 在激活时应用滤镜） */
    syncScanEnhanceParams(){
      if(!window.Bridge || !window.Bridge.invoke){ return; }
      const self = this;
      if(!this.scanenhanceEnabled || !this.pdfLoaded){
        window.Bridge.invoke('ScanEnhanceSyncParams','').then(function(){ if(self.pdfLoaded){ self.renderPage(self.curPage||1); } });
        return;
      }
      window.Bridge.invoke('ScanEnhanceSyncParams', JSON.stringify(paramsOf(this))).then(function(r){
        if(String(r)==='ok'){ self.renderPage(self.curPage||1); }
      });
    },
    /* 调参重绘「节流+防抖」（对齐仿扫描 _fakescanSyncDebounce） */
    _scanEnhanceSyncDebounce(){
      const now = Date.now();
      if(!this._scanEnhSyncLast || now - this._scanEnhSyncLast >= 100){ this._scanEnhSyncLast = now; this.syncScanEnhanceParams(); }
      clearTimeout(this._scanEnhSyncTimer);
      this._scanEnhSyncTimer = setTimeout(()=>{ this._scanEnhSyncLast = Date.now(); this.syncScanEnhanceParams(); }, 150);
    },

    /* 预设 1~4：下拉保存/加载。首次加载（无本地保存）用内置默认档；改参数后由 watch 自动触发预览刷新 */
    scanenhancePresetCmd(i, cmd){
      const key = 'scanenhance_preset_' + i;
      if(cmd === 'save'){
        const self = this;
        ElementPlus.ElMessageBox.confirm('确定用当前参数覆盖「参数'+i+'」已保存的参数吗？','保存确认',{confirmButtonText:'确定覆盖',cancelButtonText:'取消',type:'warning'}).then(()=>{
          try{
            localStorage.setItem(key, JSON.stringify(self.scanenhanceParams));
            self.scanenhancePreset = i; self.scanenhancePresetExists[i] = true;
            self.opHint = '已保存扫描优化参数' + i; self.addLog('已保存扫描优化参数' + i, 'ok');
          }catch(e){}
        }).catch(()=>{});
      } else {
        try{
          const s = localStorage.getItem(key);
          const val = s ? JSON.parse(s) : Object.assign({}, PRESETS[i]);
          Object.assign(this.scanenhanceParams, val);
          this.scanenhancePreset = i;
          this.opHint = '已加载扫描优化参数' + i; this.addLog('已加载扫描优化参数' + i, 'ok');
        }catch(e){}
      }
    },
    /* 恢复默认：全部参数归 0（由用户手动调整） */
    scanenhancePresetReset(){
      this.scanenhanceParams = Object.assign({}, ZERO);
      this.scanenhancePreset = 0;
      this.opHint = '扫描优化参数已全部归零'; this.addLog('扫描优化参数已全部归零', 'ok');
    }
  };
})();
