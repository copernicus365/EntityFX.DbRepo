using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EntityFX;

internal static class InternalEFCoreXtensions
{
	/// <summary>
	/// Gets the Schema Table name for the DbSet.
	/// Source: https://stackoverflow.com/a/69898129/264031
	/// </summary>
	internal static string GetTableSchemaName<T>(this DbContext context) where T : class
	{
		//DbContext context = dbSet.GetService<ICurrentDbContext>().Context;
		System.Type entityType = typeof(T);
		IEntityType m = context.Model.FindEntityType(entityType);
		return m.GetSchemaQualifiedTableName();
	}

	internal static IQueryable<T> WhereIf<T>(IQueryable<T> source, bool condition, Expression<Func<T, bool>> predicate)
	{
		if(condition)
			return source.Where(predicate);
		return source;
	}

	/// <summary>Gets the Schema Table name for the DbSet.</summary>
	internal static string GetTableSchemaName<T>(this DbSet<T> dbSet) where T : class
		=> GetTableSchemaName<T>(dbSet.GetService<ICurrentDbContext>().Context);
}
