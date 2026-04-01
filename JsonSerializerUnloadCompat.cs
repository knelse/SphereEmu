using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;

/// <summary>
/// Clears System.Text.Json reflection caches when the collectible assembly unloads so Godot can
/// reload C# without "Failed to unload assemblies" (see dotnet/runtime#65323, godotengine#78513).
/// </summary>
internal static class JsonSerializerUnloadCompat
{
    [ModuleInitializer]
    internal static void RegisterStjUnloadHook ()
    {
        AssemblyLoadContext.Default.Unloading += _ =>
        {
            try
            {
                var handler = typeof (JsonSerializer).Assembly.GetType ("System.Text.Json.JsonSerializerOptionsUpdateHandler");
                var clear = handler?.GetMethod ("ClearCache", BindingFlags.Static | BindingFlags.Public);
                clear?.Invoke (null, new object?[] { null });
            }
            catch
            {
                // best-effort
            }
        };
    }
}
