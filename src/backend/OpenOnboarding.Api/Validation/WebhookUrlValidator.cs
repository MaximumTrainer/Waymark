using System.Net;
using System.Net.Sockets;

namespace OpenOnboarding.Api.Validation;

public static class WebhookUrlValidator
{
    private static readonly IPNetwork[] BlockedNetworks =
    [
        IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("::1/128"),
        IPNetwork.Parse("169.254.0.0/16"),
        IPNetwork.Parse("fe80::/10"),
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("100.64.0.0/10"),    // Carrier-grade NAT
        IPNetwork.Parse("0.0.0.0/8"),
        IPNetwork.Parse("fc00::/7"),         // Unique local IPv6
    ];

    public static bool IsValidPublicUrl(string url, bool allowPrivateNetworks = false)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase))
            return false;

        if (allowPrivateNetworks)
            return true;

        var host = uri.Host;

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return false;

        if (host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            return false;

        if (IPAddress.TryParse(host, out var ip))
        {
            if (IPAddress.IsLoopback(ip))
                return false;

            foreach (var network in BlockedNetworks)
            {
                if (network.Contains(ip))
                    return false;
            }
        }

        return true;
    }
}
