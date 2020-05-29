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
      ParserResult<Options> result = Parser.Default.ParseArguments<Options>(args);
      if (result.Tag == ParserResultType.Parsed)
      {
        Options options = ((Parsed<Options>)result).Value;

        try
        {
          Server server = null;
          DiscordSocketClient client = new DiscordSocketClient();
          client.Ready += DiscordReady;

          await client.LoginAsync(TokenType.Bot, options.DiscordToken);
          await client.StartAsync();

          SoundboardServerImpl serviceImpl = new SoundboardServerImpl(client, options.SoundPath, options.GuildId, options.PreBuffer);
          server = new Server
          {
            Services = { SoundBoard.BindService(serviceImpl) },
            Ports = { new ServerPort("localhost", 50051, ServerCredentials.Insecure) }
          };

          Console.ReadLine();

          await server.ShutdownAsync();

          Task DiscordReady()
          {
            server.Start();

            return Task.CompletedTask;
          }
        }
        catch (Exception e)
        {
          //__log.Fatal(e.Message);
          //__log.Fatal(e.StackTrace);
        }
      }
    }
  }
}
