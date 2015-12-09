using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SchemaZen.model.ScriptBuilder;

namespace SchemaZen.model {
	public class ForeignKey : INameable {
		public bool Check;
		public List<string> Columns = new List<string>();
		public string Name { get; set; }
		public string OnDelete;
		public string OnUpdate;
		public List<string> RefColumns = new List<string>();
		public Table RefTable;
		public Table Table;

		private const string defaultRules = @"\A\z|NO ACTION|RESTRICT";
		private const string possibleRules = @"NO ACTION|RESTRICT|CASCADE|SET NULL|SET DEFAULT";

		public ForeignKey(string name) {
			Name = name;
		}

		public ForeignKey(Table table, string name, string columns, Table refTable, string refColumns)
			: this(table, name, columns, refTable, refColumns, "", "") { }

		public ForeignKey(Table table, string name, string columns, Table refTable, string refColumns, string onUpdate,
			string onDelete) {
			Table = table;
			Name = name;
			Columns = new List<string>(columns.Split(','));
			RefTable = refTable;
			RefColumns = new List<string>(refColumns.Split(','));
			OnUpdate = onUpdate;
			OnDelete = onDelete;
		}

		public string CheckText {
			get { return Check ? "CHECK" : "NOCHECK"; }
		}

		private void AssertArgNotNull(object arg, string argName) {
			if (arg == null) {
				throw new ArgumentNullException(string.Format(
					"Unable to Script FK {0}. {1} must not be null.", Name, argName));
			}
		}

		public static IEnumerable<ScriptPart> GetScriptComponents()
		{
			yield return new ConstPart { Text = "ALTER" };
			yield return new WhitespacePart();
			yield return new ConstPart { Text = "TABLE" };
			yield return new WhitespacePart();
			yield return new ConstPart { Text = "[" };
			yield return new VariablePart { Name = "Table.Owner" };
			yield return new ConstPart { Text = "].[" };
			yield return new VariablePart { Name = "Table.Name" };
			yield return new ConstPart { Text = "]" };
			yield return new WhitespacePart();
			yield return new ConstPart { Text = "WITH" };
			yield return new WhitespacePart();
			yield return new VariablePart { Name = "Check", PotentialValues = new[] { "CHECK", "NOCHECK" } };
			yield return new WhitespacePart();
			yield return new ConstPart { Text = "ADD" };
			yield return new WhitespacePart();
			yield return new ConstPart { Text = "CONSTRAINT" };
			yield return new WhitespacePart();
			yield return new ConstPart { Text = "[" };
			yield return new VariablePart { Name = "Name" };
			yield return new ConstPart { Text = "]" };
			yield return new WhitespacePart { NewLinePreferred = true };
			yield return new WhitespacePart { PreferredCount = 3 };
			yield return new ConstPart { Text = "FOREIGN KEY" };
			yield return new WhitespacePart();
			yield return new ConstPart { Text = "(" };
			yield return new MultipleOccurancesPart { Variable = new VariablePart { Name = "Columns" }, Prefix = new[] { new ConstPart { Text = "[" } }, Suffix = new[] { new ConstPart { Text = "]" } }, Separator = new ScriptPart[] { new ConstPart { Text = "," }, new WhitespacePart() } };
			yield return new ConstPart { Text = ")" };
			yield return new WhitespacePart();
			yield return new ConstPart { Text = "REFERENCES" };
			yield return new WhitespacePart();
			yield return new ConstPart { Text = "[" };
			yield return new VariablePart { Name = "RefTable.Owner" };
			yield return new ConstPart { Text = "].[" };
			yield return new VariablePart { Name = "RefTable.Name" };
			yield return new ConstPart { Text = "]" };
			yield return new WhitespacePart();
			yield return new ConstPart { Text = "(" };
			yield return new MultipleOccurancesPart { Variable = new VariablePart { Name = "RefColumns" }, Prefix = new[] { new ConstPart { Text = "[" } }, Suffix = new[] { new ConstPart { Text = "]" } }, Separator = new ScriptPart[] { new ConstPart { Text = "," }, new WhitespacePart() } };
			yield return new ConstPart { Text = ")" };
			yield return new WhitespacePart { NewLinePreferred = true };

			yield return new AnyOrderPart
			{
				Contents = new[] {
					new MaybePart { Variable = "OnUpdate", SkipIfRegexMatch = defaultRules, Contents = new ScriptPart[] { new WhitespacePart { PreferredCount = 3 }, new ConstPart { Text = "ON" }, new WhitespacePart(), new ConstPart { Text = "UPDATE" }, new WhitespacePart(), new VariablePart { Name = "OnUpdate", PotentialValues = possibleRules.Split('|') }, new WhitespacePart { NewLinePreferred = true } } },
					new MaybePart { Variable = "OnDelete", SkipIfRegexMatch = defaultRules, Contents = new ScriptPart[] { new WhitespacePart { PreferredCount = 3 }, new ConstPart { Text = "ON" }, new WhitespacePart(), new ConstPart { Text = "DELETE" }, new WhitespacePart(), new VariablePart { Name = "OnDelete", PotentialValues = possibleRules.Split('|') }, new WhitespacePart { NewLinePreferred = true } } }
				}
			};
			yield return new MaybePart
			{
				Variable = "Check",
				SkipIfRegexMatch = @"\ACHECK\Z",
				Contents = new ScriptPart[] { new WhitespacePart { PreferredCount = 3 }, new ConstPart { Text = "ALTER" }, new WhitespacePart(), new ConstPart { Text = "TABLE" }, new WhitespacePart(), new ConstPart { Text = "[" }, new VariablePart { Name = "Table.Owner" }, new ConstPart { Text = "].[" }, new VariablePart { Name = "Table.Name" }, new ConstPart { Text = "]" }, new WhitespacePart(), new VariablePart { Name = "Check"/*, PotentialValues = new string[] { "NOCHECK" }*/ }, new WhitespacePart(), new ConstPart { Text = "CONSTRAINT" }, new WhitespacePart(), new ConstPart { Text = "[" }, new VariablePart { Name = "Name" }, new ConstPart { Text = "]" }, new WhitespacePart { NewLinePreferred = true } }
			};

		}

