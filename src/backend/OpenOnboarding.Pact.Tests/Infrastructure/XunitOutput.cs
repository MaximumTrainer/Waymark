using PactNet.Infrastructure.Outputters;
using Xunit.Abstractions;

namespace OpenOnboarding.Pact.Tests.Infrastructure;

internal sealed class XunitOutput(ITestOutputHelper output) : IOutput
{
    public void WriteLine(string line) => output.WriteLine(line);
}
