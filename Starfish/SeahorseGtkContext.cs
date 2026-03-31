using System.Text.Json.Serialization;
using Starfish.Models;

namespace Starfish;

[JsonSerializable(typeof(List<AlpmPackageDto>))]
[JsonSerializable(typeof(AlpmPackageDto))]
internal partial class SeahorseGtkContext : JsonSerializerContext
{
    
}