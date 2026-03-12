using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;

namespace EntityFX;

/// <summary>Extension methods for DbContextOptionsBuilder</summary>
public static class EFCoreExtensions
{
	// --- SetLog ---

	/// <summary>
	/// Configures EF Core to use a custom logging callback for database operations.
	/// Useful for capturing SQL queries, commands, parameters, and EF Core diagnostics.
	/// </summary>
	/// <param name="optionsBuilder">The DbContextOptionsBuilder to configure.</param>
	/// <param name="logCallback">
	/// A callback invoked for each log entry. The EFCoreLogEntry contains:
	/// - Level: The severity (Debug, Information, Warning, Error)
	/// - Category: The logger category (can be used for filtering)
	/// - Message: The formatted message (SQL queries, parameters, diagnostics)
	/// </param>
	/// <returns>The same DbContextOptionsBuilder instance for fluent chaining.</returns>
	/// <remarks>
	/// Call this in your DbContext's OnConfiguring method.
	/// Example - <code>optionsBuilder.SetLog(log => WriteLine($"[{log.Level}] {log.Message}"));</code>
	/// Example - Filter by category: (within SetLog, `if(log.Category.Contains("Database.Command"))` ...)
	/// </remarks>
	/// <exception cref="ArgumentNullException">Thrown when optionsBuilder or logCallback is null.</exception>
	public static DbContextOptionsBuilder SetLog(this DbContextOptionsBuilder optionsBuilder, Action<SimpleLogEntry> logCallback)
	{
		ArgumentNullException.ThrowIfNull(optionsBuilder);
		ArgumentNullException.ThrowIfNull(logCallback);
		optionsBuilder.UseLoggerFactory(SimpleLoggerProvider.CreateFactory(logCallback));
		return optionsBuilder;
	}

	/// <summary>Configures EF Core to log all database operations to console output</summary>
	/// <param name="optionsBuilder">The DbContextOptionsBuilder to configure.</param>
	/// <param name="minLevel">Minimum log level to output (default: Information)</param>
	/// <param name="categoryFilter">Optional filter to only log categories that match the provided category</param>
	/// <returns>The same DbContextOptionsBuilder instance for fluent chaining.</returns>
	/// <exception cref="ArgumentNullException">Thrown when optionsBuilder is null.</exception>
	public static DbContextOptionsBuilder SetLogToConsole(this DbContextOptionsBuilder optionsBuilder,
		LogLevel minLevel = LogLevel.Information,
		Func<string, bool> categoryFilter = null)
	{
		ArgumentNullException.ThrowIfNull(optionsBuilder);
		optionsBuilder.UseLoggerFactory(SimpleLoggerProvider.CreateFactory(
			log => {
				if(log.Level < minLevel)
					return;
				if(categoryFilter != null && !categoryFilter(log.Category))
					return;
				Console.WriteLine($"[{log.Level}] {log.Category}: {log.Message}");
			}));
		return optionsBuilder;
	}


	// --- GetTableSchemaName ---

	/// <summary>Gets the Schema Table name for the DbSet.</summary>
	/// <remarks>Source: https://stackoverflow.com/a/69898129/264031</remarks>
	public static string GetTableSchemaName<T>(this DbContext context) where T : class
	{
		//DbContext context = dbSet.GetService<ICurrentDbContext>().Context;
		IEntityType m = context.Model.FindEntityType(typeof(T));
		return m.GetSchemaQualifiedTableName();
	}

	/// <summary>Gets the Schema Table name for the DbSet.</summary>
	public static string GetTableSchemaName<T>(this DbSet<T> dbSet) where T : class
		=> GetTableSchemaName<T>(dbSet.GetService<ICurrentDbContext>().Context);


	// --- SetPrecisionScaleOnDecimalProperties ---

	/// <summary>
	/// Sets the decimal precision and scale for all decimal properties found within all entity types found within
	/// this model, or if provided, within the entity types matched by provided filter.
	/// Default values here (18,6) is a common choice for non-financials, like when decimal represents a latitude / longitude.
	/// <para />
	/// Note: Often it is preferable to set this on a per-property basis:
	/// <code>[Column(TypeName = "decimal(18,6)")] public decimal Latitude { get; set; }</code>
	/// </summary>
	/// <param name="modelBuilder"></param>
	/// <param name="precision">Decimal precision, ie the total number of digits (both sides of the decimal point) that the number can contain</param>
	/// <param name="scale">Number of digits allowed after the decimal point</param>
	/// <param name="filter">Optional filter to limit which entity types to apply the decimal precision/scale to</param>
	/// <returns>The same ModelBuilder instance for fluent chaining.</returns>
	/// <remarks>https://stackoverflow.com/questions/43277154/entity-framework-core-setting-the-decimal-precision-and-scale-to-all-decimal-p</remarks>
	public static ModelBuilder SetPrecisionScaleOnDecimalProperties(this ModelBuilder modelBuilder, int precision = 18, int scale = 6, Func<IMutableEntityType, bool> filter = null)
	{
		ArgumentNullException.ThrowIfNull(modelBuilder);

		var query = modelBuilder.Model.GetEntityTypes();
		if(filter != null)
			query = query.Where(filter);

		foreach(var property in query.SelectMany(t => t.GetProperties())
			.Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?))) {

			//// EF Core 3 //property.SetColumnType($"decimal({precision}, {scale})");

			// EF Core 5
			property.SetPrecision(precision);
			property.SetScale(scale);
		}

		return modelBuilder;
	}
}
