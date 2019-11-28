﻿#region License
// Released under MIT License 
// License: https://www.mit.edu/~amini/LICENSE.md
// Home page: https://github.com/ffhighwind/DapperExtraCRUD

// Copyright(c) 2018 Wesley Hamilton

// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:

// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.

// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
#endregion

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper.Extra.Utilities;
using System.Data.Common;
using Fasterflect;

namespace Dapper.Extra.Internal
{
	/// <summary>
	/// Stores metadata and generates SQL commands and queries for the given type.
	/// </summary>
	/// <typeparam name="T">The type of to generate queries for.</typeparam>
	public sealed class SqlBuilder<T> : ISqlBuilder
		where T : class
	{
		public SqlBuilder(SqlTypeInfo info, LazyThreadSafetyMode threadSafety = LazyThreadSafetyMode.ExecutionAndPublication)
		{
			if (info.Type.IsGenericTypeDefinition && info.Type.GetGenericTypeDefinition() == typeof(List<>))
				throw new InvalidOperationException("List<> is not a valid table type.");
			if (info.Type.IsArray)
				throw new InvalidOperationException("Array<> is not a valid table type.");
			if (info.Type == typeof(string))
				throw new InvalidOperationException("String is not a valid table type.");
			Info = info;
			BulkStagingTable = Info.Adapter.CreateTempTableName(Info.Type.Name + (Info.Type.FullName.GetHashCode() % 10000));
			Queries = new SqlQueries<T>()
			{
				Delete = CreateDelete(),
				Get = CreateGet(),
				GetList = CreateGetList(),
				Insert = CreateInsert(),
				Update = CreateUpdate(),
				LazyUpdateObj = new Lazy<DbObjBool<T>>(() => CreateUpdateObj()),
				LazyBulkDelete = new Lazy<SqlListInt<T>>(() => CreateBulkDelete(), threadSafety),
				LazyBulkGet = new Lazy<SqlListList<T>>(() => CreateBulkGet(), threadSafety),
				LazyBulkInsert = new Lazy<SqlListVoid<T>>(() => CreateBulkInsert(), threadSafety),
				LazyBulkInsertIfNotExists = new Lazy<SqlListInt<T>>(() => CreateBulkInsertIfNotExists(), threadSafety),
				LazyBulkUpdate = new Lazy<SqlListInt<T>>(() => CreateBulkUpdate(), threadSafety),
				LazyBulkUpsert = new Lazy<SqlListInt<T>>(() => CreateBulkUpsert(), threadSafety),
				LazyDeleteAll = new Lazy<DbVoid>(() => CreateDeleteAll(), threadSafety),
				LazyDeleteList = new Lazy<DbWhereInt<T>>(() => CreateDeleteList(), threadSafety),
				LazyGetDistinct = new Lazy<DbTypeWhereList<T>>(() => CreateGetDistinct(), threadSafety),
				LazyGetDistinctLimit = new Lazy<DbTypeLimitList<T>>(() => CreateGetDistinctLimit(), threadSafety),
				LazyGetKeys = new Lazy<DbWhereList<T>>(() => CreateGetKeys(), threadSafety),
				LazyGetLimit = new Lazy<DbLimitList<T>>(() => CreateGetLimit(), threadSafety),
				LazyInsertIfNotExists = new Lazy<DbTBool<T>>(() => CreateInsertIfNotExists(), threadSafety),
				LazyRecordCount = new Lazy<DbWhereInt<T>>(() => CreateRecordCount(), threadSafety),
				LazyUpsert = new Lazy<DbTBool<T>>(() => CreateUpsert(), threadSafety),
				InsertAutoSync = CreateAutoSync(Info.InsertAutoSyncColumns),
				UpdateAutoSync = CreateAutoSync(info.UpdateAutoSyncColumns),
				LazyGetFilter = new Lazy<DbTypeWhereList<T>>(() => CreateGetFilterList(), threadSafety),
				LazyGetFilterLimit = new Lazy<DbTypeLimitList<T>>(() => CreateGetFilterLimit(), threadSafety),
			};
			DataReaderFactory = new DataReaderFactory(typeof(T), info.Columns.Select(c => c.Property));

			if (info.EqualityColumns.Count == 1) {
				EqualityComparer = new TableKeyEqualityComparer<T>(TableName, EqualityColumns[0]);
				Type type = info.EqualityColumns[0].Type;
				TypeCode typeCode = Type.GetTypeCode(type);
				switch (typeCode) {
					case TypeCode.Int16:
						KeyBuilder = new SqlBuilder<T, short>(this);
						break;
					case TypeCode.Int32:
						KeyBuilder = new SqlBuilder<T, int>(this);
						break;
					case TypeCode.Int64:
						KeyBuilder = new SqlBuilder<T, long>(this);
						break;
					case TypeCode.SByte:
						KeyBuilder = new SqlBuilder<T, sbyte>(this);
						break;
					case TypeCode.Single:
						KeyBuilder = new SqlBuilder<T, float>(this);
						break;
					case TypeCode.String:
						KeyBuilder = new SqlBuilder<T, string>(this);
						break;
					case TypeCode.UInt16:
						KeyBuilder = new SqlBuilder<T, ushort>(this);
						break;
					case TypeCode.Double:
						KeyBuilder = new SqlBuilder<T, double>(this);
						break;
					case TypeCode.UInt32:
						KeyBuilder = new SqlBuilder<T, uint>(this);
						break;
					case TypeCode.UInt64:
						KeyBuilder = new SqlBuilder<T, ulong>(this);
						break;
					case TypeCode.Byte:
						KeyBuilder = new SqlBuilder<T, byte>(this);
						break;
					case TypeCode.Char:
						KeyBuilder = new SqlBuilder<T, char>(this);
						break;
					case TypeCode.DateTime:
						KeyBuilder = new SqlBuilder<T, DateTime>(this);
						break;
					case TypeCode.Decimal:
						KeyBuilder = new SqlBuilder<T, decimal>(this);
						break;
					default:
						if (type == typeof(Guid))
							KeyBuilder = new SqlBuilder<T, Guid>(this);
						else if (type == typeof(DateTimeOffset))
							KeyBuilder = new SqlBuilder<T, DateTimeOffset>(this);
						else if (type == typeof(TimeSpan))
							KeyBuilder = new SqlBuilder<T, TimeSpan>(this);
						break;
				}
			}
			else
				EqualityComparer = new TableEqualityComparer<T>(TableName, EqualityColumns);
		}

		/// <summary>
		/// The quoted table name or the class name.
		/// </summary>
		public string TableName => Info.TableName;
		/// <summary>
		/// The temporary table name for bulk operations.
		/// </summary>
		public string BulkStagingTable { get; private set; }
		/// <summary>
		/// The syntax used to generate SQL commands.
		/// </summary>
		public SqlSyntax Syntax => Info.Syntax;
		/// <summary>
		/// Stores metadata for for the given type.
		/// </summary>
		public SqlTypeInfo Info { get; private set; }
		/// <summary>
		///  All valid columns for the given type.
		/// </summary>
		public IReadOnlyList<SqlColumn> Columns => Info.Columns;
		/// <summary>
		/// The columns that determine uniqueness. This is every column if there are no keys.
		/// </summary>
		public IReadOnlyList<SqlColumn> EqualityColumns => Info.EqualityColumns;
		/// <summary>
		/// The queries and commands for this type.
		/// </summary>
		public ISqlQueries<T> Queries { get; private set; }
		/// <summary>
		/// Compares two objects of the given type and determines if they are equal.
		/// </summary>
		public IEqualityComparer<T> EqualityComparer { get; private set; }
		/// <summary>
		/// A <see cref="SqlBuilder{T, KeyType}"/> if a single key exists.
		/// </summary>
		public ISqlBuilder KeyBuilder { get; private set; }
		/// <summary>
		/// The same value as <see cref="Queries"/>.
		/// </summary>
		public object QueriesObject => Queries;
		/// <summary>
		/// Generates <see cref="DbDataReader"/>
		/// </summary>
		public DataReaderFactory DataReaderFactory { get; private set; }

		/// <summary>
		/// Casts <see cref="KeyBuilder"/> to the given <see cref="SqlBuilder{T, KeyType}"/>.
		/// </summary>
		/// <typeparam name="KeyType">The key type.</typeparam>
		public SqlBuilder<T, KeyType> Create<KeyType>()
		{
			if (KeyBuilder is SqlBuilder<T, KeyType> child)
				return child;
			if (Info.KeyColumns.Count != 1)
				throw new InvalidOperationException(typeof(T).Name + " requires a single key");
			if (KeyBuilder != null) {
				Type expected = Info.KeyColumns[0].Property.PropertyType;
				throw new InvalidOperationException(expected.Name + " is not the correct key type for " + typeof(T).Name + ". Expected " + expected.Name + ".");
			}
			throw new InvalidOperationException(typeof(KeyType).Name + " is an unsupported key type.");
		}

		#region Filtered Selects
		private ConcurrentDictionary<Type, string> SelectMap = new ConcurrentDictionary<Type, string>();

		private IEnumerable<SqlColumn> GetSharedColumns(Type type, IEnumerable<SqlColumn> columns)
		{
			if (type == typeof(T))
				return columns;
			IEnumerable<string> propNames = type.GetProperties(BindingFlags.Public | BindingFlags.SetProperty | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(p => p.Name);
			HashSet<string> columnNames = new HashSet<string>(propNames);
			List<SqlColumn> list = columns.Where(c => propNames.Contains(c.Property.Name)).ToList();
			if (list.Count == 0)
				throw new InvalidOperationException(type.FullName + " does not have any matching columns with " + typeof(T).FullName);
			return list;
		}

		private string CreateParamsSelect(Type type)
		{
			IEnumerable<SqlColumn> columns = GetSharedColumns(type, Info.SelectColumns);
			string paramsSelect = ParamsSelect(columns);
			return SelectMap.GetOrAdd(type, paramsSelect);
		}
		#endregion Filtered Selects

		#region StringCache
		/// <summary>
		/// Stores strings to reduce memory usage.
		/// </summary>
		/// <remarks> String.Intern could be used to cache these strings, but this would prevent clearing the caches.</remarks>
		private ConcurrentDictionary<string, string> StringCache = new ConcurrentDictionary<string, string>();

		/// <summary>
		/// Checks the cache for a string and returns it if it exists, otherwise it adds the string to the cache.
		/// </summary>
		/// <param name="str">The string to add/search for.</param>
		/// <remarks> String.Intern could be used to cache these strings, but this would prevent clearing the caches.</remarks>
		internal string Store(string str)
		{
			return StringCache.GetOrAdd(str, str);
		}

		internal string TruncateTableQuery()
		{
			return Store(Info.Adapter.TruncateTable(TableName));
		}

		internal string DeleteCmd()
		{
			return Store("DELETE FROM " + TableName + "\n");
		}

		internal string WhereEquals(IEnumerable<SqlColumn> columns)
		{
			return Store(SqlBuilderHelper.WhereEquals(columns));
		}

		internal string DropBulkTableCmd()
		{
			return Store(Info.Adapter.DropTempTableIfExists(BulkStagingTable));
		}

		internal string ParamsSelect()
		{
			return ParamsSelect(Info.SelectColumns);
		}

		internal string ParamsSelect(IEnumerable<SqlColumn> columns)
		{
			return Store(SqlBuilderHelper.SelectedColumns(columns));
		}

		internal string ParamsSelectFromTable()
		{
			return Store(SqlBuilderHelper.SelectedColumns(Info.SelectColumns) + "\nFROM " + TableName + "\n");
		}

		internal string ParamsSelectFromTableBulk()
		{
			return Store(SqlBuilderHelper.SelectedColumns(Info.SelectColumns, TableName) + "\nFROM " + TableName + "\n");
		}

		internal string SelectIntoStagingTable(IEnumerable<SqlColumn> columns)
		{
			return Store(SqlBuilderHelper.SelectIntoTableQuery(TableName, BulkStagingTable, columns));
		}

		internal string WhereEqualsTables(IEnumerable<SqlColumn> columns)
		{
			return Store(SqlBuilderHelper.WhereEqualsTables(BulkStagingTable, TableName, columns));
		}

		internal string InsertedValues(IEnumerable<SqlColumn> columns)
		{
			return Store(SqlBuilderHelper.InsertedValues(columns));
		}

		internal string InsertIntoCmd()
		{
			return Store($"INSERT INTO {TableName} ({ColumnNames(Info.InsertColumns)})\n");
		}

		internal string ColumnNames(IEnumerable<SqlColumn> columns)
		{
			return Store(string.Join(",", columns.Select(c => c.ColumnName)));
		}

		internal string UpdateSetTables()
		{
			// "\nSET \t" + Params
			return Store(SqlBuilderHelper.UpdateSetTables(BulkStagingTable, TableName, Info.UpdateColumns));
		}

		internal string SelectAutoKey()
		{
			return Store(Info.Adapter.SelectIdentityQuery(Info.AutoKeyColumn.Type));
		}

		internal string UpdateSet(IEnumerable<SqlColumn> columns)
		{
			return Store(SqlBuilderHelper.UpdateSet(columns));
		}
		#endregion StringCache

		#region DoNothing
		private static int DoNothing(SqlConnection connection, IEnumerable<T> objs, SqlTransaction transaction, int commandTimeout)
		{
			return 0;
		}

		private static bool DoNothing(IDbConnection connection, object obj, IDbTransaction transaction, int commandTimeout)
		{
			return false;
		}

		private static void DoNothing(IDbConnection connection, IDbTransaction transaction, int commandTimeout)
		{
		}

		private static void DoNothingSqlList(SqlConnection connection, IEnumerable<T> objs, SqlTransaction transaction, int commandTimeout)
		{
		}

		private static bool DoNothing(IDbConnection connection, T obj, IDbTransaction transaction, int commandTimeout)
		{
			return false;
		}

		private static int DoNothing(IDbConnection connection, string whereCondition, object param, IDbTransaction transaction, int commandTimeout)
		{
			return 0;
		}

		private static void DoNothingVoid(IDbConnection connection, T obj, IDbTransaction transaction, int commandTimeout)
		{
		}
		#endregion DoNothing

		#region Selects
		private DbWhereInt<T> CreateRecordCount()
		{
			return (connection, whereCondition, param, transaction, commandTimeout) =>
			{
				string query = $"SELECT COUNT(*) FROM {TableName}\n{whereCondition}";
				int count = connection.Query<int>(query, param, transaction, true, commandTimeout).First();
				return count;
			};
		}

		public DbTVoid<T> CreateAutoSync(IEnumerable<SqlColumn> columns)
		{
			if (!columns.Any())
				return null;
			string paramsSelect = ParamsSelect(columns);
			string whereEquals = WhereEquals(EqualityColumns);
			MemberSetter[] setters = columns.Select(c => c.Setter).ToArray();
			string[] names = columns.Select(c => c.Property.Name).ToArray();
			return (connection, obj, transaction, commandTimeout) =>
			{
				string cmd = $"SELECT {paramsSelect}\nFROM {TableName}\nWHERE \t{whereEquals}";
				IDictionary<string, object> value = connection.QueryFirstOrDefault(cmd, obj, transaction, commandTimeout);
				if (value != null) {
					for (int i = 0; i < setters.Length; i++) {
						setters[i](obj, value[names[i]]);
					}
				}
			};
		}

		private DbTypeWhereList<T> CreateGetFilterList()
		{
			return (connection, type, whereCondition, param, transaction, buffered, commandTimeout) =>
			{
				if (!SelectMap.TryGetValue(type, out string paramsSelect)) {
					paramsSelect = CreateParamsSelect(type);
				}
				string query = $"SELECT {paramsSelect}\nFROM {TableName}\n{whereCondition}";
				IEnumerable<T> result = connection.Query<T>(query, param, transaction, buffered, commandTimeout);
				return result;
			};
		}

		private DbTT<T> CreateGet()
		{
			string paramsSelectFromTable = ParamsSelectFromTable();
			string whereEquals = WhereEquals(EqualityColumns);
			return (connection, obj, transaction, commandTimeout) =>
			{
				string query = $"SELECT {paramsSelectFromTable}WHERE \t{whereEquals}";
				T val = connection.QueryFirstOrDefault<T>(query, obj, transaction, commandTimeout);
				return val;
			};
		}

		private DbWhereList<T> CreateGetKeys()
		{
			string paramsSelectKeys = ParamsSelect(EqualityColumns);
			return (connection, whereCondition, param, transaction, buffered, commandTimeout) =>
			{
				string query = $"SELECT {paramsSelectKeys}\nFROM {TableName}\n{whereCondition}";
				IEnumerable<T> result = connection.Query<T>(query, param, transaction, buffered, commandTimeout);
				return result;
			};
		}

		private DbWhereList<T> CreateGetList()
		{
			string paramsSelectFromTable = ParamsSelectFromTable();
			return (connection, whereCondition, param, transaction, buffered, commandTimeout) =>
			{
				string query = $"SELECT {paramsSelectFromTable}{whereCondition}";
				IEnumerable<T> result = connection.Query<T>(query, param, transaction, buffered, commandTimeout);
				return result;
			};
		}

		private DbTypeWhereList<T> CreateGetDistinct()
		{
			//string paramsSelectFromTable = ParamsSelectFromTable();
			return (connection, type, whereCondition, param, transaction, buffered, commandTimeout) =>
			{
				if (!SelectMap.TryGetValue(type, out string paramsSelect)) {
					paramsSelect = CreateParamsSelect(type);
				}
				string query = $"SELECT DISTINCT {paramsSelect}\nFROM {TableName}\n{whereCondition}";
				IEnumerable<T> result = connection.Query<T>(query, param, transaction, buffered, commandTimeout);
				return result;
			};
		}

		private DbLimitList<T> CreateGetLimit()
		{
			string paramsSelectFromTable = ParamsSelectFromTable();
			string limitStartQuery = Info.Adapter.SelectLimitStart;
			string limitEndQuery = Info.Adapter.SelectLimitEnd;
			return (connection, limit, whereCondition, param, transaction, buffered, commandTimeout) =>
			{
				string queryStart = string.Format(limitStartQuery, limit);
				string queryEnd = string.Format(limitEndQuery, limit);
				string query = $"SELECT {queryStart}{paramsSelectFromTable}{whereCondition}{queryEnd}";
				IEnumerable<T> result = connection.Query<T>(query, param, transaction, false, commandTimeout);
				return result;
			};
		}

		private DbTypeLimitList<T> CreateGetFilterLimit()
		{
			string limitStartQuery = Info.Adapter.SelectLimitStart;
			string limitEndQuery = Info.Adapter.SelectLimitEnd;
			return (connection, type, limit, whereCondition, param, transaction, buffered, commandTimeout) =>
			{
				string queryStart = string.Format(limitStartQuery, limit);
				string queryEnd = string.Format(limitEndQuery, limit);
				if (!SelectMap.TryGetValue(type, out string paramsSelect)) {
					paramsSelect = CreateParamsSelect(type);
				}
				string query = $"SELECT {queryStart}{paramsSelect}\nFROM {TableName}\n{whereCondition}{queryEnd}";
				IEnumerable<T> result = connection.Query<T>(query, param, transaction, false, commandTimeout);
				return result;
			};
		}

		private DbTypeLimitList<T> CreateGetDistinctLimit()
		{
			//string paramsSelectFromTable = ParamsSelectFromTable();
			string limitStartQuery = Info.Adapter.SelectLimitStart;
			string limitEndQuery = Info.Adapter.SelectLimitEnd;
			return (connection, type, limit, whereCondition, param, transaction, buffered, commandTimeout) =>
			{
				string queryStart = string.Format(limitStartQuery, limit);
				string queryEnd = string.Format(limitEndQuery, limit);
				if (!SelectMap.TryGetValue(type, out string paramsSelect)) {
					paramsSelect = CreateParamsSelect(type);
				}
				string query = $"SELECT DISTINCT {queryStart}{paramsSelect}\nFROM {TableName}\n{whereCondition}{queryEnd}";
				IEnumerable<T> result = connection.Query<T>(query, param, transaction, false, commandTimeout);
				return result;
			};
		}

		private SqlListList<T> CreateBulkGet()
		{
			string dropBulkTableCmd = DropBulkTableCmd();
			string selectEqualityIntoStagingCmd = SelectIntoStagingTable(EqualityColumns);
			string paramsSelectFromTableBulk = ParamsSelectFromTableBulk();
			string equalsTables = WhereEqualsTables(EqualityColumns);
			return (connection, objs, transaction, commandTimeout) =>
			{
				connection.Execute(dropBulkTableCmd, null, transaction, commandTimeout);
				connection.Execute(selectEqualityIntoStagingCmd, null, transaction, commandTimeout);
				ExtraUtil.BulkInsert(connection, objs, transaction, BulkStagingTable, DataReaderFactory, EqualityColumns, commandTimeout,
					SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.KeepNulls | SqlBulkCopyOptions.TableLock);
				string bulkGetQuery = $"SELECT {paramsSelectFromTableBulk}\tINNER JOIN {BulkStagingTable} ON {equalsTables}";
				IEnumerable<T> result = connection.Query<T>(bulkGetQuery, null, transaction, true, commandTimeout);
				connection.Execute(dropBulkTableCmd, null, transaction, commandTimeout);
				return result;
			};
		}
		#endregion Selects

		#region Deletes
		private DbTBool<T> CreateDelete()
		{
			if (Info.DeleteKeyColumns.Count == 0) {
				//	NoDeltesAttribute
				return DoNothing;
			}
			else {
				string deleteCmd = DeleteCmd();
				string deleteEquals = WhereEquals(Info.DeleteKeyColumns);
				return (connection, obj, transaction, commandTimeout) =>
				{
					string cmd = deleteCmd + "WHERE \t" + deleteEquals;
					int count = connection.Execute(cmd, obj, transaction, commandTimeout);
					return count > 0;
				};
			}
		}

		private DbWhereInt<T> CreateDeleteList()
		{
			if (Info.DeleteKeyColumns.Count == 0) {
				//	NoDeltesAttribute
				return DoNothing;
			}
			else {
				string deleteCmd = DeleteCmd();
				return (connection, whereCondition, param, transaction, commandTimeout) =>
				{
					string cmd = deleteCmd + whereCondition;
					int count = connection.Execute(cmd, param, transaction, commandTimeout);
					return count;
				};
			}
		}

		private DbVoid CreateDeleteAll()
		{
			if (Info.DeleteKeyColumns.Count == 0) {
				//	NoDeltesAttribute
				return DoNothing;
			}
			else {
				string truncateCmd = Store(Info.Adapter.TruncateTable(TableName));
				return (connection, transaction, commandTimeout) =>
				{
					int count = connection.Execute(truncateCmd, null, transaction, commandTimeout);
				};
			}
		}

		private SqlListInt<T> CreateBulkDelete()
		{
			if (Info.DeleteKeyColumns.Count == 0) {
				//	NoDeltesAttribute
				return DoNothing;
			}
			else {
				string dropBulkTableCmd = DropBulkTableCmd();
				string selectEqualityIntoStagingCmd = SelectIntoStagingTable(EqualityColumns);
				string equalsTables = WhereEqualsTables(EqualityColumns);
				return (connection, objs, transaction, commandTimeout) =>
				{
					connection.Execute(dropBulkTableCmd, null, transaction, commandTimeout);
					connection.Execute(selectEqualityIntoStagingCmd, null, transaction, commandTimeout);
					ExtraUtil.BulkInsert(connection, objs, transaction, BulkStagingTable, DataReaderFactory, EqualityColumns, commandTimeout,
						SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.KeepNulls | SqlBulkCopyOptions.TableLock);
					string bulkDeleteCmd = $"DELETE FROM {TableName} FROM {TableName} INNER JOIN {BulkStagingTable} ON {equalsTables}";
					int count = connection.Execute(bulkDeleteCmd, null, transaction, commandTimeout);
					connection.Execute(dropBulkTableCmd, null, transaction, commandTimeout);
					return count;
				};
			}
		}
		#endregion Deletes

		#region Inserts
		private DbTVoid<T> CreateInsert()
		{
			if (!Info.InsertColumns.Any()) {
				// NoInsertsAttribute
				return DoNothingVoid;
			}
			else {
				string insertIntoCmd = InsertIntoCmd();
				string insertedValues = InsertedValues(Info.InsertColumns);
				if (Info.AutoKeyColumn == null) {
					if (!Info.InsertAutoSyncColumns.Any()) {
						return (connection, obj, transaction, commandTimeout) =>
						{
							string cmd = insertIntoCmd + insertedValues;
							connection.Execute(cmd, obj, transaction, commandTimeout);
						};
					}
					else {
						DbTVoid<T> insertAutoSync = Queries.InsertAutoSync;
						return (connection, obj, transaction, commandTimeout) =>
						{
							string cmd = insertIntoCmd + insertedValues;
							connection.Execute(cmd, obj, transaction, commandTimeout);
							insertAutoSync(connection, obj, transaction, commandTimeout);
						};
					}
				}
				else {
					string selectAutoKey = SelectAutoKey();
					MemberSetter autoKeySetter = Info.AutoKeyColumn.Setter;
					if (!Info.InsertAutoSyncColumns.Any()) {
						return (connection, obj, transaction, commandTimeout) =>
						{
							string cmd = insertIntoCmd + insertedValues + ";\n" + selectAutoKey;
							IDictionary<string, object> key = connection.QueryFirst(cmd, obj, transaction, commandTimeout);
							autoKeySetter(obj, key.Values.First());
						};
					}
					else {
						DbTVoid<T> insertAutoSync = Queries.InsertAutoSync;
						return (connection, obj, transaction, commandTimeout) =>
						{
							string cmd = insertIntoCmd + insertedValues + ";\n" + selectAutoKey;
							IDictionary<string, object> key = connection.QueryFirst(cmd, obj, transaction, commandTimeout);
							autoKeySetter(obj, key.Values.First());
							insertAutoSync(connection, obj, transaction, commandTimeout);
						};
					}
				}
			}
		}

		private DbTBool<T> CreateInsertIfNotExists()
		{
			if (!Info.InsertColumns.Any()) {
				return DoNothing;
			}
			else {
				string insertIntoCmd = InsertIntoCmd();
				string insertedValues = InsertedValues(Info.InsertColumns);
				string whereEquals = WhereEquals(EqualityColumns);
				if (Info.AutoKeyColumn == null) {
					if (!Info.InsertAutoSyncColumns.Any()) {
						return (connection, obj, transaction, commandTimeout) =>
						{
							string cmd = $"IF NOT EXISTS (\nSELECT * FROM {TableName}\nWHERE \t{whereEquals})\n{insertIntoCmd}{insertedValues}";
							int count = connection.Execute(cmd, obj, transaction, commandTimeout);
							return count > 0;
						};
					}
					else {
						DbTVoid<T> insertAutoSync = Queries.InsertAutoSync;
						return (connection, obj, transaction, commandTimeout) =>
						{
							string cmd = $"IF NOT EXISTS (\nSELECT * FROM {TableName}\nWHERE \t{whereEquals})\n{insertIntoCmd}{insertedValues}";
							int count = connection.Execute(cmd, obj, transaction, commandTimeout);
							insertAutoSync(connection, obj, transaction, commandTimeout);
							return count > 0;
						};
					}
				}
				else {
					string selectAutoKey = SelectAutoKey();
					MemberSetter autoKeySetter = Info.AutoKeyColumn.Setter;
					if (!Info.InsertAutoSyncColumns.Any()) {
						return (connection, obj, transaction, commandTimeout) =>
						{
							string cmd = $"IF NOT EXISTS (\nSELECT * FROM {TableName}\nWHERE \t{whereEquals})\n{insertIntoCmd}{insertedValues};\n{selectAutoKey}";
							object key = connection.QueryFirst<dynamic>(cmd, obj, transaction, commandTimeout).Id;
							if (key != null) {
								autoKeySetter(obj, key);
								return true;
							}
							return false;
						};
					}
					else {
						DbTVoid<T> insertAutoSync = Queries.InsertAutoSync;
						return (connection, obj, transaction, commandTimeout) =>
						{
							string cmd = $"IF NOT EXISTS (\nSELECT * FROM {TableName}\nWHERE \t{whereEquals})\n{insertIntoCmd}{insertedValues};\n{selectAutoKey}";
							object key = connection.QueryFirst<dynamic>(cmd, obj, transaction, commandTimeout).Id;
							if (key != null) {
								insertAutoSync(connection, obj, transaction, commandTimeout);
								autoKeySetter(obj, key);
								return true;
							}
							return false;
						};
					}
				}
			}
		}

		private SqlListVoid<T> CreateBulkInsert()
		{
			if (!Info.InsertColumns.Any()) {
				return DoNothingSqlList;
			}
			else {
				return (connection, objs, transaction, commandTimeout) =>
				{
					ExtraUtil.BulkInsert(connection, objs, transaction, TableName, DataReaderFactory, Info.InsertColumns, commandTimeout,
						SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.KeepNulls | SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.FireTriggers | SqlBulkCopyOptions.TableLock);
				};
			}
		}

		private SqlListInt<T> CreateBulkInsertIfNotExists()
		{
			if (!Info.InsertColumns.Any()) {
				return DoNothing;
			}
			else {
				string selectInsertIntoStagingCmd = SelectIntoStagingTable(Info.BulkInsertIfNotExistsColumns);
				string dropBulkTableCmd = DropBulkTableCmd();
				string equalsTables = WhereEqualsTables(EqualityColumns);
				string insertColumns = ColumnNames(Info.InsertColumns);
				string insertIntoCmd = InsertIntoCmd();
				string insertedValues = InsertedValues(Info.InsertColumns);
				return (connection, objs, transaction, commandTimeout) =>
				{
					connection.Execute(dropBulkTableCmd, null, transaction, commandTimeout);
					connection.Execute(selectInsertIntoStagingCmd, null, transaction, commandTimeout);
					ExtraUtil.BulkInsert(connection, objs, transaction, BulkStagingTable, DataReaderFactory, Info.BulkInsertIfNotExistsColumns, commandTimeout,
						SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.KeepNulls | SqlBulkCopyOptions.TableLock);
					string bulkInsertIfNotExistsCmd = $"{insertIntoCmd}\nSELECT {insertColumns}\nFROM {BulkStagingTable}\nWHERE NOT EXISTS (\nSELECT * FROM {TableName}\nWHERE \t{equalsTables})";
					int count = connection.Execute(bulkInsertIfNotExistsCmd, null, transaction, commandTimeout);
					connection.Execute(dropBulkTableCmd, null, transaction, commandTimeout);
					return count;
				};
			}
		}
		#endregion Inserts

		#region Updates
		private DbTBool<T> CreateUpdate()
		{
			if (Info.UpdateKeyColumns.Count == 0) {
				// NoUpdatesAttribute
				return DoNothing;
			}
			else {
				string whereUpdateEquals = WhereEquals(Info.UpdateKeyColumns);
				string updateSet = UpdateSet(Info.UpdateColumns);
				if (!Info.UpdateAutoSyncColumns.Any()) {
					return (connection, obj, transaction, commandTimeout) =>
					{
						string updateCmd = $"UPDATE {TableName}{updateSet}\nWHERE \t{whereUpdateEquals}";
						int count = connection.Execute(updateCmd, obj, transaction, commandTimeout);
						return count > 0;
					};
				}
				else {
					DbTVoid<T> updateAutoSync = Queries.UpdateAutoSync;
					return (connection, obj, transaction, commandTimeout) =>
					{
						string updateCmd = $"UPDATE {TableName}{updateSet}\nWHERE \t{whereUpdateEquals}";
						int count = connection.Execute(updateCmd, obj, transaction, commandTimeout);
						bool success = count > 0;
						if (success) {
							updateAutoSync(connection, obj, transaction, commandTimeout);
						}
						return success;
					};
				}
			}
		}

		private DbObjBool<T> CreateUpdateObj()
		{
			if (Info.UpdateKeyColumns.Count == 0) {
				// NoUpdatesAttribute
				return DoNothing;
			}
			else {
				string whereUpdateEquals = WhereEquals(Info.UpdateKeyColumns);
				ConcurrentDictionary<Type, string> UpdateSetMap = new ConcurrentDictionary<Type, string>();
				return (connection, obj, transaction, commandTimeout) =>
				{
					Type type = obj.GetType();
					if (UpdateSetMap.TryGetValue(type, out string updateSet)) {
						IEnumerable<SqlColumn> columns = GetSharedColumns(type, Info.UpdateColumns);
						HashSet<string> keyColumnNames = new HashSet<string>(Info.UpdateKeyColumns.Select(c => c.Property.Name));
						updateSet = UpdateSet(columns);
						UpdateSetMap.GetOrAdd(type, updateSet);
					}
					int count = connection.Execute($"UPDATE {TableName}{updateSet}\nWHERE \t{whereUpdateEquals}", obj, transaction, commandTimeout);
					return count > 0;
				};
			}
		}

		private SqlListInt<T> CreateBulkUpdate()
		{
			if (Info.UpdateKeyColumns.Count == 0) {
				// NoUpdatesAttribute
				return DoNothing;
			}
			else {
				string dropBulkTableCmd = DropBulkTableCmd();
				string bulkUpdateSetParams = UpdateSetTables();
				string selectEqualityIntoStagingCmd = SelectIntoStagingTable(Info.BulkUpdateColumns);
				string updateEquals = WhereEqualsTables(Info.UpdateKeyColumns);
				return (connection, objs, transaction, commandTimeout) =>
				{
					connection.Execute(dropBulkTableCmd, null, transaction, commandTimeout);
					connection.Execute(selectEqualityIntoStagingCmd, null, transaction, commandTimeout);
					ExtraUtil.BulkInsert(connection, objs, transaction, BulkStagingTable, DataReaderFactory, Info.BulkUpdateColumns, commandTimeout,
						SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.KeepNulls | SqlBulkCopyOptions.TableLock);
					string bulkUpdateCmd = $"UPDATE {TableName}{bulkUpdateSetParams}\nFROM {BulkStagingTable}\nWHERE \t{updateEquals}";
					int count = connection.Execute(bulkUpdateCmd, null, transaction, commandTimeout);
					connection.Execute(dropBulkTableCmd, null, transaction, commandTimeout);
					return count;
				};
			}
		}
		#endregion Updates

		#region Upserts
		private DbTBool<T> CreateUpsert()
		{
			if (!Info.UpdateColumns.Any()) {
				if (!Info.InsertColumns.Any()) {
					return DoNothing;
				}
				else {
					// Insert if not exists
					return Queries.InsertIfNotExists;
				}
			}
			else {
				DbTBool<T> update = Queries.Update;
				if (!Info.InsertColumns.Any()) {
					// Update only
					return (connection, obj, transaction, commandTimeout) =>
					{
						bool success = update(connection, obj, transaction, commandTimeout);
						return false;
					};
				}
				else {
					DbTBool<T> insertIfNotExists = Queries.InsertIfNotExists;
					return (connection, obj, transaction, commandTimeout) =>
					{
						bool success = update(connection, obj, transaction, commandTimeout);
						if (success)
							return false;
						success = insertIfNotExists(connection, obj, transaction, commandTimeout);
						return success;
					};
				}
			}
		}

		private SqlListInt<T> CreateBulkUpsert()
		{
			if (!Info.UpdateColumns.Any()) {
				if (!Info.InsertColumns.Any()) {
					return DoNothing;
				}
				else {
					// Insert if not exists
					return Queries.BulkInsertIfNotExists;
				}
			}
			else {
				SqlListInt<T> bulkUpdate = Queries.BulkUpdate;
				if (!Info.InsertColumns.Any()) {
					// Update only
					return (connection, objs, transaction, commandTimeout) =>
					{
						int count = bulkUpdate(connection, objs, transaction, commandTimeout);
						return 0;
					};
				}
				else {
					string dropBulkTableCmd = DropBulkTableCmd();
					// Insert or Update
					string bulkUpdateSetParams = UpdateSetTables();
					string updateEquals = WhereEqualsTables(Info.UpdateKeyColumns);
					string selectUpsertIntoStagingCmd = SelectIntoStagingTable(Info.UpsertColumns);
					string equalsTables = WhereEqualsTables(EqualityColumns);
					string insertIntoCmd = InsertIntoCmd();
					string insertColumns = ColumnNames(Info.InsertColumns);
					return (connection, objs, transaction, commandTimeout) =>
					{
						connection.Execute(dropBulkTableCmd, null, transaction, commandTimeout);
						connection.Execute(selectUpsertIntoStagingCmd, null, transaction, commandTimeout);
						ExtraUtil.BulkInsert(connection, objs, transaction, BulkStagingTable, DataReaderFactory, Info.UpsertColumns, commandTimeout,
							SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.KeepNulls | SqlBulkCopyOptions.TableLock);
						string bulkUpdateCmd = $"UPDATE {TableName}{bulkUpdateSetParams}\nFROM {BulkStagingTable}\nWHERE \t{updateEquals}";
						int countUpdate = connection.Execute(bulkUpdateCmd, null, transaction, commandTimeout);
						string bulkInsertIfNotExistsCmd = $"{insertIntoCmd}\nSELECT {insertColumns}\nFROM {BulkStagingTable}\nWHERE NOT EXISTS (\nSELECT * FROM {TableName}\nWHERE \t{equalsTables})";
						int countInsert = connection.Execute(bulkInsertIfNotExistsCmd, null, transaction, commandTimeout);
						connection.Execute(dropBulkTableCmd, null, transaction, commandTimeout);
						return countInsert;
					};
				}
			}
		}
		#endregion Upserts
	}

	/// <summary>
	/// Stores metadata and generates SQL commands and queries for the given type.
	/// </summary>
	/// <typeparam name="T">The table type.</typeparam>
	/// <typeparam name="KeyType">The key type.</typeparam>
	public sealed class SqlBuilder<T, KeyType> : ISqlBuilder
		where T : class
	{
		internal SqlBuilder(SqlBuilder<T> parent)
		{
			Parent = parent;
			SqlQueries<T, KeyType> queries = new SqlQueries<T, KeyType>()
			{
				Get = CreateGet(),
				LazyBulkDelete = new Lazy<SqlKeysInt<T, KeyType>>(() => CreateBulkDelete()),
				LazyBulkGet = new Lazy<DbKeysList<T, KeyType>>(() => CreateBulkGet()),
				LazyDelete = new Lazy<DbKeyBool<KeyType>>(() => CreateDelete()),
				LazyGetKeys = new Lazy<DbWhereKeys<KeyType>>(() => CreateGetKeys()),
			};
			Queries = queries;
		}

		/// <summary>
		/// The <see cref="SqlBuilder{T}"/> that created this object.
		/// </summary>
		public SqlBuilder<T> Parent { get; private set; }
		/// <summary>
		/// The quoted table name or the class name.
		/// </summary>
		public string TableName => Info.TableName;
		/// <summary>
		/// The temporary table name for bulk operations.
		/// </summary>
		public string BulkStagingTable => Parent.BulkStagingTable;
		/// <summary>
		/// The syntax used to generate SQL commands.
		/// </summary>
		public SqlSyntax Syntax => Info.Syntax;
		/// <summary>
		/// Stores metadata for for the given type.
		/// </summary>
		public SqlTypeInfo Info => Parent.Info;
		/// <summary>
		///  All valid columns for the given type.
		/// </summary>
		public IReadOnlyList<SqlColumn> Columns => Parent.Columns;
		/// <summary>
		/// The key column.
		/// </summary>
		public SqlColumn EqualityColumn => Parent.EqualityColumns[0];
		/// <summary>
		/// The queries and commands for this type.
		/// </summary>
		public ISqlQueries<T, KeyType> Queries { get; private set; }
		/// <summary>
		/// The same value as <see cref="Queries"/>.
		/// </summary>
		public object QueriesObject => Queries;

		#region Create Delegates
		private DbWhereKeys<KeyType> CreateGetKeys()
		{
			string paramsSelectKeys = Parent.ParamsSelect(Parent.EqualityColumns);
			return (connection, whereCondition, param, transaction, buffered, commandTimeout) =>
			{
				string query = $"SELECT {paramsSelectKeys}\nFROM {TableName}\n{whereCondition}";
				IEnumerable<KeyType> result = connection.Query<KeyType>(query, param, transaction, buffered, commandTimeout);
				return result;
			};
		}

		private SqlKeysInt<T, KeyType> CreateBulkDelete()
		{
			if (Info.DeleteKeyColumns.Count == 0) {
				//	NoDeltesAttribute
				return (connection, keys, transaction, commandTimeout) =>
				{
					return 0;
				};
			}
			else {
				string deleteCmd = Parent.DeleteCmd();
				string keyName = EqualityColumn.ColumnName;
				return (connection, keys, transaction, commandTimeout) =>
				{
					int count = 0;
					string bulkDeleteCmd = $"{deleteCmd}WHERE \t{keyName} in @Keys";
					foreach (IEnumerable<KeyType> Keys in Extensions.UtilExtensions.Partition<KeyType>(keys, 2000)) {
						int deleted = connection.Execute(bulkDeleteCmd, new { Keys }, transaction, commandTimeout);
						count += deleted;
					}
					return count;
				};
			}
		}

		private DbKeyObj<T, KeyType> CreateGet()
		{
			string paramsSelectFromTable = Parent.ParamsSelectFromTable();
			string whereEquals = Parent.WhereEquals(Parent.EqualityColumns);
			string keyName = EqualityColumn.Property.Name;
			return (connection, key, transaction, commandTimeout) =>
			{
				string query = $"SELECT {paramsSelectFromTable}WHERE \t{whereEquals}";
				IDictionary<string, object> obj = new ExpandoObject();
				obj.Add(keyName, key);
				T val = connection.QueryFirstOrDefault<T>(query, obj, transaction, commandTimeout);
				return val;
			};
		}

		private DbKeyBool<KeyType> CreateDelete()
		{
			if (Info.DeleteKeyColumns.Count == 0) {
				//	NoDeltesAttribute
				return (connection, obj, transaction, commandTimeout) =>
				{
					return false;
				};
			}
			else {
				string deleteCmd = Parent.DeleteCmd();
				string deleteEquals = Parent.WhereEquals(Info.DeleteKeyColumns);
				string keyName = EqualityColumn.Property.Name;
				return (connection, key, transaction, commandTimeout) =>
				{
					string cmd = $"{deleteCmd}WHERE \t{deleteEquals}";
					IDictionary<string, object> obj = new ExpandoObject();
					obj.Add(keyName, key);
					int count = connection.Execute(cmd, obj, transaction, commandTimeout);
					return count > 0;
				};
			}
		}

		private DbKeysList<T, KeyType> CreateBulkGet()
		{
			string paramsSelectFromTableBulk = Parent.ParamsSelectFromTableBulk();
			string keyName = EqualityColumn.ColumnName;
			return (connection, keys, transaction, commandTimeout) =>
			{
				List<T> result = new List<T>();
				string bulkGetKeysQuery = $"SELECT {paramsSelectFromTableBulk}WHERE \t{keyName} in @Keys";
				foreach (IEnumerable<KeyType> Keys in Extensions.UtilExtensions.Partition<KeyType>(keys.AsList(), 2000)) {
					IEnumerable<T> list = connection.Query<T>(bulkGetKeysQuery, new { Keys }, transaction, true, commandTimeout);
					result.AddRange(list);
				}
				return result;
			};
		}
		#endregion Create Delegates
	}
}