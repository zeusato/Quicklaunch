using QuickStartLauncher.Models;
using System;
using System.IO;
using System.Text.Json;

namespace QuickStartLauncher.Services
{
    public class ConfigService : IConfigService
    {
        private readonly string _configPath;
        private readonly ILogService _logService;
        private readonly JsonSerializerOptions _jsonOptions;

        public ConfigService(ILogService logService)
        {
            _logService = logService;
            // Store config in %LocalAppData%\QuickStart\config\config.json
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _configPath = Path.Combine(appDataPath, "QuickStart", "config", "config.json");

            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
        }

        public AppConfig LoadConfig()
        {
            if (!File.Exists(_configPath))
            {
                return CreateDefaultConfig();
            }

            try
            {
                string json = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions);
                return config ?? CreateDefaultConfig();
            }
            catch (Exception ex) // Could be JsonException, IOException, etc.
            {
                _logService.Error("Failed to load config file. Falling back to default.", ex);
                return CreateDefaultConfig();
            }
        }

        public void SaveConfig(AppConfig config)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
                string json = JsonSerializer.Serialize(config, _jsonOptions);
                File.WriteAllText(_configPath, json);
            }
            catch (Exception ex)
            {
                _logService.Error("Failed to save config file.", ex);
            }
        }

        private AppConfig CreateDefaultConfig()
        {
            var defaultConfig = new AppConfig
            {
                ActiveProfile = "Default",
                Profiles = new System.Collections.Generic.List<Profile>
                {
                    new Profile { Name = "Default", Actions = new () }
                }
            };
            // Save the default config so the user has a file to start with.
            SaveConfig(defaultConfig);
            return defaultConfig;
        }
    }
}