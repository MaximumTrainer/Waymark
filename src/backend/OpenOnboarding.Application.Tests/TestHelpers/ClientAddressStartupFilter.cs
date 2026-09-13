using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace OpenOnboarding.Application.Tests.TestHelpers;

/// <summary>
/// Gives a test control over <c>HttpContext.Connection.RemoteIpAddress</c>, which
/// <see cref="Microsoft.AspNetCore.TestHost.TestServer"/> otherwise leaves null.
/// <para>
/// Rate limit partitioning and the forwarded-headers trust check both read the connection address,
/// so a test cannot pose as two different callers without setting it. The middleware runs ahead of
/// everything in <c>Program.cs</c> - including <c>UseForwardedHeaders</c> - so the address it sets
/// is the one the proxy trust check sees.
/// </para>
/// </summary>
internal sealed class ClientAddressStartupFilter : IStartupFilter
{
    /// <summary>Header a test sets to choose the address the request appears to come from.</summary>
    public const string HeaderName = "X-Test-Client-Address";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(HeaderName, out var address)
                    && IPAddress.TryParse(address.ToString(), out var parsed))
                {
                    context.Connection.RemoteIpAddress = parsed;
                }

                await nextMiddleware();
            });

            next(app);
        };
}
