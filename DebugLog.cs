using System;
using System.IO;
using System.Net.Http.Headers;
using System.Threading;

namespace ClubmanSharp
{
    public class DebugLog
    {
        public static bool isActive = false;
        private readonly static string path = Path.Combine(Directory.GetCurrentDirectory(), "debug.log");

        public static void SetActive(bool state)
        {
            isActive = state;
        }

        public static void Log(string msg)
        {
            if (!isActive)
                return;

            byte retryCount = 0;
            while (retryCount < 10)
            {
                try
                {
                    File.AppendAllText(path, $"[{DateTime.Now:yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'ffff}] {msg}\n");
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
