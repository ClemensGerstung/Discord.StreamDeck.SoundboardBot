using Discord.Audio;
using Discord.WebSocket;
using Grpc.Core;
using Soundboard;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Discord
{
  public class SoundboardServerImpl : SoundBoard.SoundBoardBase
  {
    private DiscordSocketClient _client;
    private string _soundPath;
    private ulong _guildId;
    private bool _prebuf;
    private IAudioClient _audioClient;
    private SocketUser _currentUser;
    private ConcurrentQueue<string> _queue;
    private ManualResetEventSlim _playSoundSignal;
    private ManualResetEventSlim _runningSignal;

    public SoundboardServerImpl(DiscordSocketClient client,
                                string soundPath,
                                ulong guildId,
                                bool prebuf = false)
    {
      _client = client ?? throw new ArgumentNullException(nameof(client));
      _soundPath = soundPath;
      _guildId = guildId;
      _prebuf = prebuf;
      _queue = new ConcurrentQueue<string>();
      _playSoundSignal = new ManualResetEventSlim(false);
      _runningSignal = new ManualResetEventSlim(false);

      

      //_client.VoiceServerUpdated

      ThreadPool.QueueUserWorkItem(PlayThreadHandler);
    }

    public override Task<JoinMeReply> JoinMe(JoinMeRequest request, ServerCallContext context)
    {
      if (ulong.TryParse(request.UserId, out ulong userId))
      {
        _currentUser = _client.GetUser(userId);
      }
      return Task.FromResult(new JoinMeReply());
    }

    public override Task<ListSongsReply> ListSongs(ListSongsRequest request, ServerCallContext context)
    {
      DirectoryInfo info = new DirectoryInfo(_soundPath);
      ListSongsReply reply = new ListSongsReply();

      foreach (var file in info.GetFiles())
      {
        reply.Files.Add(file.Name);
      }

      return Task.FromResult(reply);
    }

    public override async Task<ListUsersReply> ListUsers(ListUsersRequest request, ServerCallContext context)
    {
      string filter = request.Filter;
      ListUsersReply reply = new ListUsersReply();
      IGuild guild = _client.GetGuild(_guildId);
      var users = await guild.GetUsersAsync();

      if (!string.IsNullOrEmpty(filter))
      {
        filter = ".*";
      }

      Regex regex = new Regex(filter,
                                RegexOptions.IgnoreCase | RegexOptions.Compiled);

      foreach (IGuildUser user in users)
      {
        if(request.OnlyOnline &&
          user.Status != UserStatus.Online)
        {
          continue;
        }

        if(!regex.IsMatch(user.Username))
        {
          continue;
        }

        User replyUser = new User();
        replyUser.Id = user.Id.ToString();
        replyUser.Name = user.Username;

        reply.Users.Add(replyUser);
      }

      return reply;
    }

    public override Task<PlaySongReply> PlaySong(PlaySongRequest request, ServerCallContext context)
    {
      _queue.Enqueue(request.FileName);
      _playSoundSignal.Set();

      return Task.FromResult(new PlaySongReply());
    }

    public Task Wait()
    {
      _runningSignal.Wait();
      return Task.CompletedTask;
    }

    private void PlayThreadHandler(object unused)
    {
      while (true)
      {
        _playSoundSignal.Wait();

        if (_audioClient == null)
        {
          var guild = _client.GetGuild(_guildId);
          var user = guild.GetUser(_currentUser.Id);
          _audioClient = user.VoiceChannel
                                     .ConnectAsync()
                                     .GetAwaiter()
                                     .GetResult();
        }

        do
        {
          if (_queue.TryDequeue(out string fileName))
          {
            PlaySound(fileName);
          }
        }
        while (!_queue.IsEmpty);

        _playSoundSignal.Reset();
      }
    }

    private void PlaySound(string fileName)
    {
      string path = Path.Combine(_soundPath, fileName);
      var psi = new ProcessStartInfo
      {
        FileName = "ffmpeg",
        Arguments = $"-hide_banner -loglevel panic -i \"{path}\" -ac 2 -f s16le -ar 48000 pipe:1",
        UseShellExecute = false,
        RedirectStandardOutput = true,
      };

      using(var ffmpeg = Process.Start(psi))
      using(var output = ffmpeg.StandardOutput.BaseStream)
      using(MemoryStream buffer = new MemoryStream())
      using(var discord = _audioClient.CreatePCMStream(AudioApplication.Mixed))
      {
        Stream stream = output;
        if(_prebuf)
        {
          output.CopyTo(buffer);
          stream = buffer;
        }

        try { stream.CopyTo(discord); }
        finally { discord.Flush(); }
      }
    }
  }
}
