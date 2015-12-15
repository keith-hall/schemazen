using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SchemaZen.model.ScriptBuilder;

namespace SchemaZen.model {
	public class Routine : INameable, IHasOwner {
		public enum RoutineKind {
			Procedure,
			Function,
			Trigger,
			View,
			XmlSchemaCollection
		}

		public bool AnsiNull;
		public string Name { get; set; }
		public bool QuotedId;
		public RoutineKind RoutineType;
		public string Owner { get; set; }
		public string Text;
		public Database Db;

		private const string SqlCreateRegex =
			@"\A" + Database.SqlWhitespaceOrCommentRegex + @"*?(CREATE)" + Database.SqlWhitespaceOrCommentRegex;

		private const string SqlCreateWithNameRegex =
			SqlCreateRegex + @"+{0}" + Database.SqlWhitespaceOrCommentRegex + @"+?(?:(?:(" + Database.SqlEnclosedIdentifierRegex +
			@"|" + Database.SqlRegularIdentifierRegex + @")\.)?(" + Database.SqlEnclosedIdentifierRegex + @"|" +
			Database.SqlRegularIdentifierRegex + @"))(?:\(|" + Database.SqlWhitespaceOrCommentRegex + @")";

		public Routine(string owner, string name, Database db) {
			Owner = owner;
			Name = name;
			Db = db;

			if (db != null)
			{
				var prop = db.FindProp("QUOTED_IDENTIFIER");
				if (prop != null)
					QuotedId = prop.Value == "ON";

				prop = db.FindProp("ANSI_NULLS");
				if (prop != null)
					AnsiNull = prop.Value == "ON";
			}
		}
		
		protected virtual string ScriptBase(string definition)
		{
			var dbQuotedId = string.Empty;
			var dbAnsiNulls = string.Empty;
			if (Db != null)
			{
				var prop = Db.FindProp("QUOTED_IDENTIFIER");
				if (prop != null)
					dbQuotedId = prop.Value;

				prop = Db.FindProp("ANSI_NULLS");
				if (prop != null)
					dbAnsiNulls = prop.Value;
			}

			var props = GetScriptPropComponents(dbQuotedId, dbAnsiNulls);
			var d = new Dictionary<string, object>();
			d["QuotedId"] = QuotedId ? "ON" : "OFF";
			d["AnsiNulls"] = AnsiNull ? "ON" : "OFF";

			var before = ScriptPart.ScriptFromComponents(props, d);

			props = GetScriptPropComponents((string)d["QuotedId"], (string)d["AnsiNulls"]);
			d["QuotedId"] = dbQuotedId;
			d["AnsiNulls"] = dbAnsiNulls;
			var after = ScriptPart.ScriptFromComponents(props, d);

			if (after != string.Empty)
			{
				after = Environment.NewLine + "GO" + Environment.NewLine + after;
			}
			
			if (string.IsNullOrEmpty(definition))
				definition = string.Format("/* missing definition for {0} [{1}].[{2}] */", RoutineType, Owner, Name);

			return before + definition + after;
		}

		public string ScriptCreate() {
			return ScriptBase(Text);
		}

		public static IEnumerable<ScriptPart> GetScriptPropComponents(string QuotedId, string AnsiNulls)
		{
			yield return new AnyOrderPart(Contents: new[] { new MaybePart(Variable: "QuotedId", SkipGenerationIfVariableValueMatchesAny: new[] { QuotedId, string.Empty }, Contents: new ScriptPart[] { new ConstPart("SET"), new WhitespacePart(), new ConstPart("QUOTED_IDENTIFIER"), new WhitespacePart(), new OneOfPredefinedValuesPart(VariableName: "QuotedId", PotentialValues: new[] { "ON", "OFF" }), new WhitespacePart(PreferredChar: '\n'), new ConstPart(Text: "GO"), new WhitespacePart(PreferredChar: '\n') }), new MaybePart(Variable: "AnsiNulls", SkipGenerationIfVariableValueMatchesAny: new[] { AnsiNulls, string.Empty }, Contents: new ScriptPart[] { new ConstPart("SET"), new WhitespacePart(), new ConstPart("ANSI_NULLS"), new WhitespacePart(), new OneOfPredefinedValuesPart(VariableName: "AnsiNulls", PotentialValues: new[] { "ON", "OFF" }), new WhitespacePart(PreferredChar: '\n'), new ConstPart(Text: "GO"), new WhitespacePart(PreferredChar: '\n') }) });
		}

