namespace OpenOnboarding.Application.Exceptions;

public sealed class ScanFailedException(string fileName, string? threatName)
    : Exception($"File '{fileName}' failed security scan" + (threatName != null ? $": {threatName}" : string.Empty))
{
    public string FileName { get; } = fileName;
    public string? ThreatName { get; } = threatName;
}
