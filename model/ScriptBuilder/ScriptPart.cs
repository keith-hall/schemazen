using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SchemaZen.model.ScriptBuilder
{
	public abstract class ScriptPart {
		public abstract string GenerateScript(Dictionary<string, object> variables);
		public abstract string ConsumeScript(Dictionary<string, object> variables, string script, ScriptPart next);

		public static string ScriptFromComponents(IEnumerable<ScriptPart> components, Dictionary<string, object> variables)
		{
			var sb = new StringBuilder();
			foreach (var component in components)
			{
				sb.Append(component.GenerateScript(variables));
			}
			return sb.ToString();
		}

		public static Dictionary<string, object> VariablesFromScript(IEnumerable<ScriptPart> components, string script)
		{
			var variables = new Dictionary<string, object>();
			ScriptPart current = null;
			Action<ScriptPart> process = next =>
			{
				var remaining = script;
				if (current != null)
				{
					//if (current is WhitespacePart && next is WhitespacePart)
					//{ // ignore multiple consecutive whitespace parts
					//} else
						script = current.ConsumeScript(variables, script, next);
				}
				if (script == null)
					throw new ArgumentOutOfRangeException("script", remaining, "Script does not match component.");
				current = next;
			};

			foreach (var component in components)
				process(component);
			process(null);
			return variables;
		}
	}

	public class ConstPart : ScriptPart
	{
		public string Text;

		public override string ConsumeScript(Dictionary<string, object> variables, string script, ScriptPart next)
		{
			if (script.StartsWith(Text, StringComparison.InvariantCultureIgnoreCase))
			{
				return script.Substring(Text.Length);
			} else
			{
				return null;
			}
		}

		public override string GenerateScript(Dictionary<string, object> variables)
		{
			return Text;
		}
	}

	public class WhitespacePart : ScriptPart
	{
		public bool NewLinePreferred;
		public int PreferredCount = 1;
		private static Regex ws = new Regex(@"\A\s+");

		public override string ConsumeScript(Dictionary<string, object> variables, string script, ScriptPart next)
		{
			var match = ws.Match(script);
			if (match.Success)
			{
				return script.Substring(match.Length);
			} else
			{
				//return null;
				return script; // make whitespace optional
			}
		}

		public override string GenerateScript(Dictionary<string, object> variables)
		{
			var wsChar = NewLinePreferred ? Environment.NewLine : " ";
			return string.Join(string.Empty, Enumerable.Repeat(wsChar, PreferredCount).ToArray());
		}
	}

	public class VariablePart : ScriptPart
	{
		public string Name;
		public string[] PotentialValues;

		private static Regex ws = new Regex(@"\s+");

		public override string ConsumeScript(Dictionary<string, object> variables, string script, ScriptPart next)
		{
			Func<string, string> setValue = value =>
			{
				if (variables.ContainsKey(Name))
				{
					if (!((string)variables[Name]).Equals(value, StringComparison.InvariantCultureIgnoreCase))
						throw new FormatException("Variable '" + Name + "' has multiple values in the script.");
				}
				else
				{
					variables[Name] = value;
				}
				return script.Substring(value.Length);
			};

			if (PotentialValues != null && PotentialValues.Any())
			{
				foreach (var value in PotentialValues)
				{
					if (script.StartsWith(value, StringComparison.InvariantCultureIgnoreCase))
					{
						return setValue(value);
					}
				}
				return null;
			} else
			{
				var index = FindNextPart(next, script);
				if (index > -1)
				{
					return setValue(script.Substring(0, index));
				}
				else
				{
					throw new NotImplementedException("Unsupported script part after freely-valued variable.");
				}
			}
		}

		protected int FindNextPart(ScriptPart next, string script)
		{
			if (next is ConstPart)
			{
				return script.IndexOf(((ConstPart)next).Text, StringComparison.InvariantCultureIgnoreCase);
			}
			else if (next is WhitespacePart)
			{
				var match = ws.Match(script);
				return match.Index;
			}
			else
			{
				return -1;
			}
		}

		public override string GenerateScript(Dictionary<string, object> variables)
		{
			if (!variables.ContainsKey(Name))
				throw new ArgumentOutOfRangeException("Variable '" + Name + "' does not exist.");
			var value = variables[Name];
			if (!(value is string))
				throw new FormatException("Variable '" + Name + "' is not a string.");
			if (PotentialValues != null && PotentialValues.Any() && !PotentialValues.Contains((string)value, StringComparer.InvariantCultureIgnoreCase))
				throw new FormatException("Variable '" + Name + "' does not match any of the expected values. Found value: '" + (string)value + "'");
			
			return (string)value;
		}
	}

	public class MultipleOccurancesPart : VariablePart
	{
		public ScriptPart[] Separator;
		public ScriptPart[] Prefix;
		public ScriptPart[] Suffix;

		public override string ConsumeScript(Dictionary<string, object> variables, string script, ScriptPart next)
		{
			var values = new List<string>();
			var remaining = script;

			Action<ScriptPart[]> process = components =>
			{
				foreach (var component in components)
				{
					remaining = component.ConsumeScript(null, remaining, null); // we don't support complex actions here like variables, and next part
					if (remaining == null)
						break;
				}
			};

			while (remaining != null)
			{
				process(Prefix);
				if (remaining == null)
					break;
				var index = FindNextPart(Suffix.FirstOrDefault() ?? Separator.FirstOrDefault() ?? next, remaining);
				if (index == -1)
					break;
				var value = remaining.Substring(0, index);
				remaining = remaining.Substring(index);
				process(Suffix);
				if (remaining == null)
					break;
				script = remaining;
				values.Add(value);
				process(Separator);
			}
			variables[Name] = values;
			return script;
		}

		public override string GenerateScript(Dictionary<string, object> variables)
		{
			if (!variables.ContainsKey(Name))
				throw new ArgumentOutOfRangeException("Variable '" + Name + "' does not exist.");
			if (!(variables[Name] is IEnumerable<string>))
				throw new FormatException("Variable '" + Name + "' is not a string enumerable.");

			var sb = new StringBuilder();
			var values = (IEnumerable<string>)variables[Name];
			var first = true;
			foreach (var value in values)
			{
				if (!first)
				{
					foreach (var part in Separator)
						sb.Append(part.GenerateScript(variables));
				}
				else
				{
					first = false;
				}
				foreach (var part in Prefix)
					sb.Append(part.GenerateScript(variables));
				sb.Append(value);
				foreach (var part in Suffix)
					sb.Append(part.GenerateScript(variables));
			}
			return sb.ToString();
		}
	}

	public class MaybePart : ScriptPart
	{
		public string Variable;
		public string SkipIfRegexMatch;
		public ScriptPart[] Contents;

		public override string GenerateScript(Dictionary<string, object> variables)
		{
			if (!variables.ContainsKey(Variable))
				throw new ArgumentOutOfRangeException("Variable '" + Variable + "' does not exist.");

			var value = variables[Variable];
			if (!(value is string))
				throw new FormatException("Variable '" + Variable + "' is not a string.");

			if (Regex.IsMatch((string)value, SkipIfRegexMatch))
				return string.Empty;

			return ScriptFromComponents(Contents, variables);
		}

		public override string ConsumeScript(Dictionary<string, object> variables, string script, ScriptPart next)
		{
			// look for the contents regardless of whether the variable is already set or not / whether or not the set value matches the Skip Regex

			var existingState = variables.ToArray();
			var remaining = script;
			foreach (var index in Enumerable.Range(0, Contents.Length))
			{
				remaining = Contents[index].ConsumeScript(variables, remaining, index + 1 < Contents.Length ? Contents[index + 1] : next);
				if (remaining == null)
					break;
			}
			if (remaining == null)
			{
				variables = existingState.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
				return script;
			}
			else
				return remaining;
		}
	}
}
