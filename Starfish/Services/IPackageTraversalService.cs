using Starfish.Models;

namespace Starfish.Services;

public interface IPackageTraversalService
{
    Task<Dictionary<string, List<string>>> FetchFullDependencyPackageInformation(string rootPackageName, int depth = 0);
    
    Task<Dictionary<string, List<string>>> FetchInverseFullDependencyPackageInformation(string rootPackageName, int depth = 0);
    
    Task<Dictionary<string, List<string>>> FetchFullDependencyPackageInformationInstalled(string rootPackageName, int depth = 0);
    
    Task<Dictionary<string, List<string>>> FetchInverseFullDependencyPackageInformationInstalled(string rootPackageName, int depth = 0);
    
    Task<AlpmPackageDto?> GetPackageInfo(string packageName);
}