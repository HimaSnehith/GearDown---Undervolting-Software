using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using GearDown.Core;
using Microsoft.Web.WebView2.Core;

using Application = System.Windows.Application;

namespace GearDown
{
    public partial class MainWindow : Window
    {
        private GpuController _gpu = new GpuController();
        private CpuController _cpu = new CpuController();
        private AppGovernor _appGovernor = new AppGovernor();
        private DispatcherTimer _monitor;
        private NotifyIcon? _trayIcon;

        private string _configPath;
        private string _gpuNameCache = "NVIDIA GPU DETECTED";

        // Current setting state
        private int _cpuState = 100;
        private int _fixedFreqMhz = 1800;
        private int _targetTempC = 75;
        private int _maxCapMhz = 2200;

        public MainWindow()
        {
            InitializeComponent();

            string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GearDown");
            if (!Directory.Exists(appDataFolder)) Directory.CreateDirectory(appDataFolder);
            _configPath = Path.Combine(appDataFolder, "config.json");

            _gpuNameCache = GetGpuName();

            SetupTrayIcon();
            LoadSettings();

            InitializeWebView();

            // Monitor loop (1.5 sec interval)
            _monitor = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _monitor.Tick += Monitor_Tick;
            _monitor.Start();
        }

        private async void InitializeWebView()
        {
            try
            {
                // Set WebView2 user data folder to LocalAppData to prevent Program Files write permission errors
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string webViewDataFolder = Path.Combine(localAppData, "GearDown", "WebView2Data");
                if (!Directory.Exists(webViewDataFolder)) Directory.CreateDirectory(webViewDataFolder);

                var env = await CoreWebView2Environment.CreateAsync(null, webViewDataFolder);
                await webView.EnsureCoreWebView2Async(env);

                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                webView.CoreWebView2.Settings.AreDevToolsEnabled = true;

                webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

                string wwwrootFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
                if (!Directory.Exists(wwwrootFolder))
                {
                    wwwrootFolder = Path.Combine(AppContext.BaseDirectory, "wwwroot");
                }

                if (Directory.Exists(wwwrootFolder))
                {
                    webView.CoreWebView2.SetVirtualHostNameToFolderMapping("app.geardown", wwwrootFolder, CoreWebView2HostResourceAccessKind.Allow);
                    webView.CoreWebView2.Navigate("https://app.geardown/index.html");
                }
                else
                {
                    string indexPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "index.html");
                    if (File.Exists(indexPath))
                    {
                        webView.CoreWebView2.Navigate(new Uri(indexPath).AbsoluteUri);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to initialize WebView2: {ex.Message}", "GearDown Error");
            }
        }

        private void Monitor_Tick(object? sender, EventArgs e)
        {
            int currentTemp = _gpu.GetCurrentTemp();
            bool profileChanged = _appGovernor.EvaluateForegroundProcess(out var activeProfile, out string processName);

            string activeAppDisplay = string.IsNullOrEmpty(processName)
                ? "GLOBAL DEFAULT (NO FOCUS)"
                : $"{processName.ToUpper()} (GLOBAL DEFAULT)";

            if (_appGovernor.IsEnabled && activeProfile != null)
            {
                activeAppDisplay = $"{processName.ToUpper()} [{activeProfile.DisplaySummary}]";

                if (profileChanged)
                {
                    if (activeProfile.Mode == GpuControlMode.TemperatureLock)
                    {
                        _gpu.Mode = GpuControlMode.TemperatureLock;
                        _gpu.Governor.Initialize(activeProfile.TargetTemp, Math.Min(1500, activeProfile.MaxMhz), activeProfile.MaxMhz);
                    }
                    else
                    {
                        _gpu.Mode = GpuControlMode.FixedFrequency;
                        _gpu.SetClockLimit(activeProfile.MaxMhz);
                    }
                    _cpu.SetThrottleLevel(activeProfile.CpuThrottle);
                }
            }

            int activeDynamicMhz = _gpu.FixedMaxMhz;
            string govStateText = _gpu.Mode == GpuControlMode.TemperatureLock ? $"LOCK @ {_gpu.Governor.TargetTemp}°C" : "FIXED CAP";

            if (_gpu.Mode == GpuControlMode.TemperatureLock)
            {
                _gpu.ProcessThermalGovernorTick(currentTemp, out activeDynamicMhz);
                govStateText = $"LOCK @ {_gpu.Governor.TargetTemp}°C (ACTIVE)";
            }

            // Post telemetry payload to JS Web UI
            SendTelemetryToUI(currentTemp, activeDynamicMhz, govStateText, activeAppDisplay);
        }

        private void SendTelemetryToUI(int temp, int activeClock, string govState, string activeApp)
        {
            if (webView.CoreWebView2 == null) return;

            var payload = new
            {
                type = "telemetry",
                temp = temp,
                gpuName = _gpuNameCache,
                activeClock = activeClock,
                govState = govState,
                activeApp = activeApp
            };

            webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload));
        }

