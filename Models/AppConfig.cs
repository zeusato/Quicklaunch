using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace QuickStartLauncher.Models
{
    public class AppConfig
    {
        [JsonPropertyName("activeProfile")]
        public string ActiveProfile { get; set; } = "Default";

        [JsonPropertyName("profiles")]
        public List<Profile> Profiles { get; set; } = new();
    }
}