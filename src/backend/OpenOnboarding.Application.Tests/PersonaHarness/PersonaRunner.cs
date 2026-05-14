using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OpenOnboarding.Application.Contracts;
using OpenOnboarding.Application.Tests.TestHelpers;
using OpenOnboarding.Application.Validators;
using OpenOnboarding.Domain.Entities;
using OpenOnboarding.Infrastructure.Persistence;
using OpenOnboarding.Infrastructure.Services;

namespace OpenOnboarding.Application.Tests.PersonaHarness;

/// <summary>
/// Executes a catalog of <see cref="PersonaDefinition"/> entries against a workflow and
/// produces a <see cref="PersonaReport"/> containing pass/fail status and node-transition
/// details for each persona.
/// </summary>
public sealed class PersonaRunner
{
    private readonly Func<Flow> _buildFlow;

    /// <param name="buildFlow">
    /// Factory that creates a fresh <see cref="Flow"/> (including nodes and connections)
    /// for each persona run. A fresh instance is required because each run uses an
    /// isolated in-memory database context.
    /// </param>
    public PersonaRunner(Func<Flow> buildFlow) => _buildFlow = buildFlow;

    /// <summary>
    /// Runs every persona in <paramref name="personas"/> sequentially and aggregates
    /// the results into a <see cref="PersonaReport"/>.
    /// </summary>
    public async Task<PersonaReport> RunAllAsync(IEnumerable<PersonaDefinition> personas,
        CancellationToken cancellationToken = default)
    {
        var report = new PersonaReport();
        foreach (var persona in personas)
            report.Results.Add(await RunPersonaAsync(persona, cancellationToken));
        return report;
    }

    // ── Internal execution ────────────────────────────────────────────────────

    private async Task<PersonaRunResult> RunPersonaAsync(PersonaDefinition persona,
        CancellationToken cancellationToken)
    {
        var actualPath = new List<string>();
        string? failureReason = null;
        var isCompleted = false;

        try
        {
            var db = BuildDbContext();
            var flow = _buildFlow();
            db.Flows.Add(flow);
            await db.SaveChangesAsync(cancellationToken);

            var service = CreateWorkflowService(db);

            var step = await service.StartSessionAsync(new StartSessionRequest
            {
                FlowId = flow.Id,
                CustomerProfile = persona.CustomerProfile
            }, cancellationToken);

            if (step.CurrentNode is not null)
                actualPath.Add(step.CurrentNode.Key);

            foreach (var personaStep in persona.Steps)
            {
                if (step.IsCompleted || step.CurrentNode is null)
                    break;

                step = await service.SubmitStepAsync(
                    step.SessionId,
                    step.CurrentNode.Id,
                    new SubmitStepRequest { Payload = personaStep.Payload },
                    cancellationToken);

                if (step.CurrentNode is not null)
                    actualPath.Add(step.CurrentNode.Key);
            }

            isCompleted = step.IsCompleted;
        }
        catch (Exception ex)
        {
            failureReason = $"Exception during persona execution: {ex.Message}";
        }

        var pathMatches = actualPath.SequenceEqual(persona.ExpectedNodePath);
        var completionMatches = isCompleted == persona.ExpectedCompletion;
        var passed = pathMatches && completionMatches && failureReason is null;

        if (failureReason is null && !passed)
        {
            var reasons = new List<string>();
            if (!pathMatches)
                reasons.Add(
                    $"Node path mismatch: expected [{string.Join(" → ", persona.ExpectedNodePath)}]" +
                    $" but got [{string.Join(" → ", actualPath)}]");
            if (!completionMatches)
                reasons.Add(
                    $"Completion mismatch: expected {persona.ExpectedCompletion} but got {isCompleted}");
            failureReason = string.Join("; ", reasons);
        }

        return new PersonaRunResult
        {
            PersonaName = persona.Name,
            Passed = passed,
            ActualNodePath = actualPath,
            ExpectedNodePath = [.. persona.ExpectedNodePath],
            ActualCompletion = isCompleted,
            ExpectedCompletion = persona.ExpectedCompletion,
            FailureReason = failureReason
        };
    }

    // ── Infrastructure helpers ────────────────────────────────────────────────

    private static OnboardingDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new OnboardingDbContext(options);
    }

    private static WorkflowService CreateWorkflowService(OnboardingDbContext db)
    {
        var customerService = new CustomerService(
            db,
            new CreateCustomerRequestValidator(),
            new UpdateCustomerRequestValidator());

        return new WorkflowService(
            db,
            new StartSessionRequestValidator(),
            new SubmitStepRequestValidator(),
            customerService,
            new ComplianceRuleEvaluator(),
            NullLogger<WorkflowService>.Instance,
            logicNodeExecutors: [],
            new InMemorySessionEventEmitter(),
            new NoOpWebhookService(),
            serviceScopeFactory: null,
            new NoOpDocumentStorageService(),
            new NoOpMetricsService());
    }
}