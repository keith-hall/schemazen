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

		private const string defaultRules = @"|NO ACTION|RESTRICT";
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
			foreach (var part in ConstPart.FromString("ALTER TABLE "))
				yield return part;
			yield return new IdentifierPart(VariableName: "Table.Owner");
			yield return new ConstPart(Text: ".");
			yield return new IdentifierPart(VariableName: "Table.Name");
			foreach (var part in ConstPart.FromString(" WITH "))
				yield return part;
			yield return new OneOfPredefinedValuesPart(VariableName: "Check", PotentialValues: new[] { "CHECK", "NOCHECK" });
			foreach (var part in ConstPart.FromString(" ADD CONSTRAINT "))
				yield return part;
			yield return new IdentifierPart(VariableName: "Name");
			foreach (var part in ConstPart.FromString(Environment.NewLine + "   FOREIGN KEY ("))
				yield return part;
			yield return new MultipleSeparatedIdentifiersPart(VariableName: "Columns", Separator: ",");
			foreach (var part in ConstPart.FromString(") REFERENCES "))
				yield return part;
			yield return new IdentifierPart(VariableName: "RefTable.Owner");
			yield return new ConstPart(Text: ".");
			yield return new IdentifierPart(VariableName: "RefTable.Name");
			foreach (var part in ConstPart.FromString(" ("))
				yield return part;
			yield return new MultipleSeparatedIdentifiersPart(VariableName: "RefColumns", Separator: ",");
			foreach (var part in ConstPart.FromString(")" + Environment.NewLine))
				yield return part;
			
			yield return new AnyOrderPart
			(
				Contents: new[] {
					new MaybePart(Variable: "OnUpdate", SkipGenerationIfVariableValueMatchesAny: defaultRules.Split('|'), Contents: ConstPart.FromString("   ON UPDATE ").Concat(new ScriptPart[] { new OneOfPredefinedValuesPart(VariableName: "OnUpdate", PotentialValues: possibleRules.Split('|') ), new WhitespacePart(PreferredChar: '\n') })),
					new MaybePart(Variable: "OnDelete", SkipGenerationIfVariableValueMatchesAny: defaultRules.Split('|'), Contents: ConstPart.FromString("   ON DELETE ").Concat(new ScriptPart[] { new OneOfPredefinedValuesPart(VariableName: "OnDelete", PotentialValues: possibleRules.Split('|') ), new WhitespacePart(PreferredChar: '\n') }))
				}
			);
			yield return new MaybePart
			(
				Variable: "Check",
				SkipGenerationIfVariableValueMatchesAny: new[] { "CHECK" },
				Contents: ConstPart.FromString("   ALTER TABLE ").Concat(new ScriptPart[] { new IdentifierPart(VariableName: "Table.Owner"), new ConstPart(Text: "."), new IdentifierPart(VariableName: "Table.Name"), new WhitespacePart() }).Concat(new ScriptPart[] { new OneOfPredefinedValuesPart(VariableName: "Check", PotentialValues: new string[] { "NOCHECK" }) }).Concat(ConstPart.FromString(" CONSTRAINT ")).Concat(new ScriptPart[] { new IdentifierPart(VariableName: "Name"), new WhitespacePart(PreferredChar: '\n') })
			);

		}

		public static ForeignKey FromScript(string script, Database db)
		{
			var d = ScriptPart.VariablesFromScript(GetScriptComponents(), script);
			if (db.FindForeignKey((string)d["Name"]) != null)
				throw new InvalidOperationException(string.Format("Database model already contains the foreign key named {0} that is defined in this script.", (string)d["Name"]));
			var fk = new ForeignKey((string)d["Name"]);
			fk.Table = db.FindTable((string)d["Table.Name"], (string)d["Table.Owner"]);
			fk.Columns = (List<string>)d["Columns"];
			fk.RefTable = db.FindTable((string)d["RefTable.Name"], (string)d["RefTable.Owner"]);
			fk.RefColumns = (List<string>)d["RefColumns"];
			fk.Check = ((string)d["Check"]).Equals("CHECK", StringComparison.InvariantCultureIgnoreCase);
			if (d.ContainsKey("OnUpdate"))
				fk.OnUpdate = (string)d["OnUpdate"];
			if (d.ContainsKey("OnDelete"))
				fk.OnDelete = (string)d["OnDelete"];

			if (fk.Table == null || fk.RefTable == null)
				throw new InvalidOperationException("One or more of the tables referenced by this foreign key do not exist in the database model.");

			db.ForeignKeys.Add(fk);

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
