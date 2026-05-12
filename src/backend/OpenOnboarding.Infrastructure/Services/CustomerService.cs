using FluentValidation;
using Microsoft.EntityFrameworkCore;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Exceptions;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Domain.Entities;
using OpenOnboarding.Domain.Enums;
using OpenOnboarding.Infrastructure.Persistence;

namespace OpenOnboarding.Infrastructure.Services;

public sealed class CustomerService(
    OnboardingDbContext dbContext,
    IValidator<CreateCustomerRequest> createValidator,
    IValidator<UpdateCustomerRequest> updateValidator) : ICustomerService
{
    public async Task<CustomerProfileDto> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);

        var exists = await dbContext.CustomerProfiles
            .AnyAsync(x => x.ExternalCustomerId == request.ExternalCustomerId, cancellationToken);

        if (exists)
        {
            throw new ConflictException($"A customer profile with externalCustomerId '{request.ExternalCustomerId}' already exists.");
        }

        var profile = new CustomerProfile
        {
            ExternalCustomerId = request.ExternalCustomerId,
            Country = request.Country,
            Email = request.Email,
            MetadataJson = string.IsNullOrWhiteSpace(request.MetadataJson) ? "{}" : request.MetadataJson
        };

        dbContext.CustomerProfiles.Add(profile);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToDto(profile);
    }

    public async Task<CustomerProfileDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var profile = await dbContext.CustomerProfiles
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException($"Customer profile '{id}' was not found.");

        return ToDto(profile);
    }

    public async Task<CustomerProfileDto> GetByExternalIdAsync(string externalId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(externalId))
        {
            throw new ArgumentException("externalId must not be empty.", nameof(externalId));
        }

        var profile = await dbContext.CustomerProfiles
            .FirstOrDefaultAsync(x => x.ExternalCustomerId == externalId, cancellationToken)
            ?? throw new InvalidOperationException($"Customer profile with externalCustomerId '{externalId}' was not found.");

        return ToDto(profile);
    }

    public async Task<CustomerProfileDto> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, cancellationToken);

        var profile = await dbContext.CustomerProfiles
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException($"Customer profile '{id}' was not found.");

        profile.Country = request.Country;
        profile.Email = request.Email;
        profile.MetadataJson = string.IsNullOrWhiteSpace(request.MetadataJson) ? "{}" : request.MetadataJson;

        await dbContext.SaveChangesAsync(cancellationToken);

        return ToDto(profile);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var profile = await dbContext.CustomerProfiles
            .Include(x => x.Sessions)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException($"Customer profile '{id}' was not found.");

        var hasActiveSessions = profile.Sessions.Any(s =>
            s.Status != SessionStatus.Completed && s.Status != SessionStatus.Abandoned);

        if (hasActiveSessions)
        {
            throw new ConflictException($"Customer profile '{id}' has active sessions and cannot be deleted.");
        }

        dbContext.CustomerProfiles.Remove(profile);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<CustomerProfileDto> UpsertByExternalIdAsync(InlineCustomerProfileRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.CustomerProfiles
            .FirstOrDefaultAsync(x => x.ExternalCustomerId == request.ExternalCustomerId, cancellationToken);

        if (existing is not null)
        {
            return ToDto(existing);
        }

        var profile = new CustomerProfile
        {
            ExternalCustomerId = request.ExternalCustomerId,
            Country = request.Country,
            Email = request.Email,
            MetadataJson = string.IsNullOrWhiteSpace(request.MetadataJson) ? "{}" : request.MetadataJson
        };

        dbContext.CustomerProfiles.Add(profile);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToDto(profile);
    }

    private static CustomerProfileDto ToDto(CustomerProfile profile) => new()
    {
        Id = profile.Id,
        ExternalCustomerId = profile.ExternalCustomerId,
        Country = profile.Country,
        Email = profile.Email,
        MetadataJson = profile.MetadataJson
    };
}
