using QuickStartLauncher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace QuickStartLauncher.Services
{
    public class AppIndexService : IAppIndexService
    {
        private readonly string _cachePath;
        private readonly ILogService _logService;
        private List<IndexedApp> _indexedApps = new();
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        public int IndexCount => _indexedApps.Count;
        public DateTime? LastUpdated { get; private set; }

        public AppIndexService(ILogService logService)
        {
            _logService = logService;
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _cachePath = Path.Combine(appDataPath, "QuickStart", "appIndex.json");
            _ = LoadIndexFromCacheAsync();
        }

        public Task<List<IndexedApp>> GetIndexedAppsAsync()
        {
            return Task.FromResult(_indexedApps);
        }

        public async Task RebuildIndexAsync()
        {
            _indexedApps.Clear();

            // 1. Scan Start Menu
            var startMenuApps = await ScanStartMenuAsync();
            _indexedApps.AddRange(startMenuApps);

            // 2. Scan Registry (Placeholder)
            // var registryApps = await ScanRegistryAsync();
            // _indexedApps.AddRange(registryApps);

            LastUpdated = DateTime.Now;
            await SaveIndexToCacheAsync();
        }

        public List<IndexedApp> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return _indexedApps;
            }
            // Simple search for now. Fuzzy search is more complex.
            return _indexedApps.Where(app => app.AppName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private async Task LoadIndexFromCacheAsync()
        {
            if (!File.Exists(_cachePath)) return;

            try
            {
                var json = await File.ReadAllTextAsync(_cachePath);
                var cachedIndex = JsonSerializer.Deserialize<List<IndexedApp>>(json, _jsonOptions);
                if (cachedIndex != null)
                {
                    _indexedApps = cachedIndex;
                    LastUpdated = File.GetLastWriteTime(_cachePath);
                }
            }
            catch (Exception ex) { _logService.Error("Failed to load app index cache.", ex); }
        }

        private async Task SaveIndexToCacheAsync()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
                var json = JsonSerializer.Serialize(_indexedApps, _jsonOptions);
                await File.WriteAllTextAsync(_cachePath, json);
            }
            catch (Exception ex) { _logService.Error("Failed to save app index cache.", ex); }
        }

        private Task<List<IndexedApp>> ScanStartMenuAsync()
        {
            var apps = new List<IndexedApp>();
            var startMenuPaths = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
            };

            foreach (var path in startMenuPaths)
            {
                if (Directory.Exists(path))
                {
                    var links = Directory.GetFiles(path, "*.lnk", SearchOption.AllDirectories);
                    foreach (var link in links)
                    {
                        // Resolving .lnk files requires P/Invoke or a library.
                        // For now, we'll just add a placeholder.
                        apps.Add(new IndexedApp
                        {
                            AppName = Path.GetFileNameWithoutExtension(link),
                            AppPath = "TBD: Resolve " + link,
                            Source = AppSource.StartMenu
                        });
                    }
                }
            }
            return Task.FromResult(apps);
        }
    }
}