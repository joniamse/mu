namespace MuCustom.PlugIns;

/// <summary>
/// Entry point of the assembly for the OpenMU startup.
/// </summary>
/// <remarks>
/// OpenMU discovers plugins in the loaded assemblies only. The startup calls <see cref="EnsureLoaded"/>
/// (see patches/openmu-core.patch), so that this assembly is loaded before the plugins are discovered.
/// </remarks>
public static class MuCustomPlugIns
{
    /// <summary>
    /// Makes sure that this assembly is loaded. Calling it is enough, it does nothing else.
    /// </summary>
    public static void EnsureLoaded()
    {
    }
}
