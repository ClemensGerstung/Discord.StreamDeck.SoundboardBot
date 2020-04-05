let websocket = null;
let uuid = null;
let actionInfo = {};

function connectElgatoStreamDeckSocket(inPort, inUUID, inRegisterEvent, inInfo, inActionInfo) {
    uuid = inUUID;
    actionInfo = JSON.parse(inActionInfo);
    websocket = new WebSocket('ws://localhost:' + inPort);

    websocket.onopen = function () {
        let json = {
            event: inRegisterEvent,
            uuid: inUUID
        };
        websocket.send(JSON.stringify(json));
    }

    websocket.onmessage = onReceiveWebSocketMessage;
}

function onReceiveWebSocketMessage(msgStr) {
    let message = JSON.parse(msgStr.data);
    switch (message.event) {
        case 'didReceiveSettings':
            receivedSettings(message.payload);
            break;
        default:
            break;
    }
}

function sendValueToPlugin(value, param) {
    if (websocket) {
        const json = {
            "action": actionInfo['action'],
            "event": "setSettings",
            "context": uuid,
            "payload": {
                [param]: value
            }
        };
        websocket.send(JSON.stringify(json));
    }
}
