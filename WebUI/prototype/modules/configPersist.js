/* PDFQFZ Web UI 前端模块：盖章参数/UI配置持久化/日期格式（V2.4.0.85 拆分）
 * index.html methods 中通过 ...window.PdfqModules.configPersist 注入，
 * this 指向组件实例（Vue3 options API spread 保留绑定），函数体零改动。
 */
window.PdfqModules = window.PdfqModules || {};
window.PdfqModules.configPersist = {
    applyUiConfig(cfg){
      this._shellLoading=true;
      if(cfg.outputQualityDpi!==undefined) this.dpi=String(cfg.outputQualityDpi);
      if(cfg.outputFormat!==undefined){ this.outFormatArr=String(cfg.outputFormat||'pdf').split(',').filter(function(x){ return x==='pdf'||x==='jpg'||x==='png'; }); if(!this.outFormatArr.length){ this.outFormatArr=['pdf']; } } // V1.0.0.47：输出格式多选（逗号分隔存储）
      if(cfg.outputNameMark!==undefined) this.nameMark=cfg.outputNameMark;
      if(cfg.outputNamePos!==undefined) this.namePos=cfg.outputNamePos===1?'before':'after';
      if(cfg.outputNameSeqType!==undefined) this.seqType=['num','upper','lower'][cfg.outputNameSeqType]||'num';
      if(cfg.outputNamePad!==undefined) this.seqPad=String(cfg.outputNamePad||1);
      if(cfg.outputNameTs!==undefined) this.useTs=!!cfg.outputNameTs;
      if(cfg.outputNameTsFormat!==undefined) this.tsFormat=this.fmtToFront(cfg.outputNameTsFormat);
      if(cfg.contextFilterEnabled!==undefined) this.keywordEnabled=!!cfg.contextFilterEnabled;
      if(cfg.contextKeywords!==undefined) this.keywords=cfg.contextKeywords||'盖章,公章';
      if(cfg.contextRange!==undefined) this.ctxRange=cfg.contextRange;
      if(cfg.contextMatch!==undefined) this.matchMode=cfg.contextMatch===1?'all':'any';
      if(cfg.contextExcludeSpaces!==undefined) this.ignoreSpace=!!cfg.contextExcludeSpaces;
      if(cfg.outputDir!==undefined) this.saveDir=cfg.outputDir;
      if(cfg.outputDirLocked!==undefined) this.dirLocked=cfg.outputDirLocked===1;
      if(cfg.badgeDebug!==undefined) this.badgeDebug=!!cfg.badgeDebug; // V2.4.0.397：等级徽章调试开关（config.ini badgeDebug=1）
      if(cfg.enableTheme!==undefined) this.enableTheme=!!cfg.enableTheme; // V2.4.0.400：主题色切换开关（config.ini enableTheme=1）
      if(cfg.themeColor!==undefined) this.themeColor=cfg.themeColor||'blue'; // V2.4.0.400：主题色板（blue/purple/green）
      if(cfg.enableCustomTitle!==undefined) this.enableCustomTitle=!!cfg.enableCustomTitle; // V2.4.0.407：12.9 档自定义标题开关
      if(cfg.version!==undefined) this.versionStr=cfg.version; // V1.0.0.14：完整版本号（使用说明/关于弹窗展示，反馈定位用）
      if(cfg.tierActive!==undefined && !cfg.tierActive && typeof this.addLog==='function') this.addLog('授权已绑定 2 台设备，收费功能已停用（当前为免费版效果）','warn'); // V2.4.0.407：满 2 台降级提示
      if(cfg.leftPanelWidth!==undefined && cfg.leftPanelWidth>0){ const mw=Math.min(760,window.innerWidth-10-600); const lo=Math.min(this.leftMinW,mw); this.leftWidth=Math.max(lo,Math.min(cfg.leftPanelWidth,mw)); } // v2.4.0.36：clamp[min(leftMinW,上限),min(760,内容宽-10-600)]
      if(cfg.foldDisp!==undefined) this.stampSect.disp=!!cfg.foldDisp;
      if(cfg.foldRidge!==undefined) this.stampSect.ridge=!!cfg.foldRidge;
      if(cfg.foldWhite!==undefined) this.stampSect.white=!!cfg.foldWhite;
      if(cfg.foldRange!==undefined) this.batchSect.range=!!cfg.foldRange;
      if(cfg.foldText!==undefined){ this.batchSect.text=this.textForce && !!cfg.foldText; } // V2.4.0.70：恢复折叠态与开关联动（开关关则强制收起，杜绝"开关关/区域展开"初始不一致）
      if(cfg.foldMode!==undefined) this.outSect.mode=!!cfg.foldMode;
      if(cfg.foldDpi!==undefined) this.outSect.dpi=!!cfg.foldDpi;
      if(cfg.foldName!==undefined) this.outSect.name=!!cfg.foldName;
      if(cfg.qfzType!==undefined) this.seamType=String(cfg.qfzType);
      if(cfg.maxfgs!==undefined && cfg.maxfgs>0) this.segCount=cfg.maxfgs;
      if(cfg.wzType!==undefined) this.sealPos=['下','上','左','右'][cfg.wzType]||'右';
      if(cfg.wz!==undefined) this.posVal=cfg.wz+'%';
      if (typeof this.badgeInit === 'function') this.badgeInit(); // V2.4.0.397：徽章初始化（幂等；badgeDebug 已就位）
      if (typeof this.themeInit === 'function') this.themeInit(); // V2.4.0.400：主题色初始化（幂等；enableTheme/themeColor 已就位）
      this._shellLoading=false;
    },
    applyStampParams(p){
      this._shellLoading=true;
      if(p.size!==undefined) this.sealSize=p.size;
      if(p.rotation!==undefined) this.rotation=p.rotation;
      if(p.opacity!==undefined) this.opacity=p.opacity;
      if(p.maxSplit!==undefined) this.segCount=p.maxSplit;
      if(p.removeWhite!==undefined) this.removeWhite=!!p.removeWhite;
      if(p.tolerance!==undefined) this.tolerance=p.tolerance;
      if(p.randomParams!==undefined) this.randEnabled=!!p.randomParams;
      if(p.randomRange!==undefined) this.rand.angle=p.randomRange;
      if(p.randomOffsetXMm!==undefined) this.rand.h=p.randomOffsetXMm;
      if(p.randomOffsetYMm!==undefined) this.rand.v=p.randomOffsetYMm;
      if(p.textureQuality!==undefined) this.renderEnabled=!!p.textureQuality;
      if(p.textureBrightness!==undefined) this.tex.brightness=p.textureBrightness;
      if(p.textureRadial!==undefined) this.tex.pressure=p.textureRadial;
      if(p.textureSpot!==undefined) this.tex.blob=p.textureSpot;
      if(p.textureBlob!==undefined) this.tex.blobSize=p.textureBlob;
      if(p.textureGradient!==undefined) this.tex.gradient=p.textureGradient;
      if(p.textureCast!==undefined) this.tex.hue=p.textureCast;
      if(p.textureWhite!==undefined) this.tex.white=p.textureWhite;
      if(p.texturePresetIndex!==undefined) this.preset=p.texturePresetIndex;
      this._shellLoading=false;
    },
    fmtToFront(f){ return {yyyyMMdd:'d8','yyyy-MM-dd':'dash','yyyyMMdd-HHmm':'d8-t','yyyy-MM-dd-HH-mm':'full'}[f]||'d8'; },
    fmtToBack(v){ return {d8:'yyyyMMdd',dash:'yyyy-MM-dd','d8-t':'yyyyMMdd-HHmm',full:'yyyy-MM-dd-HH-mm'}[v]||'yyyyMMdd'; },
    saveUiConfig(){
      if(this._shellLoading || !window.Bridge || !window.Bridge.invoke) return;
      const cfg={
        outputQualityDpi: parseInt(this.dpi)||150,
        outputFormat: (this.outFormatArr&&this.outFormatArr.length)?this.outFormatArr.join(','):'pdf', // V1.0.0.47：输出格式多选（逗号分隔）
        outputNameMark: this.nameMark,
        outputNamePos: this.namePos==='before'?1:0,
        outputNameSeqType: ['num','upper','lower'].indexOf(this.seqType),
        outputNamePad: parseInt(this.seqPad)||1,
        outputNameTs: !!this.useTs,
        outputNameTsFormat: this.fmtToBack(this.tsFormat),
        contextFilterEnabled: !!this.keywordEnabled,
        contextKeywords: this.keywords,
        contextRange: parseInt(this.ctxRange)||10,
        contextMatch: this.matchMode==='all'?1:0,
        contextExcludeSpaces: !!this.ignoreSpace,
        outputDir: this.saveDir,
        outputDirLocked: this.dirLocked?1:0,
        wzType: ['下','上','左','右'].indexOf(this.sealPos),
        wz: parseInt(this.posVal)||50,
        qfzType: parseInt(this.seamType)||0,
        maxfgs: parseInt(this.segCount)||20,
        leftPanelWidth: this.leftWidth,
        foldDisp: this.stampSect.disp?1:0, foldRidge: this.stampSect.ridge?1:0, foldWhite: this.stampSect.white?1:0,
        foldRange: this.batchSect.range?1:0, foldText: this.batchSect.text?1:0,
        foldMode: this.outSect.mode?1:0, foldDpi: this.outSect.dpi?1:0, foldName: this.outSect.name?1:0,
      };
      // V2.4.0.80：写盘防抖 200ms（折叠/参数连续变化合并为一次写盘）；pagehide/visibilitychange/关键切换点 flush
      this._uiConfigPending=cfg;
      if(this._uiConfigTimer){ clearTimeout(this._uiConfigTimer); }
      this._uiConfigTimer=setTimeout(()=>{ this._flushUiConfig(); },200);
    },
    _flushUiConfig(){
      if(this._uiConfigTimer){ clearTimeout(this._uiConfigTimer); this._uiConfigTimer=null; }
      if(!this._uiConfigPending){ return; }
      const cfg=this._uiConfigPending; this._uiConfigPending=null;
      if(this._shellLoading || !window.Bridge || !window.Bridge.invoke) return;
      window.Bridge.invoke('SetUiConfig', JSON.stringify(cfg)).catch(()=>{});
    },
    saveStampParams(){
      if(this._shellLoading || !window.Bridge || !window.Bridge.invoke) return;
      const p={
        size: parseInt(this.sealSize)||40,
        rotation: parseInt(this.rotation)||0,
        opacity: parseInt(this.opacity)||60,
        maxSplit: this._segModified ? (parseInt(this.segCount)||500) : -1,
        removeWhite: !!this.removeWhite,
        tolerance: parseInt(this.tolerance)||20,
        randomParams: !!this.randEnabled,
        randomRange: this.rand.angle,
        randomOffsetXMm: this.rand.h,
        randomOffsetYMm: this.rand.v,
        textureQuality: !!this.renderEnabled,
        textureBrightness: this.tex.brightness,
        textureRadial: this.tex.pressure,
        textureSpot: this.tex.blob,
        textureBlob: this.tex.blobSize,
        textureGradient: this.tex.gradient,
        textureCast: this.tex.hue,
        textureWhite: this.tex.white,
        texturePresetIndex: this.preset,
      };
      // V2.4.0.80：写盘防抖 200ms（滑块拖动/连续调参合并一次写盘，替代 V47 即时写盘；防丢参数靠 flush）
      this._stampParamsPending=p;
      if(this._stampParamsTimer){ clearTimeout(this._stampParamsTimer); }
      this._stampParamsTimer=setTimeout(()=>{ this._flushStampParams(); },200);
    },
    _flushStampParams(){
      if(this._stampParamsTimer){ clearTimeout(this._stampParamsTimer); this._stampParamsTimer=null; }
      if(!this._stampParamsPending){ return; }
      const p=this._stampParamsPending; this._stampParamsPending=null;
      if(this._shellLoading || !window.Bridge || !window.Bridge.invoke) return;
      window.Bridge.invoke('SetStampParams', this.curSeal, JSON.stringify(p)).catch(()=>{});
    },
};
