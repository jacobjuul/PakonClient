using System;

namespace ConsoleClient
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            var app = new PakonConsole();
            return app.Run(args);
        }
    }
}
