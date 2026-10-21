using QuickStartLauncher.Models;
using System.Collections.Generic;
using System.Threading.Tasks;


namespace QuickStartLauncher.Services
{
    public interface IAppIndexService
    {
        Task<List<IndexedApp>> GetIndexedAppsAsync();
        Task RebuildIndexAsync();
        List<IndexedApp> Search(string query);
        int IndexCount { get; }
        DateTime? LastUpdated { get; }
    }
}