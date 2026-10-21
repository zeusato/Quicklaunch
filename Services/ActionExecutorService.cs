using QuickStartLauncher.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace QuickStartLauncher.Services
{
    public class ActionExecutorService : IActionExecutor
    {
        private readonly ILogService _logService;

        public ActionExecutorService(ILogService logService)
        {
            _logService = logService;
        }

        public async Task ExecuteActions(IEnumerable<ActionBase> actions, Action<string> onActionStart, Action<string, bool> onActionComplete)
        {
            foreach (var action in actions)
            {
                bool success = false;
                onActionStart(action.Description);
                try
                {
                    await ExecuteAction(action);
                    success = true;
                }
                catch (Exception ex)
                {
                    _logService.Error($"Failed to execute action: {action.Description}", ex);
                }
                finally
                {
                    onActionComplete(action.Description, success);
                }
            }
        }

        private async Task ExecuteAction(ActionBase action)
        {
            switch (action.ActionType)
            {
                case ActionType.App:
                    await ExecuteAppAction((AppAction)action);
                    break;
                case ActionType.Command:
                    await ExecuteCommandAction((CommandAction)action);
                    break;
                default:
                    throw new NotSupportedException($"Action type '{action.ActionType}' is not supported.");
            }
        }

        private Task ExecuteAppAction(AppAction action)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = action.Path,
                UseShellExecute = true,
                Verb = action.RunAsAdmin ? "runas" : string.Empty
            };

            var arguments = new List<string>();
            if (action.IsBrowser)
            {
                if (!string.IsNullOrWhiteSpace(action.Profile))
                {
                    arguments.Add($"--profile-directory=\"{action.Profile}\"");
                }
                if (action.NewWindow)
                {
                    arguments.Add("--new-window");
                }
                if (!string.IsNullOrWhiteSpace(action.Url))
                {
                    arguments.Add(action.Url);
                }
            }
            if (!string.IsNullOrWhiteSpace(action.Arguments))
            {
                arguments.Add(action.Arguments);
            }
            startInfo.Arguments = string.Join(" ", arguments);

            Process.Start(startInfo);
            return Task.CompletedTask;
        }


        private Task ExecuteCommandAction(CommandAction action)
        {
            var process = new Process();
            process.StartInfo.UseShellExecute = true;
            process.StartInfo.WorkingDirectory = action.WorkingDir ?? string.Empty;
            process.StartInfo.Verb = action.RunAsAdmin ? "runas" : string.Empty;

            switch (action.Shell)
            {
                case ShellType.Cmd:
                    process.StartInfo.FileName = "cmd.exe";
                    process.StartInfo.Arguments = $"/c {action.Command}";
                    break;
                case ShellType.PowerShell:
                    process.StartInfo.FileName = "powershell.exe";
                    process.StartInfo.Arguments = $"-Command {action.Command}";
                    break;
            }

            process.Start();
            return Task.CompletedTask;
        }
    }
}