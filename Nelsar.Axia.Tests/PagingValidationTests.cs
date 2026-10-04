using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Nelsar.Axia.Application;
using Nelsar.Axia.Application.Common.Validation;
using Nelsar.Axia.Application.Features.Audit.Queries;
using Nelsar.Axia.Application.Features.Computers.Queries;
using Nelsar.Axia.Application.Features.Groups.Queries;
using Nelsar.Axia.Application.Features.OrganizationalUnits.Queries;
using Nelsar.Axia.Application.Features.Users.Queries;
using Nelsar.Axia.Domain.Exceptions;
using Nelsar.Axia.Domain.Repositories;

namespace Nelsar.Axia.Tests;

/// <summary>
/// The bound on a caller-supplied page size, and that it is enforced before a handler runs.
///
/// Every one of these used to be served. <c>pageSize=0</c> returned an empty page while still
/// advertising more and handing back the offset the caller already had, so a client paging "until
/// HasMore is false" looped forever; <c>pageSize=-1</c> reached <c>Take(-1)</c> and left as a 500;
/// <c>pageSize=int.MaxValue</c> defeated paging altogether and overflowed the next offset to a
/// negative number on the way out.
/// </summary>
public class PagingValidationTests
{
    private static IServiceProvider Pipeline(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationLayer();
        services.WithAuthorizedCaller();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Every paged request, not just the one with a handler cheap enough to drive end to end.
    /// A new paged query that forgets <c>.PageSize()</c> is exactly the regression this catches,
    /// so the list is the point rather than an implementation detail of the test.
    /// </summary>
    public static TheoryData<object> PagedRequests() =>
    [
        new SearchUsersQuery(null, 0, null),
        new SearchGroupsQuery(null, 0, null),
        new SearchComputersQuery(null, 0, null),
        new SearchOrganizationalUnitsQuery(null, 0, null),
        new GetRecentAuditEventsQuery(0, null),
    ];

    [Theory]
    [MemberData(nameof(PagedRequests))]
    public async Task EveryPagedRequest_RejectsAPageSizeOfZero(object request)
    {
        var provider = Pipeline();

        var validatorType = typeof(IValidator<>).MakeGenericType(request.GetType());
        var validator = (IValidator?)provider.GetService(validatorType);

        Assert.NotNull(validator);

        var context = new ValidationContext<object>(request);
        var result = await validator.ValidateAsync(context);

        Assert.False(result.IsValid, $"{request.GetType().Name} accepted a page size of 0.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task AnOutOfRangePageSize_IsRefusedBeforeTheHandlerRuns(int pageSize)
    {
        var repo = new Mock<IAuditRepository>();

        var sender = Pipeline(services => services.AddSingleton(repo.Object))
            .GetRequiredService<ISender>();

        var failure = await Assert.ThrowsAsync<DomainValidationException>(
            () => sender.Send(new GetRecentAuditEventsQuery(pageSize, null)));

        // The limits are named, so a client author can fix the call from the response alone.
        Assert.Contains(PagingRules.MaximumPageSize.ToString(), failure.Message);

        // DomainValidationException is what GlobalExceptionHandler maps to 400 — the point being
        // that an out-of-range parameter is a bad request, not the 500 that Take(-1) produced.
        Assert.IsAssignableFrom<DomainException>(failure);

        repo.Verify(x => x.GetRecentAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task APageSizeInsideTheRange_ReachesTheHandler()
    {
        var repo = new Mock<IAuditRepository>();
        repo.Setup(x => x.GetRecentAsync(0, 50)).ReturnsAsync(([], false));

        var sender = Pipeline(services => services.AddSingleton(repo.Object))
            .GetRequiredService<ISender>();

        var page = await sender.Send(new GetRecentAuditEventsQuery(50, null));

        Assert.Equal(50, page.PageSize);
        repo.Verify(x => x.GetRecentAsync(0, 50), Times.Once);
    }

    [Fact]
    public async Task TheLargestPermittedPageSize_IsAccepted()
    {
        var repo = new Mock<IAuditRepository>();
        repo.Setup(x => x.GetRecentAsync(0, PagingRules.MaximumPageSize)).ReturnsAsync(([], false));

        var sender = Pipeline(services => services.AddSingleton(repo.Object))
            .GetRequiredService<ISender>();

        await sender.Send(new GetRecentAuditEventsQuery(PagingRules.MaximumPageSize, null));

        repo.Verify(x => x.GetRecentAsync(0, PagingRules.MaximumPageSize), Times.Once);
    }

    /// <summary>
    /// The generator is bounded by MaxItems alone: a mask expands lazily and is cut only by
    /// <c>Take(MaxItems)</c>, so an unbounded value is a request to materialise as many strings
    /// as the caller cares to name.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task AnOutOfRangeGenerationLimit_IsRefused(int maxItems)
    {
        var sender = Pipeline().GetRequiredService<ISender>();

        await Assert.ThrowsAsync<DomainValidationException>(
            () => sender.Send(new GenerateUserPatternsQuery(
                Pattern: "user{1..2000000000}",
                MaxItems: maxItems,
                RawText: null,
                RegexPattern: null,
                UsernameTemplate: null,
                Transliterate: false,
                CheckConflicts: false)));
    }
}
