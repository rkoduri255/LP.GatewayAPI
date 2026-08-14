using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using System.Text.Json;

namespace LP.GatewayAPI.Middlewares
{
    // Loads route-versions from the single shared RouteVersions/services.json -- one file for all
    // apps and all services. Each service carries its own clientVersion -> apiVersion map, so one
    // service can diverge for a hotfix or a staged release without touching any other service's
    // mapping. Watches the file and reloads automatically on any edit.
    public class RouteVersionsRepository
    {
        private const string FileName = "services.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IFileProvider _fileProvider;
        private readonly ILogger<RouteVersionsRepository> _logger;
        private IReadOnlyDictionary<string, ServiceRoute> _services =
            new Dictionary<string, ServiceRoute>(StringComparer.OrdinalIgnoreCase);

        public RouteVersionsRepository(IHostEnvironment env, ILogger<RouteVersionsRepository> logger)
        {
            _logger = logger;
            _fileProvider = new PhysicalFileProvider(Path.Combine(env.ContentRootPath, "RouteVersions"));
            Load();
            ChangeToken.OnChange(() => _fileProvider.Watch(FileName), Load);
        }

        public IReadOnlyDictionary<string, ServiceRoute> Services => _services;

        private void Load()
        {
            var file = _fileProvider.GetFileInfo(FileName);
            if (!file.Exists)
            {
                _logger.LogError("Route-versions file {File} not found; no routes will resolve.", FileName);
                Interlocked.Exchange(ref _services, new Dictionary<string, ServiceRoute>(StringComparer.OrdinalIgnoreCase));
                return;
            }

            try
            {
                using var stream = file.CreateReadStream();
                var config = JsonSerializer.Deserialize<RouteVersionsConfig>(stream, JsonOptions);
                var loaded = new Dictionary<string, ServiceRoute>(
                    config?.Services ?? [],
                    StringComparer.OrdinalIgnoreCase);

                Interlocked.Exchange(ref _services, loaded);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load route-versions file {File}; keeping previous routes.", FileName);
            }
        }
    }

    public class RouteVersionsConfig
    {
        public Dictionary<string, ServiceRoute> Services { get; set; } = [];
    }
}
