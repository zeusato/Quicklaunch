using System;

namespace QuickStartLauncher.Services
{
    public interface ILogService
    {
        void Info(string message);
        void Error(string message, Exception? ex = null);
    }
}