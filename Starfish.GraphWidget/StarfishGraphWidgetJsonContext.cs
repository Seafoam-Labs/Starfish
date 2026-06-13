using System.Text.Json.Serialization;
using Starfish.Models;

namespace Starfish.GraphWidget;

[JsonSerializable(typeof(List<AlpmPackageDto>))]
[JsonSerializable(typeof(AlpmPackageDto))]
public partial class StarfishGraphWidgetJsonContext : JsonSerializerContext
{
    
}
