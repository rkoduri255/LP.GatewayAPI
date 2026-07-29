using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using System.Text.Json;

namespace LP.GatewayAPI.Middlewares
{
    // Loads route-versions from RouteVersions/*.json -- one file per app -- instead of a single
    // shared route-versions.json. A hotfix to one app's dependency on one microservice is now a
    // one-file edit that can't collide with, or break JSON parsing for, any other app's file.
    // Watches the directory and reloads automatically on any add/edit/delete/rename, the same
    // hot-reload guarantee the old single-file + IOptionsMonitor setup provided.
    public class RouteVersionsRepository
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IFileProvider _fileProvider;
        private readonly ILogger<RouteVersionsRepository> _logger;
        private IReadOnlyList<AppRouteVersions> _apps = Array.Empty<AppRouteVersions>();

        public RouteVersionsRepository(IHostEnvironment env, ILogger<RouteVersionsRepository> logger)
        {
            _logger = logger;
            _fileProvider = new PhysicalFileProvider(Path.Combine(env.ContentRootPath, "RouteVersions"));
            Load();
            ChangeToken.OnChange(() => _fileProvider.Watch("*.json"), Load);
        }

        public IReadOnlyList<AppRouteVersions> Apps => _apps;

        private void Load()
        {
            var loaded = new List<AppRouteVersions>();

            foreach (var file in _fileProvider.GetDirectoryContents("")
                .Where(f => !f.IsDirectory && f.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    using var stream = file.CreateReadStream();
                    var app = JsonSerializer.Deserialize<AppRouteVersions>(stream, JsonOptions);
                    if (app != null && !string.IsNullOrEmpty(app.AppName))
                        loaded.Add(app);
                }
                catch (Exception ex)
                {
                    // A bad edit to one app's file is skipped, not fatal -- every other app's file
                    // keeps loading and resolving normally.
                    _logger.LogError(ex, "Failed to load route-versions file {File}; skipping it.", file.Name);
                }
            }

            Interlocked.Exchange(ref _apps, loaded);
        }
    }
}
