using FluentValidation;
using MediatR;
using Nelsar.Axia.Domain.Exceptions;

namespace Nelsar.Axia.Application.Common.Behaviours;

/// <summary>
/// Runs every validator registered for a request before its handler is reached.
///
/// The failure is raised as <see cref="DomainValidationException"/> rather than
/// FluentValidation's own <c>ValidationException</c> on purpose: GlobalExceptionHandler already
/// maps that to 400 with the message returned verbatim, so validation arrives at the client the
/// same way a domain rule breach does. Introducing a second exception type would have meant a
/// second mapping to keep in step, and a validator whose message never reached the caller if
/// anyone forgot.
///
/// A request with no validators is passed straight through — most queries have nothing to check,
/// and requiring an empty validator for each would be ceremony with no reader.
/// </summary>
internal sealed class ValidationBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehaviour(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(_validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var messages = results
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .Select(f => f.ErrorMessage)
            .Distinct()
            .ToList();

        if (messages.Count > 0)
            throw new DomainValidationException(string.Join(" ", messages));

        return await next();
    }
}
