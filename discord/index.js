const Discord = require("discord.js");
const { token, guildId, folder, proto } = require("./config.json");
const path = require('path');
const fs = require('fs');
const grpc = require('grpc');
const protoLoader = require('@grpc/proto-loader');
const packageDefinition = protoLoader.loadSync(
  path.join(__dirname, proto),
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
let voiceConnection = null;
let currentChannel = null;

client.once("ready", async () => {
  console.log("Ready!");

  guild = await client.guilds.resolve(guildId);

  console.log("Start gRPC server")
  var server = new grpc.Server();
  server.addService(soundboard.SoundBoard.service, { playSong: PlaySong, listSongs: ListSongs, joinMe: JoinMe, listUsers: ListUsers });
  server.bind('0.0.0.0:50051', grpc.ServerCredentials.createInsecure());
  server.start();
  console.log("Started gRPC server");
});

client.once("reconnecting", () => {
  console.log("Reconnecting!");
});

client.once("disconnect", () => {
  console.log("Disconnect!");
});

client.on('voiceStateUpdate', async (oldState, newState) => {
  try {
    if (user == null ||
        (oldState.channel != null &&
         newState.channel != null &&
         oldState.channelID == newState.channelID)) {
      return;
    }
   
    if (oldState.channel != null &&
      oldState.member.user.id == user.id) {
      console.log("leave old channel " + oldState.channel.name);
      await oldState.channel.leave();
      voiceConnection = null;
    }

    if (newState.channel != null &&
      newState.member.user.id == user.id) {
      console.log("join new channel " + newState.channel.name);
      currentChannel = newState.channel;
    }
  } catch (error) {
    console.log("Got error in client.on('voiceStateUpdate', ...)");
    console.log(error);
  }
})

async function PlaySong(call, callback) {
  try {
    var request = call.request;
    var fileName = path.join(folder, request.fileName);

    if (voiceConnection == null) {
      voiceConnection = await currentChannel.join();
    }

    await voiceConnection.play(fileName);

    callback(null, {});
  } catch (error) {
    console.log("Got error in PlaySong(..., ...)");
    console.log(error);
  }
}

function ListSongs(call, callback) {
  try {
    let res = fs.readdirSync(folder);
    callback(null, { files: res });
  } catch (error) {
    console.log("Got error in ListSongs(..., ...)");
    console.log(error);
  }
}

async function JoinMe(call, callback) {
  try {
    var request = call.request;

    if (user == null) {
      user = await guild.members.fetch(request.userId);
      console.log("Joined user " + user.displayName);

      if (user.voice != null &&
        user.voice.channel != null) {
          currentChannel = user.voice.channel;
      }
    }

    callback(null, {});
  } catch (error) {
    console.log("Got error in JoinMe(..., ...)");
    console.log(error);
  }
}

async function ListUsers(call, callback) {
  try {
    let online = call.request.onlyOnline;
    let nameFilter = call.request.filter;
    let members = await guild.members.fetch();
    let result = [];

    if (online) {
      members = members.filter(user => user.presence.status == "online");
    }

    if (nameFilter) {
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
  catch (error) {
    console.log("Got error in ListUsers(..., ...)");
    console.log(error);
  }
}

console.log("Logon to Discord")
client.login(token);
