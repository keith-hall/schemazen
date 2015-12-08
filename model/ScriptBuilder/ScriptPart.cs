using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SchemaZen.model.ScriptBuilder
{
	public abstract class ScriptPart {
		public abstract string GenerateScript(Dictionary<string, object> variables);
		public abstract string ConsumeScript(Action<string, object> setVariable, string script, ScriptPart next);

		public static string ScriptFromComponents(IEnumerable<ScriptPart> components, Dictionary<string, object> variables)
		{
			var sb = new StringBuilder();
			foreach (var component in components)
			{
				sb.Append(component.GenerateScript(variables));
			}
			return sb.ToString();
		}

		internal static KeyValuePair<ScriptPart, string> VariablesFromScript(IEnumerable<ScriptPart> components, string script, Action<string, object> setVariable)
		{
			ScriptPart current = null;
			var remaining = script;
			var first = true;
			Action<ScriptPart> process = next =>
			{
				if (current != null && !(current is WhitespacePart && next is WhitespacePart)) // skip multiple consecutive whitespace parts
				{
					script = current.ConsumeScript(setVariable, remaining, next);
					if (current is WhitespacePart && first)
						script = remaining;
				}
				if (script != null)
				{
					if (current != null)
					{
						remaining = script;
						first = false;
					}
					current = next;
				}
			};
			
			foreach (var component in components)
			{
				process(component);
				if (script == null)
					return new KeyValuePair<ScriptPart, string>(current, remaining);
			}
			process(null);
			return new KeyValuePair<ScriptPart, string>(current, remaining);
		}

		internal static void SetVariableIfNotDifferent(Dictionary<string, object> variables, string name, object value)
		{
			if (variables.ContainsKey(name))
			{
				if (!variables[name].GetType().Equals(value.GetType()))
					throw new FormatException(string.Format("Variable '{0}' is a {1}, yet part of the script is expecting it to be a {2}.", name, variables[name].GetType().Name, value.GetType().Name));
				if (!variables[name].Equals(value))
					throw new FormatException(string.Format("Variable '{0}' is '{1}', yet part of the script is expecting it to be '{2}'.", name, variables[name], value));
			}
			else
			{
				variables[name] = value;
			}
		}

		public static Dictionary<string, object> VariablesFromScript(IEnumerable<ScriptPart> components, string script)
		{
			var variables = new Dictionary<string, object>();
			var result = VariablesFromScript(components, script, (name, value) => SetVariableIfNotDifferent(variables, name, value));
			if (result.Key != null)
				throw new FormatException(string.Format("Script does not match component. Script component: '{0}' Remaining script: '{1}'", result.Key, result.Value));
			return variables;
		}
	}

	public class ConstPart : ScriptPart
	{
		public string Text;

		public override string ConsumeScript(Action<string, object> setVariable, string script, ScriptPart next)
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

		public override string ConsumeScript(Action<string, object> setVariable, string script, ScriptPart next)
		{
			var match = ws.Match(script);
			if (match.Success)
			{
				return script.Substring(match.Length);
			} else
			{
				return null;
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

		public override string ConsumeScript(Action<string, object> setVariable, string script, ScriptPart next)
		{
			if (PotentialValues != null && PotentialValues.Any())
			{
				foreach (var value in PotentialValues)
				{
					if (script.StartsWith(value, StringComparison.InvariantCultureIgnoreCase))
					{
						setVariable(Name, script.Substring(0, value.Length));
						return script.Substring(value.Length);
					}
				}
				return null;
			} else
			{
				var index = FindNextPart(next, script);
				if (index > -1)
				{
					setVariable(Name, script.Substring(0, index));
					return script.Substring(index);
				}
				else
				{
					throw new NotImplementedException(string.Format("Unsupported script part after freely-valued variable {0}.", Name));
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
				throw new ArgumentOutOfRangeException(string.Format("Variable '{0}' does not exist.", Name));
			var value = variables[Name];
			if (!(value is string))
				throw new FormatException(string.Format("Variable '{0}' is not a string.", Name));
			if (PotentialValues != null && PotentialValues.Any() && !PotentialValues.Contains((string)value, StringComparer.InvariantCultureIgnoreCase))
				throw new FormatException(string.Format("Variable '{0}' does not match any of the expected values. Found value: '{1}' Allowed values: '{2}'", Name, value, string.Join("|", PotentialValues)));
			
			return (string)value;
		}
	}

	public class MultipleOccurancesPart : ScriptPart
	{
		public ScriptPart[] Prefix;
		public VariablePart Variable;
		public ScriptPart[] Suffix;
		public ScriptPart[] Separator;

		public override string ConsumeScript(Action<string, object> setVariable, string script, ScriptPart next)
		{
			if (Variable.PotentialValues != null && Variable.PotentialValues.Any())
				throw new NotSupportedException(string.Format("Variable {0} is expected to have multiple values, so it cannot have any potential values defined.", Variable.Name));

			var values = new Dictionary<string, List<string>>();
			Action<string, object> setMultiVar = (name, value) => {
				if (!values.ContainsKey(name))
				{
					values[name] = new List<string>();
				}
				if (!(value is string))
					throw new FormatException(string.Format("Variable {0} is expected to be a list of strings.", Variable.Name));
				values[name].Add((string)value);
			};

			var first = true;
			var components = Prefix.Concat(new[] { Variable }).Concat(Suffix);
			while (true)
			{
				var remaining = script;
				var result = VariablesFromScript(first ? components : Separator.Concat(components), remaining, setMultiVar);
				first = false;
				if (result.Key == null)
					script = result.Value;
				else
					break;
			}
			foreach (var kvp in values)
			{
				setVariable(kvp.Key, kvp.Value);
			}
			
			return script;
		}

		public override string GenerateScript(Dictionary<string, object> variables)
		{
			if (!variables.ContainsKey(Variable.Name))
				throw new ArgumentOutOfRangeException(string.Format("Variable '{0}' does not exist.", Variable.Name));
			if (!(variables[Variable.Name] is IEnumerable<string>))
				throw new FormatException(string.Format("Variable '{0}' is not a string enumerable.", Variable.Name));

			var sb = new StringBuilder();
			var values = (IEnumerable<string>)variables[Variable.Name];
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
				throw new ArgumentOutOfRangeException(string.Format("Variable '{0}' does not exist.", Variable));

			var value = variables[Variable];
			if (!(value is string))
				throw new FormatException(string.Format("Variable '{0}' is not a string.", Variable));

			if (Regex.IsMatch((string)value, SkipIfRegexMatch))
				return string.Empty;

			return ScriptFromComponents(Contents, variables);
		}

		public override string ConsumeScript(Action<string, object> setVariable, string script, ScriptPart next)
		{
			// look for the contents regardless of whether the variable is already set or not / whether or not the set value matches the Skip Regex

			var values = new Dictionary<string, object>();
			Action<string, object> setVar = (name, value) => SetVariableIfNotDifferent(values, name, value);

			var result = VariablesFromScript(Contents, script, setVar);
			if (result.Key != null)
			{
				return script;
			}
			else
			{
				// commit the variables
				foreach (var kvp in values)
					setVariable(kvp.Key, kvp.Value);
				return result.Value;
			}
		}
	}
}
