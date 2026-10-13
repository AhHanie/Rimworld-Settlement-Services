using System;
using Verse;

namespace Settlement_Services
{
    public static class Logger
    {
        private const string Prefix = "[Settlement Services] ";

        private static bool Enabled => ModSettings.Current.verboseLoggingEnabled;

        public static void Message(string message)
        {
            if (!Enabled) return;
            Log.Message(Prefix + message);
        }

        public static void Warning(string message)
        {
            if (!Enabled) return;
            Log.Warning(Prefix + message);
        }

        public static void Error(string message)
        {
            if (!Enabled) return;
            Log.Error(Prefix + message);
        }

        public static void Exception(Exception exception, string context = null)
        {
            if (!Enabled || exception == null)
            {
                return;
            }

            string prefix = string.IsNullOrWhiteSpace(context) ? Prefix : Prefix + context + ": ";
            Log.Error(prefix + exception);
        }
    }
}
