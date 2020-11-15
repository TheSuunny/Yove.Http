using System;
using System.Linq;

namespace Yove.Http
{
    public class HttpUtils
    {
        public static string Parser(string start, string body, string end)
        {
            try
            {
                if (!body.Contains(start))
                    return null;

                int a = body.IndexOf(start, StringComparison.Ordinal) + start.Length;

                body = body.Substring(a, body.Length - a);

                int b = body.IndexOf(end, StringComparison.Ordinal);

                return (b > 0) ? body.Substring(0, b) : null;
            }
            catch
            {
                return null;
            }
        }

        public static string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abcdefghijklmnopqrstuywxyz";

            return new string(Enumerable.Repeat(chars, length).Select(s => s[new Random().Next(s.Length)]).ToArray());
        }

        public static string GenerateUserAgent(HttpSystem system, HttpBrowser browser)
        {
            return UserAgent(system, browser);
        }

        public static string GenerateUserAgent(HttpSystem system)
        {
            HttpBrowser[] browserList = Enum.GetValues(typeof(HttpBrowser)).Cast<HttpBrowser>().ToArray();

            return UserAgent(system, browserList[new Random().Next(0, browserList.Count())]);
        }

        public static string GenerateUserAgent(HttpBrowser browser)
        {
            HttpSystem[] systemList = Enum.GetValues(typeof(HttpSystem)).Cast<HttpSystem>().ToArray();

            return UserAgent(systemList[new Random().Next(0, systemList.Count())], browser);
        }

        public static string GenerateUserAgent()
        {
            HttpSystem[] systemList = Enum.GetValues(typeof(HttpSystem)).Cast<HttpSystem>().ToArray();
            HttpBrowser[] browserList = Enum.GetValues(typeof(HttpBrowser)).Cast<HttpBrowser>().ToArray();

            return UserAgent(systemList[new Random().Next(0, systemList.Count())], browserList[new Random().Next(0, browserList.Count())]);
        }

        private static string UserAgent(HttpSystem system, HttpBrowser browser)
        {
            string systemString = string.Empty;
            string browserString = string.Empty;

            switch (system)
            {
                case HttpSystem.Windows:
                    systemString = $"(Windows NT {GenerateVersionOS(system)}; Win64; x64)";
                    break;
                case HttpSystem.Linux:
                    systemString = $"(X11; Linux {GenerateVersionOS(system)})";
                    break;
                case HttpSystem.Mac:
                    systemString = $"(Macintosh; Intel Mac OS X {GenerateVersionOS(system)})";
                    break;
                case HttpSystem.ChromeOS:
                    systemString = $"(X11; CrOS x86_64 {GenerateVersionOS(system)})";
                    break;
            }

            switch (browser)
            {
                case HttpBrowser.Chrome:
                    browserString = $"AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{GenerateBrowserVersion(browser)} Safari/605.1.15";
                    break;
                case HttpBrowser.Firefox:
                    browserString = $"Gecko/20100101 Firefox/{GenerateBrowserVersion(browser)}";
                    break;
                case HttpBrowser.Opera:
                    browserString = $"AppleWebKit/537.36 (KHTML, like Gecko) {GenerateBrowserVersion(browser)}";
                    break;
                case HttpBrowser.Safari:
                    browserString = $"AppleWebKit/533.20.25 (KHTML, like Gecko) Version/{GenerateBrowserVersion(browser)} Safari/605.1.15";
                    break;
                case HttpBrowser.Edge:
                    browserString = $"AppleWebKit/537.36 (KHTML, like Gecko) {GenerateBrowserVersion(browser)}";
                    break;
            }

            return $"Mozilla/5.0 {systemString} {browserString}";
        }

        private static string GenerateBrowserVersion(HttpBrowser browser)
        {
            string[] chrome = new[]
            {
                "70.0.3538", "71.0", "72.0", "69.0.3497.32", "70.0.3538", "71.0.3578", "72.0.3626", "73.0.3683",
                "74.0.3729", "75.0.3770", "76.0.3809", "77.0.3865", "78.0.3904", "79.0.3945", "80.0.3987", "81.0.4044",
                "83.0.4103", "84.0.4147", "85.0.4183", "86.0.4240", "87.0.4280", "88.0"
            };

            string[] opera = new[]
            {
                "57.0.3098.14", "56.0.3051.52", "58.0.3111.0", "56.0.3051.43", "55.0.2994.34",
                "57.0.3098.1", "54.0.2952.64", "53.0.2907.68", "54.0.2952.60", "53.0.2907.99"
            };

            string[] safari = new[]
            {
                "14.0", "13.1.2", "12.1.2"
            };

            string[] Edge = new[]
            {
                "17.17134", "16.16299", "15.15063", "14.14393", "13.10586", "12.10240"
            };

            switch (browser)
            {
                case HttpBrowser.Chrome:
                    return chrome[new Random().Next(0, chrome.Count())];
                case HttpBrowser.Firefox:
                    return $"{new Random().Next(49, 70)}.0";
                case HttpBrowser.Opera:
                    return $"Chrome/{chrome[new Random().Next(0, chrome.Count())]} Safari/537.36 OPR/{opera[new Random().Next(0, opera.Count())]}";
                case HttpBrowser.Safari:
                    return safari[new Random().Next(0, safari.Count())];
                case HttpBrowser.Edge:
                    return $"Chrome/{chrome[new Random().Next(0, chrome.Count())]} Safari/537.36 Edge/{Edge[new Random().Next(0, Edge.Count())]}";
                default:
                    return null;
            }
        }

        private static string GenerateVersionOS(HttpSystem system)
        {
            string[] windows = new[] { "10.0", "6.2", "6.1", "6.3" };
            string[] linux = new[] { "i686", "x86_64" };

            string[] mac = new[]
            {
                "10_9", "10_9_1", "10_9_2", "10_9_3", "10_9_4", "10_9_5", "10_10", "10_10_1",
                "10_10_2", "10_10_3", "10_10_4", "10_10_5", "10_11", "10_11_1", "10_11_2",
                "10_11_3", "10_11_4", "10_11_5", "10.11.6", "10_12", "10_12_1", "10_12_2",
                "10_12_3","10_12_4", "10_12_5", "10_12_6", "10_13", "10_13_1", "10_13_2",
                "10_13_3", "10_13_4", "10_13_5", "10_13_6", "10_14_0", "10.15", "11.0"
            };

            string[] chromeOS = new[]
            {
                "10575.58.0", "10718.71.2", "10718.88.2", "10895.78.0", "10895.78.0", "11021.45.0", "11151.4.0",
                "11167.0.0", "10895.56.0", "11021.34.0", "11166.0.0"
            };

            switch (system)
            {
                case HttpSystem.Windows:
                    return windows[new Random().Next(0, windows.Count())];
                case HttpSystem.Mac:
                    return mac[new Random().Next(0, mac.Count())];
                case HttpSystem.Linux:
                    return linux[new Random().Next(0, linux.Count())];
                case HttpSystem.ChromeOS:
                    return chromeOS[new Random().Next(0, chromeOS.Count())];
                default:
                    return null;
            }
        }
    }
}