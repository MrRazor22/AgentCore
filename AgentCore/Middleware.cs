using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgentCore;

/// <summary>
/// Defines a middleware component for processing async operations.
/// </summary>
/// <typeparam name="TInput">The input type.</typeparam>
/// <typeparam name="TOutput">The output type.</typeparam>
public interface IMiddleware<TInput, TOutput>
{
    /// <summary>
    /// Processes the input and produces an output.
    /// </summary>
    /// <param name="input">The input to process.</param>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The processed output.</returns>
    Task<TOutput> InvokeAsync(
        TInput input,
        Func<TInput, CancellationToken, Task<TOutput>> next,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A pipeline that chains multiple middleware components together.
/// </summary>
/// <typeparam name="TInput">The input type.</typeparam>
/// <typeparam name="TOutput">The output type.</typeparam>
public sealed class MiddlewarePipeline<TInput, TOutput> : IMiddleware<TInput, TOutput>
{
    private readonly List<IMiddleware<TInput, TOutput>> _middlewares = new();
    private readonly Func<TInput, CancellationToken, Task<TOutput>> _terminalHandler;

    /// <summary>
    /// Creates a middleware pipeline with a terminal handler.
    /// </summary>
    /// <param name="terminalHandler">The final handler in the pipeline.</param>
    public MiddlewarePipeline(Func<TInput, CancellationToken, Task<TOutput>> terminalHandler)
    {
        _terminalHandler = terminalHandler ?? throw new ArgumentNullException(nameof(terminalHandler));
    }

    /// <summary>
    /// Adds a middleware to the pipeline.
    /// </summary>
    /// <param name="middleware">The middleware to add.</param>
    public void Use(IMiddleware<TInput, TOutput> middleware)
    {
        if (middleware != null)
        {
            _middlewares.Add(middleware);
        }
    }

    /// <summary>
    /// Processes the input through the middleware pipeline.
    /// </summary>
    /// <param name="input">The input to process.</param>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The processed output.</returns>
    public Task<TOutput> InvokeAsync(
        TInput input,
        Func<TInput, CancellationToken, Task<TOutput>> next,
        CancellationToken cancellationToken = default)
    {
        return InvokeAsyncWithTerminal(input, cancellationToken);
    }

    /// <summary>
    /// Processes the input through the middleware pipeline with the pre-configured terminal handler.
    /// </summary>
    /// <param name="input">The input to process.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The processed output.</returns>
    public Task<TOutput> InvokeAsyncWithTerminal(
        TInput input,
        CancellationToken cancellationToken = default)
    {
        // Build the chain: each middleware calls the next one
        Task<TOutput> Chain(int index, TInput currentInput)
        {
            if (index >= _middlewares.Count)
            {
                // We've reached the end of the middleware chain, call the terminal handler
                return _terminalHandler(currentInput, cancellationToken);
            }

            var middleware = _middlewares[index];
            return middleware.InvokeAsync(
                currentInput,
                (innerInput, ct) => Chain(index + 1, innerInput),
                cancellationToken);
        }

        // Start the chain with the first middleware
        return Chain(0, input);
    }
}