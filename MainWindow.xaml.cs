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
        private DispatcherTimer _monitor;
        private NotifyIcon? _trayIcon; 
        
        private string _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

        public MainWindow()
        {
            InitializeComponent();
            
            // Auto-detect the user's GPU and set the title
            GpuNameText.Text = GetGpuName();

            SetupTrayIcon();
            LoadAndApplySettings();

            // Zero-overhead loop: Only reads GPU temp
            _monitor = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _monitor.Tick += (s, e) => {
                TempText.Text = $"{_gpu.GetCurrentTemp()} °C";
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
                        int savedGpu = doc.RootElement.GetProperty("Gpu").GetInt32();
                        
                        CpuPowerSlider.Value = savedCpu;
                        FreqSlider.Value = savedGpu;
                        
                        _cpu.SetThrottleLevel(savedCpu);
                        _gpu.SetClockLimit(savedGpu);
                        
                        CpuStatusText.Text = $"SYSTEM: {savedCpu}%";
                        GpuStatusText.Text = $"SYSTEM: {savedGpu} MHz";
                        SetStatusText("INITIALIZED: CONFIG LOADED");
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
                    Gpu = (int)FreqSlider.Value
                };
                File.WriteAllText(_configPath, JsonSerializer.Serialize(config));
            }
            catch { }
        }

        // --- TRAY LOGIC ---
        private void SetupTrayIcon()
        {
            _trayIcon = new NotifyIcon();
            try { _trayIcon.Icon = new System.Drawing.Icon("fan.ico"); } 
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

        // --- BUTTON HANDLERS ---
        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            int powerState = (int)CpuPowerSlider.Value;
            int mhz = (int)FreqSlider.Value;
            
            _cpu.SetThrottleLevel(powerState);
            _gpu.SetClockLimit(mhz);
            SaveSettings();

            CpuStatusText.Text = $"SYSTEM: {powerState}%";
            GpuStatusText.Text = $"SYSTEM: {mhz} MHz";
            SetStatusText($"EXECUTED · CPU {powerState}% · GPU {mhz} MHz");
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            ResetHardware();
            if (File.Exists(_configPath)) File.Delete(_configPath);
            
            CpuPowerSlider.Value = 100;
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

        private void CpuSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sender is Slider slider)
            {
                int roundedValue = (int)(Math.Round(e.NewValue / 1.0) * 1); // 1% smooth increments
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