using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Reflection;

namespace ArchieCopilot
{
    public static class Config
    {
        private static string? _apiKey;
        private static readonly string ConfigPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
            "config.json"
        );

        public static string? GetApiKey()
        {
            if (_apiKey != null)
                return _apiKey;

            // 1. Try environment variable
            _apiKey = Environment.GetEnvironmentVariable("ARCHIE_COPILOT_API_KEY");
            if (!string.IsNullOrEmpty(_apiKey))
                return _apiKey;

            // 2. Try config.json
            if (File.Exists(ConfigPath))
            {
                try
                {
                    var json = JObject.Parse(File.ReadAllText(ConfigPath));
                    _apiKey = json["apiKey"]?.ToString();
                    if (!string.IsNullOrEmpty(_apiKey))
                        return _apiKey;
                }
                catch { }
            }

            return null;
        }

        public static void SaveApiKey(string apiKey)
        {
            _apiKey = apiKey;
            var json = new JObject { ["apiKey"] = apiKey };
            File.WriteAllText(ConfigPath, json.ToString(Formatting.Indented));
        }
    }
}
