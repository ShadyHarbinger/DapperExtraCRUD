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

namespace Dapper.Extra.Internal.Adapters
{
	/// <summary>
	/// An <see cref="SqlAdapter"/> that generates SQL commands for Oracle.
	/// </summary>
	internal class OracleAdapter : SqlAdapterImpl
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OracleAdapter"/> class.
        /// </summary>
        public OracleAdapter() : base(SqlDialect.Oracle)
        {
			QuoteLeft = "'";
			QuoteRight = "'";
			EscapeQuoteRight = "''";
			SelectIntIdentityQuery = ""; // SEQUENCE?
			DropTableIfExistsQuery = @"
BEGIN
	EXECUTE IMMEDIATE 'DROP TABLE {0}';
	EXCEPTION
	WHEN OTHERS THEN NULL;
END;";
			TruncateTableQuery = "TRUNCATE TABLE {0};";
			TempTableName = "{0}";
			CreateTempTable = "";
			LimitQuery = @"TOP({0})
{1}";
        }
    }
}
