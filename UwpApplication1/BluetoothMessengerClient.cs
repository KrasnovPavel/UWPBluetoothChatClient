using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.System.Threading;

namespace UwpApplication1
{
    public class BluetoothMessengerClient
    {
        private readonly DeviceWatcher _deviceWatcher;
        private BluetoothDevice _bluetoothDevice;
        private RfcommDeviceService _chatService;
        private StreamSocket _chatSocket;
        private DataWriter _chatWriter;
        private DataReader _chatReader;
        private bool _isDisconnectedByUser;

        protected static readonly Guid RFCOMMChatServiceUuid = Guid.Parse("34B1CF4D-1069-4AD6-89B6-E161D79BE4D8");

        public delegate void EnumerationCompletedDel(List<DeviceInformation> devices);

        public delegate void MessageReceivedDel(string message);

        public delegate void ConnectedToServerDel(Exception exception);

        public delegate void DisconnectedDel(string reason);

        public EnumerationCompletedDel EnumerationCompleted;
        public MessageReceivedDel MessageReceived;
        public ConnectedToServerDel ConnectedToServer;
        public DisconnectedDel Disconnected;

        public List<DeviceInformation> Devices { get; }
        public bool IsConnected { get; private set; }

        public BluetoothMessengerClient()
        {
            Devices = new List<DeviceInformation>();
            string[] requestedProperties = { "System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected" };
            _deviceWatcher = DeviceInformation.CreateWatcher(BluetoothDevice.GetDeviceSelector(),
                                                             requestedProperties,
                                                             DeviceInformationKind.AssociationEndpoint);
            
            // Hook up handlers for the watcher events before starting the watcher
            _deviceWatcher.Added += AddDevice;
            _deviceWatcher.Updated += UpdateDevice;
            _deviceWatcher.EnumerationCompleted += DeviceWatcherOnEnumerationCompleted;
            _deviceWatcher.Removed += RemoveDevice;
        }

        public void FindDevices()
        {
            Devices.Clear();
            if (_deviceWatcher.Status == DeviceWatcherStatus.Stopped
                || _deviceWatcher.Status == DeviceWatcherStatus.Created
                || _deviceWatcher.Status == DeviceWatcherStatus.Aborted)
            {
                _deviceWatcher.Start();   
            }
        }

        public async void Disconnect(string reason = "Disconnected by user")
        {
            if (reason == "Disconnected by user")
            {
                _isDisconnectedByUser = true;
            }
            IsConnected = false;
            if (_chatWriter != null)
            {
                _chatWriter.DetachStream();
                _chatWriter = null;
            }
            
            if (_chatService != null)
            {
                _chatService.Dispose();
                _chatService = null;
            }

            if (_chatSocket != null)
            {
                _chatSocket.Dispose();
                _chatSocket = null;
            }

            Disconnected(reason);
        }

        public async void ConnectToDevice(DeviceInformation deviceInformation)
        {
            if (IsConnected)
            {
                ConnectedToServer(new Exception("Already connected"));
                return;
            }
            
            // Проверка доступности
            DeviceAccessStatus accessStatus = DeviceAccessInformation.CreateFromId(deviceInformation.Id).CurrentStatus;
            if (accessStatus != DeviceAccessStatus.Allowed) {
                ConnectedToServer(new Exception("Access status isn't Allowed but " + accessStatus));
                return;
            }
            
            try
            {
                _bluetoothDevice = await BluetoothDevice.FromIdAsync(deviceInformation.Id);
                if (_bluetoothDevice == null)
                {
                    ConnectedToServer(new Exception("Device not found"));
                    return;
                }
                
                // Получение доступных сервисов на устройстве
                RfcommDeviceServicesResult rfcommServices = await _bluetoothDevice.GetRfcommServicesForIdAsync(
                    RfcommServiceId.FromUuid(RFCOMMChatServiceUuid), BluetoothCacheMode.Uncached);
                if (rfcommServices.Services.Count == 0)
                {
                    ConnectedToServer(new Exception("Chat service not found on remove device"));
                    return;
                
                }
                _chatService = rfcommServices.Services[0];
                
                _chatSocket = new StreamSocket();
                
                await _chatSocket.ConnectAsync(_chatService.ConnectionHostName, _chatService.ConnectionServiceName);

                _chatWriter = new DataWriter(_chatSocket.OutputStream);
                _chatReader = new DataReader(_chatSocket.InputStream);
                
                IsConnected = true;
                ConnectedToServer(null);
            
                ReceiveStringLoop();
            }
            catch (Exception ex)
            {
                ConnectedToServer(new Exception("Connection failed with exception: " + ex.Message));
            }
        }

        public async void SendMessage(string message)
        {
            try
            {
                if (message.Length == 0) return;

                _chatWriter.WriteString(message);

                await _chatWriter.StoreAsync();
            }
            catch (Exception ex)
            {
                Disconnect("Exception on message sending: " + ex.Message);
            }
        }

        private void AddDevice(DeviceWatcher watcher, DeviceInformation deviceInfo)
        {
            if (deviceInfo.Name == "") return;

            foreach (DeviceInformation device in Devices)
            {
                if (device.Id == deviceInfo.Id) return;
            }

            Devices.Add(deviceInfo);
        }

        private void DeviceWatcherOnEnumerationCompleted(DeviceWatcher sender, object args)
        {
            EnumerationCompleted(Devices);
            _deviceWatcher.Stop();
        }

        private void UpdateDevice(DeviceWatcher sender, DeviceInformationUpdate args)
        {
            foreach (DeviceInformation device in Devices)
            {
                if (device.Id != args.Id) continue;
                
                device.Update(args);
                break;
            }
        }

        private void RemoveDevice(DeviceWatcher watcher, DeviceInformationUpdate deviceInfoUpdate)
        {
            foreach (DeviceInformation deviceInfo in Devices)
            {
                if (deviceInfo.Id != deviceInfoUpdate.Id) continue;
                    
                Devices.Remove(deviceInfo);
                break;
            }
        }

        private async void ReceiveStringLoop()
        {
            while (IsConnected)
            {
                try
                {
                    if (await _chatReader.LoadAsync(sizeof(byte)) < sizeof(byte))
                    {
                        Disconnect("Remote device terminated connection");
                        return;
                    }

                    uint length = _chatReader.ReadByte();

                    await _chatReader.LoadAsync(length);
                    string data = _chatReader.ReadString(length);
                    if (length != data.Length)
                    {
                        Disconnect("Remote device terminated connection in the middle of transition");
                        return;
                    }

                    MessageReceived(data);
                }
                catch (Exception ex)
                {
                    if (!_isDisconnectedByUser)
                    {
                        Disconnect("Reading stream failed with exception: " + ex.Message);
                        _isDisconnectedByUser = false;
                    }
                }
            }
        }
    }
}