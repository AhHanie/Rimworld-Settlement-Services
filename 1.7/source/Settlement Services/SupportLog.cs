using System.Collections.Generic;
using Verse;

namespace Settlement_Services
{
    public static class SupportLog
    {
        private const string Prefix = "[Settlement Services] ";
        private static readonly HashSet<string> loggedInfoMessages = new HashSet<string>();

        public static void Info(string message)
        {
            if (!loggedInfoMessages.Add(message)) return;
            Log.Message(Prefix + message);
        }

        public static void Warning(string message)
        {
            Log.WarningOnce(Prefix + message, message.GetHashCode());
        }

        public static void Error(string message)
        {
            Log.ErrorOnce(Prefix + message, message.GetHashCode());
        }
    }
}
