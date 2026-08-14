using System;

namespace LP.GatewayAPI.Middlewares
{
    // Resolves the full base address (host + version) a request should hit for a given service
    // key, based on the caller's declared clientVersion -- RouteVersions/services.json (one
    // shared file) is this gateway's only source of routing data.
    //
    // Each service tracks its own clientVersion -> apiVersion map, independent of every other
    // service. Resolution for a given (clientVersion, serviceKey):
    //   1. Look up the service by key. No match -> null ("no route for this request").
    //   2. Within that service's versionMap, find the entry with the highest clientVersion that
    //      is <= the caller's clientVersion -- i.e. "the most recent mapping known as of the
    //      client version making the request."
    //   3. If no such entry exists (empty map, missing/non-numeric clientVersion, or every mapped
    //      clientVersion is higher than what was requested), resolve with no version segment at
    //      all -- the caller hits the service's bare url.
    public class RouteVersionResolver
    {
        public const string ClientVersionHeader = "clientVersion";

        private readonly RouteVersionsRepository _repository;

        public RouteVersionResolver(RouteVersionsRepository repository)
        {
            _repository = repository;
        }

        public (string ApiUri, string Version)? Resolve(string? clientVersion, string? serviceKey)
        {
            if (string.IsNullOrEmpty(serviceKey))
                return null;

            var services = _repository.Services;
            if (!services.TryGetValue(serviceKey, out var service) || string.IsNullOrEmpty(service.Url))
                return null;

            var apiVersion = ResolveApiVersion(service.VersionMap, clientVersion);
            return (service.Url, apiVersion ?? string.Empty);
        }

        private static string? ResolveApiVersion(List<VersionMapEntry>? versionMap, string? clientVersion)
        {
            if (versionMap == null || versionMap.Count == 0)
                return null;

            var requested = ParseVersion(clientVersion);
            if (requested == null)
                return null;

            VersionMapEntry? best = null;
            int bestVersion = int.MinValue;

            foreach (var entry in versionMap)
            {
                var entryVersion = ParseVersion(entry.ClientVersion);
                if (entryVersion == null || entryVersion > requested)
                    continue;

                if (best == null || entryVersion > bestVersion)
                {
                    best = entry;
                    bestVersion = entryVersion.Value;
                }
            }

            return best?.ApiVersion;
        }

        private static int? ParseVersion(string? value) =>
            int.TryParse(value, out var parsed) ? parsed : null;
    }

    // Deserialized from RouteVersions/services.json by RouteVersionsRepository.
    public class ServiceRoute
    {
        public string Url { get; set; } = string.Empty;
        public List<VersionMapEntry> VersionMap { get; set; } = [];
    }

    public class VersionMapEntry
    {
        public string ClientVersion { get; set; } = string.Empty;
        public string ApiVersion { get; set; } = string.Empty;
    }
}
