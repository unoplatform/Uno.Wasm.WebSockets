using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

#if __WASM__
using ClientWebSocket = Uno.Wasm.WebSockets.WasmWebSocket;
#else
using ClientWebSocket = System.Net.WebSockets.ClientWebSocket;
#endif

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace WasmWebSocketsSample
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        ClientWebSocket _socket;
        CancellationTokenSource _cancellation;
        ObservableCollection<WebSocketMessage> _messages = new ObservableCollection<WebSocketMessage>();

        public MainPage()
        {
            this.InitializeComponent();

            UpdateControlStates();
        }

        private void UpdateControlStates()
        {
            switch(_socket?.State ?? WebSocketState.None)
            {
                case System.Net.WebSockets.WebSocketState.Open:
                    messageContent.IsEnabled = true;
                    sendButton.IsEnabled = true;
                    connectButton.Content = "Disconnect";
                    serverUrl.IsEnabled = false;
                    break;

                case System.Net.WebSockets.WebSocketState.None:
                    serverUrl.IsEnabled = true;
                    messageContent.IsEnabled = false;
                    sendButton.IsEnabled = false;
                    connectButton.Content = "Connect";
                    break;
            }
        }

        private async void OnSendMessage(object sender, RoutedEventArgs e)
        {
            if(_socket != null)
            {
                await AppendMessage($"Sending [{messageContent.Text}]");
                var buffer = Encoding.UTF8.GetBytes(messageContent.Text);
                await _socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Binary, true, _cancellation.Token);
                await AppendMessage($"Sent [{messageContent.Text}]");
            }
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {

            if (_socket == null)
            {
                _socket = new ClientWebSocket();
                _cancellation = new CancellationTokenSource();

                try
                {
                    await AppendMessage($"Connecting to {serverUrl.Text}");

                    UpdateControlStates();
                    await _socket.ConnectAsync(new Uri(serverUrl.Text), _cancellation.Token);

                    RunReceiveLoop();

                    UpdateControlStates();

                    await AppendMessage("Connected!");
                }
                catch (Exception ex)
                {
                    UpdateControlStates();

                    await AppendMessage($"Failed to connect {ex}");

                    _cancellation.Cancel();
                    _socket = null;
                }
            }
            else
            {

                await AppendMessage("Closing...");
                await _socket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "Done", _cancellation.Token);
                await AppendMessage("Closed!");
                _cancellation.Cancel();
                _socket = null;

                UpdateControlStates();
            }
        }

        private async void RunReceiveLoop()
        {
            var buffer = new ArraySegment<byte>(new byte[4096]);

            while (!_cancellation?.IsCancellationRequested ?? true)
            {
                try
                {
                    var r = await _socket.ReceiveAsync(buffer, _cancellation.Token);

                    await AppendMessage($"Received {r.MessageType}: [{Encoding.UTF8.GetString(buffer.Array, buffer.Offset, r.Count)}]");
                }
                catch(Exception e)
                {
                    if (!_cancellation.IsCancellationRequested)
                    {
                        await AppendMessage($"Failed to receive: {e}");
                        break;
                    }
                }
            }
        }

        private async Task AppendMessage(string v)
        {
            Console.WriteLine($"{v}");

            await Dispatcher.RunAsync(
                Windows.UI.Core.CoreDispatcherPriority.Normal, 
                () => logsTextBox.Text = $"{DateTime.Now}: {v}\r\n" + logsTextBox.Text
            );
        }
    }

    [Bindable]
    internal class WebSocketMessage
    {
        public string Message { get; internal set; }
        public string Timestamp { get; internal set; }
    }
}
