using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SchemaZen.model;
using SchemaZen.model.ScriptBuilder;

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

		[Test]
		public void TestIdentifierConsistentValue()
		{
			var components = new ScriptPart[] { new IdentifierPart(VariableName: "Table.Owner"), new ConstPart(Text: "."), new IdentifierPart(VariableName: "Table.Name") };

			var d = ScriptPart.VariablesFromScript(components.Concat(components), "[dbo].[name] dbo.[name]");
			Assert.AreEqual(d["Table.Owner"], "dbo");
			Assert.AreEqual(d["Table.Name"], "name");

			components = new ScriptPart[] { new ConstPart("SELECT"), new WhitespacePart(), new CommaSeparatedIdentifiersPart(VariableName: "test") };
			d = ScriptPart.VariablesFromScript(components.Concat(components), "SELECT hello,a1,b2,c3 select hello, a1,[b2], c3 ");
		}

		[Test]
		[ExpectedException(typeof(FormatException))]
		public void TestIdentifierInconsistentValue()
		{
			var components = new ScriptPart[] { new IdentifierPart(VariableName: "Table.Owner"), new ConstPart(Text: "."), new IdentifierPart(VariableName: "Table.Name") };

			var d = ScriptPart.VariablesFromScript(components.Concat(components), "[owner].[name][owner2].[name]");
		}

		[Test]
		[ExpectedException(typeof(FormatException))]
		public void TestCommaSeparatedIdentifierInconsistentValue()
		{
			var components = new ScriptPart[] { new ConstPart("SELECT"), new WhitespacePart(), new CommaSeparatedIdentifiersPart(VariableName: "test") };
			var d = ScriptPart.VariablesFromScript(components.Concat(components), "SELECT hello,a1,b2,c3 select hello, world ");
		}

		[Test]
		public void TestCommentsInWhitespace()
		{
			var components = new ScriptPart[] { new IdentifierPart(VariableName: "Identifier") };
			var d = ScriptPart.VariablesFromScript(components, "[name] --test"); // test trailing whitespace and comments
			Assert.AreEqual(d["Identifier"], "name");

			components = new ScriptPart[] { new IdentifierPart(VariableName: "schema"), new ConstPart("."), new IdentifierPart(VariableName: "identifier") };
			d = ScriptPart.VariablesFromScript(components, "[dbo] /*test*/ . [name]"); // test optional whitespace and comments in whitespace
			Assert.AreEqual(d["identifier"], "name");
		}

		[Test]
		[ExpectedException(typeof(FormatException))]
		public void TestTrailingText()
		{
			var components = new ScriptPart[] { new IdentifierPart(VariableName: "Identifier") };
			var d = ScriptPart.VariablesFromScript(components, "[name] select 1");
		}

		[Test]
		public void TestAnyOrder()
		{
			var components = new AnyOrderPart(Contents: new ScriptPart[] { new ConstPart("1"), new WhitespacePart(), new ConstPart("SELECT") });
			var d = ScriptPart.VariablesFromScript(new[] { components }, "select 1");
			Assert.AreEqual(d.Keys.Count, 0);
		}
	}
}
