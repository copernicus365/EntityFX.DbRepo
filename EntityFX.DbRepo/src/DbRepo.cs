using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace EntityFX;

/// <summary>
/// Generic repository base class for Entity Framework Core with flexible ID types.
/// Handles common CRUD operations, "already tracked" scenarios, and provides extension points for custom behavior.
/// </summary>
/// <typeparam name="T">The entity type</typeparam>
/// <typeparam name="TId">The primary key type (int, Guid, string, composite key, etc.)</typeparam>
public abstract class DbRepo<T, TId> : IDbRepo<T, TId> where T : class
{
	// --- FIELDS ---

	protected DbContext _dbContext;
	protected DbSet<T> _dbSet;
	protected DatabaseFacade _db;

	// === STATIC FIELDS ===

	/// <summary>
	/// Full table name including schema (e.g., "dbo.Users"). Cached per entity type.
	/// Automatically populated from EF Core metadata on first repository instantiation.
	/// </summary>
	public static string FullTableName { get; private set; }

	/// <summary>
	/// Name of the primary key column. Defaults to "Id". Override in derived class if different.
	/// </summary>
	public static string IdName { get; set; } = "Id";


	public DbRepo(DbContext dbContext)
	{
		ArgumentNullException.ThrowIfNull(dbContext);

		_dbContext = dbContext;
		_dbSet = _dbContext.Set<T>();
		_db = _dbContext.Database;

		if(FullTableName == null)
			FullTableName = _dbContext.GetTableSchemaName<T>();
	}

	#region --- abstract / virtual members  ---

	/// <summary>
	/// Builds a LINQ query to filter by the given ID. Required for GetById operations.
	/// </summary>
	/// <example>
	/// <code>
	/// public override IQueryable&lt;User&gt; WhereMatchTId(IQueryable&lt;User&gt; source, int id)
	///     => source.Where(u => u.Id == id);
	/// </code>
	/// </example>
	public abstract IQueryable<T> WhereMatchTId(IQueryable<T> source, TId id);

	/// <summary>
	/// Returns true if the entity's ID is not set (e.g., default value for identity columns).
	/// Used by <see cref="Upsert"/> to determine insert vs update.
	/// </summary>
	public abstract bool IdNotSet(T item);

	/// <summary>
	/// Returns true if two entities have the same ID. Used when resolving already-tracked entities.
	/// </summary>
	public abstract bool MatchesId(T item1, T item2);

	/// <summary>
	/// When true (default), queries use AsNoTracking for better performance and to prevent memory leaks.
	/// Set to false if you need change tracking for updates.
	/// </summary>
	public bool AsNoTracking { get; set; } = true;

	/// <summary>
	/// Set to true in order to disable Upsert. Upsert must call
	/// <see cref="IdNotSet"/>, but that is only useful on Identity types
	/// (like with an integer Id), where at ADD time it's value is default.
	/// While e.g. composite primary keys will always need to be set for both
	/// Add and Update, in which case Upsert can't be called.
	/// </summary>
	public virtual bool DisableUpsert { get; }

	public Func<IQueryable<T>, IOrderedQueryable<T>> PrimaryOrder { get; set; }

	#endregion


	#region --- GET ---

	/// <summary>
	/// Retrieves a single entity by ID, or null if not found.
	/// </summary>
	public T GetById(TId id, bool? noTracking = null)
		=> WhereMatchTId(Get(noTracking ?? AsNoTracking), id).FirstOrDefault();

	/// <summary>
	/// Asynchronously retrieves a single entity by ID, or null if not found.
	/// </summary>
	public async Task<T> GetByIdAsync(TId id, bool? noTracking = null)
		=> await WhereMatchTId(Get(noTracking ?? AsNoTracking), id).FirstOrDefaultAsync();

	/// <summary>
	/// Returns the base DbSet queryable, optionally with AsNoTracking applied.
	/// </summary>
	public IQueryable<T> Get(bool? noTracking = null)
		=> noTracking ?? AsNoTracking ? _dbSet.AsNoTracking() : _dbSet;

	/// <summary>
	/// Applies <see cref="PrimaryOrder"/> to the source if set, otherwise returns source unchanged.
	/// Useful for ensuring consistent default ordering across repository methods.
	/// </summary>
	public IQueryable<T> GET_PrimaryOrderedOrDefault(IQueryable<T> source)
	{
		ArgumentNullException.ThrowIfNull(source);
		return PrimaryOrder == null
			? source
			: PrimaryOrder(source);
	}

