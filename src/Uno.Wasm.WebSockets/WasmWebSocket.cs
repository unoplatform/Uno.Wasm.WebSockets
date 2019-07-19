using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WebAssembly;

namespace Uno.Wasm.WebSockets
{
    public class WasmWebSocket : WebSocket
    {
        private GCHandle _handle;
        private static Dictionary<IntPtr, WasmWebSocket> _socket = new Dictionary<IntPtr, WasmWebSocket>();
        private Queue<ServerMessage> _messages = new Queue<ServerMessage>();
        private ArraySegment<byte> _pendingSegment;
        private WebSocketState _state;
        private string _subProtocol;

        private TaskCompletionSource<bool> _pendingConnect;
        private TaskCompletionSource<WebSocketReceiveResult> _pendingReceive;
        private TaskCompletionSource<bool> _pendingClose;
        private WebSocketCloseStatus? _closeStatus;
        private string _closeStatusDescription;

        public WasmWebSocket()
        {
            _handle = GCHandle.Alloc(this);
            _socket[(IntPtr)_handle] = this;
        }

        public override WebSocketState State => _state;

        public async Task ConnectAsync(Uri uri, CancellationToken token)
        {
            await Task.Yield();

            if (_pendingConnect != null)
            {
                _pendingConnect.TrySetCanceled();
            }

            _pendingConnect = new TaskCompletionSource<bool>();

            _state = WebSocketState.Connecting;

            Debug.WriteLine($"Connecting to {uri}");

            var result = Runtime.InvokeJS($"WebSocketInterop.connect({GetHandle()}, \"{uri.OriginalString}\")", out var exception);

            if (exception != 0)
            {
                _state = WebSocketState.None;

                Debug.WriteLine($"failed to execute {result}");
                throw new WebSocketException(WebSocketError.Faulted);
            }

            await _pendingConnect.Task;
        }

        public override void Dispose()
        {
            Abort();
        }

        public override async Task SendAsync(ArraySegment<byte> segment, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            await Task.Yield();

            Debug.WriteLine($"SendAsync {segment.Count} bytes, {messageType}, endOfMessage: {endOfMessage}");

            if (messageType == WebSocketMessageType.Binary)
            {
                var gch = GCHandle.Alloc(segment.Array, GCHandleType.Pinned);
                var pinnedData = gch.AddrOfPinnedObject();

                try
                {
                    var str = $"WebSocketInterop.send({GetHandle()}, {pinnedData}, {segment.Count}, {segment.Offset})";
                    var invocationResult = Runtime.InvokeJS(str, out var result);

                    if (result != 0)
                    {
                        Debug.WriteLine($"Eval failed {result} / {invocationResult}");
                        throw new WebSocketException(WebSocketError.Faulted, invocationResult);
                    }
                }
                finally
                {
                    gch.Free();
                }
            }
            else
            {
                throw new NotSupportedException($"Sending WebSocketMessageType {messageType} is not supported");
            }
        }

        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> arraySegment, CancellationToken none)
        {
            if (_messages.Count != 0)
            {
                return ProcessQueue(arraySegment);
            }
            else
            {
                _pendingSegment = arraySegment;
                _pendingReceive = new TaskCompletionSource<WebSocketReceiveResult>();

                return await _pendingReceive.Task;
            }
        }

        public override void Abort() {
            if (_state == WebSocketState.Open)
            {
                var unused = CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection was aborted", CancellationToken.None);
            }
        }

