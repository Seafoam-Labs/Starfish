using Starfish.Models;

namespace Starfish.Services;

public interface IPackageTraversalService
{
    Task<Dictionary<string, List<string>>> FetchFullDepdencyPackageInfomation(string rootPackageName, int depth = 0);
}