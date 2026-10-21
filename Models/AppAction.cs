using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace QuickStartLauncher.Models
{
    public class AppAction : ActionBase
    {
        private string _path = string.Empty;
        [JsonPropertyName("path")]
        public string Path
        {
            get => _path;
            set
            {
                if (_path != value)
                {
                    _path = value;
                    OnPropertyChanged(nameof(Path));
                    OnPropertyChanged(nameof(Description));
                    OnPropertyChanged(nameof(IsBrowser));
                    OnPropertyChanged(nameof(Icon));
                }
            }
        }

        [JsonIgnore]
        public bool IsBrowser => Path.EndsWith("chrome.exe") || Path.EndsWith("msedge.exe") || Path.EndsWith("firefox.exe") || Path.EndsWith("brave.exe") || Path.EndsWith("opera.exe");

[JsonIgnore]
        public string Icon => Path;
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("profile")]
        public string Profile { get; set; } = string.Empty;

        [JsonPropertyName("newWindow")]
        public bool NewWindow { get; set; }

        [JsonPropertyName("arguments")]
        public string Arguments { get; set; } = string.Empty;

        [JsonPropertyName("runAsAdmin")]
        public bool RunAsAdmin { get; set; }

        public AppAction() : base(ActionType.App) { }

        [JsonIgnore]
        public override string Description
        {
            get
            {
                var preview = $"\"{Path}\"";
                if (IsBrowser)
                {
                    if (!string.IsNullOrWhiteSpace(Profile))
                    {
                        preview += $" --profile-directory=\"{Profile}\"";
                    }
                    if (NewWindow)
                    {
                        preview += " --new-window";
                    }
                    if (!string.IsNullOrWhiteSpace(Url))
                    {
                        preview += $" {Url}";
                    }
                }
                if (!string.IsNullOrWhiteSpace(Arguments))
                {
                    preview += $" {Arguments}";
                }
                return preview;
            }
        }
    }
}