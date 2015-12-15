using System;
using System.Collections.Generic;
using System.Linq;
using SchemaZen.model.ScriptBuilder;

namespace SchemaZen.model
{
	public class Trigger : Routine
	{
		public bool Disabled;
		public string RelatedTableSchema;
		public string RelatedTableName;

		public Trigger(string owner, string name, Database db) : base(owner, name, db)
		{
			RoutineType = RoutineKind.Trigger;
		}

		public static IEnumerable<ScriptPart> GetScriptComponents()
		{
			yield return new OneOfPredefinedValuesPart(VariableName: "State", PotentialValues: new[] { "ENABLE", "DISABLE" });
			foreach (var part in ConstPart.FromString(" TRIGGER "))
				yield return part;
			yield return new IdentifierPart(VariableName: "Owner");
			yield return new ConstPart(".");
			yield return new IdentifierPart(VariableName: "Name");
			foreach (var part in ConstPart.FromString(" ON "))
				yield return part;
			yield return new IdentifierPart(VariableName: "Table.Owner");
			yield return new ConstPart(".");
			yield return new IdentifierPart(VariableName: "Table.Name");
			yield return new WhitespacePart(PreferredChar: '\n');
			yield return new ConstPart("GO");
			yield return new WhitespacePart(PreferredChar: '\n');
		}

		protected override string ScriptBase(string definition)
		{
			var d = new Dictionary<string, object>();
			d["State"] = Disabled ? "DISABLE" : "ENABLE";
			d["Table.Owner"] = RelatedTableSchema;
			d["Table.Name"] = RelatedTableName;
			d["Owner"] = Owner;
			d["Name"] = Name;

			var updateState = Environment.NewLine + "GO" + Environment.NewLine + ScriptPart.ScriptFromComponents(GetScriptComponents(), d);
			return base.ScriptBase(definition + updateState);
		}

		public static new Trigger FromScript(string script, Database db)
		{
			var d = VarsFromScript(script, db);
			if (db.FindRoutine((string)d["Name"], (string)d["Owner"]) != null)
				throw new InvalidOperationException(string.Format("Database model already contains the routine named {0}.{1} that is defined in this script.", d["Name"], d["Owner"]));

			var t = new Trigger((string)d["Owner"], (string)d["Name"], db);
			t.Text = (string)d["Text"];
			t.QuotedId = (string)d["QuotedId"] == "ON";
			t.AnsiNull = (string)d["AnsiNulls"] == "ON";
			t.RoutineType = (RoutineKind)Enum.Parse(typeof(RoutineKind), (string)d["RoutineKind"]);

			if (t.RoutineType != RoutineKind.Trigger)
				throw new InvalidCastException(string.Format("Routine in script is a {0}, not a trigger.", t.RoutineType));

			script = ((IEnumerable<string>)d["Batches"]).FirstOrDefault();
			if (script != null)
			{
				d = ScriptPart.VariablesFromScript(GetScriptComponents(), ((IEnumerable<string>)d["Batches"]).First() + Environment.NewLine + "GO" + Environment.NewLine);
				t.RelatedTableSchema = (string)d["Table.Owner"];
				t.RelatedTableName = (string)d["Table.Name"];
				t.Disabled = (string)d["State"] == "DISABLE";
			}

			db.Routines.Add(t);

			return t;
		}
	}
}
