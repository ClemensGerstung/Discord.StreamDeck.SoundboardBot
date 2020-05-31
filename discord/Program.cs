using CommandLine;
using Discord.WebSocket;
using Grpc.Core;
using Soundboard;
using System;
using System.Threading.Tasks;

namespace Discord
{
  class Program
  {
    static async Task Main(string[] args)
    {
      Server server = null;
      ParserResult<Options> result = Parser.Default.ParseArguments<Options>(args);
      if (result.Tag == ParserResultType.Parsed)
      {
        Options options = ((Parsed<Options>)result).Value;

        try
        {
          DiscordSocketClient client = new DiscordSocketClient();
          client.Ready += DiscordReady;
          client.Log += Log;

          await client.LoginAsync(TokenType.Bot, options.DiscordToken);
          await client.StartAsync();

          SoundboardServerImpl serviceImpl = new SoundboardServerImpl(client, options.SoundPath, options.GuildId, options.PreBuffer);
          server = new Server
          {
            Services = { SoundBoard.BindService(serviceImpl) },
            Ports = { new ServerPort("localhost", options.Port, ServerCredentials.Insecure) }
          };

          await serviceImpl.Wait();

          await server.ShutdownAsync();
          await client.StopAsync();
          await client.LogoutAsync();
        }
        catch (Exception e)
        {
          //__log.Fatal(e.Message);
          //__log.Fatal(e.StackTrace);
        }
      }

      Task DiscordReady()
      {
        server.Start();

        return Task.CompletedTask;
      }

      Task Log(LogMessage message)
      {
        Console.WriteLine(message);
        return Task.CompletedTask;
      }
    }
  }
}
