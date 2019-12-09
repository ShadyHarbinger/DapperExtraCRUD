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
using System.Collections.Generic;
using Dapper.Extra.Internal;
using Dapper.Extra.Cache.Internal;
using Dapper.Extra.Utilities;
using Fasterflect;
using System.Collections.Concurrent;
using System.Linq;

namespace Dapper.Extra.Cache
{
	public sealed class DbCacheTable<T, R> : ICacheTable<T, R>, ICacheTable
		where T : class
		where R : CacheItem<T>, new()
	{
		internal DbCacheTable(string connectionString)
		{
			Builder = ExtraCrud.Builder<T>();
			if (Builder.Info.KeyColumns.Count == 0)
				throw new InvalidOperationException(typeof(T).FullName + " is not usable without a valid key.");
			DAO = new DataAccessObject<T>(connectionString);
			AAO = new AutoAccessObject<T>(connectionString);
			Access = AAO;
			AutoCache = new CacheAutoStorage<T, R>();
			Storage = AutoCache;
			CreateFromKey = Builder.ObjectFromKey;
			AutoKeyColumn = Builder.Info.AutoKeyColumn;
			AutoSyncInsert = Builder.Queries.InsertAutoSync == null;
			AutoSyncUpdate = Builder.Queries.UpdateAutoSync != null;
		}

		private readonly Func<object, T> CreateFromKey;

		public ICacheStorage<T, R> Storage { get; private set; }
		private readonly CacheAutoStorage<T, R> AutoCache;
		private IAccessObjectSync<T> Access;
		private readonly DataAccessObject<T> DAO;
		private readonly AutoAccessObject<T> AAO;
		private readonly SqlBuilder<T> Builder;
		private readonly SqlColumn AutoKeyColumn;
		private readonly bool AutoSyncInsert;
		private readonly bool AutoSyncUpdate;

		public SqlTypeInfo Info => Builder.Info;

		private long MaxAutoKey()
		{
			long max = Access.GetKeys<long?>("WHERE " + AutoKeyColumn.ColumnName + " = (SELECT MAX(" + AutoKeyColumn.ColumnName + ") FROM " + Info.TableName + ")").FirstOrDefault() ?? int.MinValue;
			return max;
		}

		public R this[T key, int commandTimeout = 30] {
			get {
				if (!((IDictionary<T, R>)Storage).TryGetValue(key, out R value)) {
					value = Get(key, commandTimeout);
				}
				return value;
			}
		}

		public R this[object key, int commandTimeout = 30] {
			get {
				T obj = CreateFromKey(key);
				R ret = this[obj, commandTimeout];
				return ret;
			}
		}

		#region ICacheTable
		public DbCacheTransaction BeginTransaction()
		{
			if (Access != AutoCache)
				throw new InvalidOperationException("Cache is already part of a transaction.");
			try {
				DAO.Connection.Open();
				DAO.Transaction = DAO.Connection.BeginTransaction();
				DbCacheTransaction transaction = new DbCacheTransaction(DAO.Transaction);
				CacheTransactionStorage<T, R> storage = new CacheTransactionStorage<T, R>(AutoCache.Cache, transaction, CreateFromKey, CloseTransaction);
				Storage = storage;
				transaction.TransactionStorage.Add(storage);
				Access = DAO;
				return transaction;
			}
			catch {
				DAO.Transaction = null;
				if (DAO.Connection.State == System.Data.ConnectionState.Open) {
					DAO.Connection.Close();
				}
				throw;
			}
		}

		public void BeginTransaction(DbCacheTransaction transaction)
		{
			if (Access != AutoCache)
				throw new InvalidOperationException("Cache is already part of a transaction.");
			DAO.Connection = transaction.Transaction.Connection;
			DAO.Transaction = transaction.Transaction;
			Storage = new CacheTransactionStorage<T, R>(AutoCache.Cache, transaction, CreateFromKey, CloseTransaction);
			Access = DAO;
		}

		private void CloseTransaction()
		{
			if (DAO.Transaction != null) {
				DAO.Transaction.Dispose();
				DAO.Connection.Close();
				DAO.Transaction = null;
				Storage = AutoCache;
				Access = AAO;
			}
		}
		#endregion ICacheTable

		#region Bulk

		/// <summary>
		/// Deletes the rows with the given keys.
		/// </summary>
		/// <param name="keys">The keys for the rows to delete.</param>
		/// <param name="commandTimeout">Number of seconds before command execution timeout.</param>
		/// <returns>The number of deleted rows.</returns>
		public int BulkDelete(IEnumerable<object> keys, int commandTimeout = 30)
		{
			int count = Access.BulkDelete(keys, commandTimeout);
			Storage.RemoveKeys(keys);
			return count;
		}

		/// <summary>
		/// Deletes the given rows.
		/// </summary>
		/// <param name="objs">The objects to delete.</param>
		/// <param name="commandTimeout">Number of seconds before command execution timeout.</param>
		/// <returns>The number of deleted rows.</returns>
		public int BulkDelete(IEnumerable<T> objs, int commandTimeout = 30)
		{
			int count = Access.BulkDelete(objs, commandTimeout);
			Storage.Remove(objs);
			return count;
		}

		/// <summary>
		/// Selects the rows with the given keys.
		/// </summary>
		/// <param name="keys">The keys of the rows to select.</param>
		/// <param name="commandTimeout">Number of seconds before command execution timeout.</param>
		/// <returns>The rows with the given keys.</returns>
		public IEnumerable<R> BulkGet(IEnumerable<object> keys, int commandTimeout = 30)
		{
			IEnumerable<T> list = Access.BulkGet(keys, commandTimeout);
			IEnumerable<R> result = Storage.Add(list);
			return result;
		}

		/// <summary>
		/// Selects the rows with the given keys.
		/// </summary>
		/// <param name="objs">The objects to select.</param>
		/// <param name="commandTimeout">Number of seconds before command execution timeout.</param>
		/// <returns>The rows that match the given keys.</returns>
		public IEnumerable<R> BulkGet(IEnumerable<T> objs, int commandTimeout = 30)
		{
			IEnumerable<T> list = Access.BulkGet(objs, commandTimeout);
			IEnumerable<R> result = Storage.Add(list);
			return result;
		}

		/// <summary>
		/// Inserts the given rows.
		/// </summary>
		/// <param name="objs">The objects to insert.</param>
		/// <param name="commandTimeout">Number of seconds before command execution timeout.</param>
		public void BulkInsert(IEnumerable<T> objs, int commandTimeout = 30)
		{
			if(AutoKeyColumn != null) {
				long maxAutoKey = MaxAutoKey();
				Access.BulkInsert(objs, commandTimeout);
				GetList("WHERE " + AutoKeyColumn.ColumnName + " > " + maxAutoKey, commandTimeout);
			}
			else {
				Access.BulkInsert(objs, commandTimeout);
				if(AutoSyncInsert)
					BulkGet(objs);
				else
					Storage.Add(objs);
			}
		}

		/// <summary>
		/// Inserts the given rows if they do not exist.
		/// </summary>
		/// <param name="objs">The objects to insert.</param>
		/// <param name="commandTimeout">Number of seconds before command execution timeout.</param>
		/// <returns>The number of rows inserted.</returns>
		public int BulkInsertIfNotExists(IEnumerable<T> objs, int commandTimeout = 30)
		{
			int count;
			if (AutoKeyColumn != null) {
				long maxAutoKey = MaxAutoKey();
				count = Access.BulkInsertIfNotExists(objs, commandTimeout);
				GetList("WHERE " + AutoKeyColumn.ColumnName + " > " + maxAutoKey, commandTimeout);
			}
			else {
				count = Access.BulkInsertIfNotExists(objs, commandTimeout);
				BulkGet(objs);
			}
			return count;
		}

		/// <summary>
		/// Updates the given rows.
		/// </summary>
		/// <param name="objs">The objects to update.</param>
		/// <param name="commandTimeout">Number of seconds before command execution timeout.</param>
		/// <returns>The number of updated rows.</returns>
		public int BulkUpdate(IEnumerable<T> objs, int commandTimeout = 30)
		{
			int count = Access.BulkUpdate(objs, commandTimeout);
			if (AutoSyncUpdate)
				BulkGet(objs);
			else {
				foreach (T obj in objs) {
					if (((IDictionary<T, R>)Storage).TryGetValue(obj, out R item)) {
						item.CacheValue = obj;
					}
				}
			}
			return count;
		}

		/// <summary>
		/// Upserts the given rows.
		/// </summary>
		/// <param name="objs">The objects to upsert.</param>
		/// <param name="commandTimeout">Number of seconds before command execution timeout.</param>
		/// <returns>The number of upserted rows.</returns>
		public int BulkUpsert(IEnumerable<T> objs, int commandTimeout = 30)
		{
			int count;
			if(AutoKeyColumn != null) {
				long maxAutoKey = MaxAutoKey();
				Storage.Add(objs);
				count = Access.BulkUpsert(objs, commandTimeout);
				GetList("WHERE " + AutoKeyColumn.ColumnName + " > " + maxAutoKey, commandTimeout);
			}
			else {
				count = Access.BulkUpsert(objs, commandTimeout);
				if (AutoSyncInsert || AutoSyncUpdate) {
					BulkGet(objs, commandTimeout);
				}
				else {
					Storage.Add(objs);
				}
			}
			return count;
		}

		#endregion Bulk

		#region Other Methods

		/// <summary>
		/// Deletes the row with the given key.
		/// </summary>
		/// <param name="key">The key of the row to delete.</param>
		/// <param name="commandTimeout">Number of seconds before command execution timeout.</param>
		/// <returns>True if the row was deleted; false otherwise.</returns>
		public bool Delete(object key, int commandTimeout = 30)
		{
			bool success = Access.Delete(key, commandTimeout);
			Storage.RemoveKey(key);
			return success;
		}

		/// <summary>
		/// Deletes the given row.
		/// </summary>
		/// <param name="obj">The object to delete.</param>
		/// <param name="commandTimeout">Number of seconds before command execution timeout.</param>
		/// <returns>True if the row was deleted; false otherwise.</returns>
		public bool Delete(T obj, int commandTimeout = 30)
		{
			bool success = Access.Delete(obj, commandTimeout);
			Storage.RemoveKey(obj);
			return success;
		}

		/// <summary>
		/// Deletes the rows that match the given condition.
		/// </summary>
		/// <param name="whereCondition">The where condition to use for this query.</param>
		/// <param name="param">The parameters to use for this query.</param>
		/// <param name="commandTimeout">Number of seconds before command execution timeout.</param>
		/// <returns>The number of deleted rows.</returns>
		public int DeleteList(string whereCondition = "", object param = null, int commandTimeout = 30)
		{
			List<T> list = Access.GetList(whereCondition, param, true, commandTimeout).AsList();
			int count = Access.DeleteList(whereCondition, param, commandTimeout);
			Storage.Remove(list);
			return count;
		}

		/// <summary>
		/// Selects the row with the given key.
		/// </summary>
		/// <param name="key">The key of the row to select.</param>
		/// <param name="commandTimeout">Number of seconds before command execution timeout.</param>
		/// <returns>The row with the given key.</returns>
		public R Get(object key, int commandTimeout = 30)
		{
			T obj = Access.Get(key, commandTimeout);
			R item = Storage.Add(obj);
			return item;
		}

		/// <summary>
		/// Selects a row.
		/// </summary>
		/// <param name="obj">The object to select.</param>
		/// <param name="commandTimeout">Number of seconds before command execution timeout.</param>
		/// <returns>The selected row if it exists; otherwise null.</returns>
		public R Get(T obj, int commandTimeout = 30)
		{
			obj = Access.Get(obj, commandTimeout);
			R item = Storage.Add(obj);
			return item;
		}

		/// <summary>
		/// Selects the rows that match the given condition.
		/// </summary>
		/// <param name="whereCondition">The where condition to use for this query.</param>
		/// <param name="param">The parameters to use for this query.</param>
		/// <param name="commandTimeout">Number of seconds before command execution timeout.</param>
		/// <returns>The rows that match the given condition.</returns>
		public IEnumerable<R> GetDistinct(string whereCondition = "", object param = null, int commandTimeout = 30)
		{
			List<T> list = Access.GetDistinct(whereCondition, param, true, commandTimeout).AsList();
			List<R> result = Storage.Add(list);
			return result;
		}

		/// <summary>
		/// Selects the rows that match the given condition.
		/// </summary>
		/// <param name="columnFilter">The type whose properties will filter the result.</param>
		/// <param name="whereCondition">The where condition to use for this query.</param>
		/// <param name="param">The parameters to use for this query.</param>
		/// <param name="commandTimeout">Number of seconds before command execution timeout.</param>
		/// <returns>The rows that match the given condition.</returns>
		public IEnumerable<R> GetDistinct(Type columnFilter, string whereCondition = "", object param = null, int commandTimeout = 30)
		{
			List<T> list = Access.GetDistinct(columnFilter, whereCondition, param, true, commandTimeout).AsList();
			List<R> result = Storage.Add(list);
			return result;
		}

		/// <summary>
		/// Selects the rows that match the given condition.
		/// </summary>
		/// <param name="limit">The maximum number of rows.</param>
		/// <param name="whereCondition">The where condition to use for this query.</param>
		/// <param name="param">The parameters to use for this query.</param>
		/// <param name="buffered">Whether to buffer the results in memory.</param>
		/// <param name="commandTimeout">Number of seconds before command execution timeout.</param>
		/// <returns>The rows that match the given condition.</returns>
		public IEnumerable<R> GetDistinctLimit(int limit, string whereCondition = "", object param = null, int commandTimeout = 30)
		{
			List<T> list = Access.GetDistinctLimit(limit, whereCondition, param, true, commandTimeout).AsList();
			List<R> result = Storage.Add(list);
			return result;
		}

		/// <summary>
		/// Selects the rows that match the given condition.
		/// </summary>
		/// <param name="columnFilter">The type whose properties will filter the result.</param>
		/// <param name="limit">The maximum number of rows.</param>
		/// <param name="whereCondition">The where condition to use for this query.</param>
		/// <param name="param">The parameters to use for this query.</param>
		/// <param name="commandTimeout">Number of seconds before command execution timeout.</param>
		/// <returns>The rows that match the given condition.</returns>
		public IEnumerable<R> GetDistinctLimit(Type columnFilter, int limit, string whereCondition = "", object param = null, int commandTimeout = 30)
		{
			List<T> list = Access.GetDistinctLimit(limit, whereCondition, param, true, commandTimeout).AsList();
			List<R> result = Storage.Add(list);
			return result;
		}

		/// <summary>
		/// Selects the rows with the given keys.
		/// </summary>
		/// <typeparam name="KeyType">The key type.</typeparam>
		/// <param name="whereCondition">The where condition to use for this query.</param>
		/// <param name="param">The parameters to use for this query.</param>
		/// <param name="buffered">Whether to buffer the results in memory.</param>
		/// <param name="commandTimeout">Number of seconds before command execution timeout.</param>
		/// <returns>The keys that match the given condition.</returns>
		public IEnumerable<KeyType> GetKeys<KeyType>(string whereCondition = "", object param = null, int commandTimeout = 30)
		{
			List<KeyType> keys = Access.GetKeys<KeyType>(whereCondition, param, true, commandTimeout).AsList();
			return keys;
		}

		/// <summary>
		/// Selects the keys that match the given condition.
		/// </summary>
		/// <param name="whereCondition">The where condition to use for this query.</param>
		/// <param name="param">The parameters to use for this query.</param>
		/// <param name="buffered">Whether to buffer the results in memory.</param>
		/// <param name="commandTimeout">Number of seconds before command execution timeout.</param>
		/// <returns>The keys that match the given condition.</returns>
		public IEnumerable<T> GetKeys(string whereCondition = "", object param = null, int commandTimeout = 30)
		{
			List<T> keys = Access.GetKeys(whereCondition, param, true, commandTimeout).AsList();
			return keys;
		}

		/// <summary>
		/// Selects a limited number of rows that match the given condition.
		/// </summary>
		/// <param name="limit">The maximum number of rows.</param>
		/// <param name="whereCondition">The where condition to use for this query.</param>
		/// <param name="param">The parameters to use for this query.</param>
		/// <param name="buffered">Whether to buffer the results in memory.</param>
		/// <param name="commandTimeout">Number of seconds before command execution timeout.</param>
		/// <returns>The limited number of rows that match the given condition.</returns>
		public IEnumerable<R> GetLimit(int limit, string whereCondition = "", object param = null, int commandTimeout = 30)
		{
			List<T> list = Access.GetLimit(limit, whereCondition, param, true, commandTimeout).AsList();
			List<R> result = Storage.Add(list);
			return result;
		}

		/// <summary>
		/// Selects a limited number of rows that match the given condition.
		/// </summary>
		/// <param name="columnFilter">The type whose properties will filter the result.</param>
		/// <param name="limit">The maximum number of rows.</param>
		/// <param name="whereCondition">The where condition to use for this query.</param>
		/// <param name="param">The parameters to use for this query.</param>
		/// <param name="commandTimeout">Number of seconds before command execution timeout.</param>
		/// <returns>A limited number of rows that match the given condition.</returns>
		public IEnumerable<R> GetLimit(Type columnFilter, int limit, string whereCondition = "", object param = null, int commandTimeout = 30)
		{
			List<T> list = Access.GetLimit(columnFilter, limit, whereCondition, param, true, commandTimeout).AsList();
			List<R> result = Storage.Add(list);
			return result;
		}

		/// <summary>
		/// Selects the rows that match the given condition.
		/// </summary>
		/// <param name="whereCondition">The where condition to use for this query.</param>
		/// <param name="param">The parameters to use for this query.</param>
		/// <param name="buffered">Whether to buffer the results in memory.</param>
		/// <param name="commandTimeout">Number of seconds before command execution timeout.</param>
		/// <returns>The rows that match the given condition.</returns>
		public IEnumerable<R> GetList(string whereCondition = "", object param = null, int commandTimeout = 30)
		{
			List<T> list = Access.GetList(whereCondition, param, true, commandTimeout).AsList();
			List<R> result = Storage.Add(list);
			return result;
		}

		/// <summary>
		/// Selects the rows that match the given condition.
		/// </summary>
		/// <param name="columnFilter">The type whose properties will filter the result.</param>
		/// <param name="whereCondition">The where condition to use for this query.</param>
		/// <param name="param">The parameters to use for this query.</param>
		/// <param name="buffered">Whether to buffer the results in memory.</param>
		/// <param name="commandTimeout">Number of seconds before command execution timeout.</param>
		/// <returns>The rows that match the given condition.</returns>
		public IEnumerable<R> GetList(Type columnFilter, string whereCondition = "", object param = null, int commandTimeout = 30)
		{
			List<T> list = Access.GetList(columnFilter, whereCondition, param, true, commandTimeout).AsList();
			List<R> result = Storage.Add(list);
			return result;
		}

		/// <summary>
		/// Inserts a row.
		/// </summary>
		/// <param name="obj">The object to insert.</param>
		/// <param name="commandTimeout">Number of seconds before command execution timeout.</param>
		public R Insert(T obj, int commandTimeout = 30)
		{
			Access.Insert(obj, commandTimeout);
			R item = Storage.Add(obj);
			return item;
		}

		/// <summary>
		/// Inserts a row if it does not exist.
		/// </summary>
		/// <param name="obj">The object to insert.</param>
		/// <param name="commandTimeout">Number of seconds before command execution timeout.</param>
		/// <returns>True if the the row was inserted; false otherwise.</returns>
		public R InsertIfNotExists(T obj, int commandTimeout = 30)
		{
			if (!Access.InsertIfNotExists(obj, commandTimeout)) {
				return null;
			}
			R item = Storage.Add(obj);
			return item;
		}

		/// <summary>
		/// Counts the number of rows that match the given condition.
		/// </summary>
		/// <param name="whereCondition">The where condition to use for this query.</param>
		/// <param name="param">The parameters to use for this query.</param>
		/// <param name="commandTimeout">Number of seconds before command execution timeout.</param>
		/// <returns>The number of rows that match the given condition.</returns>
		public int RecordCount(string whereCondition = "", object param = null, int commandTimeout = 30)
		{
			int count = Access.RecordCount(whereCondition, param, commandTimeout);
			return count;
		}

		/// <summary>
		/// Truncates all rows.
		/// </summary>
		/// <param name="commandTimeout">Number of seconds before command execution timeout.</param>
		public void Truncate(int commandTimeout = 30)
		{
			Access.Truncate(commandTimeout);
		}

		private readonly ConcurrentDictionary<Type, ObjectMapper> mappers = new ConcurrentDictionary<Type, ObjectMapper>();

		/// <summary>
		/// Updates a row.
		/// </summary>
		/// <param name="obj">The object to update.</param>
		/// <param name="commandTimeout">Number of seconds before command execution timeout.</param>
		/// <returns>True if the row was updated; false otherwise.</returns>
		public bool Update(object obj, int commandTimeout = 30)
		{
			bool success = Access.Update(obj, commandTimeout);
			if (success) {
				Type type = obj.GetType();
				if (!mappers.TryGetValue(type, out ObjectMapper mapper)) {
					mapper = Reflect.Mapper(type, typeof(T), Info.KeyColumns.Select(c => c.Property.Name).ToArray());
					mappers.TryAdd(type, mapper);
				}
				T value = (T)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(T));
				mapper(obj, value);
				if (((IDictionary<T, R>)Storage).TryGetValue(value, out R item)) {
					mapper(obj, item.CacheValue);
				}
			}
			return success;
		}


		/// <summary>
		/// Updates a row.
		/// </summary>
		/// <param name="obj">The object to update.</param>
		/// <param name="commandTimeout">Number of seconds before command execution timeout.</param>
		/// <returns>True if the row was updated; false otherwise.</returns>
		public bool Update(T obj, int commandTimeout = 30)
		{
			bool success = Access.Update(obj, commandTimeout);
			if(success) {
				Storage.Add(obj);
			}
			return success;
		}

		/// <summary>
		/// Upserts a row.
		/// </summary>
		/// <param name="obj">The object to upsert.</param>
		/// <param name="commandTimeout">Number of seconds before command execution timeout.</param>
		/// <returns>True if the object was upserted; false otherwise.</returns>
		public bool Upsert(T obj, int commandTimeout = 30)
		{
			bool success = Access.Upsert(obj, commandTimeout);
			_ = Storage.Add(obj);
			return success;
		}

		#endregion Other Methods
	}
}