	/// <summary>
	/// Gets the DbSet with optional AsNoTracking and PrimaryOrder applied.
	/// </summary>
	public IQueryable<T> GET_PrimaryOrderedOrDefault(bool? noTracking = null)
		=> PrimaryOrder == null ? Get(noTracking) : PrimaryOrder(Get(noTracking));

	#endregion

	#region --- GetRange ---

	/// <summary>
	/// Returns a paged subset of entities with optional filtering and ordering.
	/// Uses Skip/Take for pagination.
	/// </summary>
	/// <param name="index">Number of records to skip</param>
	/// <param name="take">Number of records to return</param>
	/// <param name="predicate">Optional filter expression</param>
	/// <param name="noTracking">Override default tracking behavior</param>
	public IQueryable<T> GetRange(
		int index,
		int take,
		Expression<Func<T, bool>> predicate = null,
		bool? noTracking = null)
	{
		var q = _WhereIf(GET_PrimaryOrderedOrDefault(noTracking), predicate != null, predicate)
			.Skip(index)
			.Take(take);
		return q;
	}

	static IQueryable<T> _WhereIf(IQueryable<T> source, bool condition, Expression<Func<T, bool>> predicate)
	{
		if(condition)
			return source.Where(predicate);
		return source;
	}

	/// <summary>
	/// Returns a paged subset from a custom source query with optional filtering.
	/// </summary>
	public IQueryable<T> GetRange(
		IQueryable<T> source,
		int index,
		int take,
		Expression<Func<T, bool>> predicate = null)
	{
		var q = _WhereIf(source, predicate != null, predicate)
			.Skip(index)
			.Take(take);
		return q;
	}

	#endregion


	#region --- Count ---

	/// <summary>
	/// Returns the total count of entities, optionally filtered by predicate.
	/// </summary>
	public int Count(Expression<Func<T, bool>> predicate = null)
		=> predicate == null ? _dbSet.Count() : _dbSet.Count(predicate);

	/// <summary>
	/// Asynchronously returns the total count of entities, optionally filtered by predicate.
	/// </summary>
	public async Task<int> CountAsync(Expression<Func<T, bool>> predicate = null)
		=> predicate == null ? await _dbSet.CountAsync() : await _dbSet.CountAsync(predicate);

	#endregion


	// --- CUD ---

	/// <summary>
	/// Marks the entity for insertion. Call <see cref="SaveChanges"/> to persist.
	/// </summary>
	public void Add(T entity)
	{
		var dbEntityEntry = _dbContext.Entry(entity);
		if(dbEntityEntry.State != EntityState.Detached)
			dbEntityEntry.State = EntityState.Added;
		else
			_dbSet.Add(entity);
	}


	#region --- UPDATE ---

	/// <summary>
	/// Inserts if <see cref="IdNotSet"/> returns true, otherwise updates.
	/// Useful for forms where you don't know if the entity is new or existing.
	/// </summary>
	public void Upsert(T entity)
	{
		ArgumentNullException.ThrowIfNull(entity);
		if(DisableUpsert)
			throw new InvalidOperationException("UPSERT is disabled for this type, see `DisableUpsert` property");

		if(IdNotSet(entity)) // GetIdFromT(entity).Equals(_defaultId)) // entity.Id.Equals(_defaultId))
			Add(entity);
		else
			Update(entity);
	}

	/// <summary>
	/// Updates an entity, handling the "already tracked" scenario by merging values
	/// into the existing tracked entity when necessary.
	/// </summary>
	public void Update(T entity)
	{
		ArgumentNullException.ThrowIfNull(entity);

		var entry = _dbContext.Entry(entity);

		if(entry.State == EntityState.Detached) {
			T attachedEntity = _dbSet.Local.SingleOrDefault(e => MatchesId(e, entity)); // _getIdFromT(e).Equals(_getIdFromT(entity))); //e.Id.Equals(entity.Id));  // You need to have access to key
			if(attachedEntity != null) {
				var attachedEntry = _dbContext.Entry(attachedEntity);
				attachedEntry.CurrentValues.SetValues(entity);
			}
			else
				entry.State = EntityState.Modified; // This should attach entity
		}
	}