		public static ForeignKey FromScript(string script)
		{
			var d = ScriptPart.VariablesFromScript(GetScriptComponents(), script);
			var fk = new ForeignKey((string)d["Name"]);
			fk.Table = new Table((string)d["Table.Owner"], (string)d["Table.Name"]); // TODO: rather than creating a new table, it should look it up from the database (meaning order in which scripts are parsed is important)
			fk.Columns = (List<string>)d["Columns"];
			fk.RefTable = new Table((string)d["RefTable.Owner"], (string)d["RefTable.Name"]); // TODO: same here
			fk.RefColumns = (List<string>)d["RefColumns"];
			fk.Check = ((string)d["Check"]).Equals("CHECK", StringComparison.InvariantCultureIgnoreCase);
			if (d.ContainsKey("OnUpdate"))
				fk.OnUpdate = (string)d["OnUpdate"];
			if (d.ContainsKey("OnDelete"))
				fk.OnDelete = (string)d["OnDelete"];
			
			return fk;
		}

		public string ScriptCreate ()
		{
			AssertArgNotNull(Table, "Table");
			AssertArgNotNull(Columns, "Columns");
			AssertArgNotNull(RefTable, "RefTable");
			AssertArgNotNull(RefColumns, "RefColumns");

			var d = new Dictionary<string, object>();
			d["Name"] = Name;
			d["Table.Owner"] = Table.Owner;
			d["Table.Name"] = Table.Name;
			d["Columns"] = Columns;
			d["RefTable.Owner"] = RefTable.Owner;
			d["RefTable.Name"] = RefTable.Name;
			d["RefColumns"] = RefColumns;
			d["Check"] = CheckText;
			d["OnUpdate"] = OnUpdate ?? string.Empty;
			d["OnDelete"] = OnDelete ?? string.Empty;
			return ScriptPart.ScriptFromComponents(GetScriptComponents(), d);
		}

		public string ScriptDrop() {
			return string.Format("ALTER TABLE [{0}].[{1}] DROP CONSTRAINT [{2}]\r\n", Table.Owner, Table.Name, Name);
		}
	}
}
