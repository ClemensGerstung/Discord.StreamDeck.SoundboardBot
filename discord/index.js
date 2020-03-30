const messages = require('./soundboard_pb');
const services = require('./soundboard_grpc_pb');

const grpc = require('grpc');

const Discord = require("discord.js");
const { prefix, token } = require("./config.json");

const client = new Discord.Client();
const queue = new Map();

client.once("ready", async () => {
  console.log("Ready!");

  var user = await client.users.fetch("216314417913135105");
  const channels = client.channels.cache;
  // console.log(channel.members.some(member => member.user === user));

  channels.forEach(async channel => {
    if(channel.type === 'voice') {
      console.log("Voice Channel: " + channel.name);

      if(channel.members.some(member => member.user === user)) {
        console.log("User \""+user.username+"\" is in this channel");

        var connection = await channel.join();
        const dispatcher = connection.play("D:/temp/Streamdeck/Audiospur-3.mp3")
                                     .on("finish", () => { /* TODO: leave? */ })
                                     .on("error", error => console.error(error));
        dispatcher.setVolumeLogarithmic(1);
      }
    }
  });
});

client.once("reconnecting", () => {
  console.log("Reconnecting!");
});

client.once("disconnect", () => {
  console.log("Disconnect!");
});

client.on('voiceStateUpdate', async (oldMember, newMember) => {
  const newUserChannel = newMember.channel;

  console.log("New Channel: " + newMember.channel);
  console.log("Old Channel: " + oldMember.channel);

  if(newUserChannel !== null) {
    var connection = await newUserChannel.join();
    const dispatcher = connection.play("D:/temp/Streamdeck/Audiospur-3.mp3")
                                 .on("finish", () => { /* TODO: leave? */ })
                                 .on("error", error => console.error(error));
    dispatcher.setVolumeLogarithmic(1);

  } else {
    await oldMember.channel.leave();
  }
})

client.on("message", async message => {
  if (message.author.bot) return;
  if (!message.content.startsWith(prefix)) return;

  const serverQueue = queue.get(message.guild.id);

  if (message.content.startsWith(`${prefix}play`)) {
    execute(message, serverQueue);
  } else {
    message.channel.send("You need to enter a valid command!");
  }
});

async function execute(message, serverQueue) {
  const voiceChannel = message.member.voice.channel;
  if (!voiceChannel)
    return message.channel.send(
      "You need to be in a voice channel to play music!"
    );
  const permissions = voiceChannel.permissionsFor(message.client.user);
  if (!permissions.has("CONNECT") || !permissions.has("SPEAK")) {
    return message.channel.send(
      "I need the permissions to join and speak in your voice channel!"
    );
  }

  if (!serverQueue) {
    const queueContruct = {
      textChannel: message.channel,
      voiceChannel: voiceChannel,
      connection: null,
      volume: 5,
      playing: true
    };

    queue.set(message.guild.id, queueContruct);

    try {
      var connection = await voiceChannel.join();
      queueContruct.connection = connection;
      play(message.guild, "D:/temp/Streamdeck/Audiospur-3.mp3");
    } catch (err) {
      console.log(err);
      queue.delete(message.guild.id);
      return message.channel.send(err);
    }
  } else {
    return message.channel.send(`${song.title} has been added to the queue!`);
  }
}

function play(guild, song) {
  const serverQueue = queue.get(guild.id);
  if (!song) {
    serverQueue.voiceChannel.leave();
    queue.delete(guild.id);
    return;
  }

  const dispatcher = serverQueue.connection
    .play(song)
    .on("finish", () => { /* TODO: leave? */ })
    .on("error", error => console.error(error));
  dispatcher.setVolumeLogarithmic(serverQueue.volume / 5);
}

function PlaySong(call, callback) {
  var request = call.request;
  var fileName = request.getFileName();



  callback(null, new messages.PlaySongReply());
}

function ListSongs(call, callback) {
  var request = call.request;



  callback(null, new messages.ListSongsReply());
}

function JoinMe(call, callback) {
  var request = call.request;
  


  callback(null, new messages.JoinMeReply());
}

var server = new grpc.Server();
server.addService(services.SoundBoardService, { playSong: PlaySong, listSongs: ListSongs, joinMe: JoinMe });
server.bind('0.0.0.0:50051', grpc.ServerCredentials.createInsecure());
server.start();

client.login(token);
