using OpenOnboarding.Application.Contracts;

namespace OpenOnboarding.Application.Interfaces;

public interface ICustomerService
{
    Task<CustomerProfileDto> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default);
    Task<CustomerProfileDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CustomerProfileDto> GetByExternalIdAsync(string externalId, CancellationToken cancellationToken = default);
    Task<CustomerProfileDto> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the existing profile for <paramref name="request"/>.<see cref="InlineCustomerProfileRequest.ExternalCustomerId"/>
    /// if one exists, or creates a new profile.
    /// </summary>
    Task<CustomerProfileDto> UpsertByExternalIdAsync(InlineCustomerProfileRequest request, CancellationToken cancellationToken = default);
}
