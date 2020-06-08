using CommandLine;

namespace Discord
{
  public class Options
  {
    [Option('d', "discord", Required = true, HelpText = "Discord Bot Token")]
    public string DiscordToken { get; set; }

    [Option('p', "path", Required = true, HelpText = "Path to the folder where all soundfiles are")]
    public string SoundPath { get; set; }

    [Option('g', "guild", Required = true, HelpText = "GuildId to join")]
    public ulong GuildId { get; set; }

    [Option('l', "log4net", Required = false, Default = "log4net.config", HelpText = "Path to the log4net configuration file")]
    public string Log4NetConfig { get; set; }

    [Option('b', "buffer", Required = false, Default = false, HelpText = "Flag if sound files should be loaded into memory before sending to discord")]
    public bool PreBuffer { get; set; }

    [Option('P', "port", Required = false, Default = 50051, HelpText = "Port to use to start the server")]
    public int Port { get; set; }
  }
}
