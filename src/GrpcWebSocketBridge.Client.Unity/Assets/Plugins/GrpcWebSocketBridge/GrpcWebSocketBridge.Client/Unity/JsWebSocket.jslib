var JsWebSocketLibrary = {
    $connections: [],
    $connectionsSequence: 0,

    /* int JsWebSocket_Init(string url, string subProtocol) */
    JsWebSocket_Init: function (url, subProtocol) {
        var connection = {
            id: connectionsSequence++,
            socket: null,
            url: UTF8ToString(url),
            subProtocol: UTF8ToString(subProtocol),
            onReceive: null,
            onClose: null,
            onConnected: null
        };

        connections.push(connection);
        return connections.length - 1;
    },

    /* void JsWebSocket_Connect(int handle) */
    JsWebSocket_Connect: function (handle) {
        var connection = connections[handle];

        connection.socket = new WebSocket(connections[handle].url, connections[handle].subProtocol);
        
        connection.socket.binaryType = 'arraybuffer';
        connection.socket.onopen = function (e) {
            dynCall('vi', connection.onConnected, [connection.id]);
        };
        connection.socket.onclose = function (e) {
            dynCall('viii', connection.onClose, [connection.id, e.code, e.wasClean ? 1 : 0]);
        };
        connection.socket.onmessage = function (e) {
            var buffer = _malloc(e.data.byteLength);
            HEAPU8.set(new Uint8Array(e.data), buffer);
            dynCall('viii', connection.onReceive, [connection.id, buffer, e.data.byteLength]);
            _free(buffer);
        };
    },

    /* void JsWebSocket_Send(int handle, byte[] bytes, int length) */
    JsWebSocket_Send: function (handle, bufferPtr, length) {
        connections[handle].socket.send(HEAP8.subarray(bufferPtr, bufferPtr + length));
    },

    /* void JsWebSocket_Close(int handle, int code, string reason) */
    JsWebSocket_Close: function (handle, code, reason) {
        connections[handle].socket.close(code, UTF8ToString(reason));
    },

    /* void JsWebSocket_RegisterReceiveCallback(int handle, Action<int, IntPtr, int> callback) */
    JsWebSocket_RegisterReceiveCallback: function (handle, callbackPtr) {
        connections[handle].onReceive = callbackPtr;
    },

    /* void JsWebSocket_RegisterOnCloseCallback(int handle, Action<int, int, int> callback) */
    JsWebSocket_RegisterOnCloseCallback: function (handle, callbackPtr) {
        connections[handle].onClose = callbackPtr;
    },

    /* void JsWebSocket_RegisterOnConnectedCallback(int handle, Action<int> callback) */
    JsWebSocket_RegisterOnConnectedCallback: function (handle, callbackPtr) {
        connections[handle].onConnected = callbackPtr;
    },

    /* void JsWebSocket_Dispose(int handle) */
    JsWebSocket_Dispose: function (handle) {
        delete connections[handle];
    }
};

autoAddDeps(JsWebSocketLibrary, '$connections');
autoAddDeps(JsWebSocketLibrary, '$connectionsSequence');
mergeInto(LibraryManager.library, JsWebSocketLibrary);