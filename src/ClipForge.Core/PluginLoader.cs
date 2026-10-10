using System.Diagnostics;
using System.Reflection;

namespace ClipForge.Core;

/// <summary>
/// Dead-simple plugin discovery: scans plugins/ next to the exe for exported types
/// implementing T with a parameterless constructor. No versioning, no sandboxing,
/// no signing — a bad DLL is skipped with a Debug note, never a crash.
/// </summary>
public static class PluginLoader
{
    /// <summary>
    /// Single-implementation seam (secrets, search, store): the first plugin
    /// implementation wins; otherwise the shipped built-in is used.
    /// </summary>
    public static T Load<T>(T builtin) where T : class
    {
        ArgumentNullException.ThrowIfNull(builtin);
        foreach (var impl in Discover<T>())
            return impl;
        return builtin;
    }

    /// <summary>
    /// Multi-implementation seam (format handlers): the built-ins plus every
    /// plugin implementation found, ordered by the caller.
    /// </summary>
    public static IReadOnlyList<T> LoadAll<T>(IEnumerable<T> builtins) where T : class
    {
        ArgumentNullException.ThrowIfNull(builtins);
        var all = new List<T>(builtins);
        all.AddRange(Discover<T>());
        return all;
    }

    private static IEnumerable<T> Discover<T>() where T : class
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "plugins");
        if (!Directory.Exists(dir)) yield break;
        foreach (string dll in Directory.GetFiles(dir, "*.dll"))
        {
            Assembly asm;
            try
            {
                asm = Assembly.LoadFrom(dll);
            }
            // CA1031: a bad plugin DLL must never take down the app — skip it with a note.
            #pragma warning disable CA1031
            catch (Exception ex)
            {
                Debug.WriteLine($"ClipForge: skipping plugin {dll}: {ex.Message}");
                continue;
            }
            #pragma warning restore CA1031

            Type[] types;
            try
            {
                types = asm.GetExportedTypes();
            }
            // CA1031: same as above — ReflectionTypeLoadException on hostile DLLs.
            #pragma warning disable CA1031
            catch (Exception ex)
            {
                Debug.WriteLine($"ClipForge: skipping plugin {dll}: {ex.Message}");
                continue;
            }
            #pragma warning restore CA1031

            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsInterface) continue;
                if (!typeof(T).IsAssignableFrom(type)) continue;
                if (type.GetConstructor(Type.EmptyTypes) is null) continue;
                T? instance;
                try
                {
                    instance = (T?)Activator.CreateInstance(type);
                }
                // CA1031: a throwing plugin constructor must not take down the app.
                #pragma warning disable CA1031
                catch (Exception ex)
                {
                    Debug.WriteLine($"ClipForge: plugin {type.FullName} constructor failed: {ex.Message}");
                    continue;
                }
                #pragma warning restore CA1031
                if (instance is not null) yield return instance;
            }
        }
    }
}
