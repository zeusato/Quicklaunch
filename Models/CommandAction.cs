using System.Text.Json.Serialization;

namespace QuickStartLauncher.Models
{
    public enum ShellType
    {
        Cmd,
        PowerShell
    }

    public class CommandAction : ActionBase
    {
        [JsonPropertyName("command")]
        public string Command { get; set; } = string.Empty;

        [JsonPropertyName("shell")]
        public ShellType Shell { get; set; } = ShellType.Cmd;

        [JsonPropertyName("workingDir")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? WorkingDir { get; set; }

        [JsonPropertyName("runAsAdmin")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool RunAsAdmin { get; set; }

        public CommandAction() : base(ActionType.Command) { }

        [JsonIgnore]
        public override string Description => $"Run command: {Command}";
    }
}