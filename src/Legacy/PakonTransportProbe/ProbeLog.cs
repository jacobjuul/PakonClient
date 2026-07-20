using System;
using System.Globalization;
using System.IO;

namespace PakonTransportProbe
{
    internal sealed class ProbeLog : IDisposable
    {
        private readonly StreamWriter file;

        public ProbeLog(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                file = new StreamWriter(path, false);
            }
        }

        public void Write(string format, params object[] arguments)
        {
            var message = arguments == null || arguments.Length == 0
                ? format
                : string.Format(CultureInfo.InvariantCulture, format, arguments);
            var line = string.Format(CultureInfo.InvariantCulture, "{0:O} {1}", DateTimeOffset.Now, message);
            Console.WriteLine(line);
            if (file != null)
            {
                file.WriteLine(line);
                file.Flush();
            }
        }

        public void Dispose()
        {
            if (file != null) file.Dispose();
        }
    }
}
