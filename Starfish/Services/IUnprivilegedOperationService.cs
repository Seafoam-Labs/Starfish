using Starfish.Models;

namespace Starfish.Services;

public interface IUnprivilegedOperationService
{
    Task<List<AlpmPackageDto>> GetAllPackagesAsync();
    
    Task<List<AlpmPackageDto>> GetAllInstalledPackagesAsync();
}

public class UnprivilegedOperationResult
{
    public bool Success { get; init; }
    public string Output { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
    public int ExitCode { get; init; }
}