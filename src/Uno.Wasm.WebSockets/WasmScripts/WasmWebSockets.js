define([""], () => {
});

class ActiveSocket {
    constructor(handle, socket) {
        this.handle = handle;
        this.socket = socket;
    }
}

var WebSocketInterop = {

    activeSockets: {},
    debug: false,

    connect: function (handle, url) {
        this.ensureInitialized();

        if (this.debug) console.log("WebSocketInterop: connect " + url);

        var webSocket = new WebSocket(url);

        webSocket.onopen = function () {
            if (this.debug) console.log(`Socket is opened [${webSocket.protocol}] ${WebSocketInterop.dispatchConnectedMethod}`);

            var handleStr = MonoRuntime.mono_string(String(handle));
            var protocolStr = webSocket.protocol.length !== 0 ? MonoRuntime.mono_string(webSocket.protocol) : null;
            MonoRuntime.call_method(WebSocketInterop.dispatchConnectedMethod, null, [handleStr, protocolStr]);
        };

        webSocket.onerror = function (evt) {
            var handleStr = MonoRuntime.mono_string(String(handle));
            var errorStr = MonoRuntime.mono_string(String(evt.error));
            MonoRuntime.call_method(WebSocketInterop.dispatchErrorMethod, null, [handleStr, errorStr]);
        };

        webSocket.onclose = function (evt) {
            var handleStr = MonoRuntime.mono_string(String(handle));
            MonoRuntime.call_method(WebSocketInterop.dispatchCloseMethod, null, [handleStr, webSocket.readyState]);
        };

        webSocket.onmessage = function (evt) {
            var msg = evt.data;

            if (msg instanceof Blob) {
                if (this.debug) console.log(`Received Blob`);

                reader = new FileReader();

                reader.onload = e => {
                    var result = e.target.result;
                    if (result !== null) {
                        var arraySize = result.byteLength;

                        if (this.debug) console.log(`Result: ${result} / ${arraySize}`);

                        var ptr = Module._malloc(arraySize);
                        try {
                            // This section is not particularly efficient, as the received message is copied twice,
                            // once at the ptr location, then again in the _mono_wasm_typed_array_new method.
                            // There should be a better way to do this.

                            writeArrayToMemory(new Int8Array(result), ptr);
                            
                            var array = Module._mono_wasm_typed_array_new(ptr, arraySize, 1, 11 /*MARSHAL_ARRAY_BYTE*/);

                            var handleStr = MonoRuntime.mono_string(String(handle));
                            MonoRuntime.call_method(WebSocketInterop.dispatchReceivedBinaryMethod, null, [handleStr, array]);
                        }
                        finally {
                            Module._free(ptr);
                        }
                    }
                    else {
                        if (this.debug) console.error(`empty blob ? ${msg}`);
                    }
                };

                reader.readAsArrayBuffer(msg);
            }
            else {
                if (this.debug) console.log(`Received message ${msg}`);
            }
        };

        this.activeSockets[handle] = new ActiveSocket(handle, webSocket);
    },

    close: function (handle, code, statusDescription) {
        this.getActiveSocket(handle).close(code, statusDescription);

        delete this.activeSockets[handle];
    },

    send: function (handle, pData, count, offset) {
        var data = new ArrayBuffer(count);
        var bytes = new Int8Array(data);

        for (var i = 0; i < count; i++) {
            bytes[i] = Module.HEAPU8[pData + i + offset];
        }

        this.activeSockets[handle].socket.send(data);
    },

    getActiveSocket: function (handle) {

        var activeSocket = this.activeSockets[handle];

        if (activeSocket === null) {
            throw `Unknown WasmWebSocket instance ${handle}`;
        }

        return activeSocket.socket;
    },

    ensureInitialized: function () {

        if (WebSocketInterop.asm === undefined) {
            WebSocketInterop.asm = MonoRuntime.assembly_load("Uno.Wasm.WebSockets");
            WebSocketInterop.wasmWebSocketClass = MonoRuntime.find_class(WebSocketInterop.asm, "Uno.Wasm.WebSockets", "WasmWebSocket");

            WebSocketInterop.dispatchConnectedMethod = WebSocketInterop.findMonoMethod(WebSocketInterop.wasmWebSocketClass, "DispatchConnected");
            WebSocketInterop.dispatchMessageMethod = WebSocketInterop.findMonoMethod(WebSocketInterop.wasmWebSocketClass, "DispatchMessage");
            WebSocketInterop.dispatchErrorMethod = WebSocketInterop.findMonoMethod(WebSocketInterop.wasmWebSocketClass, "DispatchError");
            WebSocketInterop.dispatchReceivedBinaryMethod = WebSocketInterop.findMonoMethod(WebSocketInterop.wasmWebSocketClass, "DispatchReceivedBinary");
            WebSocketInterop.dispatchCloseMethod = WebSocketInterop.findMonoMethod(WebSocketInterop.wasmWebSocketClass, "DispatchClose");
        }
    },

    findMonoMethod: function (klass, methodName) {
        var method = MonoRuntime.find_method(klass, methodName, -1);

        if (method === null) {
            throw `Unable to find managed method ${methodName}`;
        }

        return method;
    }
};