namespace Fleptix.Observer.Services;

using System.Reflection;
using System.Runtime.Loader;
using Fleptix.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Probes and dynamically activates proprietary commercial extensions (such as Fleptix.TimeMachine)
/// at runtime without compile-time project dependencies in the open-source codebase.
/// </summary>
public static class PluginLoader
{
    /// <summary>
    /// Searches for and registers the commercial Time Machine plugin if present;
    /// otherwise, gracefully registers open-source Null Object fallbacks.
    /// </summary>
    public static IServiceCollection AddFleptixTimeMachine(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        var probedPaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "plugins", "Fleptix.TimeMachine.dll"),
            Path.Combine(AppContext.BaseDirectory, "Fleptix.TimeMachine.dll"),
            "/app/plugins/Fleptix.TimeMachine.dll",
            // Local dev path: relative project output probing
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Fleptix.TimeMachine", "bin", "Debug", "net10.0", "Fleptix.TimeMachine.dll")
        };

        string? resolvedPath = null;
        foreach (var candidate in probedPaths)
        {
            try
            {
                var fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                {
                    resolvedPath = fullPath;
                    break;
                }
            }
            catch
            {
                // Path resolution error or invalid candidate; ignore and check next
            }
        }

        if (resolvedPath != null)
        {
            try
            {
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(resolvedPath);
                var pluginType = assembly.GetType("Fleptix.TimeMachine.TimeMachinePlugin");
                var registerMethod = pluginType?.GetMethod("RegisterServices", BindingFlags.Public | BindingFlags.Static);

                if (registerMethod != null)
                {
                    registerMethod.Invoke(null, new object[] { services, configuration });
                    Console.WriteLine($"[Fleptix] Successfully activated commercial plugin: {resolvedPath}");
                    return services;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Fleptix] Warning: Failed to load commercial plugin from '{resolvedPath}': {ex.Message}. Falling back to Community mode.");
            }
        }

        // Community Fallback: Open-core null implementations
        Console.WriteLine("[Fleptix] Running in Open-Core Community edition (Fleptix Time Machine plugin not detected).");
        services.AddSingleton<ITimeMachineSettingsStore, NullTimeMachineSettingsStore>();
        services.AddSingleton<ISnapshotService, NullSnapshotService>();

        return services;
    }
}
