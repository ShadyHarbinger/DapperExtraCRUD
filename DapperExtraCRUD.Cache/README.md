﻿# Description:

A cache framework for Dapper.ExtraCRUD. This combines the functionality of DataAccessObjects/AutoAccessObject and a ConcurrentDictionary with support for transactions and rollbacks.

# Example:

```csharp
using Dapper.Extra;
using Dapper.Extra.Cache

public class Person
{
	[Key]
	public int Id { get; set; }
	public string FirstName { get; set; }
	public string LastName { get; set; }
	public DateTime DateOfBirth { get; set; }
}

public class PersonItem : CacheItem<Person>
{
	public int Id => CacheValue.Id;
	public Name => CacheValue.FirstName + " " + CacheValue.LastName;
	public DateTime DOB => CacheValue.DateOfBirth;
	public double Age => (DateTime.Today - CacheValue.DateOfBirth).TotalDays / 365.0;
}

public class Employee
{
	[Key]
	[Required]
	public int Id { get; set; }
	public DateTime HireDate { get; set; }
}

public static class Program
{
	private const string ConnString = "Server=myServerAddress;Database=myDataBase;User Id=myUsername;Password=myPassword;";
	public static void Main()
	{
		DbCache cache = new DbCache(ConnString);
		DbCacheTable<Person, PersonItem> personCache = cache.CreateTable<Person, PersonItem>();
		DbCacheTable<Person> employeeCache = cache.CreateTable<Employee>();
		personCache.GetList(); // get all rows in the database

		Person person = new Person() {
			Name = "Joe Smith",
			DateOfBirth = DateTime.Today.AddDays(-365 * 20);
		};

		using (IDbTransaction transaction = personCache.BeginTransaction()) {
			transaction.Add(employeeCache); // employee table is now part of the transaction
			PersonItem personItem = personCache.Insert(person); // automatically uses the transaction
			Employee employee = new Employee() {
				Id = personItem.Id,
				HireDate = DateTime.Now,
			};
			CacheItem<Employee> employeeItem = employeeCache.Insert(employee);
		}
	}
}

```