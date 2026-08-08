using System;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Drawing; 
using System.Windows.Forms; 
using System.IO;
using GearDown.Core;

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
        private bool _isInitializing = true;

        public MainWindow()
        {
            InitializeComponent();
            string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GearDown");
            if (!Directory.Exists(appDataFolder)) Directory.CreateDirectory(appDataFolder);
            _configPath = Path.Combine(appDataFolder, "config.json");
            
            // Auto-detect the user's GPU and set the title
            GpuNameText.Text = GetGpuName();

            SetupTrayIcon();
            LoadAndApplySettings();
            _isInitializing = false;

            // Monitor loop (1.5 sec interval for responsive thermal governance)
            _monitor = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            _monitor.Tick += (s, e) => {
                int currentTemp = _gpu.GetCurrentTemp();
                TempText.Text = $"{currentTemp} °C";

                bool profileChanged = _appGovernor.EvaluateForegroundProcess(out var activeProfile, out string processName);
                
                if (_appGovernor.IsEnabled && activeProfile != null)
                {
                    ActiveAppText.Text = $"{processName.ToUpper()} [{activeProfile.DisplaySummary}]";
                    
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
                else
                {
                    ActiveAppText.Text = string.IsNullOrEmpty(processName) 
                        ? "GLOBAL DEFAULT (NO FOCUS)" 
                        : $"{processName.ToUpper()} (GLOBAL DEFAULT)";
                }

                if (_gpu.Mode == GpuControlMode.TemperatureLock)
                {
                    _gpu.ProcessThermalGovernorTick(currentTemp, out int activeDynamicMhz);
                    GpuStatusText.Text = $"LOCK @ {_gpu.Governor.TargetTemp}°C (ACTIVE: {activeDynamicMhz} MHz)";
                }
            };
            _monitor.Start();
        }

        // --- HARDWARE DETECTION ---
        private string GetGpuName()
        {
            try
            {
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
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

        // --- PERSISTENCE ENGINE ---
        private void LoadAndApplySettings()
        {
            try 
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    using (JsonDocument doc = JsonDocument.Parse(json)) 
                    {
                        int savedCpu = doc.RootElement.GetProperty("Cpu").GetInt32();
                        int savedGpuMode = doc.RootElement.TryGetProperty("GpuMode", out var modeProp) ? modeProp.GetInt32() : 0;
                        int savedGpuMhz = doc.RootElement.GetProperty("Gpu").GetInt32();
                        int savedTargetTemp = doc.RootElement.TryGetProperty("TargetTemp", out var tempProp) ? tempProp.GetInt32() : 75;
                        int savedMaxCap = doc.RootElement.TryGetProperty("MaxCapMhz", out var capProp) ? capProp.GetInt32() : 2200;

                        bool appGovEnabled = doc.RootElement.TryGetProperty("AppGovernorEnabled", out var appGovProp) && appGovProp.GetBoolean();
                        _appGovernor.IsEnabled = appGovEnabled;
                        EnableAppGovernorCheck.IsChecked = appGovEnabled;

                        if (doc.RootElement.TryGetProperty("AppProfiles", out var profilesProp) && profilesProp.ValueKind == JsonValueKind.Array)
                        {
                            var profiles = JsonSerializer.Deserialize<System.Collections.Generic.List<AppProfile>>(profilesProp.GetRawText());
                            if (profiles != null) _appGovernor.Profiles = profiles;
                        }

                        RefreshAppRulesList();

                        CpuPowerSlider.Value = savedCpu;
                        FreqSlider.Value = savedGpuMhz;
                        TempTargetSlider.Value = savedTargetTemp;
                        MaxCapSlider.Value = savedMaxCap;

                        _cpu.SetThrottleLevel(savedCpu);

                        if (savedGpuMode == (int)GpuControlMode.TemperatureLock)
                        {
                            TempLockRadio.IsChecked = true;
                            _gpu.Mode = GpuControlMode.TemperatureLock;
                            _gpu.Governor.Initialize(savedTargetTemp, Math.Min(1500, savedMaxCap), savedMaxCap);
                            GpuStatusText.Text = $"LOCK @ {savedTargetTemp}°C (CEILING: {savedMaxCap} MHz)";
                        }
                        else
                        {
                            FixedFreqRadio.IsChecked = true;
                            _gpu.Mode = GpuControlMode.FixedFrequency;
                            _gpu.SetClockLimit(savedGpuMhz);
                            GpuStatusText.Text = $"SYSTEM: {savedGpuMhz} MHz";
                        }
                        
                        CpuStatusText.Text = $"SYSTEM: {savedCpu}%";
                        SetStatusText("INITIALIZED: CONFIG LOADED");
                        UpdateModePanels();
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
                    Cpu = (int)CpuPowerSlider.Value,
                    GpuMode = (int)_gpu.Mode,
                    Gpu = (int)FreqSlider.Value,
                    TargetTemp = (int)TempTargetSlider.Value,
                    MaxCapMhz = (int)MaxCapSlider.Value,
                    AppGovernorEnabled = _appGovernor.IsEnabled,
                    AppProfiles = _appGovernor.Profiles
                };
                File.WriteAllText(_configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        // --- TRAY LOGIC ---
        private void SetupTrayIcon()
        {
            _trayIcon = new NotifyIcon();
            try {
                var streamInfo = Application.GetResourceStream(new Uri("pack://application:,,,/fan.ico"));
                _trayIcon.Icon = new System.Drawing.Icon(streamInfo.Stream);
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
            if (this.WindowState == WindowState.Minimized) {
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

        // --- MODE TOGGLE & UI HANDLERS ---
        private void GpuMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            UpdateModePanels();
        }

        private void UpdateModePanels()
        {
            if (TempLockRadio.IsChecked == true)
            {
                _gpu.Mode = GpuControlMode.TemperatureLock;
                FixedFreqPanel.Visibility = Visibility.Collapsed;
                TempLockPanel.Visibility = Visibility.Visible;
            }
            else
            {
                _gpu.Mode = GpuControlMode.FixedFrequency;
                FixedFreqPanel.Visibility = Visibility.Visible;
                TempLockPanel.Visibility = Visibility.Collapsed;
            }
        }

        // --- PER-APP PROFILE HANDLERS ---
        private void EnableAppGovernor_Click(object sender, RoutedEventArgs e)
        {
            _appGovernor.IsEnabled = EnableAppGovernorCheck.IsChecked == true;
            SaveSettings();
            SetStatusText(_appGovernor.IsEnabled ? "AUTO APP PROFILES: ENABLED" : "AUTO APP PROFILES: DISABLED");
        }

        private void AddAppRule_Click(object sender, RoutedEventArgs e)
        {
            string targetExe = NewAppExeInput.Text.Trim();
            if (string.IsNullOrEmpty(targetExe))
            {
                targetExe = _appGovernor.GetForegroundProcessExeName();
            }

            if (string.IsNullOrEmpty(targetExe) || targetExe.Equals("geardown", StringComparison.OrdinalIgnoreCase))
            {
                SetStatusText("PLEASE ENTER A PROCESS NAME (E.G. CYBERPUNK2077)");
                return;
            }

            targetExe = System.IO.Path.GetFileNameWithoutExtension(targetExe).ToLowerInvariant();

            // Check for existing profile
            var existing = _appGovernor.Profiles.Find(p => p.ProcessName.Equals(targetExe, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                _appGovernor.Profiles.Remove(existing);
            }

            var newProfile = new AppProfile
            {
                ProcessName = targetExe,
                DisplayName = targetExe.ToUpper(),
                Mode = TempLockRadio.IsChecked == true ? GpuControlMode.TemperatureLock : GpuControlMode.FixedFrequency,
                TargetTemp = (int)TempTargetSlider.Value,
                MaxMhz = TempLockRadio.IsChecked == true ? (int)MaxCapSlider.Value : (int)FreqSlider.Value,
                CpuThrottle = (int)CpuPowerSlider.Value
            };

            _appGovernor.Profiles.Add(newProfile);
            NewAppExeInput.Clear();
            RefreshAppRulesList();
            SaveSettings();
            SetStatusText($"PROFILE SAVED FOR {targetExe.ToUpper()}");
        }

        private void DeleteAppRule_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is AppProfile profile)
            {
                _appGovernor.Profiles.Remove(profile);
                RefreshAppRulesList();
                SaveSettings();
                SetStatusText($"REMOVED PROFILE FOR {profile.ProcessName.ToUpper()}");
            }
        }

        private void RefreshAppRulesList()
        {
            AppRulesListBox.ItemsSource = null;
            AppRulesListBox.ItemsSource = _appGovernor.Profiles;
        }

        // --- BUTTON HANDLERS ---
        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            int powerState = (int)CpuPowerSlider.Value;
            _cpu.SetThrottleLevel(powerState);

            if (_gpu.Mode == GpuControlMode.FixedFrequency)
            {
                int mhz = (int)FreqSlider.Value;
                _gpu.SetClockLimit(mhz);
                GpuStatusText.Text = $"SYSTEM: {mhz} MHz";
                SetStatusText($"EXECUTED · CPU {powerState}% · GPU {mhz} MHz (FIXED)");
            }
            else
            {
                int targetTemp = (int)TempTargetSlider.Value;
                int maxCap = (int)MaxCapSlider.Value;
                int currentTemp = _gpu.GetCurrentTemp();
                _gpu.Governor.Initialize(targetTemp, Math.Min(1500, maxCap), maxCap);
                _gpu.ProcessThermalGovernorTick(currentTemp, out int activeMhz);

                GpuStatusText.Text = $"LOCK @ {targetTemp}°C (ACTIVE: {activeMhz} MHz)";
                SetStatusText($"EXECUTED · CPU {powerState}% · GPU TEMP LOCK {targetTemp}°C");
            }

            SaveSettings();
            CpuStatusText.Text = $"SYSTEM: {powerState}%";
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            ResetHardware();
            if (File.Exists(_configPath)) File.Delete(_configPath);
            
            _gpu.Mode = GpuControlMode.FixedFrequency;
            FixedFreqRadio.IsChecked = true;
            _appGovernor.IsEnabled = false;
            EnableAppGovernorCheck.IsChecked = false;
            _appGovernor.Profiles.Clear();
            RefreshAppRulesList();
            UpdateModePanels();

            CpuPowerSlider.Value = 100;
            FreqSlider.Value = 1800;
            TempTargetSlider.Value = 75;
            MaxCapSlider.Value = 2200;

            GpuStatusText.Text = "SYSTEM: DEFAULT";
            CpuStatusText.Text = "SYSTEM: 100% (DEFAULT)";
            SetStatusText("ABORTED: FACTORY DEFAULTS RESTORED");
        }

        private void SetStatusText(string text)
        {
            StatusText.Text = text;
            StatusText.Visibility = string.IsNullOrWhiteSpace(text)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void FreqSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sender is Slider slider)
            {
                int roundedValue = (int)(Math.Round(e.NewValue / 10.0) * 10);
                if (roundedValue != (int)slider.Value)
                {
                    slider.Value = roundedValue;
                    return;
                }
            }
            if (SliderValueText != null) SliderValueText.Text = $"{(int)e.NewValue} MHz";
        }

        private void TempTargetSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sender is Slider slider)
            {
                int roundedValue = (int)(Math.Round(e.NewValue / 1.0) * 1);
                if (roundedValue != (int)slider.Value)
                {
                    slider.Value = roundedValue;
                    return;
                }
            }
            if (TempTargetValueText != null) TempTargetValueText.Text = $"{(int)e.NewValue} °C";
        }

        private void MaxCapSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sender is Slider slider)
            {
                int roundedValue = (int)(Math.Round(e.NewValue / 10.0) * 10);
                if (roundedValue != (int)slider.Value)
                {
                    slider.Value = roundedValue;
                    return;
                }
            }
            if (MaxCapValueText != null) MaxCapValueText.Text = $"{(int)e.NewValue} MHz";
        }

        private void CpuSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sender is Slider slider)
            {
                int roundedValue = (int)(Math.Round(e.NewValue / 1.0) * 1);
                if (roundedValue != (int)slider.Value)
                {
                    slider.Value = roundedValue;
                    return;
                }
            }
            if (CpuSliderValueText != null) CpuSliderValueText.Text = $"{(int)e.NewValue} %";
        }
    }
}