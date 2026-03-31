using Starfish.Models;

namespace Starfish.Services;

public interface IUnprivilegedOperationService
{
    Task<List<AlpmPackageDto>> GetAllPackagesAsync();
}

public class UnprivilegedOperationResult
{
    public bool Success { get; init; }
    public string Output { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
    public int ExitCode { get; init; }
}