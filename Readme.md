# Notice

**This repository is now deprecated, as the support for `ClientWebSocket` is included in the .NET runtime support for WebAssembly.**

# Uno.Wasm.WebSockets

Uno.Wasm.WebSockets is a concrete implementation of the [System.Net.WebSocket](https://docs.microsoft.com/en-us/dotnet/api/system.net.websockets) 
class for WebAssembly, named `Uno.Wasm.WebSockets.WasmWebSocket`.

This package requires the use of the [Uno.Wasm.Bootstrap](https://www.nuget.org/packages/Uno.Wasm.Bootstrap) package to work properly.

## Sample applications

Try WebSockets from .NET code live here: https://websockets-wasm.platform.uno

The sample applications use the [Uno Platform](https://github.com/nventive/Uno) and are built to use WebSockets in
their respective platforms. The WebAssembly one uses `Uno.Wasm.WebSockets.WasmWebSocket`.

Open [`Uno.Wasm.WebSockets.sln`] to discover the sample, in the `WasmWebSocketsSample.Shared` project.

## How to use the WasmWebSocket class

Given a project that already references `Uno.Wasm.Bootstrap`, add the [`Uno.Wasm.WebSockets`](https://www.nuget.org/packages/Uno.Wasm.WebSockets) 
nuget package, and write the following:

```csharp
var ws = new Uno.Wasm.WebSockets.WasmWebSocket();

// Connect to a simple echo WebSocket server
await ws.ConnectAsync(new Uri("wss://echo.websocket.org"), CancellationToken.None);

Console.WriteLine("Program: Connected!");

// Send some data
var data = new ArraySegment<byte>(Encoding.UTF8.GetBytes("Hello websocket !"));
await ws.SendAsync(data, System.Net.WebSockets.WebSocketMessageType.Binary, false, CancellationToken.None);

Console.WriteLine("Program: Sent!");

// Read the echo back
var buffer = new byte[1024];
var received = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

var receivedString = Encoding.UTF8.GetString(buffer, 0, received.Count);
Console.WriteLine($"Received {received.Count} bytes: {receivedString}");
```
