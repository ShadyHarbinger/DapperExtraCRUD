﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dapper
{
	[AttributeUsage(AttributeTargets.Property)]
	public class MatchDeleteAttribute : Attribute
	{
	}
}