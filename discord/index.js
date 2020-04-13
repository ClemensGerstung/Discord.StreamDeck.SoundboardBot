const PROTO_PATH = __dirname + '/../soundboard.proto';

const Discord = require("discord.js");
const { token, guildId, folder } = require("./config.json");
const path = require('path');
const fs = require('fs');
const grpc = require('grpc');
const protoLoader = require('@grpc/proto-loader');
const packageDefinition = protoLoader.loadSync(
  PROTO_PATH,
  {
    keepCase: true,
    longs: String,
    enums: String,
    defaults: true,
    oneofs: true
  });
const soundboard = grpc.loadPackageDefinition(packageDefinition).soundboard;

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

client.on('voiceStateUpdate', async (oldState, newState) => {
  if (user == null) {
    return;
  }

  console.log(newState.member);
  console.log(user.id)
  console.log(oldState.member);

  // const newUserChannel = newState.channel;
  // if (newUserChannel !== null) {
  //   await newUserChannel.join();
  // } else {
  //   await oldState.channel.leave();
  // }
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
  var fileName = path.join(folder, request.fileName);

  if (user != null) {
    var channel = user.voice.channel;
    var connection = await channel.join();
    await connection.play(fileName);
  }

  callback(null, {});
}

function ListSongs(call, callback) {
  let res = fs.readdirSync(folder);
  callback(null, { files: res });
}

async function JoinMe(call, callback) {
  var request = call.request;

  if (user == null) {
    user = await guild.members.fetch(request.userId);

    if (user.voice != null && 
        user.voice.channel != null) {
      await user.voice.channel.join();
    }
  }

  callback(null, {});
}

async function ListUsers(call, callback)
{
  let online = call.request.onlyOnline;
  let nameFilter = call.request.filter;
  let members = await guild.members.fetch();
  let result = [];
  
  if(online) {
    members = members.filter(user => user.presence.status == "online");
  }

  if(nameFilter) {
    let regex = new RegExp(nameFilter, 'gi');
    members = members.filter(user => user.user.username.match(regex));
  }

  result = members.filter(member => !member.user.bot)
                  .map(member => {
                    let obj = {};
                    obj.id = member.id;
                    obj.name = member.user.username;
                    return obj;
                  });

  callback(null, { users: result });
}

var server = new grpc.Server();
server.addService(soundboard.SoundBoard.service, { playSong: PlaySong, listSongs: ListSongs, joinMe: JoinMe, listUsers: ListUsers });
server.bind('0.0.0.0:50051', grpc.ServerCredentials.createInsecure());
server.start();

client.login(token);
