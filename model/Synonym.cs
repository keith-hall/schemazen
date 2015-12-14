using System;
using System.Collections.Generic;
using System.Linq;
using SchemaZen.model.ScriptBuilder;

namespace SchemaZen.model {
	public class Synonym : INameable, IHasOwner {
		public string Name { get; set; }
		public string Owner { get; set; }
		public string BaseObjectName;

		public Synonym(string name, string owner) {
			Name = name;
			Owner = owner;
		}

		public static IEnumerable<ScriptPart> GetScriptComponents()
		{
			foreach (var part in ConstPart.FromString("CREATE SYNONYM "))
				yield return part;
			yield return new IdentifierPart(VariableName: "Owner");
			yield return new ConstPart(Text: ".");
			yield return new IdentifierPart(VariableName: "Name");
			foreach (var part in ConstPart.FromString(" FOR "))
				yield return part;
			yield return new MultipleSeparatedIdentifiersPart(VariableName: "BaseObjectName", Separator: ".");
		}

		public static Synonym FromScript(string script, Database db)
		{
			var d = ScriptPart.VariablesFromScript(GetScriptComponents(), script);
			if (db.FindSynonym((string)d["Name"], (string)d["Owner"]) != null)
				throw new InvalidOperationException(string.Format("Database model already contains the synonym named {0}.{1} that is defined in this script.", (string)d["Owner"], (string)d["Name"]));
			var s = new Synonym((string)d["Name"], (string)d["Owner"]);
			s.BaseObjectName = string.Join(".", ((List<string>)d["BaseObjectName"]).Select(p => "[" + p + "]").ToArray());
			
			db.Synonyms.Add(s);

			return s;
		}

		public string ScriptCreate()
		{
			var d = new Dictionary<string, object>();
			d["Owner"] = Owner;
			d["Name"] = Name;
			new MultipleSeparatedIdentifiersPart(VariableName: "BaseObjectName", Separator: ".").ConsumeScript((s, o) => d["BaseObjectName"] = o, BaseObjectName);
			return ScriptPart.ScriptFromComponents(GetScriptComponents(), d);
		}

		public string ScriptDrop()
		{
			return string.Format("DROP SYNONYM [{0}].[{1}]", Owner, Name);
		}
	}
}
