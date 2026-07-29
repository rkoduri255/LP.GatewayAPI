using System;

namespace LP.GatewayAPI.Middlewares
{
    // Resolves the full base address (host + version) a request should hit for a given service
    // key, based on the calling app's declared name -- RouteVersions/*.json (one file per app) is
    // this gateway's only source of routing data now, keyed per app, not per route.
    //
    // Resolution order for a given (appName, serviceKey):
    //   1. That app's own entry for this service, if the app is found and has it.
    //   2. A sentinel app literally named "default", if a file carries this service under that
    //      name -- this is the fallback for a missing/unrecognized appName header. There is no
    //      such file today, so until one is added, an unmatched request 404s rather than
    //      silently resolving somewhere.
    //   3. Otherwise null -- "no route for this request."
    public class RouteVersionResolver
    {
        public const string AppNameHeader = "appName";
        public const string DefaultAppName = "default";
        private const string FallbackVersion = "v1";

        private readonly RouteVersionsRepository _repository;

        public RouteVersionResolver(RouteVersionsRepository repository)
        {
            _repository = repository;
        }

        public (string ApiUri, string Version)? Resolve(string? appName, string? serviceKey)
        {
            if (string.IsNullOrEmpty(serviceKey))
                return null;

            var apps = _repository.Apps;
            if (apps.Count == 0)
                return null;

            var app = FindApp(apps, appName) ?? FindApp(apps, DefaultAppName);
            if (app?.Services == null)
                return null;

            var match = app.Services.FirstOrDefault(kvp =>
                string.Equals(kvp.Key, serviceKey, StringComparison.OrdinalIgnoreCase));
            var service = match.Value;

            if (service == null || string.IsNullOrEmpty(service.Url))
                return null;

            return (service.Url,service.Version);          
        }

        private static AppRouteVersions? FindApp(IReadOnlyList<AppRouteVersions> apps, string? appName)
        {
            if (string.IsNullOrEmpty(appName))
                return null;

            return apps.FirstOrDefault(a => string.Equals(a.AppName, appName, StringComparison.OrdinalIgnoreCase));
        }
    }

    // Deserialized from RouteVersions/{appName}.json by RouteVersionsRepository.
    public class AppRouteVersions
    {
        public string AppName { get; set; } = string.Empty;

        // Descriptive/tracking only -- not used to resolve a request.
        public string ApiVersion { get; set; } = string.Empty;

        // Key = service key (matches the route's first path segment, e.g. "LPServicesAddress"
        // for a request to "/lpservicesaddress/..."), value = that service's full base address.
        public Dictionary<string, ServiceOverride> Services { get; set; } = [];
    }

    public class ServiceOverride
    {
        public string Url { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }
}
