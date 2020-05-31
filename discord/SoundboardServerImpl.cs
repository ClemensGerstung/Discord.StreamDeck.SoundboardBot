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
    private readonly DiscordSocketClient _client;
    private readonly string _soundPath;
    private readonly ulong _guildId;
    private readonly bool _prebuf;
    private IAudioClient _audioClient;
    private IGuildUser _currentUser;
    private IVoiceChannel _currentChannel;
    private readonly ConcurrentQueue<string> _queue;
    private readonly ManualResetEventSlim _playSoundSignal;
    private readonly ManualResetEventSlim _playingSoundSignal;
    private readonly ManualResetEventSlim _runningSignal;
    private readonly ManualResetEventSlim _rejoinSignal;
    private readonly ManualResetEventSlim _rejoiningSignal;

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
      _playingSoundSignal = new ManualResetEventSlim(true);
      _runningSignal = new ManualResetEventSlim(false);
      _rejoinSignal = new ManualResetEventSlim(false);
      _rejoiningSignal = new ManualResetEventSlim(true);

      _client.UserVoiceStateUpdated += OnUserVoiceStateUpdated;

      ThreadPool.QueueUserWorkItem(PlayThreadHandler);
      ThreadPool.QueueUserWorkItem(RejoinVoiceChannel);
    }

    private Task OnUserVoiceStateUpdated(SocketUser user, SocketVoiceState oldState, SocketVoiceState newState)
    {
      if (_currentUser?.Id == user.Id &&
        !Equals(oldState.VoiceChannel, 
                newState.VoiceChannel))
      {
        if (_rejoiningSignal.Wait(100))
        {
          Console.WriteLine("Rejoin");
          _rejoinSignal.Set();
        }
      }

      return Task.CompletedTask;
    }

    public override Task<JoinMeReply> JoinMe(JoinMeRequest request, ServerCallContext context)
    {
      if (ulong.TryParse(request.UserId, out ulong userId) &&
          _currentUser == null)
      {
        var guild = _client.GetGuild(_guildId);
        _currentUser = guild.GetUser(userId);

        Console.WriteLine("Join user with ID {0}", userId);
        _rejoinSignal.Set();
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
        if (request.OnlyOnline &&
          user.Status != UserStatus.Online)
        {
          continue;
        }

        if (!regex.IsMatch(user.Username))
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

      Console.WriteLine("Added file {0} (Total: {1})", request.FileName, _queue.Count);

      return Task.FromResult(new PlaySongReply());
    }

    public async Task Wait()
    {
      _runningSignal.Wait();
      _playingSoundSignal.Wait();

      if (_currentChannel != null)
      {
        await _currentChannel.DisconnectAsync();
      }
    }

    private void PlayThreadHandler(object unused)
    {
      do
      {
        _playSoundSignal.Wait();

        if (_audioClient == null)
        {
          _playSoundSignal.Reset();
          continue;
        }

        using (var discord = _audioClient.CreatePCMStream(AudioApplication.Mixed))
        {
          do
          {
            if (_queue.TryDequeue(out string fileName))
            {
              Console.WriteLine("Play file {0} (Total: {1})", fileName, _queue.Count);
              _playingSoundSignal.Reset();
              PlaySound(fileName, discord);
              _playingSoundSignal.Set();
              Console.WriteLine("Played file {0} (Remaining: {1})", fileName, _queue.Count);
            }
          }
          while (!_queue.IsEmpty);
        }

        _playSoundSignal.Reset();
      }
      while (!_runningSignal.IsSet);
    }

    private void RejoinVoiceChannel(object unused)
    {
      do
      {
        _rejoinSignal.Wait();
        _playingSoundSignal.Wait();
        _rejoiningSignal.Reset();

        Task diconnectingTask = Task.Delay(0);
        if (_currentChannel != null)
        {
          diconnectingTask = _currentChannel.DisconnectAsync();
        }
        diconnectingTask.Wait();

        _currentChannel = _currentUser.VoiceChannel;
        var audioClientTask = _currentChannel.ConnectAsync();
        audioClientTask.Wait();
        _audioClient = audioClientTask.Result;

        _rejoiningSignal.Set();
        _rejoinSignal.Reset();

        if (!_queue.IsEmpty)
        {
          _playSoundSignal.Set();
        }
      }
      while (!_runningSignal.IsSet);
    }

    private void PlaySound(string fileName, Stream destination)
    {
      string path = Path.Combine(_soundPath, fileName);
      var psi = new ProcessStartInfo
      {
        FileName = "ffmpeg",
        Arguments = $"-hide_banner -loglevel panic -i \"{path}\" -ac 2 -f s16le -ar 48000 pipe:1",
        UseShellExecute = false,
        RedirectStandardOutput = true,
      };

      using (var ffmpeg = Process.Start(psi))
      using (var output = ffmpeg.StandardOutput.BaseStream)
      using (MemoryStream buffer = new MemoryStream())
      {
        Stream stream = output;
        if (_prebuf)
        {
          output.CopyTo(buffer);
          buffer.Seek(0, SeekOrigin.Begin);
          stream = buffer;
        }

        try { stream.CopyTo(destination); }
        finally { destination.Flush(); }
      }
    }
  }
}