		internal static Dictionary<string, object> VarsFromScript(string script, Database db)
		{
			var d = new Dictionary<string, object>();
			var batches = helpers.BatchSqlParser.SplitBatch(script).ToList();
			var dbQuotedId = string.Empty;
			var dbAnsiNulls = string.Empty;

			if (db != null)
			{
				var prop = db.FindProp("QUOTED_IDENTIFIER");
				if (prop != null)
					dbQuotedId = prop.Value;

				prop = db.FindProp("ANSI_NULLS");
				if (prop != null)
					dbAnsiNulls = prop.Value;
			}

			if (batches.Count > 1)
			{
				var props = GetScriptPropComponents(dbQuotedId, dbAnsiNulls);
				foreach (var batch in batches.ToArray().Take(2))
				{
					var result = ScriptPart.VariablesFromScript(props, batch + Environment.NewLine + "GO" + Environment.NewLine, (name, value) => ScriptPart.SetVariableIfNotDifferent(d, name, value));
					if (string.IsNullOrEmpty(result.Value))
						batches.Remove(batch);
				}
			}
			
			var components = new ScriptPart[] { new ConstPart(Text: "CREATE"), new WhitespacePart(), new OneOfPredefinedValuesPart(VariableName: "RoutineKind", PotentialValues: new[] { "PROCEDURE", "PROC", "FUNCTION", "TRIGGER", "VIEW", "XML SCHEMA COLLECTION" }), new IdentifierPart(VariableName: "Owner"), new ConstPart(Text: "."), new IdentifierPart(VariableName: "Name") };
			ScriptPart.VariablesFromScript(components, batches.First(), (name, value) => ScriptPart.SetVariableIfNotDifferent(d, name, value));
			d["Text"] = batches.First();
			d["Batches"] = batches.Skip(1).ToList();
			if (!d.ContainsKey("QuotedId"))
				d["QuotedId"] = dbQuotedId;
			if (!d.ContainsKey("AnsiNulls"))
				d["AnsiNulls"] = dbAnsiNulls;
			
			var firstWord = ((string)d["RoutineKind"]).Split(new[] { ' ' }).First();
			foreach (var e in Enum.GetNames(typeof(RoutineKind)))
				if (e.StartsWith(firstWord, StringComparison.InvariantCultureIgnoreCase))
					d["RoutineKind"] = e;
			return d;
		}

		public static Routine FromScript(string script, Database db)
		{
			var d = VarsFromScript(script, db);
			
			if (db.FindRoutine((string)d["Name"], (string)d["Owner"]) != null)
				throw new InvalidOperationException(string.Format("Database model already contains the routine named {0}.{1} that is defined in this script.", d["Name"], d["Owner"]));

			var r = new Routine((string)d["Owner"], (string)d["Name"], db);
			r.Text = (string)d["Text"];
			r.QuotedId = (string)d["QuotedId"] == "ON";
			r.AnsiNull = (string)d["AnsiNulls"] == "ON";
			r.RoutineType = (RoutineKind)Enum.Parse(typeof(RoutineKind), (string)d["RoutineKind"]);

			db.Routines.Add(r);

			return r;
		}
		
		public string GetSQLTypeForRegEx() {
			var text = GetSQLType();
			if (RoutineType == RoutineKind.Procedure) // support shorthand - PROC
				return "(?:" + text + "|" + text.Substring(0, 4) + ")";
			return text;
		}

		public string GetSQLType() {
			var text = RoutineType.ToString();
			return string.Join(string.Empty, text.AsEnumerable().Select(
				(c, i) => ((char.IsUpper(c) || i == 0) ? " " + char.ToUpper(c).ToString() : c.ToString())
				).ToArray()).Trim();
		}

		public string ScriptDrop() {
			return string.Format("DROP {0} [{1}].[{2}]", GetSQLType(), Owner, Name);
		}


		public string ScriptAlter() {
			if (RoutineType != RoutineKind.XmlSchemaCollection) {
				var regex = new Regex(SqlCreateRegex, RegexOptions.IgnoreCase);
				var match = regex.Match(Text);
				var group = match.Groups[1];
				if (group.Success) {
					return ScriptBase(Text.Substring(0, group.Index) + "ALTER" + Text.Substring(group.Index + group.Length));
				}
			}
			throw new Exception(string.Format("Unable to script routine {0} {1}.{2} as ALTER", RoutineType, Owner, Name));
		}

		public IEnumerable<string> Warnings() {
			if (string.IsNullOrEmpty(Text)) {
				yield return "Script definition could not be retrieved.";
			} else {
				// check if the name is correct
				var regex = new Regex(string.Format(SqlCreateWithNameRegex, GetSQLTypeForRegEx()),
					RegexOptions.IgnoreCase | RegexOptions.Singleline);
				var match = regex.Match(Text);

				// the schema is captured in group index 2, and the name in 3

				var nameGroup = match.Groups[3];
				if (nameGroup.Success) {
					var name = nameGroup.Value;
					if (name.StartsWith("[") && name.EndsWith("]"))
						name = name.Substring(1, name.Length - 2);

					if (string.Compare(Name, name, StringComparison.InvariantCultureIgnoreCase) != 0) {
						yield return string.Format("Name from script definition '{0}' does not match expected name '{1}'", name, Name);
					}
				}
			}
		}
	}
}
