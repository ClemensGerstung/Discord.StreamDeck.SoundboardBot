using CommandLine;
using log4net;
using System;
using System.Threading;

namespace Streamdeck
{
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
