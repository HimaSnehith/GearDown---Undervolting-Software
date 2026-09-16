// Web UI State Controller for GearDown (Fluent Glass Design)
(function () {
  let state = {
    gpuMode: 0, // 0 = FixedFrequency, 1 = TemperatureLock
    cpu: 100,
    gpuFreq: 1800,
    targetTemp: 75,
    maxCapMhz: 2200,
    profiles: []
  };

  // DOM Elements
  const el = {
    tempVal: document.getElementById('tempVal'),
    tempGaugeArc: document.getElementById('tempGaugeArc'),
    statGovState: document.getElementById('statGovState'),
    statActiveClock: document.getElementById('statActiveClock'),
    gpuNameBadge: document.getElementById('gpuNameBadge'),
    gpuVendorBadge: document.getElementById('gpuVendorBadge'),
    sidebarGpuChip: document.getElementById('sidebarGpuChip'),
    statusBanner: document.getElementById('statusBanner'),

    // Hardware Telemetry Chips
    pcieVal: document.getElementById('pcieVal'),
    powerVal: document.getElementById('powerVal'),
    pstateVal: document.getElementById('pstateVal'),
    vramVal: document.getElementById('vramVal'),
    driverVal: document.getElementById('driverVal'),

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

    customProfileInput: document.getElementById('customProfileInput'),
    btnSaveCustomProfile: document.getElementById('btnSaveCustomProfile'),
    customProfilesList: document.getElementById('customProfilesList'),
    customProfilesCountBadge: document.getElementById('customProfilesCountBadge'),
    navProfilesBadge: document.querySelector('.nav-item[data-tab="profiles"] .nav-badge'),

    btnApply: document.getElementById('btnApply'),
    btnReset: document.getElementById('btnReset')
  };

  // --- TAB NAVIGATION SYSTEM ---
  const navItems = document.querySelectorAll('.nav-item');
  const tabViews = document.querySelectorAll('.tab-view');

  navItems.forEach(item => {
    item.addEventListener('click', () => {
      const tabName = item.getAttribute('data-tab');
      navItems.forEach(n => n.classList.remove('active'));
      tabViews.forEach(v => v.classList.add('hidden'));

      item.classList.add('active');
      const targetView = document.getElementById(`view${tabName.charAt(0).toUpperCase() + tabName.slice(1)}`);
      if (targetView) targetView.classList.remove('hidden');
    });
  });

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

  function formatGpuName(rawName) {
    if (!rawName) return { vendor: 'GPU', model: 'Detecting GPU...' };
    let str = rawName.trim();
    let vendor = 'GPU';

    if (/nvidia/i.test(str)) {
      vendor = 'NVIDIA';
      str = str.replace(/nvidia/i, '').trim();
    } else if (/amd|radeon/i.test(str)) {
      vendor = 'AMD';
      str = str.replace(/amd/i, '').trim();
    } else if (/intel/i.test(str)) {
      vendor = 'INTEL';
      str = str.replace(/intel/i, '').trim();
    }

    let model = str || rawName;
    if (model === model.toUpperCase() && model.length > 5) {
      model = model.toLowerCase().replace(/\b\w/g, c => c.toUpperCase());
      model = model.replace(/\b(rtx|gtx|rx|gpu|pcie|ti|super)\b/gi, m => m.toUpperCase());
    }

    return { vendor, model };
  }

  // --- TELEMETRY UPDATES ---
  function updateTelemetry(data) {
    if (data.temp !== undefined) {
      el.tempVal.textContent = data.temp;
      // Gauge ring calculation: circumference = 2 * PI * 64 = ~402.12
      const maxTemp = 95;
      const pct = Math.min(1, Math.max(0, data.temp / maxTemp));
      const offset = 402 - (402 * pct);
      if (el.tempGaugeArc) {
        el.tempGaugeArc.style.strokeDashoffset = offset;
      }

      // Thermal threshold color states: <70 Green, 70-80 Yellow, >80 Red
      let tempColor = '#00E676';
      if (data.temp > 80) {
        tempColor = '#FF5252';
      } else if (data.temp >= 70) {
        tempColor = '#FFB300';
      }

      if (el.tempVal) el.tempVal.style.color = tempColor;
      if (el.tempGaugeArc) {
        el.tempGaugeArc.style.stroke = tempColor;
        el.tempGaugeArc.style.filter = `drop-shadow(0 0 10px ${tempColor}70)`;
      }
    }

    if (data.gpuName) {
      const parsed = formatGpuName(data.gpuName);
      if (el.gpuVendorBadge) el.gpuVendorBadge.textContent = (parsed.vendor === 'NVIDIA') ? 'NV' : parsed.vendor;
      const statusVendorTag = document.getElementById('statusVendorTag');
      if (statusVendorTag) statusVendorTag.textContent = parsed.vendor;
      if (el.sidebarGpuChip) el.sidebarGpuChip.setAttribute('data-tooltip', `${parsed.vendor} ${parsed.model}`);
      if (el.gpuNameBadge) el.gpuNameBadge.textContent = parsed.model;
    }
    if (data.activeClock && el.statActiveClock) {
      const clockStr = data.activeClock.toString();
      el.statActiveClock.textContent = (clockStr.includes('MHz') || clockStr.includes('UNCAPPED') || clockStr.includes('STOCK'))
        ? clockStr
        : `${clockStr} MHz`;
    }
    if (data.govState && el.statGovState) el.statGovState.textContent = data.govState;

    // Update Header Hardware Telemetry
    if (data.power && el.powerVal) el.powerVal.textContent = data.power;
    if (data.vram && el.vramVal) el.vramVal.textContent = data.vram;
    if (data.driver && el.driverVal) el.driverVal.textContent = data.driver;
  }

  // --- SLIDER STYLING ---
  function updateSliderTrack(slider, color = '#00E676') {
    if (!slider) return;
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

  // --- CONFIG APPLIER ---
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
    if (cfg.profiles) {
      state.profiles = cfg.profiles;
      renderCustomProfilesList();
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

  // --- CUSTOM PROFILES RENDERING ---
  function renderCustomProfilesList() {
    if (!el.customProfilesList) return;
    el.customProfilesList.innerHTML = '';

    const count = state.profiles ? state.profiles.length : 0;
    if (el.customProfilesCountBadge) {
      el.customProfilesCountBadge.textContent = `${count} ${count === 1 ? 'Profile' : 'Profiles'}`;
    }
    if (el.navProfilesBadge) {
      el.navProfilesBadge.textContent = (count + 3).toString(); // 3 presets + custom
    }

    if (!state.profiles || state.profiles.length === 0) {
      el.customProfilesList.innerHTML = `
        <div style="grid-column: 1 / -1; font-size: 11.5px; color: var(--text-muted); padding: 18px; text-align: center; background: rgba(6, 10, 8, 0.4); border-radius: var(--radius-sm); border: 1px dashed var(--border-glass);">
          No custom profiles saved yet. Set your sliders on the dashboard and save your first profile above!
        </div>
      `;
      return;
    }

    state.profiles.forEach((profile) => {
      const card = document.createElement('div');
      card.className = 'custom-profile-card';

      const isTempLock = profile.GpuMode === 1;
      const modeLabel = isTempLock ? 'Dynamic Temp Lock' : 'Fixed Clock';
      const targetDetail = isTempLock
        ? `<div><span>Target Temp:</span> ${profile.TargetTemp} °C</div><div><span>Max Cap:</span> ${profile.MaxCapMhz} MHz</div>`
        : `<div><span>Clock Cap:</span> ${profile.GpuFreq} MHz</div>`;

      card.innerHTML = `
        <div class="custom-profile-header">
          <span class="custom-profile-title">${escapeHtml(profile.Name)}</span>
          <div class="custom-profile-actions">
            <button type="button" class="btn-card-del" title="Delete Profile" data-name="${escapeHtml(profile.Name)}">✕</button>
          </div>
        </div>
        <div class="custom-profile-specs">
          <div><span>Mode:</span> ${modeLabel}</div>
          ${targetDetail}
          <div><span>CPU Scaling:</span> ${profile.Cpu} %</div>
        </div>
        <button type="button" class="btn-preset-apply" data-apply="true">Apply Profile</button>
      `;

      card.querySelector('[data-apply="true"]').addEventListener('click', () => {
        window.applyPreset(profile.GpuFreq, profile.Cpu, profile.GpuMode, profile.TargetTemp, profile.MaxCapMhz, profile.Name);
      });

      card.querySelector('.btn-card-del').addEventListener('click', () => {
        sendToHost('deleteProfile', { name: profile.Name });
      });

      el.customProfilesList.appendChild(card);
    });
  }

  function escapeHtml(str) {
    if (!str) return '';
    return str.replace(/[&<>"']/g, m => ({
      '&': '&amp;',
      '<': '&lt;',
      '>': '&gt;',
      '"': '&quot;',
      "'": '&#39;'
    }[m]));
  }

  // --- EVENT LISTENERS ---
  el.cpuSlider.addEventListener('input', (e) => {
    el.cpuValBadge.textContent = `${e.target.value} %`;
    state.cpu = parseInt(e.target.value);
    updateSliderTrack(el.cpuSlider, '#FFB300');
    syncHeaderPresetPill();
  });

  el.freqSlider.addEventListener('input', (e) => {
    el.freqValBadge.textContent = `${e.target.value} MHz`;
    state.gpuFreq = parseInt(e.target.value);
    updateSliderTrack(el.freqSlider, '#00E676');
    syncHeaderPresetPill();
  });

  el.targetTempSlider.addEventListener('input', (e) => {
    el.targetTempValBadge.textContent = `${e.target.value} °C`;
    state.targetTemp = parseInt(e.target.value);
    updateSliderTrack(el.targetTempSlider, '#FF5252');
    syncHeaderPresetPill();
  });

  el.maxCapSlider.addEventListener('input', (e) => {
    el.maxCapValBadge.textContent = `${e.target.value} MHz`;
    state.maxCapMhz = parseInt(e.target.value);
    updateSliderTrack(el.maxCapSlider, '#00E676');
    syncHeaderPresetPill();
  });

  el.btnFixedMode.addEventListener('click', () => {
    setGpuModeUI(0);
    syncHeaderPresetPill();
    sendToHost('setGpuMode', { mode: 0 });
  });

  el.btnTempMode.addEventListener('click', () => {
    setGpuModeUI(1);
    syncHeaderPresetPill();
    sendToHost('setGpuMode', { mode: 1 });
  });

  // Save Custom Profile Button handler
  function saveCurrentProfile() {
    const name = el.customProfileInput ? el.customProfileInput.value.trim() : '';
    if (!name) {
      showStatus("PLEASE ENTER A PROFILE NAME");
      setTimeout(() => showStatus(""), 2500);
      return;
    }

    sendToHost('saveProfile', {
      name: name,
      cpu: state.cpu,
      gpuMode: state.gpuMode,
      gpuFreq: state.gpuFreq,
      targetTemp: state.targetTemp,
      maxCapMhz: state.maxCapMhz
    });

    if (el.customProfileInput) el.customProfileInput.value = '';
  }

  if (el.btnSaveCustomProfile) {
    el.btnSaveCustomProfile.addEventListener('click', saveCurrentProfile);
  }

  if (el.customProfileInput) {
    el.customProfileInput.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') {
        saveCurrentProfile();
      }
    });
  }

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

  // Global helper for profile card click handlers
  window.applyPreset = function (gpuFreq, cpu, gpuMode, targetTemp, maxCapMhz, presetName) {
    state.gpuFreq = gpuFreq;
    state.cpu = cpu;
    state.gpuMode = gpuMode;
    state.targetTemp = targetTemp;
    state.maxCapMhz = maxCapMhz;

    applyConfigToUI({
      gpuFreq,
      cpu,
      gpuMode,
      targetTemp,
      maxCapMhz
    });

    sendToHost('apply', {
      cpu,
      gpuMode,
      gpuFreq,
      targetTemp,
      maxCapMhz
    });

    const label = presetName ? `"${presetName.toUpperCase()}" PROFILE` : 'PRESET PROFILE';
    showStatus(`${label} APPLIED SUCCESSFULLY`);
    setTimeout(() => showStatus(""), 3000);
  };

  // Signal C# host that web app is ready
  document.addEventListener('DOMContentLoaded', () => {
    updateAllSliderTracks();
    sendToHost('ready');
  });
})();
