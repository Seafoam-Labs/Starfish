using Starfish.Models;

namespace Starfish.Services;

public class PackageTraversalService(IUnprivilegedOperationService unprivilegedOperationService)
    : IPackageTraversalService
{
    private List<AlpmPackageDto>? _packages = null;

    public async Task<Dictionary<string, List<string>>> FetchFullDepdencyPackageInfomation(string rootPackageName, int depth = 0)
    {
        await InitializeAsync();
        var dictionary = new Dictionary<string, List<string>>();
        var queue = new Queue<(string Name, int CurrentDepth)>();
        var visited = new HashSet<string>();

        queue.Enqueue((rootPackageName, 0));
        visited.Add(rootPackageName);

        while (queue.Count > 0)
        {
            var (currentName, currentDepth) = queue.Dequeue();

            var package = _packages?.FirstOrDefault(x => x.Name == currentName);
            if (package == null)
            {
                continue;
            }

            if (currentDepth >= depth) continue;
            dictionary[currentName] = package.Depends;
            foreach (var dep in package.Depends.Where(dep => !visited.Contains(dep)))
            {
                visited.Add(dep);
                queue.Enqueue((dep, currentDepth + 1));
            }
        }

        return dictionary;
    }
    
    private async Task InitializeAsync()
    {
        _packages ??= await unprivilegedOperationService.GetAllPackagesAsync();
    }
}