        private void SendConfigToUI()
        {
            if (webView.CoreWebView2 == null) return;

            var payload = new
            {
                type = "config",
                cpu = _cpuState,
                gpuMode = (int)_gpu.Mode,
                gpuFreq = _fixedFreqMhz,
                targetTemp = _targetTempC,
                maxCapMhz = _maxCapMhz,
                appGovEnabled = _appGovernor.IsEnabled,
                appProfiles = _appGovernor.Profiles
            };

            webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload));
        }

        private void SendStatusToUI(string message)
        {
            if (webView.CoreWebView2 == null) return;

            var payload = new
            {
                type = "status",
                text = message
            };

            webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload));
        }

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string json = e.WebMessageAsJson;
                using JsonDocument doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string action = root.GetProperty("action").GetString() ?? "";

                switch (action)
                {
                    case "ready":
                        SendConfigToUI();
                        break;

                    case "setGpuMode":
                        int modeVal = root.GetProperty("mode").GetInt32();
                        _gpu.Mode = (GpuControlMode)modeVal;
                        SaveSettings();
                        break;

                    case "toggleAppGov":
                        bool enabled = root.GetProperty("enabled").GetBoolean();
                        _appGovernor.IsEnabled = enabled;
                        SaveSettings();
                        SendStatusToUI(enabled ? "AUTO PROFILES: ENABLED" : "AUTO PROFILES: DISABLED");
                        break;

                    case "addRule":
                        string exe = root.TryGetProperty("exe", out var exeProp) ? exeProp.GetString() ?? "" : "";
                        if (string.IsNullOrWhiteSpace(exe))
                        {
                            exe = _appGovernor.GetForegroundProcessExeName();
                        }
                        if (!string.IsNullOrWhiteSpace(exe) && !exe.Equals("geardown", StringComparison.OrdinalIgnoreCase))
                        {
                            exe = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
                            _appGovernor.Profiles.RemoveAll(p => p.ProcessName.Equals(exe, StringComparison.OrdinalIgnoreCase));

                            int ruleMode = root.GetProperty("mode").GetInt32();
                            int ruleTargetTemp = root.GetProperty("targetTemp").GetInt32();
                            int ruleMaxMhz = root.GetProperty("maxMhz").GetInt32();
                            int ruleCpu = root.GetProperty("cpuThrottle").GetInt32();

                            _appGovernor.Profiles.Add(new AppProfile
                            {
                                ProcessName = exe,
                                DisplayName = exe.ToUpper(),
                                Mode = (GpuControlMode)ruleMode,
                                TargetTemp = ruleTargetTemp,
                                MaxMhz = ruleMaxMhz,
                                CpuThrottle = ruleCpu
                            });

                            SaveSettings();
                            SendConfigToUI();
                            SendStatusToUI($"SAVED PROFILE FOR {exe.ToUpper()}");
                        }
                        else
                        {
                            SendStatusToUI("PLEASE ENTER A PROCESS NAME (E.G. CYBERPUNK2077)");
                        }
                        break;

                    case "deleteRule":
                        if (root.TryGetProperty("processName", out var nameProp))
                        {
                            string pName = nameProp.GetString() ?? "";
                            _appGovernor.Profiles.RemoveAll(p => p.ProcessName.Equals(pName, StringComparison.OrdinalIgnoreCase));
                            SaveSettings();
                            SendConfigToUI();
                            SendStatusToUI($"REMOVED PROFILE FOR {pName.ToUpper()}");
                        }
                        break;

                    case "apply":
                        _cpuState = root.GetProperty("cpu").GetInt32();
                        _fixedFreqMhz = root.GetProperty("gpuFreq").GetInt32();
                        _targetTempC = root.GetProperty("targetTemp").GetInt32();
                        _maxCapMhz = root.GetProperty("maxCapMhz").GetInt32();
                        int setMode = root.GetProperty("gpuMode").GetInt32();

                        _gpu.Mode = (GpuControlMode)setMode;
                        _cpu.SetThrottleLevel(_cpuState);

                        if (_gpu.Mode == GpuControlMode.FixedFrequency)
                        {
                            _gpu.SetClockLimit(_fixedFreqMhz);
                            SendStatusToUI($"EXECUTED · CPU {_cpuState}% · GPU {_fixedFreqMhz} MHz (FIXED)");
                        }
                        else
                        {
                            int currentTemp = _gpu.GetCurrentTemp();
                            _gpu.Governor.Initialize(_targetTempC, Math.Min(1500, _maxCapMhz), _maxCapMhz);
                            _gpu.ProcessThermalGovernorTick(currentTemp, out _);
                            SendStatusToUI($"EXECUTED · CPU {_cpuState}% · GPU TEMP LOCK {_targetTempC}°C");
                        }

                        SaveSettings();
                        break;

                    case "reset":
                        ResetHardware();
                        if (File.Exists(_configPath)) File.Delete(_configPath);

                        _cpuState = 100;
                        _fixedFreqMhz = 1800;
                        _targetTempC = 75;
                        _maxCapMhz = 2200;
                        _gpu.Mode = GpuControlMode.FixedFrequency;
                        _appGovernor.IsEnabled = false;
                        _appGovernor.Profiles.Clear();

                        SendConfigToUI();
                        SendStatusToUI("RESTORED FACTORY DEFAULTS");
                        break;
                }
            }
            catch { }
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        _cpuState = doc.RootElement.GetProperty("Cpu").GetInt32();
                        int savedGpuMode = doc.RootElement.TryGetProperty("GpuMode", out var modeProp) ? modeProp.GetInt32() : 0;
                        _fixedFreqMhz = doc.RootElement.GetProperty("Gpu").GetInt32();
                        _targetTempC = doc.RootElement.TryGetProperty("TargetTemp", out var tempProp) ? tempProp.GetInt32() : 75;
                        _maxCapMhz = doc.RootElement.TryGetProperty("MaxCapMhz", out var capProp) ? capProp.GetInt32() : 2200;

                        bool appGovEnabled = doc.RootElement.TryGetProperty("AppGovernorEnabled", out var appGovProp) && appGovProp.GetBoolean();
                        _appGovernor.IsEnabled = appGovEnabled;

                        if (doc.RootElement.TryGetProperty("AppProfiles", out var profilesProp) && profilesProp.ValueKind == JsonValueKind.Array)
                        {
                            var profiles = JsonSerializer.Deserialize<List<AppProfile>>(profilesProp.GetRawText());
                            if (profiles != null) _appGovernor.Profiles = profiles;
                        }

                        _cpu.SetThrottleLevel(_cpuState);
                        _gpu.Mode = (GpuControlMode)savedGpuMode;

                        if (_gpu.Mode == GpuControlMode.TemperatureLock)
                        {
                            _gpu.Governor.Initialize(_targetTempC, Math.Min(1500, _maxCapMhz), _maxCapMhz);
                        }
                        else
                        {
                            _gpu.SetClockLimit(_fixedFreqMhz);
                        }
                    }
                }
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                var config = new
                {
                    Cpu = _cpuState,
                    GpuMode = (int)_gpu.Mode,
                    Gpu = _fixedFreqMhz,
                    TargetTemp = _targetTempC,
                    MaxCapMhz = _maxCapMhz,
                    AppGovernorEnabled = _appGovernor.IsEnabled,
                    AppProfiles = _appGovernor.Profiles
                };
                File.WriteAllText(_configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private string GetGpuName()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "nvidia-smi",
                        Arguments = "--query-gpu=name --format=csv,noheader",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();

                return string.IsNullOrEmpty(output) ? "NVIDIA GPU DETECTED" : output.ToUpper();
            }
            catch
            {
                return "NVIDIA GRAPHICS DEVICE";
            }
        }

        private void SetupTrayIcon()
        {
            _trayIcon = new NotifyIcon();
            try
            {
                var streamInfo = Application.GetResourceStream(new Uri("pack://application:,,,/fan.ico"));
                _trayIcon.Icon = new Icon(streamInfo.Stream);
            }
            catch { _trayIcon.Icon = SystemIcons.Shield; }

            _trayIcon.Text = "Gear Down (Active)";
            _trayIcon.Visible = true;
            _trayIcon.DoubleClick += (s, e) => { this.Show(); this.WindowState = WindowState.Normal; };

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Open Dashboard", null, (s, e) => { this.Show(); this.WindowState = WindowState.Normal; });
            menu.Items.Add("Reset & Exit", null, (s, e) => {
                ResetHardware();
                _trayIcon?.Dispose();
                Application.Current.Shutdown();
            });
            _trayIcon.ContextMenuStrip = menu;
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                this.Hide();
                _trayIcon?.ShowBalloonTip(2000, "Gear Down", "Running in background.", ToolTipIcon.Info);
            }
            base.OnStateChanged(e);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            ResetHardware();
            _trayIcon?.Dispose();
            base.OnClosing(e);
        }

        private void ResetHardware()
        {
            _gpu.ResetLimits();
            _cpu.ResetLimits();
        }
    }
}