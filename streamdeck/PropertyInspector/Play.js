function receivedSettings(payload) {
    let domItem = document.getElementById("soundfile-input");

    if (payload.settings.songs) {
        payload.settings.songs.forEach(function (item) {
            let option = document.createElement("option");
            option.text = item;
            domItem.add(option);
        });
    }

    if (payload.settings.soundfile) {
        domItem.value = payload.settings.soundfile;
    }
}