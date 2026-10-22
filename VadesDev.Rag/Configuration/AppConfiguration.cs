using Microsoft.Extensions.Configuration;

namespace VadesDev.Rag.Configuration;

/// <summary>
/// Builds the app configuration: appsettings.json, then appsettings.Local.json (secrets), then environment variables.
/// </summary>
public static class AppConfiguration
{
    /// <summary>
    /// Builds the configuration. Later sources override earlier ones.
    /// </summary>
    public static IConfiguration Build()
    {
        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Local.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
    }

    /// <summary>
    /// Reads one section into a settings record, or throws a clear error if the section is missing.
    /// </summary>
    public static T GetRequired<T>(IConfiguration configuration, string sectionName) where T : class
    {
        return configuration.GetSection(sectionName).Get<T>()
               ?? throw new InvalidOperationException($"Section '{sectionName}' is missing in appsettings.json.");
    }
}