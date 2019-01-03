using System;
using System.Collections.Generic;
using Windows.Devices.Enumeration;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace UwpApplication1
{
    /// <inheritdoc />
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private readonly BluetoothMessengerClient _bluetoothMessengerClient;
        
        public MainPage()
        {
            InitializeComponent();
            
            _bluetoothMessengerClient = new BluetoothMessengerClient();

            _bluetoothMessengerClient.Disconnected += async delegate(string reason)
                {
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Low,
                        () => { InputBlock.Text += "Disconnected:" + reason; });
                };
            _bluetoothMessengerClient.EnumerationCompleted += async delegate(List<DeviceInformation> devices) 
                {
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Low,() =>
                    {
                        DeviceNamesBlock.Text = "";
                        foreach (DeviceInformation device in devices)
                        {
                            DeviceNamesBlock.Text += device.Name + " ";
                        }
                    });
                    
                };
            _bluetoothMessengerClient.ConnectedToServer += async delegate(Exception exception)
                {
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Low,
                        () => { InputBlock.Text = exception == null ? "Connected" : exception.Message; });
                };
            _bluetoothMessengerClient.MessageReceived += async delegate(string message)
                {
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Low,
                        () => { InputBlock.Text += " | " + message; });
                };
        }
       
        private void FindButton_Click(object sender, RoutedEventArgs e)
        {
            _bluetoothMessengerClient.FindDevices();
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_bluetoothMessengerClient.IsConnected) return;
            
            try
            {
                if (_bluetoothMessengerClient.Devices.Count < int.Parse(DeviceNumberBox.Text)) return;
                
                _bluetoothMessengerClient.ConnectToDevice(_bluetoothMessengerClient.Devices[int.Parse(DeviceNumberBox.Text)]);
            }
            catch (Exception ex)
            {
                InputBlock.Text = ex.Message;
            }
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            _bluetoothMessengerClient.Disconnect();
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            _bluetoothMessengerClient.SendMessage(OutputBox.Text);
            OutputBox.Text = "";
        }
    }
}