        public override async Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
        {
            await Task.Yield();

            _closeStatus = closeStatus;
            _closeStatusDescription = statusDescription;

            var str = $"WebSocketInterop.close({GetHandle()}, {(int)closeStatus}, \"{statusDescription}\")";
            var invocationResult = Runtime.InvokeJS(str, out var result);

            if (result != 0)
            {
                Debug.WriteLine($"Eval failed {result} / {invocationResult}");
                throw new WebSocketException(WebSocketError.Faulted, invocationResult);
            }
        }

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken) => throw new NotImplementedException();

        public override WebSocketCloseStatus? CloseStatus => _closeStatus;

        public override string CloseStatusDescription => _closeStatusDescription;

        public override string SubProtocol => _subProtocol;

        private WebSocketReceiveResult ProcessQueue(ArraySegment<byte> arraySegment)
        {
            var message = _messages.Peek();

            if (message.AvailableLength <= arraySegment.Count)
            {
                Buffer.BlockCopy(message.Data, message.Offset, arraySegment.Array, arraySegment.Offset, message.AvailableLength);

                _messages.Dequeue();

                return new WebSocketReceiveResult(message.Data.Length, WebSocketMessageType.Binary, true);
            }
            else
            {
                Buffer.BlockCopy(message.Data, message.Offset, arraySegment.Array, arraySegment.Offset, arraySegment.Count);

                message.SetReadBytes(arraySegment.Count);

                return new WebSocketReceiveResult(arraySegment.Count, WebSocketMessageType.Binary, false);
            }
        }

        internal static void DispatchError(string handleStr, string message)
            => GetWebSocket(handleStr).DispatchError(message);

        internal static void DispatchConnected(string handleStr, string subProtocol) 
            => GetWebSocket(handleStr).DispatchConnected(subProtocol);

        internal static void DispatchReceivedBinary(string handleStr, int pArray, int arraySize)
            => GetWebSocket(handleStr).DispatchReceivedBinary((IntPtr)pArray, arraySize);

        internal static void DispatchClosed(string handleStr, int state, string error)
            => GetWebSocket(handleStr).DispatchClosed(state, error);

        private static WasmWebSocket GetWebSocket(string handleString)
        {
            if (int.TryParse(handleString, out var handle))
            {
                if (_socket.TryGetValue((IntPtr)handle, out var socket))
                {
                    return socket;
                }
            }

            throw new InvalidOperationException($"Unknown WebSocket [{handleString}]");
        }

        private void DispatchReceivedBinary(IntPtr pArray, int arraySize)
        {
            Debug.WriteLine($"DispatchReceivedBinary {arraySize} bytes");

            var array = new byte[arraySize];
            Marshal.Copy(pArray, array, 0, arraySize);

            _messages.Enqueue(new ServerMessage(array));

            if (_pendingReceive != null)
            {
                _pendingReceive.TrySetResult(ProcessQueue(_pendingSegment));
                _pendingReceive = null;
            }
        }

        private void DispatchConnected(string subProtocol)
        {
            Debug.WriteLine($"Connected with {subProtocol}");

            if (_pendingConnect != null)
            {
                _subProtocol = subProtocol;
                _state = WebSocketState.Open;
                _pendingConnect.SetResult(true);
            }
        }

        private void DispatchError(string message)
        {
            Debug.WriteLine($"Connect Error: {message}");

            if (_pendingConnect != null)
            {
                _pendingConnect.SetException(new WebSocketException(WebSocketError.Faulted, message));
            }
        }

        private void DispatchClosed(int state, string error)
        {
            if(_pendingClose != null)
            {
                _state = MapJavascriptSocketState(state);
                _pendingClose.TrySetResult(true);
                _pendingClose = null;
            }
        }

        private WebSocketState MapJavascriptSocketState(int state)
        {
            switch (state)
            {
                case 0: // (CONNECTING)
                    return WebSocketState.Connecting;

                case 1: // (OPEN)
                    return WebSocketState.Open;

                case 2: // (CLOSING)
                    return WebSocketState.CloseSent;

                case 3: // (CLOSED)
                    return WebSocketState.Closed;

                default:
                    throw new InvalidOperationException($"Unknown Javascript WebSocket state {state}");
            }
        }

        private IntPtr GetHandle() => (IntPtr)_handle;

        class ServerMessage
		{
			public byte[] Data { get; }

			public int AvailableLength { get; private set; }
			public int Offset => Data.Length - AvailableLength;

			public void SetReadBytes(int readBytes) 
				=> AvailableLength -= readBytes;

			public ServerMessage(byte[] array)
			{
				this.Data = array;
				AvailableLength = Data.Length;
			}
		}
	}
}
