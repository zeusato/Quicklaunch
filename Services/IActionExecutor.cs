using QuickStartLauncher.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace QuickStartLauncher.Services
{
    public interface IActionExecutor
    {
        Task ExecuteActions(IEnumerable<ActionBase> actions, Action<string> onActionStart, Action<string, bool> onActionComplete);
    }
}