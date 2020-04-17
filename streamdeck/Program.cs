using CommandLine;
using Grpc.Core;
using log4net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Soundboard;
using streamdeck_client_csharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.IO;

namespace Streamdeck
{
  public class Soundboard
  {
    private const string PLAY_BUTTON_ID = "org.clemensgerstung.soundboard.play";

    private static readonly ILog __log = LogManager.GetLogger(typeof(Soundboard));

    private string _server;
    private int _port;
    private string _userId;
    private StreamDeckConnection _connection;
    private Channel _channel;
    private SoundBoard.SoundBoardClient _client;
    private ConcurrentDictionary<string, string> _songs;

    public bool IsRunning { get; private set; }

    public string[] Songs
    {
      get
      {
        var reply = Client.ListSongs(new ListSongsRequest());
        var songs = reply.Files
                      .ToArray();

        return songs;
      }
    }

    public SoundBoard.SoundBoardClient Client
    {
      get
      {
        if(_channel == null)
        {
          _channel = new Channel(_server, _port, ChannelCredentials.Insecure); 
        }

        if(_channel.State != ChannelState.Ready)
        {
          _channel.ConnectAsync().Wait();
        }

        if(_client == null)
        {
          _client = new SoundBoard.SoundBoardClient(_channel);
          _client.JoinMe(new JoinMeRequest { UserId = _userId });
        }

        return _client;
      }
    }

    public Soundboard(Options options)
    {
      _connection = new StreamDeckConnection(options.Port,
                                             options.PluginUUID,
                                             options.RegisterEvent);

      _connection.OnApplicationDidLaunch += OnStreamDeckLaunched;
      _connection.OnConnected += OnStreamDeckConnected;
      _connection.OnDeviceDidConnect += OnStreamDeckDeviceConnected;
      _connection.OnWillAppear += OnItemAppear;
      _connection.OnWillDisappear += OnItemDisappear;
      _connection.OnDeviceDidDisconnect += OnStreamDeckDeviceDisconnected;
      _connection.OnDisconnected += OnStreamDeckDisconnected;
      _connection.OnApplicationDidTerminate += OnStreamDeckTerminated;

      _connection.OnPropertyInspectorDidAppear += OnPropertyInspectorAppeared;
      _connection.OnDidReceiveSettings += OnReceiveSettings;
      _connection.OnDidReceiveGlobalSettings += OnReceiveGlobalSettings;
      _connection.OnKeyDown += OnKeyDown;

      _connection.Run();
      IsRunning = true;
    }

    private void OnReceiveGlobalSettings(object sender, StreamDeckEventReceivedEventArgs<streamdeck_client_csharp.Events.DidReceiveGlobalSettingsEvent> e)
    {
      var settings = e.Event.Payload.Settings;
      __log.Debug(settings.ToString());

      _server = settings["server"].Value<string>();
      _port = int.Parse(settings["port"].Value<string>());
      _userId = settings["user"].Value<string>();
    }

    private void OnKeyDown(object sender, StreamDeckEventReceivedEventArgs<streamdeck_client_csharp.Events.KeyDownEvent> e)
    {
      try
      {
        string context = e.Event.Context;

        if (e.Event.Action == PLAY_BUTTON_ID)
        {
          if (_songs.TryGetValue(context, out string value))
          {
            Client.PlaySong(new PlaySongRequest { FileName = value });
          }
        }
      }
      catch (Exception ex)
      {
        __log.Fatal(ex);
      }
    }

    private void OnPropertyInspectorAppeared(object sender, StreamDeckEventReceivedEventArgs<streamdeck_client_csharp.Events.PropertyInspectorDidAppearEvent> e)
    {
      try
      {
        // TODO: use SendToPropertyInspector instead of SetSettings
        __log.DebugFormat("{0} {1} {2} {3}", e.Event.Device, e.Event.Context, e.Event.Action, e.Event.Event);
        string context = e.Event.Context;
        JObject payload = new JObject();
        JArray array = new JArray(Songs);
        payload.Add("songs", array);

        if (_songs.TryGetValue(context, out string value))
        {
          payload.Add("soundfile", value);
        }

        __log.DebugFormat("Sending Songs {0}", payload);
        Task task = _connection.SetSettingsAsync(payload, context);

        var listUsersResponse = Client.ListUsers(new ListUsersRequest { OnlyOnline = true });
        var usersPayload = new JObject();
        var users = new JArray();

        foreach (var user in listUsersResponse.Users)
        {
          JObject u = new JObject();
          u["id"] = user.Id;
          u["name"] = user.Name;

          users.Add(u);
        }

        usersPayload.Add("users", users);

        __log.DebugFormat("Sending Users {0}", usersPayload);

        var t1 = _connection.SendToPropertyInspectorAsync(PLAY_BUTTON_ID, usersPayload, context);
        Task.WaitAll(task, t1);
      }
      catch (Exception ex)
      {
        __log.Fatal(ex);
      }
    }

