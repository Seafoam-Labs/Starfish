using Starfish.Models;

namespace Starfish.Services;

public class PackageTraversalService(IUnprivilegedOperationService unprivilegedOperationService)
    : IPackageTraversalService
{
    private List<AlpmPackageDto>? _packages;
    private List<AlpmPackageDto>? _installedPackages;
    private Dictionary<string, AlpmPackageDto>? _packageMap;
    private Dictionary<string, List<AlpmPackageDto>>? _providesMap;
    private HashSet<string>? _installedNames;


    private static string StripVersion(string dependency)
    {
        var index = dependency.IndexOfAny(['>', '<', '=']);
        return index == -1 ? dependency : dependency[..index].Trim();
    }

    private IEnumerable<AlpmPackageDto> ResolveDependency(string dependency)
    {
        var name = StripVersion(dependency);
        if (_packageMap != null && _packageMap.TryGetValue(name, out var pkg))
        {
            yield return pkg;
        }

        if (_providesMap == null || !_providesMap.TryGetValue(name, out var providers)) yield break;
        foreach (var provider in providers)
        {
            yield return provider;
        }
    }

    public async Task<Dictionary<string, List<string>>> FetchFullDependencyPackageInformation(string rootPackageName,
        int depth = 0)
    {
        await InitializeAsync();
        var dictionary = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Name, int CurrentDepth)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        queue.Enqueue((rootPackageName, 0));
        visited.Add(rootPackageName);

        while (queue.Count > 0)
        {
            var (currentName, currentDepth) = queue.Dequeue();

            if (_packageMap == null || !_packageMap.TryGetValue(currentName, out var package))
            {
                continue;
            }

            if (currentDepth >= depth) continue;

            var resolvedDeps = package.Depends
                .SelectMany(ResolveDependency)
                .Select(p => p.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            dictionary[currentName] = resolvedDeps;

            foreach (var dep in resolvedDeps.Where(visited.Add))
            {
                queue.Enqueue((dep, currentDepth + 1));
            }
        }

        return dictionary;
    }

    public async Task<Dictionary<string, List<string>>> FetchInverseFullDependencyPackageInformation(
        string rootPackageName, int depth = 0)
    {
        await InitializeAsync();
        var dictionary = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Name, int CurrentDepth)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        queue.Enqueue((rootPackageName, 0));
        visited.Add(rootPackageName);

        while (queue.Count > 0)
        {
            var (currentName, currentDepth) = queue.Dequeue();

            if (_packageMap == null || !_packageMap.TryGetValue(currentName, out var package))
            {
                continue;
            }

            if (currentDepth >= depth) continue;
            dictionary[currentName] = package.RequiredBy;
            foreach (var req in package.RequiredBy.Where(visited.Add))
            {
                queue.Enqueue((req, currentDepth + 1));
            }
        }

        return dictionary;
    }

    public async Task<Dictionary<string, List<string>>> FetchFullDependencyPackageInformationInstalled(
        string rootPackageName, int depth = 0)
    {
        await InitializeAsync();
        var dictionary = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Name, int CurrentDepth)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        queue.Enqueue((rootPackageName, 0));
        visited.Add(rootPackageName);

        while (queue.Count > 0)
        {
            var (currentName, currentDepth) = queue.Dequeue();

            if (_packageMap == null || !_packageMap.TryGetValue(currentName, out var package) ||
                !_installedNames!.Contains(package.Name))
            {
                continue;
            }

            if (currentDepth >= depth) continue;

            var installedDeps = package.Depends
                .SelectMany(ResolveDependency)
                .Where(p => _installedNames!.Contains(p.Name))
                .Select(p => p.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            dictionary[currentName] = installedDeps;

            foreach (var dep in installedDeps.Where(visited.Add))
            {
                queue.Enqueue((dep, currentDepth + 1));
            }
        }

        return dictionary;
    }

    public async Task<Dictionary<string, List<string>>> FetchInverseFullDependencyPackageInformationInstalled(
        string rootPackageName, int depth = 0)
    {
        await InitializeAsync();
        var dictionary = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Name, int CurrentDepth)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        queue.Enqueue((rootPackageName, 0));
        visited.Add(rootPackageName);

        while (queue.Count > 0)
        {
            var (currentName, currentDepth) = queue.Dequeue();

            if (_packageMap == null || !_packageMap.TryGetValue(currentName, out var package) ||
                !_installedNames!.Contains(package.Name))
            {
                continue;
            }

            if (currentDepth >= depth) continue;

            var installedReqs = package.RequiredBy.Where(req => _installedNames!.Contains(req)).ToList();
            dictionary[currentName] = installedReqs;

            foreach (var req in installedReqs.Where(visited.Add))
            {
                queue.Enqueue((req, currentDepth + 1));
            }
        }

        return dictionary;
    }

    public async Task<AlpmPackageDto?> GetPackageInfo(string packageName)
    {
        await InitializeAsync();
        return _packageMap?.TryGetValue(packageName, out var package) == true ? package : null;
    }

    private async Task InitializeAsync()
    {
        if (_packages != null && _installedPackages != null && _providesMap != null && _packageMap != null) return;
        _packages = await unprivilegedOperationService.GetAllPackagesAsync();
        _installedPackages = await unprivilegedOperationService.GetAllInstalledPackagesAsync();

        _packageMap = _packages.ToDictionary(x => x.Name, x => x, StringComparer.OrdinalIgnoreCase);
        _installedNames = new HashSet<string>(_installedPackages.Select(x => x.Name), StringComparer.OrdinalIgnoreCase);

        _providesMap = new Dictionary<string, List<AlpmPackageDto>>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in _packages)
        {
            foreach (var baseProvide in package.Provides)
            {
                if (!_providesMap.TryGetValue(baseProvide, out var value))
                {
                    value = [];
                    _providesMap[baseProvide] = value;
                }

                value.Add(package);
            }
        }

        CalculateRequiredBy();
    }

    private void CalculateRequiredBy()
    {
        if (_packages == null) return;

        foreach (var package in _packages)
        {
            package.RequiredBy.Clear();
        }

        foreach (var package in _packages)
        {
            foreach (var dep in package.Depends)
            {
                foreach (var dependencyPackage in ResolveDependency(dep))
                {
                    if (!dependencyPackage.RequiredBy.Contains(package.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        dependencyPackage.RequiredBy.Add(package.Name);
                    }
                }
            }
        }
    }
}