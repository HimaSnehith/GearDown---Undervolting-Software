using System;
using System.Threading;
using System.Windows;

namespace GearDown
{
    public partial class App : Application
    {
        private static Mutex? _mutex = null;
        private static EventWaitHandle? _bringToFrontEvent = null;

        protected override void OnStartup(StartupEventArgs e)
        {
            const string appName = "GearDown_SingleInstance_Mutex_Lock";
            const string eventName = "GearDown_WakeUp_Bell";

            _mutex = new Mutex(true, appName, out bool createdNew);
            
            // Set up the "doorbell" event
            _bringToFrontEvent = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);

            if (!createdNew)
            {
                // App is already running! Ring the doorbell to wake up the first instance.
                _bringToFrontEvent.Set();
                
                // Silently kill this duplicate instance
                Application.Current.Shutdown();
                return;
            }

            // If this is the FIRST instance, start a background thread to listen for the doorbell
            Thread listenerThread = new Thread(() =>
            {
                while (true)
                {
                    // This thread pauses here forever until another instance rings the bell
                    _bringToFrontEvent.WaitOne(); 
                    
                    // Bell was rung! Tell the UI thread to bring the window back up
                    Current.Dispatcher.Invoke(() =>
                    {
                        var mainWindow = Current.MainWindow;
                        if (mainWindow != null)
                        {
                            mainWindow.Show();
                            mainWindow.WindowState = WindowState.Normal;
                            mainWindow.Activate(); // Brings it over other open apps
                        }
                    });
                }
            })
            {
                IsBackground = true // Ensures this thread dies when the app closes
            };
            listenerThread.Start();

            // Boot the app normally
            base.OnStartup(e);
        }
    }
}