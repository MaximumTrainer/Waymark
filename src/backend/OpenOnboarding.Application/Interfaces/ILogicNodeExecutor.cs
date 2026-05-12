using OpenOnboarding.Domain.Entities;

namespace OpenOnboarding.Application.Interfaces;

public interface ILogicNodeExecutor
{
    string ActionName { get; }

    Task ExecuteAsync(
        Node node,
        Session session,
        IReadOnlyDictionary<string, object?> latestPayload,
        CancellationToken cancellationToken = default);
}