    private void OnReceiveSettings(object sender, StreamDeckEventReceivedEventArgs<streamdeck_client_csharp.Events.DidReceiveSettingsEvent> e)
    {
      string context = e.Event.Context;
      JObject settings = e.Event.Payload.Settings;
      string soundFile = settings["soundfile"].Value<string>();

      __log.DebugFormat("{0} {1} {2}", e.Event.Device, e.Event.Action, context);
      __log.DebugFormat("{0}", settings);

      _songs.AddOrUpdate(context, soundFile, (key, old) => soundFile);
    }

    private void OnStreamDeckTerminated(object sender, StreamDeckEventReceivedEventArgs<streamdeck_client_csharp.Events.ApplicationDidTerminateEvent> e)
    {
      __log.DebugFormat("{0}", e.Event.Payload.Application);
      IsRunning = false;
    }

    private void OnStreamDeckDisconnected(object sender, EventArgs e)
    {
      __log.Debug("OnStreamDeckDisconnected");

      if (_songs != null)
      {
        string json = JsonConvert.SerializeObject(_songs);
        File.WriteAllText("songs.json", json);
      }
    }

    private void OnStreamDeckDeviceDisconnected(object sender, StreamDeckEventReceivedEventArgs<streamdeck_client_csharp.Events.DeviceDidDisconnectEvent> e)
    {
      __log.DebugFormat("{0}", e.Event.Device);
    }

    private void OnItemDisappear(object sender, StreamDeckEventReceivedEventArgs<streamdeck_client_csharp.Events.WillDisappearEvent> e)
    {
      __log.DebugFormat("{0} {1} {2}", e.Event.Device, e.Event.Action, e.Event.Context);
    }

    private void OnItemAppear(object sender, StreamDeckEventReceivedEventArgs<streamdeck_client_csharp.Events.WillAppearEvent> e)
    {
      __log.DebugFormat("{0} {1} {2}", e.Event.Device, e.Event.Action, e.Event.Context);
    }

    private void OnStreamDeckDeviceConnected(object sender, StreamDeckEventReceivedEventArgs<streamdeck_client_csharp.Events.DeviceDidConnectEvent> e)
    {
      __log.DebugFormat("{0}", e.Event.Device);
    }

    private void OnStreamDeckConnected(object sender, EventArgs e)
    {
      __log.Debug("OnStreamDeckConnected");

      try
      {
        _songs = new ConcurrentDictionary<string, string>(ReadJsonSettingsFromFile<Dictionary<string, string>>("songs.json"));
        var task = _connection.GetGlobalSettingsAsync();
        Task.WaitAll(task);
      }
      catch (Exception exception)
      {
        __log.Fatal(exception);
      }
    }

    private void OnStreamDeckLaunched(object sender, StreamDeckEventReceivedEventArgs<streamdeck_client_csharp.Events.ApplicationDidLaunchEvent> e)
    {
      __log.DebugFormat("{0}", e.Event.Payload.Application);
    }

    private T ReadJsonSettingsFromFile<T>(string fileName)
      where T : class, new()
    {
      T settings = null;
      if (File.Exists(fileName))
      {
        string json = File.ReadAllText(fileName);
        settings = JsonConvert.DeserializeObject<T>(json);
      }

      return settings ?? new T();
    }
  }


  class Program
  {
    private static readonly ILog __log = LogManager.GetLogger(typeof(Program));

    static void Main(string[] args)
    {
      __log.Info("Start");
      __log.InfoFormat("Args: \"{0}\"", string.Join("\", \"", args));

      for (int count = 0; count < args.Length; count++)
      {
        if (args[count].StartsWith("-") && !args[count].StartsWith("--"))
        {
          args[count] = $"-{args[count]}";
        }
      }

      Parser parser = new Parser((with) =>
      {
        with.EnableDashDash = true;
        with.CaseInsensitiveEnumValues = true;
        with.CaseSensitive = false;
        with.IgnoreUnknownArguments = true;
        with.HelpWriter = Console.Error;
      });

      Soundboard soundboard = null;
      ParserResult<Options> result = parser.ParseArguments<Options>(args);
      if (result.Tag == ParserResultType.Parsed)
      {
        Parsed<Options> options = (Parsed<Options>)result;

        try
        {
          soundboard = new Soundboard(options.Value);

          while (soundboard.IsRunning) ;
        }
        catch (Exception e)
        {
          __log.Fatal(e.Message);
          __log.Fatal(e.StackTrace);
        }
      }
    }
  }
}
