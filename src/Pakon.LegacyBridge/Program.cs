using System;
using System.Threading;

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

            var mutexName = @"Local\Pakon.LegacyBridge." + SanitizeMutexPart(pipeName);
            bool ownsMutex;
            using (var singleInstanceMutex = new Mutex(true, mutexName, out ownsMutex))
            {
                if (!ownsMutex)
                {
                    Console.WriteLine("A Pakon legacy bridge is already running on pipe '{0}'.", pipeName);
                    return 0;
                }

                return new BridgeHost(pipeName, once).Run();
            }
        }

        private static string SanitizeMutexPart(string value)
        {
            var characters = value.ToCharArray();
            for (var index = 0; index < characters.Length; index++)
            {
                if (!char.IsLetterOrDigit(characters[index]) && characters[index] != '.' && characters[index] != '-')
                    characters[index] = '_';
            }
            return new string(characters);
        }
    }
}
