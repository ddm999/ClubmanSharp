using System;
using System.IO;
using System.Net.Http.Headers;
using System.Threading;

namespace ClubmanSharp
{
    public enum LogType
    {
        Main,
        Menu,
        Driv,
        Trck
    }

    public class DebugLog
    {
        public static bool isActiveMain = false;
        public static bool isActiveMenu = false;
        public static bool isActiveDriver = false;
        public static bool isActiveTrack = false;
        private readonly static string path = Path.Combine(Directory.GetCurrentDirectory(), "debug.log");

        public static void SetActive(bool state, LogType type)
        {
            switch (type)
            {
                case LogType.Main:
                    isActiveMain = state;
                    break;
                case LogType.Menu:
                    isActiveMenu = state;
                    break;
                case LogType.Driv:
                    isActiveDriver = state;
                    break;
            }
        }

        public static void Log(string msg, LogType type)
        {
            if (type == LogType.Main && !isActiveMain)
                return;
            if (type == LogType.Menu && !isActiveMenu)
                return;
            if (type == LogType.Driv && !isActiveDriver)
                return;
            if (type == LogType.Trck && !isActiveTrack)
                return;

            byte retryCount = 0;
            while (retryCount < 10)
            {
                try
                {
                    File.AppendAllText(path, $"[{DateTime.Now:yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'ffff}] {type}: {msg}\n");
                    break;
                }
                catch (IOException)
                {
                    Thread.Sleep(100);
                    retryCount++;
                }
            }
        }
    }
}
