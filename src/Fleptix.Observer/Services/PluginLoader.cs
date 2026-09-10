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
        // 1. Check if Fleptix.TimeMachine is already referenced/loaded in the current AppDomain
        var loadedAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, "Fleptix.TimeMachine", StringComparison.OrdinalIgnoreCase));

        if (loadedAssembly != null)
        {
            if (TryRegisterFromAssembly(loadedAssembly, services, configuration, "AppDomain reference"))
            {
                return services;
            }
        }

        // 2. Probe known deployment and development locations
        var baseDir = AppContext.BaseDirectory;
        var currentDir = Directory.GetCurrentDirectory();

        var probedPaths = new[]
        {
            Path.Combine(baseDir, "Fleptix.TimeMachine.dll"),
            Path.Combine(baseDir, "plugins", "Fleptix.TimeMachine.dll"),
            "/app/Fleptix.TimeMachine.dll",
            "/app/plugins/Fleptix.TimeMachine.dll",
            // Relative development output probing
            Path.Combine(baseDir, "..", "..", "..", "..", "Fleptix.TimeMachine", "bin", "Debug", "net10.0", "Fleptix.TimeMachine.dll"),
            Path.Combine(baseDir, "..", "..", "..", "..", "Fleptix.TimeMachine", "bin", "Release", "net10.0", "Fleptix.TimeMachine.dll"),
            Path.Combine(baseDir, "..", "..", "Fleptix.TimeMachine", "bin", "Debug", "net10.0", "Fleptix.TimeMachine.dll"),
            Path.Combine(baseDir, "..", "..", "Fleptix.TimeMachine", "bin", "Release", "net10.0", "Fleptix.TimeMachine.dll"),
            Path.Combine(currentDir, "src", "Fleptix.TimeMachine", "bin", "Debug", "net10.0", "Fleptix.TimeMachine.dll"),
            Path.Combine(currentDir, "src", "Fleptix.TimeMachine", "bin", "Release", "net10.0", "Fleptix.TimeMachine.dll")
        };

        foreach (var candidate in probedPaths)
        {
            try
            {
                var fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                {
                    var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
                    if (TryRegisterFromAssembly(assembly, services, configuration, fullPath))
                    {
                        return services;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Fleptix] Debug: Candidate '{candidate}' probe failed: {ex.Message}");
            }
        }

        // Community Fallback: Open-core null implementations
        Console.WriteLine("[Fleptix] Running in Open-Core Community edition (Fleptix Time Machine plugin not detected).");
        services.AddSingleton<ITimeMachineSettingsStore, NullTimeMachineSettingsStore>();
        services.AddSingleton<ISnapshotService, NullSnapshotService>();

        return services;
    }

    private static bool TryRegisterFromAssembly(
        Assembly assembly, 
        IServiceCollection services, 
        IConfiguration configuration,
        string sourceLocation)
    {
        try
        {
            var pluginType = assembly.GetType("Fleptix.TimeMachine.TimeMachinePlugin");
            var registerMethod = pluginType?.GetMethod("RegisterServices", BindingFlags.Public | BindingFlags.Static);

            if (registerMethod != null)
            {
                registerMethod.Invoke(null, new object[] { services, configuration });
                Console.WriteLine($"[Fleptix] Successfully activated commercial plugin from {sourceLocation}");
                return true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Fleptix] Warning: Failed to register commercial plugin from '{sourceLocation}': {ex.Message}");
        }

        return false;
    }
}
