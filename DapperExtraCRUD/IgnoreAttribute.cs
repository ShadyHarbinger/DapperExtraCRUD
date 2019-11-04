﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dapper
{
	/// <summary>
	/// Ignores the <see cref="PropertyInfo"/>.
	/// </summary>
	[AttributeUsage(AttributeTargets.Property)]
	public class IgnoreAttribute : Attribute
	{
	}
}
