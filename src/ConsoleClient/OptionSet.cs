using System;
using System.Collections.Generic;
using System.Globalization;

namespace ConsoleClient
{
    internal sealed class OptionSet
    {
        private readonly Dictionary<string, string> values;

        private OptionSet(Dictionary<string, string> values)
        {
            this.values = values;
        }

        public static OptionSet Parse(IEnumerable<string> args)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var pendingKey = null as string;

            foreach (var arg in args)
            {
                if (arg.StartsWith("--", StringComparison.Ordinal))
                {
                    AddPending(values, ref pendingKey);
                    var keyValue = arg.Substring(2);
                    var separator = keyValue.IndexOf('=');
                    if (separator >= 0)
                    {
                        values[keyValue.Substring(0, separator)] = keyValue.Substring(separator + 1);
                    }
                    else
                    {
                        pendingKey = keyValue;
                    }
                }
                else if (arg.StartsWith("-", StringComparison.Ordinal) && arg.Length > 1)
                {
                    AddPending(values, ref pendingKey);
                    pendingKey = arg.Substring(1);
                }
                else if (pendingKey != null)
                {
                    values[pendingKey] = arg;
                    pendingKey = null;
                }
                else
                {
                    throw new CommandException("Unexpected argument '" + arg + "'. Options must use --name value or --name=value.");
                }
            }

            AddPending(values, ref pendingKey);
            return new OptionSet(values);
        }

        public OptionSet With(string key, string value)
        {
            var copy = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
            copy[key] = value;
            return new OptionSet(copy);
        }

        public string Get(string key, string defaultValue)
        {
            string value;
            return values.TryGetValue(key, out value) ? value : defaultValue;
        }

        public bool GetBool(params string[] keys)
        {
            foreach (var key in keys)
            {
                string value;
                if (!values.TryGetValue(key, out value))
                {
                    continue;
                }

                if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "1", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "0", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                throw new CommandException("Option --" + key + " expects true or false.");
            }

            return false;
        }

        public int GetInt(string key, int defaultValue)
        {
            string value;
            if (!values.TryGetValue(key, out value))
            {
                return defaultValue;
            }

            int parsed;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                throw new CommandException("Option --" + key + " expects an integer.");
            }

            return parsed;
        }

        public double GetDouble(string key, double defaultValue)
        {
            string value;
            if (!values.TryGetValue(key, out value))
            {
                return defaultValue;
            }

            double parsed;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                throw new CommandException("Option --" + key + " expects a number.");
            }

            return parsed;
        }

        public float GetFloat(string key, float defaultValue)
        {
            string value;
            if (!values.TryGetValue(key, out value))
            {
                return defaultValue;
            }

            float parsed;
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                throw new CommandException("Option --" + key + " expects a number.");
            }

            return parsed;
        }

        private static void AddPending(Dictionary<string, string> values, ref string pendingKey)
        {
            if (pendingKey == null)
            {
                return;
            }

            values[pendingKey] = "true";
            pendingKey = null;
        }
    }
}
