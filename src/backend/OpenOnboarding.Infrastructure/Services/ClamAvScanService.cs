using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenOnboarding.Application.Exceptions;
using OpenOnboarding.Application.Interfaces;

namespace OpenOnboarding.Infrastructure.Services;

public sealed class ClamAvScanService : IVirusScanService
{
    private readonly string _host;
    private readonly int _port;
    private readonly TimeSpan _timeout;
    private readonly ILogger<ClamAvScanService> _logger;

    public ClamAvScanService(IConfiguration configuration, ILogger<ClamAvScanService> logger)
    {
        _host = configuration["VirusScan:Host"] ?? "clamav";
        _port = configuration.GetValue<int>("VirusScan:Port", 3310);
        _timeout = TimeSpan.FromSeconds(configuration.GetValue<int>("VirusScan:TimeoutSeconds", 30));
        _logger = logger;
    }

    public async Task<ScanResult> ScanAsync(Stream stream, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);

        try
        {
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(_host, _port, cts.Token);
            await using var networkStream = tcpClient.GetStream();

            // Send INSTREAM command (newline-terminated prefix 'n')
            var command = "nINSTREAM\n"u8.ToArray();
            await networkStream.WriteAsync(command, cts.Token);

            // Send stream data in chunks with 4-byte big-endian length prefix
            var buffer = new byte[4096];
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer, cts.Token)) > 0)
            {
                var sizeBytes = new byte[4];
                sizeBytes[0] = (byte)(bytesRead >> 24);
                sizeBytes[1] = (byte)(bytesRead >> 16);
                sizeBytes[2] = (byte)(bytesRead >> 8);
                sizeBytes[3] = (byte)bytesRead;
                await networkStream.WriteAsync(sizeBytes, cts.Token);
                await networkStream.WriteAsync(buffer.AsMemory(0, bytesRead), cts.Token);
            }

            // Terminate stream with 4 zero bytes
            await networkStream.WriteAsync(new byte[4], cts.Token);
            await networkStream.FlushAsync(cts.Token);

            // Read response line
            using var reader = new StreamReader(networkStream, Encoding.ASCII, leaveOpen: true);
            var response = await reader.ReadLineAsync(cts.Token) ?? string.Empty;

            _logger.LogDebug("ClamAV response: {Response}", response);

            const string prefix = "stream: ";
            if (response.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var result = response[prefix.Length..].Trim();
                if (result.Equals("OK", StringComparison.OrdinalIgnoreCase))
                    return new ScanResult(true, null);

                if (result.EndsWith(" FOUND", StringComparison.OrdinalIgnoreCase))
                {
                    var threatName = result[..^" FOUND".Length].Trim();
                    return new ScanResult(false, threatName);
                }

                if (result.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                    throw new ScanServiceUnavailableException($"ClamAV error: {result}");
            }

            throw new ScanServiceUnavailableException($"Unexpected ClamAV response: {response}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Virus scan timed out after {_timeout.TotalSeconds} seconds.");
        }
        catch (ScanServiceUnavailableException)
        {
            throw;
        }
        catch (TimeoutException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to ClamAV at {Host}:{Port}", _host, _port);
            throw new ScanServiceUnavailableException($"Failed to connect to ClamAV: {ex.Message}", ex);
        }
    }
}
