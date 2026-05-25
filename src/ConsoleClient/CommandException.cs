using System;

namespace ConsoleClient
{
    internal sealed class CommandException : Exception
    {
        public CommandException(string message)
            : base(message)
        {
        }
    }
}
