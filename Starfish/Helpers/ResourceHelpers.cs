using System.Reflection;

namespace Starfish.Helpers;

public static class ResourceHelper
{
    private static readonly Assembly Assembly = typeof(ResourceHelper).Assembly;
     
    public static string LoadUiFile(string relativePath)
    {
        var resourceName = "Starfish." + relativePath.Replace('/', '.').Replace('\\', '.');
        using var stream = Assembly.GetManifestResourceStream(resourceName)
                           ?? throw new FileNotFoundException($"Embedded resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
     
    public static string LoadAsset(string relativePath)
    {
        return LoadUiFile(relativePath);
    }
}