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
using Fasterflect;

namespace Dapper.Extra.Adapters
{
	/// <summary>
	/// An <see cref="SqlAdapter"/> that changes the string comparison for <see cref="IEqualityComparer{T}"/> generated by <see cref="ExtraCrud.EqualityComparer{T}"/>.
	/// </summary>
	public class CollateAdapter : ISqlAdapter
	{
		private readonly ISqlAdapter Adapter;

		/// <summary>
		/// Constructs An <see cref="ISqlAdapter"/> that changes the string comparison for <see cref="IEqualityComparer{T}"/> generated by <see cref="ExtraCrud.EqualityComparer{T}"/>.
		/// </summary>
		/// <param name="stringComparer">The comparer for strings comparisons. This is <see cref="System.StringComparer.Ordinal"/> (case-sensitive) if null.</param>
		public CollateAdapter(IEqualityComparer<string> stringComparer) : this(null, stringComparer) 
		{
		}

		/// <summary>
		/// Constructs An <see cref="ISqlAdapter"/> that changes the string comparison for <see cref="IEqualityComparer{T}"/> generated by <see cref="ExtraCrud.EqualityComparer{T}"/>.
		/// </summary>
		/// <param name="adapter">The adapter to copy. By default this is the SQL Server adapter.</param>
		/// <param name="stringComparer">The comparer for strings comparisons. By default this is <see cref="System.StringComparer.Ordinal"/> (case-sensitive).</param>
		public CollateAdapter(ISqlAdapter adapter, IEqualityComparer<string> stringComparer = null)
		{
			Adapter = adapter ?? SqlAdapter.SQLServer;
			StringComparer = stringComparer ?? System.StringComparer.Ordinal;
		}

		public string LimitQuery => Adapter.LimitQuery;

		public SqlDialect Dialect => Adapter.Dialect;

		public string CurrentDate => Adapter.CurrentDate;

		public string CurrentDateTime => Adapter.CurrentDateTime;

		public string CurrentDateTimeUtc => Adapter.CurrentDateTimeUtc;

		public string CurrentDateUtc => Adapter.CurrentDateUtc;

		public IEqualityComparer<string> StringComparer { get; private set; }

		public void BulkInsert<T>(IDbConnection connection, IEnumerable<T> objs, IDbTransaction transaction, string tableName, DataReaderFactory factory, IEnumerable<SqlColumn> columns, int commandTimeout = 30, SqlBulkCopyOptions options = SqlBulkCopyOptions.Default) where T : class
		{
			Adapter.BulkInsert(connection, objs, transaction, tableName, factory, columns, commandTimeout, options);
		}

		public string CreateTempTableName(string tableName)
		{
			return Adapter.CreateTempTableName(tableName);
		}

		public string DropTempTableIfExists(string tableName)
		{
			return Adapter.DropTempTableIfExists(tableName);
		}

		public string QuoteIdentifier(string identifier)
		{
			return Adapter.QuoteIdentifier(identifier);
		}

		public string SelectIdentityQuery(Type type)
		{
			return Adapter.SelectIdentityQuery(type);
		}

		public string SelectIntoTempTable(string sourceTable, string tempTable, IEnumerable<SqlColumn> columns)
		{
			return Adapter.SelectIntoTempTable(sourceTable, tempTable, columns);
		}

		public string TruncateTable(string tableName)
		{
			return Adapter.TruncateTable(tableName);
		}
	}
}
