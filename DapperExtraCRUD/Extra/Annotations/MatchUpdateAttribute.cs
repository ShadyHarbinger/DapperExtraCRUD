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
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using Dapper.Extra.Internal;

namespace Dapper.Extra.Annotations
{
	/// <summary>
	/// Turns the <see cref="PropertyInfo"/> into a pseudo key for updates.
	/// </summary>
	[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
	public class MatchUpdateAttribute : Attribute, IDefaultAttribute
	{
		/// <summary>
		/// Turns the <see cref="PropertyInfo"/> into a pseudo key for updates and sets the value to the string input if specified.
		/// </summary>
		public MatchUpdateAttribute()
		{
		}

		/// <summary>
		/// Turns the <see cref="PropertyInfo"/> into a pseudo key for updates and sets the value to the string input if specified.
		/// </summary>
		/// <param name="value">A string that is injected into the update statement as the column's value.
		/// If this is <see langword="null"/> then the column is not modified.</param>
		/// <param name="autoSync">Determines if the property should be selected to match the database after an update.</param>
		public MatchUpdateAttribute(string value, bool autoSync = false)
		{
			AutoSync = autoSync;
			if (!string.IsNullOrWhiteSpace(value)) {
				Value = "(" + value.Trim() + ")";
			}
		}
		/// <summary>
		/// A string that is injected into the update statement as the column's new value for updates.
		/// If this is <see langword="null"/> then the column's value will not change on updates.
		/// </summary>
		public string Value { get; }
		/// <summary>
		/// Checks if the value is null.
		/// </summary>
		public bool HasValue => Value != null;
		/// <summary>
		/// Determines if this column will be automatically selected after an update.
		/// </summary>
		public bool AutoSync { get; }
	}
}