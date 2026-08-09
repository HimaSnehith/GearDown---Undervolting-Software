// Web UI State Controller
(function() {
  let state = {
    gpuMode: 0, // 0 = FixedFrequency, 1 = TemperatureLock
    cpu: 100,
    gpuFreq: 1800,
    targetTemp: 75,
    maxCapMhz: 2200,
    appGovEnabled: false,
    appProfiles: []
  };

  // DOM Elements
  const el = {
    tempVal: document.getElementById('tempVal'),
    tempGaugeArc: document.getElementById('tempGaugeArc'),
    statGovState: document.getElementById('statGovState'),
    statActiveClock: document.getElementById('statActiveClock'),
    gpuNameBadge: document.getElementById('gpuNameBadge'),
    statusBanner: document.getElementById('statusBanner'),
    liveDot: document.getElementById('liveDot'),

    cpuSlider: document.getElementById('cpuSlider'),
    cpuValBadge: document.getElementById('cpuValBadge'),

    btnFixedMode: document.getElementById('btnFixedMode'),
    btnTempMode: document.getElementById('btnTempMode'),
    fixedPanel: document.getElementById('fixedPanel'),
    tempPanel: document.getElementById('tempPanel'),

    freqSlider: document.getElementById('freqSlider'),
    freqValBadge: document.getElementById('freqValBadge'),

    targetTempSlider: document.getElementById('targetTempSlider'),
    targetTempValBadge: document.getElementById('targetTempValBadge'),

    maxCapSlider: document.getElementById('maxCapSlider'),
    maxCapValBadge: document.getElementById('maxCapValBadge'),

    appGovToggle: document.getElementById('appGovToggle'),
    activeAppText: document.getElementById('activeAppText'),
    appExeInput: document.getElementById('appExeInput'),
    btnAddRule: document.getElementById('btnAddRule'),
    rulesList: document.getElementById('rulesList'),

    btnApply: document.getElementById('btnApply'),
    btnReset: document.getElementById('btnReset')
  };

  // --- C# MESSAGE POSTING ---
  function sendToHost(action, data = {}) {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage({ action, ...data });
    }
  }

  // --- RECEIVE FROM C# HOST ---
  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener('message', event => {
      const msg = event.data;
      if (!msg) return;

      if (msg.type === 'telemetry') {
        updateTelemetry(msg);
      } else if (msg.type === 'config') {
        applyConfigToUI(msg);
      } else if (msg.type === 'status') {
        showStatus(msg.text);
      }
    });
  }

  // --- UI UPDATE HANDLERS ---
  function updateTelemetry(data) {
    if (data.temp !== undefined) {
      el.tempVal.textContent = data.temp;
      // Gauge ring calculation: circumference = 2 * PI * 68 = ~427
      const maxTemp = 95;
      const pct = Math.min(1, Math.max(0, data.temp / maxTemp));
      const offset = 427 - (427 * pct);
      el.tempGaugeArc.style.strokeDashoffset = offset;
    }

    if (data.gpuName) el.gpuNameBadge.textContent = data.gpuName;
    if (data.activeClock) el.statActiveClock.textContent = `${data.activeClock} MHz`;
    if (data.govState) el.statGovState.textContent = data.govState;
    if (data.activeApp) el.activeAppText.textContent = data.activeApp;
  }

  function updateSliderTrack(slider, color) {
    const min = parseFloat(slider.min) || 0;
    const max = parseFloat(slider.max) || 100;
    const val = parseFloat(slider.value) || 0;
    const pct = ((val - min) / (max - min)) * 100;
    slider.style.background = `linear-gradient(to right, ${color} 0%, ${color} ${pct}%, rgba(255, 255, 255, 0.08) ${pct}%, rgba(255, 255, 255, 0.08) 100%)`;
  }

  function updateAllSliderTracks() {
    updateSliderTrack(el.cpuSlider, '#FFB300');
    updateSliderTrack(el.freqSlider, '#00E676');
    updateSliderTrack(el.targetTempSlider, '#FF5252');
    updateSliderTrack(el.maxCapSlider, '#00E676');
  }

  function applyConfigToUI(cfg) {
    if (cfg.cpu !== undefined) {
      el.cpuSlider.value = cfg.cpu;
      el.cpuValBadge.textContent = `${cfg.cpu} %`;
      state.cpu = cfg.cpu;
    }
    if (cfg.gpuMode !== undefined) {
      setGpuModeUI(cfg.gpuMode);
    }
    if (cfg.gpuFreq !== undefined) {
      el.freqSlider.value = cfg.gpuFreq;
      el.freqValBadge.textContent = `${cfg.gpuFreq} MHz`;
      state.gpuFreq = cfg.gpuFreq;
    }
    if (cfg.targetTemp !== undefined) {
      el.targetTempSlider.value = cfg.targetTemp;
      el.targetTempValBadge.textContent = `${cfg.targetTemp} °C`;
      state.targetTemp = cfg.targetTemp;
    }
    if (cfg.maxCapMhz !== undefined) {
      el.maxCapSlider.value = cfg.maxCapMhz;
      el.maxCapValBadge.textContent = `${cfg.maxCapMhz} MHz`;
      state.maxCapMhz = cfg.maxCapMhz;
    }
    if (cfg.appGovEnabled !== undefined) {
      el.appGovToggle.checked = cfg.appGovEnabled;
      state.appGovEnabled = cfg.appGovEnabled;
    }
    if (cfg.appProfiles) {
      state.appProfiles = cfg.appProfiles;
      renderRulesList();
    }
    updateAllSliderTracks();
  }

  function setGpuModeUI(mode) {
    state.gpuMode = mode;
    if (mode === 1) {
      el.btnTempMode.classList.add('active');
      el.btnFixedMode.classList.remove('active');
      el.fixedPanel.classList.add('hidden');
      el.tempPanel.classList.remove('hidden');
    } else {
      el.btnFixedMode.classList.add('active');
      el.btnTempMode.classList.remove('active');
      el.fixedPanel.classList.remove('hidden');
      el.tempPanel.classList.add('hidden');
    }
  }

  function showStatus(text) {
    if (!text) {
      el.statusBanner.classList.add('hidden');
      return;
    }
    el.statusBanner.textContent = text;
    el.statusBanner.classList.remove('hidden');
  }

  function renderRulesList() {
    el.rulesList.innerHTML = '';
    if (!state.appProfiles || state.appProfiles.length === 0) {
      el.rulesList.innerHTML = '<div style="font-size:10px; color:var(--text-muted); padding:4px;">No app profiles added yet.</div>';
      return;
    }

    state.appProfiles.forEach((profile, index) => {
      const item = document.createElement('div');
      item.className = 'rule-item';
      
      const summary = profile.Mode === 1 
        ? `TEMP LOCK ${profile.TargetTemp}°C (${profile.MaxMhz} MHz)`
        : `FIXED CAP ${profile.MaxMhz} MHz`;

      item.innerHTML = `
        <span class="rule-name">${profile.ProcessName.toUpperCase()}</span>
        <span class="rule-summary">${summary}</span>
        <button class="btn-del" data-index="${index}">✕</button>
      `;

      item.querySelector('.btn-del').addEventListener('click', () => {
        sendToHost('deleteRule', { index, processName: profile.ProcessName });
      });

      el.rulesList.appendChild(item);
    });
  }

  // --- EVENT LISTENERS ---
  el.cpuSlider.addEventListener('input', (e) => {
    el.cpuValBadge.textContent = `${e.target.value} %`;
    state.cpu = parseInt(e.target.value);
    updateSliderTrack(el.cpuSlider, '#FFB300');
  });

  el.freqSlider.addEventListener('input', (e) => {
    el.freqValBadge.textContent = `${e.target.value} MHz`;
    state.gpuFreq = parseInt(e.target.value);
    updateSliderTrack(el.freqSlider, '#00E676');
  });

  el.targetTempSlider.addEventListener('input', (e) => {
    el.targetTempValBadge.textContent = `${e.target.value} °C`;
    state.targetTemp = parseInt(e.target.value);
    updateSliderTrack(el.targetTempSlider, '#FF5252');
  });

  el.maxCapSlider.addEventListener('input', (e) => {
    el.maxCapValBadge.textContent = `${e.target.value} MHz`;
    state.maxCapMhz = parseInt(e.target.value);
    updateSliderTrack(el.maxCapSlider, '#00E676');
  });

  el.btnFixedMode.addEventListener('click', () => {
    setGpuModeUI(0);
    sendToHost('setGpuMode', { mode: 0 });
  });

  el.btnTempMode.addEventListener('click', () => {
    setGpuModeUI(1);
    sendToHost('setGpuMode', { mode: 1 });
  });

  el.appGovToggle.addEventListener('change', (e) => {
    state.appGovEnabled = e.target.checked;
    sendToHost('toggleAppGov', { enabled: e.target.checked });
  });

  el.btnAddRule.addEventListener('click', () => {
    const exe = el.appExeInput.value.trim();
    sendToHost('addRule', {
      exe: exe,
      mode: state.gpuMode,
      targetTemp: state.targetTemp,
      maxMhz: state.gpuMode === 1 ? state.maxCapMhz : state.gpuFreq,
      cpuThrottle: state.cpu
    });
    el.appExeInput.value = '';
  });

  el.btnApply.addEventListener('click', () => {
    sendToHost('apply', {
      cpu: state.cpu,
      gpuMode: state.gpuMode,
      gpuFreq: state.gpuFreq,
      targetTemp: state.targetTemp,
      maxCapMhz: state.maxCapMhz
    });
  });

  el.btnReset.addEventListener('click', () => {
    sendToHost('reset');
  });

  // Signal C# host that web app is ready
  document.addEventListener('DOMContentLoaded', () => {
    sendToHost('ready');
  });
})();
