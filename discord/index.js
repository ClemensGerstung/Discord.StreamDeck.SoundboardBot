const PROTO_PATH = __dirname + '/../soundboard.proto';

const fs = require('fs');
const grpc = require('grpc');
const protoLoader = require('@grpc/proto-loader');
var packageDefinition = protoLoader.loadSync(
  PROTO_PATH,
  {
    keepCase: true,
    longs: String,
    enums: String,
    defaults: true,
    oneofs: true
  });
var soundboard = grpc.loadPackageDefinition(packageDefinition).soundboard;

const Discord = require("discord.js");
const { token, guildId } = require("./config.json");

const client = new Discord.Client();
let user = null;
let guild = null;

client.once("ready", async () => {
  console.log("Ready!");

  guild = await client.guilds.resolve(guildId);
});

client.once("reconnecting", () => {
  console.log("Reconnecting!");
});

client.once("disconnect", () => {
  console.log("Disconnect!");
});

client.on('voiceStateUpdate', async (oldMember, newMember) => {
  if (user == null) {
    return;
  }

  const newUserChannel = newMember.channel;

  console.log("New Channel: " + newMember.channel);
  console.log("Old Channel: " + oldMember.channel);

  if (newUserChannel !== null) {
    await newUserChannel.join();
  } else {
    await oldMember.channel.leave();
  }
})

// async function execute(message, serverQueue) {
//   const voiceChannel = message.member.voice.channel;
//   if (!voiceChannel)
//     return message.channel.send(
//       "You need to be in a voice channel to play music!"
//     );
//   const permissions = voiceChannel.permissionsFor(message.client.user);
//   if (!permissions.has("CONNECT") || !permissions.has("SPEAK")) {
//     return message.channel.send(
//       "I need the permissions to join and speak in your voice channel!"
//     );
//   }
// }


async function PlaySong(call, callback) {
  var request = call.request;
  var fileName = "D:/temp/Streamdeck/" + request.fileName;

  if (user != null) {
    var channel = user.voice.channel;

    var connection = await channel.join();
    await connection.play(fileName);
  }

  callback(null, {});
}

function ListSongs(call, callback) {
  const testFolder = "D:/temp/Streamdeck/";

  let res = fs.readdirSync(testFolder);

  callback(null, { files: res });
}

async function JoinMe(call, callback) {
  var request = call.request;

  if (user == null) {
    user = await guild.members.fetch(request.userId);

    let bot = await guild.members.fetch(client.user.id);
    let name = user.displayName.replace(/\d+$/, "");

    if (name.endsWith("s") || name.endsWith("z")) {
      name += "' Office";
    }
    else {
      name += "'s Office";
    }

    await bot.edit({ nick: name });

    if (user.voice.channel != null) {
      await user.voice.channel.join();
    }
  }

  callback(null, {});
}

var server = new grpc.Server();
server.addService(soundboard.SoundBoard.service, { playSong: PlaySong, listSongs: ListSongs, joinMe: JoinMe });
server.bind('0.0.0.0:50051', grpc.ServerCredentials.createInsecure());
server.start();

client.login(token);
