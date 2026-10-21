using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace QuickStartLauncher.Models
{
    public class Profile
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "Default";

        [JsonPropertyName("actions")]
        public ObservableCollection<ActionBase> Actions { get; set; } = new();
    }
}