	/// <summary>
	/// Updates only the specified properties. Useful for optimistic concurrency scenarios
	/// where you want to update specific columns without loading the entire entity.
	/// </summary>
	/// <param name="entity">The entity with updated values</param>
	/// <param name="properties">Expressions selecting which properties to update</param>
	public void Update(T entity, params Expression<Func<T, object>>[] properties)
	{
		if(properties == null || properties.Length == 0)
			Update(entity);
		else {

			EntityEntry<T> entry = null;
			T attachedEntity = _dbSet.Local.SingleOrDefault(e => MatchesId(e, entity)); // _getIdFromT(e).Equals(_getIdFromT(entity))); // e.Id.Equals(entity.Id));  // You need to have access to key

			if(attachedEntity != null) {
				entry = _dbContext.Entry(attachedEntity);
				entry.CurrentValues.SetValues(entity);
			}

			if(entry == null) {
				entry = _dbContext.Entry(entity);
				_dbSet.Attach(entity);
			}

			foreach(var selector in properties)
				entry.Property(selector).IsModified = true;
		}
	}

	#endregion


	#region --- DELETE ---

	/// <summary>Marks the entity for deletion.</summary>
	public void Delete(T entity)
	{
		var dbEntityEntry = _dbContext.Entry(entity);
		if(dbEntityEntry != null && dbEntityEntry.State != EntityState.Deleted)
			dbEntityEntry.State = EntityState.Deleted;
		else {
			_dbSet.Attach(entity);
			_dbSet.Remove(entity);
		}
	}

	/// <summary>Retrieves the entity by ID and marks it for deletion.</summary>
	/// <returns>True if entity was found and deleted, false if not found</returns>
	public bool Delete(TId id)
	{
		var entity = GetById(id);
		if(entity == null)
			return false;
		Delete(entity);
		return true;
	}

	// --- DeleteDirect ---

	/// <summary>
	/// Returns a parameterized DELETE SQL statement. Override for complex ID types.
	/// For simple types (int, Guid, string), the default implementation is secure and sufficient.
	/// </summary>
	public virtual FormattableString GetDeleteDirectSQL(TId id)
		=> $"DELETE FROM {FullTableName} WHERE {IdName} = {id}";

	/// <summary>
	/// Executes a direct SQL DELETE, bypassing change tracking and EF Core interceptors.
	/// Use for performance-critical bulk operations. Returns number of rows affected.
	/// </summary>
	public int DeleteDirect(TId id)
		=> _db.ExecuteSql(GetDeleteDirectSQL(id));

	/// <summary>
	/// Asynchronously executes a direct SQL DELETE. Returns number of rows affected.
	/// </summary>
	public async Task<int> DeleteDirectAsync(TId id)
		=> await _db.ExecuteSqlAsync(GetDeleteDirectSQL(id));

	//public virtual string IdToString(TId id) => id?.ToString();

	#endregion


	/// <summary>
	/// Persists all changes to the database. Returns the number of affected rows.
	/// </summary>
	public int SaveChanges()
		=> _dbContext.SaveChanges();

	/// <summary>
	/// Asynchronously persists all changes to the database. Returns the number of affected rows.
	/// </summary>
	public async Task<int> SaveChangesAsync()
		=> await _dbContext.SaveChangesAsync();

	//public void Dispose() => _dbContext?.Dispose();
}

/// <summary>
/// Generic repository for entities with integer identity primary keys.
/// Convenience base class that sets TId to int, so you only need to specify T.
/// </summary>
/// <typeparam name="T">The entity type</typeparam>
public abstract class DbRepo<T>(DbContext context) : DbRepo<T, int>(context) where T : class
{
}

#region --- DbExecuteSqlCommand (removed) ---

//protected int DbExecuteSqlCommand(string sql, params object[] args)
//{
//	int result = args == null || args.Length < 1
//		? _db.ExecuteSqlRaw(sql)
//		: _db.ExecuteSqlRaw(sql, args);
//	return result;
//}

//protected async Task<int> DbExecuteSqlCommandAsync(string sql, params object[] args)
//{
//	int result = args == null || args.Length < 1
//		? await _db.ExecuteSqlRawAsync(sql)
//		: await _db.ExecuteSqlRawAsync(sql, args); // TOTALLY stupid, if args is null, throws exception! but since it is a params, it should allow
//	return result;
//}

#endregion
