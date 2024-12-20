﻿#region License
// Released under MIT License 
// License: https://opensource.org/licenses/MIT
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
using System.Data;
using Microsoft.Data.SqlClient;

namespace Dapper.Extra.Cache
{
	/// <summary>
	/// Stores a backup state of all caches in case of a rollback. This has been done efficiently so that it
	/// only backs up up modified items in a cache instead of the whole state of the cache.
	/// </summary>
	public class DbCacheTransaction : IDbTransaction
	{
		internal DbCacheTransaction(IDbTransaction transaction)
		{
			Transaction = transaction;
		}

		internal List<IDbTransaction> TransactionStorage { get; set; } = new List<IDbTransaction>();

		/// <summary>
		/// Adds tables to the transaction.
		/// </summary>
		/// <param name="tables">The tables to add.</param>
		/// <returns>The transaction.</returns>
		public DbCacheTransaction Add(params ICacheTable[] tables)
		{
			if (Transaction == null)
				throw new InvalidOperationException("The transaction is closed.");
			foreach (ICacheTable table in tables) {
				table.BeginTransaction(this);
			}
			return this;
		}

		internal IDbTransaction Transaction { get; set; }
		/// <summary>
		/// Specifies the Connection object to associate with the transaction.
		/// </summary>
		public IDbConnection Connection => Transaction.Connection;
		/// <summary>
		/// Specifies the <see cref="IDbTransaction.IsolationLevel"/> for this transaction.
		/// </summary>
		public IsolationLevel IsolationLevel => Transaction.IsolationLevel;

		/// <summary>
		/// Commits the database transaction.
		/// </summary>
		public void Commit()
		{
			if (Transaction == null)
				return;
			Transaction.Commit();
			foreach (IDbTransaction storage in TransactionStorage) {
				storage.Commit();
				storage.Dispose();
			}
			TransactionStorage.Clear();
			Transaction = null;
		}

		/// <summary>
		///  Rolls back the transaction.
		/// </summary>
		public void Rollback()
		{
			if (Transaction == null)
				return;
			foreach (IDbTransaction storage in TransactionStorage) {
				storage.Rollback();
			}
			Transaction.Rollback();
		}

		#region IDisposable Support
		private bool disposedValue = false; // To detect redundant calls

		/// <summary>
		/// Disposes of the internal <see cref="SqlTransaction"/> and rolls back the caches.
		/// </summary>
		protected virtual void Dispose(bool disposing)
		{
			if (!disposedValue) {
				if (disposing) {
					// TODO: dispose managed state (managed objects).
					if(Transaction != null) {
						IDbConnection connection = Connection;
						foreach (IDbTransaction storage in TransactionStorage) {
							storage.Dispose();
						}
						Transaction.Dispose();
						Transaction = null;
						if (connection.State == ConnectionState.Open)
							connection.Close();
					}
				}
				// TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
				// TODO: set large fields to null.
				disposedValue = true;
			}
		}

		// TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
		// ~CacheTransaction() {
		//   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
		//   Dispose(false);
		// }

		/// <summary>
		/// Disposes of the internal <see cref="SqlTransaction"/> and rolls back the caches.
		/// </summary>
		public void Dispose()
		{
			// This code added to correctly implement the disposable pattern.
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			Dispose(true);
			// TODO: uncomment the following line if the finalizer is overridden above.
			// GC.SuppressFinalize(this);
		}
		#endregion
	}
}
