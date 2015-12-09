using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SchemaZen.model;

namespace SchemaZen.test
{
	[TestFixture]
	public class ScriptConsumerTester
	{
		[Test]
		public void TestForeignKey()
		{
			var fk = new ForeignKey("testing123");
			fk.Columns.AddRange(new[] { "col1", "col2" });
			fk.Table = new Table("dbo", "main");
			fk.RefColumns.AddRange(new[] { "refcol1", "refcol2" });
			fk.RefTable = new Table("dbo", "ref");
			fk.Check = false;
			fk.OnUpdate = "SET NULL";
			fk.OnDelete = "SET Default";

			var script = fk.ScriptCreate();

			var db = new Database();
			db.Tables.Add(fk.Table);
			db.Tables.Add(fk.RefTable);

			var fk2 = ForeignKey.FromScript(script, db);
			var script2 = fk2.ScriptCreate();
			Assert.AreEqual(script, script2);
			Assert.AreEqual(db.FindForeignKey(fk2.Name), fk2);
		}

		[Test]
		[ExpectedException(typeof(ArgumentException))]
		public void TestForeignKeyInvalidValue()
		{
			var fk = new ForeignKey("testing123");
			fk.Columns.AddRange(new[] { "col1", "col2" });
			fk.Table = new Table("dbo", "main");
			fk.RefColumns.AddRange(new[] { "refcol1", "refcol2" });
			fk.RefTable = new Table("dbo", "ref");
			fk.Check = false;
			fk.OnUpdate = "SET NULL";
			fk.OnDelete = "This value is not an allowed value";

			var script = fk.ScriptCreate();
		}
	}
}
