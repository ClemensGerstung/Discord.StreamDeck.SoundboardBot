using Discord.Audio;
using Discord.WebSocket;
using Grpc.Core;
using log4net;
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
    private readonly static ILog __log = LogManager.GetLogger(typeof(SoundboardServerImpl));

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
          __log.InfoFormat("User \"{0}\" switched from \"{1}\" to \"{2}\"", user.Username, oldState.VoiceChannel?.Name, newState.VoiceChannel?.Name);
          _rejoinSignal.Set();
        }
      }

      return Task.CompletedTask;
    }

    public override Task<JoinMeReply> JoinMe(JoinMeRequest request, ServerCallContext context)
    {
      __log.Debug("\"JoinMe\" Request received");

      if (ulong.TryParse(request.UserId, out ulong userId) &&
          _currentUser == null)
      {
        var guild = _client.GetGuild(_guildId);
        _currentUser = guild.GetUser(userId);

        __log.InfoFormat("Joined User with Id {0}", userId);
        _rejoinSignal.Set();
      }

      return Task.FromResult(new JoinMeReply());
    }

    public override Task<ListSongsReply> ListSongs(ListSongsRequest request, ServerCallContext context)
    {
      __log.Debug("\"ListSongs\" Request received");

      DirectoryInfo info = new DirectoryInfo(_soundPath);
      ListSongsReply reply = new ListSongsReply();

      __log.DebugFormat("Iterate over files in folder \"{0}\"", _soundPath);
      foreach (var file in info.GetFiles())
      {
        __log.DebugFormat("Add File \"{0}\"", file.Name);
        reply.Files.Add(file.Name);
      }

      __log.DebugFormat("Found {0} files in folder \"{1}\"", reply.Files.Count, _soundPath);
      return Task.FromResult(reply);
    }

    public override async Task<ListUsersReply> ListUsers(ListUsersRequest request, ServerCallContext context)
    {
      __log.Debug("\"ListUsers\" Request received");

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
        __log.DebugFormat("Check user \"{0}\"", user.Username);
        if (request.OnlyOnline &&
          user.Status != UserStatus.Online)
        {
          __log.DebugFormat("User \"{0}\" not online", user.Username);
          continue;
        }

        if (!regex.IsMatch(user.Username))
        {
          __log.DebugFormat("Username \"{0}\" does not match filter \"{1}\"", user.Username, filter);
          continue;
        }

        __log.DebugFormat("Add user \"{0}\" with Id {1}", user.Username, user.Id);
        User replyUser = new User();
        replyUser.Id = user.Id.ToString();
        replyUser.Name = user.Username;

        reply.Users.Add(replyUser);
      }

      __log.DebugFormat("Return {0} users", reply.Users.Count);
      return reply;
    }

    public override Task<PlaySongReply> PlaySong(PlaySongRequest request, ServerCallContext context)
    {
      __log.Debug("\"PlaySong\" Request received");

      _queue.Enqueue(request.FileName);
      _playSoundSignal.Set();

      __log.InfoFormat("Added file {0} (Total: {1})", request.FileName, _queue.Count);

      return Task.FromResult(new PlaySongReply());
    }

    public async Task Wait()
    {
      _runningSignal.Wait();
      __log.Info("Stop received");

      __log.Info("Wait for playing sound to finish");
      _playingSoundSignal.Wait();

      if (_currentChannel != null)
      {
        __log.Info("Disconnect from current VoiceChannel");
        await _currentChannel.DisconnectAsync();
      }

      __log.Info("Done, bye bye");
    }

    private void PlayThreadHandler(object unused)
    {
      do
      {
        __log.Debug("Wait for new sound to play");
        _playSoundSignal.Wait();
        __log.Debug("Received signal to play");

        if (_audioClient == null)
        {
          __log.Debug("AudioClient not available, reset");
          _playSoundSignal.Reset();
          continue;
        }

        _playingSoundSignal.Reset();
        using (var discord = _audioClient.CreatePCMStream(AudioApplication.Mixed))
        {
          do
          {
            if (_queue.TryDequeue(out string fileName))
            {
              __log.InfoFormat("Play file {0} (Total: {1})", fileName, _queue.Count);
              PlaySound(fileName, discord);
              __log.InfoFormat("Played file {0} (Remaining: {1})", fileName, _queue.Count);
            }
          }
          while (!_queue.IsEmpty);
        }

        _playingSoundSignal.Set();
        __log.Debug("Done playing, reset and wait");
        _playSoundSignal.Reset();
      }
      while (!_runningSignal.IsSet);

      __log.Warn("Exit Signal set");
    }

    private void RejoinVoiceChannel(object unused)
    {
      do
      {
        __log.Debug("Wait to follow user");
        _rejoinSignal.Wait();
        __log.Debug("Rejoin Signal received");
        __log.Debug("Wait for current sound to finish");
        _playingSoundSignal.Wait();
        __log.Debug("Done playing sound");
        __log.Debug("Reset rejoining signal");
        _rejoiningSignal.Reset();

        Task diconnectingTask = Task.Delay(0);
        if (_currentChannel != null)
        {
          __log.InfoFormat("Leave old channel \"{0}\"", _currentChannel.Name);
          diconnectingTask = _currentChannel.DisconnectAsync();
        }
        diconnectingTask.Wait();
        __log.Info("Left old channel");

        _currentChannel = _currentUser.VoiceChannel;
        __log.InfoFormat("Join current channel \"{0}\"", _currentChannel.Name);
        var audioClientTask = _currentChannel.ConnectAsync();
        audioClientTask.Wait();
        _audioClient = audioClientTask.Result;
        __log.Info("Joined new channel");

        __log.Debug("Set rejoining signal");
        _rejoiningSignal.Set();
        _rejoinSignal.Reset();

        if (!_queue.IsEmpty)
        {
          __log.Debug("Set singal to play sound");
          _playSoundSignal.Set();
        }
      }
      while (!_runningSignal.IsSet);

      __log.Warn("Exit Signal set");
    }

    private void PlaySound(string fileName, Stream destination)
    {
      __log.InfoFormat("Play Sound \"{0}\"", fileName);

      string path = Path.Combine(_soundPath, fileName);
      var psi = new ProcessStartInfo
      {
        FileName = "ffmpeg",
        Arguments = $"-hide_banner -loglevel panic -i \"{path}\" -ac 2 -f s16le -ar 48000 pipe:1",
        UseShellExecute = false,
        RedirectStandardOutput = true,
      };

      using (var ffmpeg = new Process())
      {
        ffmpeg.StartInfo = psi;
        ffmpeg.PriorityClass = ProcessPriorityClass.RealTime;
        ffmpeg.Start();

        using (var output = ffmpeg.StandardOutput.BaseStream)
        using (MemoryStream buffer = new MemoryStream())
        {
          Stream stream = output;
          if (_prebuf)
          {
            __log.Debug("Prebuffer");
            output.CopyTo(buffer);
            buffer.Seek(0, SeekOrigin.Begin);
            stream = buffer;
          }

          try
          {
            stream.CopyTo(destination);
          }
          catch (Exception e)
          {
            __log.ErrorFormat("Exception while playing sound:\n{0}", e);
          }
          finally
          {
            destination.Flush();
            ffmpeg.WaitForExit();
          }
        }
      }
    }
  }
}
