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
    private static readonly ILog __log = LogManager.GetLogger(typeof(Soundboard));

    private StreamDeckConnection _connection;
    private SoundBoard.SoundBoardClient _client;
    private ConcurrentDictionary<string, string> _settings;

    public bool IsRunning { get; private set; }

    public string[] Songs
    {
      get
      {
        var reply = _client.ListSongs(new ListSongsRequest());
        var songs = reply.Files
                      .ToArray();

        return songs;
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
      _connection.OnKeyDown += OnKeyDown;

      _connection.Run();
      IsRunning = true;

      Channel channel = new Channel("127.0.0.1:50051", ChannelCredentials.Insecure);
      _client = new SoundBoard.SoundBoardClient(channel);
      _client.JoinMe(new JoinMeRequest { UserId = "" });
    }

    private void OnKeyDown(object sender, StreamDeckEventReceivedEventArgs<streamdeck_client_csharp.Events.KeyDownEvent> e)
    {
      string context = e.Event.Context;
      if (_settings.TryGetValue(context, out string value))
      {
        _client.PlaySong(new PlaySongRequest { FileName = value });
      }
    }

    private void OnPropertyInspectorAppeared(object sender, StreamDeckEventReceivedEventArgs<streamdeck_client_csharp.Events.PropertyInspectorDidAppearEvent> e)
    {
      __log.DebugFormat("{0} {1} {2} {3}", e.Event.Device, e.Event.Context, e.Event.Action, e.Event.Event);
      string context = e.Event.Context;
      JObject payload = new JObject();
      JArray array = new JArray(Songs);
      payload.Add("songs", array);

      if (_settings.TryGetValue(context, out string value))
      {
        payload.Add("soundfile", value);
      }

      __log.DebugFormat("Sending {0}", payload);

      Task task = _connection.SetSettingsAsync(payload, context);
      task.GetAwaiter()
          .GetResult();
    }

    private void OnReceiveSettings(object sender, StreamDeckEventReceivedEventArgs<streamdeck_client_csharp.Events.DidReceiveSettingsEvent> e)
    {
      string context = e.Event.Context;
      JObject settings = e.Event.Payload.Settings;
      string soundFile = settings["soundfile"].Value<string>();

      __log.DebugFormat("{0} {1} {2}", e.Event.Device, e.Event.Action, context);
      __log.DebugFormat("{0}", settings);

      _settings.AddOrUpdate(context, soundFile, (key, old) => soundFile);
    }

    private void OnStreamDeckTerminated(object sender, StreamDeckEventReceivedEventArgs<streamdeck_client_csharp.Events.ApplicationDidTerminateEvent> e)
    {
      __log.DebugFormat("{0}", e.Event.Payload.Application);
      IsRunning = false;
    }

    private void OnStreamDeckDisconnected(object sender, EventArgs e)
    {
      __log.Debug("OnStreamDeckDisconnected");

      if (_settings != null)
      {
        string json = JsonConvert.SerializeObject(_settings);
        File.WriteAllText("settings.json", json);
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
        if (!File.Exists("settings.json"))
        {
          _settings = new ConcurrentDictionary<string, string>();
          return;
        }

        string json = File.ReadAllText("settings.json");
        IDictionary<string, string> settings = JsonConvert.DeserializeObject<Dictionary<string, string>>(json) ?? (new Dictionary<string, string>());

        _settings = new ConcurrentDictionary<string, string>(settings);
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
      result.WithParsed(action);

      void action(Options options)
      {
        try
        {
          soundboard = new Soundboard(options);

          while (soundboard.IsRunning)
          {

          }
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
