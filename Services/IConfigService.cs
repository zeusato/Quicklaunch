using QuickStartLauncher.Models;

namespace QuickStartLauncher.Services
{
    public interface IConfigService
    {
        AppConfig LoadConfig();
        void SaveConfig(AppConfig config);
    }
}