using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using System.Drawing; 
using System.Windows.Forms; 
using System.IO;
using System.Text.Json;
using GearDown.Core;

using Application = System.Windows.Application;
using WpfBrushes = System.Windows.Media.Brushes; 

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
            SetupTrayIcon();
            LoadAndApplySettings();

            // Zero-overhead loop: Only reads GPU temp
            _monitor = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _monitor.Tick += (s, e) => {
                TempText.Text = $"{_gpu.GetCurrentTemp()} °C";
            };
            _monitor.Start();
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
                        
                        CpuStatusText.Text = $"CPU: {savedCpu}%";
                        GpuStatusText.Text = $"GPU: {savedGpu} MHz";
                        StatusText.Text = "Saved Limits Auto-Applied";
                        StatusText.Foreground = WpfBrushes.Cyan;
                    }
                }
            } 
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                var config = new { Cpu = (int)CpuPowerSlider.Value, Gpu = (int)FreqSlider.Value };
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
        private void ApplyCpu_Click(object sender, RoutedEventArgs e)
        {
            int powerState = (int)CpuPowerSlider.Value;
            _cpu.SetThrottleLevel(powerState);
            SaveSettings();
            
            CpuStatusText.Text = $"CPU: {powerState}%";
            StatusText.Text = $"CPU Max State Capped at {powerState}%";
            StatusText.Foreground = WpfBrushes.Orange;
        }

        private void ApplyGpu_Click(object sender, RoutedEventArgs e)
        {
            int mhz = (int)FreqSlider.Value;
            _gpu.SetClockLimit(mhz);
            SaveSettings();
            
            GpuStatusText.Text = $"GPU: {mhz} MHz";
            StatusText.Text = "GPU Dynamic Range Active";
            StatusText.Foreground = WpfBrushes.LightGreen;
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            ResetHardware();
            if (File.Exists(_configPath)) File.Delete(_configPath);
            
            CpuPowerSlider.Value = 100;
            GpuStatusText.Text = "GPU: Default";
            CpuStatusText.Text = "CPU: 100% (Default)";
            StatusText.Text = "Factory Defaults Restored.";
            StatusText.Foreground = WpfBrushes.White;
        }

        private void FreqSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (SliderValueText != null) SliderValueText.Text = $"{(int)e.NewValue} MHz";
        }

        private void CpuSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (CpuSliderValueText != null) CpuSliderValueText.Text = $"{(int)e.NewValue} %";
        }
    }
}