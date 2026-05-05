using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace ChatCRM.MVC.Localization;

/// <summary>
/// IStringLocalizer backed by JSON files under wwwroot-sibling /Resources/strings.{culture}.json.
/// Files are loaded once per culture and cached in memory. Falls back to the default culture
/// (English) when a key is missing in the active culture, then to the literal key as last resort.
/// </summary>
public sealed class JsonStringLocalizer : IStringLocalizer
{
    private const string DefaultCulture = "en";

    private readonly ResourceCache _cache;
    private readonly ILogger<JsonStringLocalizer> _logger;

    public JsonStringLocalizer(ResourceCache cache, ILogger<JsonStringLocalizer> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public LocalizedString this[string name] => Resolve(name, formatArgs: null);

    public LocalizedString this[string name, params object[] arguments] => Resolve(name, arguments);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        var current = _cache.Load(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
                      ?? _cache.Load(DefaultCulture)
                      ?? new Dictionary<string, string>();
        return current.Select(kv => new LocalizedString(kv.Key, kv.Value, resourceNotFound: false));
    }

    private LocalizedString Resolve(string key, object[]? formatArgs)
    {
        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var fromCulture = _cache.Load(culture);
        if (fromCulture is not null && fromCulture.TryGetValue(key, out var localized))
            return ToLocalized(key, localized, formatArgs, found: true);

        if (!string.Equals(culture, DefaultCulture, StringComparison.OrdinalIgnoreCase))
        {
            var fallback = _cache.Load(DefaultCulture);
            if (fallback is not null && fallback.TryGetValue(key, out var fb))
                return ToLocalized(key, fb, formatArgs, found: true);
        }

        // Last-resort: return the key itself so missing translations are visible without crashing.
        return ToLocalized(key, key, formatArgs, found: false);
    }

    private static LocalizedString ToLocalized(string key, string value, object[]? args, bool found)
    {
        var formatted = (args is { Length: > 0 }) ? string.Format(CultureInfo.CurrentCulture, value, args) : value;
        return new LocalizedString(key, formatted, resourceNotFound: !found);
    }

    public sealed class ResourceCache
    {
        private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>?> _byCulture = new();
        private readonly string _resourceRoot;
        private readonly ILogger<ResourceCache> _logger;

        public ResourceCache(IWebHostEnvironment env, ILogger<ResourceCache> logger)
        {
            _resourceRoot = Path.Combine(env.ContentRootPath, "Resources");
            _logger = logger;
        }

        public IReadOnlyDictionary<string, string>? Load(string culture)
        {
            return _byCulture.GetOrAdd(culture, c =>
            {
                var path = Path.Combine(_resourceRoot, $"strings.{c}.json");
                if (!File.Exists(path))
                {
                    _logger.LogWarning("No translation file found for culture {Culture} at {Path}", c, path);
                    return null;
                }
                try
                {
                    using var stream = File.OpenRead(path);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                               ?? new Dictionary<string, string>();
                    return dict;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load translation file {Path}", path);
                    return null;
                }
            });
        }
    }
}

public sealed class JsonStringLocalizerFactory : IStringLocalizerFactory
{
    private readonly JsonStringLocalizer.ResourceCache _cache;
    private readonly ILoggerFactory _loggerFactory;

    public JsonStringLocalizerFactory(JsonStringLocalizer.ResourceCache cache, ILoggerFactory loggerFactory)
    {
        _cache = cache;
        _loggerFactory = loggerFactory;
    }

    public IStringLocalizer Create(Type resourceSource) => Build();
    public IStringLocalizer Create(string baseName, string location) => Build();

    private IStringLocalizer Build()
        => new JsonStringLocalizer(_cache, _loggerFactory.CreateLogger<JsonStringLocalizer>());
}
