﻿function receivedSettings(payload) {
    
}

function receivedGlobalSettings(payload) {
    let server = document.getElementById("server");
    let port = document.getElementById("port");
    let user = document.getElementById("user");

    if (payload.settings.server) {
        server.value = payload.settings.server;
    }
    if (payload.settings.port) {
        port.value = payload.settings.port;
    }
    if (payload.settings.user) {
        user.value = payload.settings.user;
    }
}

function onConnectClick() {
    let server = document.getElementById("server");
    let port = document.getElementById("port");
    let user = document.getElementById("user");

    var json = {
        "event": "setGlobalSettings",
        "context": uuid,
        "payload": {
            "server": server.value,
            "port": port.value,
            "user": user.value
        }
    };

    websocket.send(JSON.stringify(json));
}

function receivedPropertyInspectorValue(payload){
    let user = document.getElementById("user");
    let domItem = document.getElementById("soundfile-input");

    if (payload.songs) {
        payload.songs.forEach(function (item) {
            let option = document.createElement("option");
            option.text = item;
            domItem.add(option);
        });
    }

    if (payload.soundfile) {
        domItem.value = payload.soundfile;
    }

    if (payload.users) {
        payload.users.forEach(function (item) {
            let option = document.createElement("option");
            option.value = item.id;
            option.text = item.name;
            user.add(option);
        });

        let json = {
            "event": "getGlobalSettings",
            "context": uuid
        };

        websocket.send(JSON.stringify(json));
    }
}