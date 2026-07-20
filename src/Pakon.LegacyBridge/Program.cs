using System;

namespace Pakon.LegacyBridge
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            var pipeName = "PakonLegacyBridge";
            var once = false;
            for (var index = 0; index < args.Length; index++)
            {
                if (args[index] == "--pipe" && index + 1 < args.Length)
                {
                    pipeName = args[++index];
                }
                else if (args[index] == "--once")
                {
                    once = true;
                }
                else
                {
                    Console.Error.WriteLine("Usage: Pakon.LegacyBridge.exe [--pipe <name>] [--once]");
                    return 2;
                }
            }

            return new BridgeHost(pipeName, once).Run();
        }
    }
}
