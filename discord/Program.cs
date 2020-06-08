using CommandLine;
using Discord.WebSocket;
using Grpc.Core;
using log4net;
using log4net.Config;
using Soundboard;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace Discord
{
  class Program
  {
    private readonly static ILog __log = LogManager.GetLogger(typeof(Program));

    static async Task Main(string[] args)
    {
      Server server = null;
      ParserResult<Options> result = Parser.Default.ParseArguments<Options>(args);
      if (result.Tag == ParserResultType.Parsed)
      {
        Options options = ((Parsed<Options>)result).Value;

        var logRepository = LogManager.GetRepository(Assembly.GetEntryAssembly());
        XmlConfigurator.Configure(logRepository,
                                  new FileInfo(options.Log4NetConfig));

        try
        {
          DiscordSocketClient client = new DiscordSocketClient();
          client.Ready += DiscordReady;
          client.Log += Log;

          __log.Debug("Login and Start Discord Bot");
          await client.LoginAsync(TokenType.Bot, options.DiscordToken);
          await client.StartAsync();

          SoundboardServerImpl serviceImpl = new SoundboardServerImpl(client, options.SoundPath, options.GuildId, options.PreBuffer);
          server = new Server
          {
            Services = { SoundBoard.BindService(serviceImpl) },
            Ports = { new ServerPort("0.0.0.0", options.Port, ServerCredentials.Insecure) }
          };

          __log.Debug("Server running, Waiting...");
          await serviceImpl.Wait();
          __log.Debug("Server ended");

          __log.Debug("Shutdown server");
          await server.ShutdownAsync();

          __log.Debug("Stop and Logoff from Discord");
          await client.StopAsync();
          await client.LogoutAsync();
        }
        catch (Exception e)
        {
          __log.Fatal("Got Exception in Main");
          __log.Fatal(e);
          throw e;
        }
      }

      Task DiscordReady()
      {
        __log.Info("Start Server");
        server.Start();

        return Task.CompletedTask;
      }

      Task Log(LogMessage message)
      {
        __log.InfoFormat("Discord: {0}", message);
        return Task.CompletedTask;
      }
    }
  }
}
