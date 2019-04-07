using System;
using System.Linq;
using System.Collections.Generic;

namespace Yove.Http
{
    public class HttpUtils
    {
        public static string Parser(string Start, string Source, string End)
        {
            try
            {
                if (!Source.Contains(Start)) return null;

                int a = Source.IndexOf(Start, StringComparison.Ordinal) + Start.Length;

                Source = Source.Substring(a, Source.Length - a);

                int b = Source.IndexOf(End, StringComparison.Ordinal);

                return (b > 0) ? Source.Substring(0, b) : null;
            }
            catch
            {
                return null;
            }
        }

        public static string RandomString(int length)
        {
            try
            {
                const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abcdefghijklmnopqrstuywxyz";

                return new string(Enumerable.Repeat(chars, length).Select(s => s[new Random().Next(s.Length)]).ToArray());
            }
            catch
            {
                return null;
            }
        }

        #region UserAgent

        public static string GenerateUserAgent(HttpSystem system, HttpBrowser browser)
        {
            return UserAgent(system, browser);
        }

        public static string GenerateUserAgent(HttpSystem system)
        {
            List<HttpBrowser> BrowserList = Enum.GetValues(typeof(HttpBrowser)).Cast<HttpBrowser>().ToList();

            return UserAgent(system, BrowserList[new Random().Next(0, BrowserList.Count)]);
        }

        public static string GenerateUserAgent(HttpBrowser browser)
        {
            List<HttpSystem> SystemList = Enum.GetValues(typeof(HttpSystem)).Cast<HttpSystem>().ToList();

            return UserAgent(SystemList[new Random().Next(0, SystemList.Count)], browser);
        }

        public static string GenerateUserAgent()
        {
            List<HttpSystem> SystemList = Enum.GetValues(typeof(HttpSystem)).Cast<HttpSystem>().ToList();
            List<HttpBrowser> BrowserList = Enum.GetValues(typeof(HttpBrowser)).Cast<HttpBrowser>().ToList();

            return UserAgent(SystemList[new Random().Next(0, SystemList.Count)], BrowserList[new Random().Next(0, BrowserList.Count)]);
        }

        private static string UserAgent(HttpSystem system, HttpBrowser browser)
        {
            string OS = string.Empty;
            string Browser = string.Empty;

            switch (system)
            {
                case HttpSystem.Windows:
                    OS = $"(Windows NT {GenerateVersionOS(system)}; Win64; x64)";
                    break;
                case HttpSystem.Linux:
                    OS = $"(X11; Linux {GenerateVersionOS(system)})";
                    break;
                case HttpSystem.Mac:
                    OS = $"(Macintosh; Intel Mac OS X {GenerateVersionOS(system)})";
                    break;
                case HttpSystem.ChromeOS:
                    OS = $"(X11; CrOS x86_64 {GenerateVersionOS(system)})";
                    break;
            }

            switch (browser)
            {
                case HttpBrowser.Chrome:
                    Browser = $"AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{GenerateBrowserVersion(browser)} Safari/537.36";
                    break;
                case HttpBrowser.Firefox:
                    Browser = $"Gecko/20100101 Firefox/{GenerateBrowserVersion(browser)}";
                    break;
                case HttpBrowser.Opera:
                    Browser = $"AppleWebKit/537.36 (KHTML, like Gecko) {GenerateBrowserVersion(browser)}";
                    break;
                case HttpBrowser.Safari:
                    Browser = $"AppleWebKit/533.20.25 (KHTML, like Gecko) Version/{GenerateBrowserVersion(browser)} Safari/533.20.27";
                    break;
                case HttpBrowser.Edge:
                    Browser = $"AppleWebKit/537.36 (KHTML, like Gecko) {GenerateBrowserVersion(browser)}";
                    break;
            }

            return $"Mozilla/5.0 {OS} {Browser}";
        }

        private static string GenerateBrowserVersion(HttpBrowser browser)
        {
            List<string> Chrome = new List<string>
            {
                "57.0.2987", "58.0.3029", "59.0.3071", "60.0.3112", "61.0.3163", "62.0.3202", "63.0.3239",
                "64.0.3282", "65.0.3325", "66.0.3359", "67.0.3396", "68.0.3440", "69.0.3497", "70.0.3538",
                "70.0.3538", "71.0", "72.0", "69.0.3497.32"
            };

            List<string> Opera = new List<string>
            {
                "57.0.3098.14", "56.0.3051.52", "58.0.3111.0", "56.0.3051.43", "55.0.2994.34",
                "57.0.3098.1", "54.0.2952.64", "53.0.2907.68", "54.0.2952.60", "53.0.2907.99"
            };

            List<string> Safari = new List<string>
            {
                "5.0.4", "5.1.7", "4.0.3", "5.0.0", "5.0.3", "5.0.3"
            };

            List<string> Edge = new List<string>
            {
                "17.17134", "16.16299", "15.15063", "14.14393", "13.10586", "12.10240"
            };

            switch (browser)
            {
                case HttpBrowser.Chrome:
                    return Chrome[new Random().Next(0, Chrome.Count)];
                case HttpBrowser.Firefox:
                    return $"{new Random().Next(49, 70)}.0";
                case HttpBrowser.Opera:
                    return $"Chrome/{Chrome[new Random().Next(0, Chrome.Count)]} Safari/537.36 OPR/{Opera[new Random().Next(0, Opera.Count)]}";
                case HttpBrowser.Safari:
                    return Safari[new Random().Next(0, Safari.Count)];
                case HttpBrowser.Edge:
                    return $"Chrome/{Chrome[new Random().Next(0, Chrome.Count)]} Safari/537.36 Edge/{Edge[new Random().Next(0, Edge.Count)]}";
                default:
                    return null;
            }
        }

        private static string GenerateVersionOS(HttpSystem system)
        {
            List<string> Windows = new List<string> { "10.0", "6.2", "6.1", "6.3" };
            List<string> Linux = new List<string> { "i686", "x86_64" };

            List<string> Mac = new List<string>
            {
                "10_9", "10_9_1", "10_9_2", "10_9_3", "10_9_4", "10_9_5", "10_10", "10_10_1",
                "10_10_2", "10_10_3", "10_10_4", "10_10_5", "10_11", "10_11_1", "10_11_2",
                "10_11_3", "10_11_4", "10_11_5", "10.11.6", "10_12", "10_12_1", "10_12_2",
                "10_12_3","10_12_4", "10_12_5", "10_12_6", "10_13", "10_13_1", "10_13_2",
                "10_13_3", "10_13_4", "10_13_5", "10_13_6", "10_14_0"
            };

            List<string> ChromeOS = new List<string>
            {
                "10575.58.0", "10718.71.2", "10718.88.2", "10895.78.0", "10895.78.0", "11021.45.0", "11151.4.0",
                "11167.0.0", "10895.56.0", "11021.34.0", "11166.0.0"
            };

            switch (system)
            {
                case HttpSystem.Windows:
                    return Windows[new Random().Next(0, Windows.Count)];
                case HttpSystem.Mac:
                    return Mac[new Random().Next(0, Mac.Count)];
                case HttpSystem.Linux:
                    return Linux[new Random().Next(0, Linux.Count)];
                case HttpSystem.ChromeOS:
                    return ChromeOS[new Random().Next(0, ChromeOS.Count)];
                default:
                    return null;
            }
        }

        #endregion
    }
}