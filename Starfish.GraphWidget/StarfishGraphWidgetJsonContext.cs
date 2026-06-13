using System.Text.Json.Serialization;
using Starfish.Models;

namespace Starfish.GraphWidget;

[JsonSerializable(typeof(List<AlpmPackageDto>))]
[JsonSerializable(typeof(AlpmPackageDto))]
[JsonSerializable(typeof(DisplayOnlyRequest))]
public partial class StarfishGraphWidgetJsonContext : JsonSerializerContext
{
    
}

public class DisplayOnlyRequest
{
    public string RootPackage { get; set; } = "";
    public Dictionary<string, List<string>> DependencyMap { get; set; } = new();
}
