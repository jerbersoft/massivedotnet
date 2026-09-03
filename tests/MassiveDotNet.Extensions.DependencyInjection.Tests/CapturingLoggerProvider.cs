using System.Text;
using Microsoft.Extensions.Logging;

namespace MassiveDotNet.Extensions.DependencyInjection.Tests;

/// <summary>Collects every formatted log message, including scopes' state, as flat text.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly StringBuilder _messages = new();

    public string Text
    {
        get
        {
            lock (_messages)
            {
                return _messages.ToString();
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new Sink(this);

    public void Dispose()
    {
    }

    private void Append(string message)
    {
        lock (_messages)
        {
            _messages.AppendLine(message);
        }
    }

    private sealed class Sink(CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            owner.Append(state.ToString() ?? string.Empty);
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            owner.Append(formatter(state, exception));
        }
    }
}
