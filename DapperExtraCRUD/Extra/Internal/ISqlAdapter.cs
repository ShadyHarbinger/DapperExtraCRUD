﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using Fasterflect;

namespace Dapper.Extra.Internal
{
	/// <summary>
	/// Generates SQL commands using a given syntax.
	/// </summary>
	public interface ISqlAdapter
	{
		/// <summary>
		/// The syntax used to generate SQL commands.
		/// </summary>
		SqlSyntax Syntax { get; }

		/// <summary>
		/// Creates an SQL command to clone specific columns from a table into a temporary table.
		/// </summary>
		/// <param name="sourceTable">The table to clone.</param>
		/// <param name="tempTable">The temporary to create.</param>
		/// <param name="columns">The columns to copy.</param>
		/// <returns>A command to create a temporary table with specific columns.</returns>
		string SelectIntoTempTable(string sourceTable, string tempTable, IEnumerable<SqlColumn> columns);

		/// <summary>
		/// Creates a name for a temporary table based on an input table name. This currently only affects SQL Server.
		/// </summary>
		/// <param name="tableName">The table name.</param>
		/// <returns>A temporary table name.</returns>
		string CreateTempTableName(string tableName);

		/// <summary>
		/// Quotes an identifier. You can use <see cref="ExtraUtil.IsSqlIdentifier(string)"/> to determine if an identifier needs to be quoted.
		/// </summary>
		/// <param name="identifier">The identifier.</param>
		/// <returns>The quoted identifier.</returns>
		string QuoteIdentifier(string identifier);

		/// <summary>
		/// Creates an SQL command to drop a temporary table if it exists.
		/// <code>"IF OBJECT_ID('tempdb..{0}') IS NOT NULL DROP TABLE {0}"</code>
		/// <code>"DROP TEMPORARY TABLE IF EXISTS {0}"</code>
		/// </summary>
		/// <param name="tableName">The temporary table name.</param>
		/// <returns>A command to drop a table if it exists.</returns>
		string DropTempTableIfExists(string tableName);

		/// <summary>
		/// Creates an SQL command to truncate a table.
		/// <code>"TRUNCATE TABLE {0}"</code>
		/// <code>"DELETE FROM {0}; DELETE FROM SQLITE_SEQUENCE WHERE name='{0}'"</code>
		/// </summary>
		/// <param name="tableName">The table name.</param>
		/// <returns>A command to truncate a table.</returns>
		string TruncateTable(string tableName);

		/// <summary>
		/// Creates an SQL command to select a generated row's identity (auto-increment).
		/// <code>"SELECT CAST(SCOPE_IDENTITY() as INT) as [Id]"</code>
		/// <code>"SELECT LAST_INSERT_ID() as `Id`"</code>
		/// </summary>
		/// <param name="type">The type of the identity.</param>
		/// <returns>A command to select a generated row's identity.</returns>
		string SelectIdentityQuery(Type type);

		/// <summary>
		/// Creates an SQL command to select a limited number of rows.
		/// <code>"TOP({0}) {1}"</code>
		/// <code>"{1} LIMIT {0}"</code>
		/// </summary>
		string LimitQuery { get; }

		/// <summary>
		/// Inserts data into a table using <see cref="SqlBulkCopy"/>.
		/// </summary>
		/// <typeparam name="T">The type of object to insert.</typeparam>
		/// <param name="connection">The database connection.</param>
		/// <param name="objs">The objects to insert into the table.</param>
		/// <param name="transaction">The transaction for the connection, or null if an internal transaction should be used.</param>
		/// <param name="tableName">The name of the table.</param>
		/// <param name="factory">The factory from an <see cref="SqlBuilder{T}"/>.</param>
		/// <param name="columns">The column mappings for the table.</param>
		/// <param name="commandTimeout">The command timeout in seconds. 0 or null prevent a timeout.</param>
		/// <param name="options">The bulk copy options used when transfering data.</param>
		void BulkInsert<T>(IDbConnection connection, IEnumerable<T> objs, IDbTransaction transaction, string tableName, DataReaderFactory factory,
			IEnumerable<SqlColumn> columns, int commandTimeout = 30, SqlBulkCopyOptions options = SqlBulkCopyOptions.Default)
			where T : class;
	}
}
