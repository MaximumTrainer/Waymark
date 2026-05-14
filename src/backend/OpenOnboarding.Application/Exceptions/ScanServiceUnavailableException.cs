namespace OpenOnboarding.Application.Exceptions;

public sealed class ScanServiceUnavailableException : Exception
{
    public ScanServiceUnavailableException() : base("Virus scan service is unavailable.") { }
    public ScanServiceUnavailableException(string message) : base(message) { }
    public ScanServiceUnavailableException(string message, Exception innerException) : base(message, innerException) { }
}
