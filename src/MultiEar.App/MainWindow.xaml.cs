using System;
using Microsoft.UI.Xaml;
using MultiEar_App.Helpers;
using Windows.Graphics;

namespace MultiEar_App
{
    public sealed partial class MainWindow : Window
    {
        private readonly TrayIcon _trayIcon;

        public MainWindow()
        {
            InitializeComponent();

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            // Set compact desktop window size (380x640)
            AppWindow.Resize(new SizeInt32(380, 640));

            try
            {
                AppWindow.SetIcon("Assets/AppIcon.ico");
            }
            catch { }

            _trayIcon = new TrayIcon(this, "MultiEar — Bluetooth Audio Sync");
            _trayIcon.TrayClicked += OnTrayClicked;

            // System tray residency: closing window minimizes to tray instead of exiting
            AppWindow.Closing += OnWindowClosing;

            RootFrame.Navigate(typeof(MainPage));
        }

        private void OnTrayClicked()
        {
            _trayIcon.ShowWindow();
        }

        private void OnWindowClosing(object? sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
        {
            args.Cancel = true;
            _trayIcon.HideWindow();
        }

        public void ExitApplication()
        {
            _trayIcon.Dispose();
            Application.Current.Exit();
        }
    }
}
