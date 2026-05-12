using FluentValidation;
using Microsoft.EntityFrameworkCore;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Exceptions;
using OpenOnboarding.Application.Validators;
using OpenOnboarding.Domain.Entities;
using OpenOnboarding.Domain.Enums;
using OpenOnboarding.Infrastructure.Persistence;
using OpenOnboarding.Infrastructure.Services;

namespace OpenOnboarding.Application.Tests;

public sealed class CustomerServiceTests
{
    [Fact]
    public async Task CreateAsync_ValidPayload_ReturnsDto()
    {
        var db = BuildDbContext();
        var service = CreateService(db);

        var result = await service.CreateAsync(new CreateCustomerRequest
        {
            ExternalCustomerId = "ext-001",
            Country = "US",
            Email = "alice@example.com",
            MetadataJson = "{}"
        });

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("ext-001", result.ExternalCustomerId);
        Assert.Equal("US", result.Country);
        Assert.Equal("alice@example.com", result.Email);
    }

    [Fact]
    public async Task CreateAsync_DuplicateExternalId_ThrowsConflictException()
    {
        var db = BuildDbContext();
        var service = CreateService(db);

        await service.CreateAsync(new CreateCustomerRequest { ExternalCustomerId = "ext-dup" });

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(new CreateCustomerRequest { ExternalCustomerId = "ext-dup" }));
    }

    [Fact]
    public async Task CreateAsync_InvalidEmail_ThrowsValidationException()
    {
        var db = BuildDbContext();
        var service = CreateService(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(new CreateCustomerRequest
            {
                ExternalCustomerId = "ext-bad-email",
                Email = "not-an-email"
            }));
    }

    [Fact]
    public async Task CreateAsync_InvalidMetadataJson_ThrowsValidationException()
    {
        var db = BuildDbContext();
        var service = CreateService(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(new CreateCustomerRequest
            {
                ExternalCustomerId = "ext-bad-json",
                MetadataJson = "{not valid json"
            }));
    }

    [Fact]
    public async Task GetByIdAsync_ExistingId_ReturnsDto()
    {
        var db = BuildDbContext();
        var service = CreateService(db);
        var created = await service.CreateAsync(new CreateCustomerRequest { ExternalCustomerId = "ext-get" });

        var result = await service.GetByIdAsync(created.Id);

        Assert.Equal(created.Id, result.Id);
        Assert.Equal("ext-get", result.ExternalCustomerId);
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ThrowsInvalidOperationException()
    {
        var db = BuildDbContext();
        var service = CreateService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetByIdAsync(Guid.NewGuid()));

        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetByExternalIdAsync_ExistingId_ReturnsDto()
    {
        var db = BuildDbContext();
        var service = CreateService(db);
        await service.CreateAsync(new CreateCustomerRequest { ExternalCustomerId = "ext-lookup" });

        var result = await service.GetByExternalIdAsync("ext-lookup");

        Assert.Equal("ext-lookup", result.ExternalCustomerId);
    }

    [Fact]
    public async Task GetByExternalIdAsync_NotFound_ThrowsInvalidOperationException()
    {
        var db = BuildDbContext();
        var service = CreateService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetByExternalIdAsync("non-existent"));

        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetByExternalIdAsync_EmptyString_ThrowsArgumentException()
    {
        var db = BuildDbContext();
        var service = CreateService(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetByExternalIdAsync(string.Empty));
    }

    [Fact]
    public async Task UpdateAsync_ValidPayload_UpdatesAndReturnsDto()
    {
        var db = BuildDbContext();
        var service = CreateService(db);
        var created = await service.CreateAsync(new CreateCustomerRequest { ExternalCustomerId = "ext-upd" });

        var result = await service.UpdateAsync(created.Id, new UpdateCustomerRequest
        {
            Country = "GB",
            Email = "bob@example.com",
            MetadataJson = "{\"key\":\"value\"}"
        });

        Assert.Equal("GB", result.Country);
        Assert.Equal("bob@example.com", result.Email);
        Assert.Equal("{\"key\":\"value\"}", result.MetadataJson);
    }

    [Fact]
    public async Task UpdateAsync_InvalidEmail_ThrowsValidationException()
    {
        var db = BuildDbContext();
        var service = CreateService(db);
        var created = await service.CreateAsync(new CreateCustomerRequest { ExternalCustomerId = "ext-upd-val" });

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.UpdateAsync(created.Id, new UpdateCustomerRequest { Email = "bad-email" }));
    }

    [Fact]
    public async Task DeleteAsync_NoSessions_Succeeds()
    {
        var db = BuildDbContext();
        var service = CreateService(db);
        var created = await service.CreateAsync(new CreateCustomerRequest { ExternalCustomerId = "ext-del" });

        await service.DeleteAsync(created.Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetByIdAsync(created.Id));
        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteAsync_WithActiveSessions_ThrowsConflictException()
    {
        var db = BuildDbContext();
        var service = CreateService(db);
        var created = await service.CreateAsync(new CreateCustomerRequest { ExternalCustomerId = "ext-del-active" });

        db.Sessions.Add(new Session
        {
            CustomerProfileId = created.Id,
            FlowId = Guid.NewGuid(),
            Status = SessionStatus.Started
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() => service.DeleteAsync(created.Id));
    }

    [Fact]
    public async Task DeleteAsync_WithCompletedSessionsOnly_Succeeds()
    {
        var db = BuildDbContext();
        var service = CreateService(db);
        var created = await service.CreateAsync(new CreateCustomerRequest { ExternalCustomerId = "ext-del-done" });

        db.Sessions.Add(new Session
        {
            CustomerProfileId = created.Id,
            FlowId = Guid.NewGuid(),
            Status = SessionStatus.Completed
        });
        await db.SaveChangesAsync();

        await service.DeleteAsync(created.Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetByIdAsync(created.Id));
        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteAsync_WithAbandonedSessionsOnly_Succeeds()
    {
        var db = BuildDbContext();
        var service = CreateService(db);
        var created = await service.CreateAsync(new CreateCustomerRequest { ExternalCustomerId = "ext-del-abandoned" });

        db.Sessions.Add(new Session
        {
            CustomerProfileId = created.Id,
            FlowId = Guid.NewGuid(),
            Status = SessionStatus.Abandoned
        });
        await db.SaveChangesAsync();

        await service.DeleteAsync(created.Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetByIdAsync(created.Id));
        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpsertByExternalIdAsync_NewProfile_CreatesAndReturns()
    {
        var db = BuildDbContext();
        var service = CreateService(db);

        var result = await service.UpsertByExternalIdAsync(new InlineCustomerProfileRequest
        {
            ExternalCustomerId = "ext-upsert-new",
            Country = "US",
            Email = "carol@example.com"
        });

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("ext-upsert-new", result.ExternalCustomerId);
    }

    [Fact]
    public async Task UpsertByExternalIdAsync_ExistingProfile_ReturnsExisting()
    {
        var db = BuildDbContext();
        var service = CreateService(db);

        var first = await service.UpsertByExternalIdAsync(new InlineCustomerProfileRequest
        {
            ExternalCustomerId = "ext-upsert-exist",
            Country = "US"
        });

        var second = await service.UpsertByExternalIdAsync(new InlineCustomerProfileRequest
        {
            ExternalCustomerId = "ext-upsert-exist",
            Country = "GB"
        });

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("US", second.Country);
    }

    private static CustomerService CreateService(OnboardingDbContext db) =>
        new(db, new CreateCustomerRequestValidator(), new UpdateCustomerRequestValidator());

    private static OnboardingDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new OnboardingDbContext(options);
    }
}
