using Serilog;
using Serilog.Core;
using System;
using System.IO;

namespace QuickStartLauncher.Services
{
    public class LogService : ILogService
    {
        private readonly Logger _logger;

        public LogService()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string logPath = Path.Combine(appDataPath, "QuickStart", "logs", "log-.txt");

            _logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                .CreateLogger();
        }

        public void Info(string message)
        {
            _logger.Information(message);
        }

        public void Error(string message, Exception? ex = null)
        {
            if (ex != null)
            {
                _logger.Error(ex, message);
            }
            else
            {
                _logger.Error(message);
            }
        }
    }
}