using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;

/// Godot cannot unload the collectible assembly while System.Text.Json still holds reflection
/// caches
internal static class JsonSerializerUnloadCompat
{
#pragma warning disable CA2255 // Intentional: STJ cache clear must run on collectible ALC unload for Godot C# reload.
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
#pragma warning restore CA2255
}
