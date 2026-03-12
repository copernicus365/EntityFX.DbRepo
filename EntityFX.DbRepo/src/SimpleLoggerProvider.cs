using Microsoft.Extensions.Logging;

namespace EntityFX;

/// <summary>A simple log entry to be used in <see cref="SimpleLoggerProvider"/>.</summary>
/// <param name="Level">The severity level of the log entry.</param>
/// <param name="Category">The logger category (e.g., "Microsoft.EntityFrameworkCore.Database.Command").</param>
/// <param name="Message">The formatted log message containing SQL queries, parameters, or diagnostics.</param>
public readonly record struct SimpleLogEntry(LogLevel Level, string Category, string Message);

/// <summary>
/// A simple logger provider (`ILoggerProvider`) that can be used to capture and handle log entries,
/// while staying agnostic to the actual logging, in that it is driven by an input callback (Action).
/// Easily used for printing to console, collecting logs in tests, or to patch into your existing logging
/// infrastructure. Provided in this library as a simple way to capture database / EF Core log entries.
/// </summary>
public class SimpleLoggerProvider(Action<SimpleLogEntry> logCallback) : ILoggerProvider
{
	readonly Action<SimpleLogEntry> _logCallback = logCallback ?? throw new ArgumentNullException(nameof(logCallback));

	public ILogger CreateLogger(string categoryName) => new DataContextLogger(_logCallback, categoryName);

	public void Dispose() { }

	private class DataContextLogger(Action<SimpleLogEntry> logCallback, string categoryName) : ILogger
	{
		readonly Action<SimpleLogEntry> _logCallback = logCallback ?? throw new ArgumentNullException(nameof(logCallback));

		public bool IsEnabled(LogLevel logLevel) => true;

		public IDisposable BeginScope<TState>(TState state) => null;

		public void Log<TState>(
			LogLevel logLevel,
			EventId eventId,
			TState state,
			Exception exception,
			Func<TState, Exception, string> formatter)
		{
			string message = formatter(state, exception);
			_logCallback(new SimpleLogEntry(logLevel, categoryName, message));
		}
	}

	/// <summary>Creates an ILoggerFactory with this logger provider.</summary>
	/// <param name="logCallback">Callback invoked for each log entry containing the log level, category, and formatted message.</param>
	public static ILoggerFactory CreateFactory(Action<SimpleLogEntry> logCallback)
	{
		LoggerFactory loggerFactory = new();
		loggerFactory.AddProvider(new SimpleLoggerProvider(logCallback));
		return loggerFactory;
	}

	/// <summary>
	/// Creates an ILoggerFactory with a simplified callback (backward compatibility).
	/// </summary>
	/// <param name="logAction">Callback with only log level and message (category ignored).</param>
	public static ILoggerFactory CreateFactory(Action<LogLevel, string> logAction)
		=> CreateFactory(log => logAction(log.Level, log.Message));
}
