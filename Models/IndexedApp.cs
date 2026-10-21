namespace QuickStartLauncher.Models
{
    public enum AppSource
    {
        StartMenu,
        Registry,
        UserAdded
    }

    public class IndexedApp
    {
        public string AppName { get; set; } = string.Empty;
        public string AppPath { get; set; } = string.Empty;
        public AppSource Source { get; set; }
        public string? IconPath { get; set; }
    }
}