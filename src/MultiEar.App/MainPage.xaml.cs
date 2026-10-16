using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MultiEar_App.ViewModels;
using NAudio.CoreAudioApi;

namespace MultiEar_App
{
    public sealed partial class MainPage : Page
    {
        public MainViewModel ViewModel { get; }

        public MainPage()
        {
            InitializeComponent();
            ViewModel = new MainViewModel(DispatcherQueue.GetForCurrentThread());
        }

        private void OnToggleMuteClick(object sender, RoutedEventArgs e)
        {
            ViewModel.ToggleMute();
        }

        private void OnDismissWarningClick(InfoBar sender, object args)
        {
            ViewModel.DismissWarning();
        }

        private void OnRemoveDeviceClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is DeviceItemViewModel item)
            {
                ViewModel.RemoveDevice(item);
            }
        }

        private void OnAddAvailableDeviceClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is MMDevice dev)
            {
                ViewModel.AddDevice(dev);
            }
        }
    }
}
