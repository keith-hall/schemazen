using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SchemaZen.model.ScriptBuilder
{
	public abstract class ScriptPart {
		public abstract string GenerateScript(Dictionary<string, object> variables);
		public abstract string ConsumeScript(Action<string, object> setVariable, string script);
		
		public static string ScriptFromComponents(IEnumerable<ScriptPart> components, Dictionary<string, object> variables)
		{
			var sb = new StringBuilder();
			foreach (var component in components)
			{
				sb.Append(component.GenerateScript(variables));
			}
			return sb.ToString();
		}

		private const string optionalWhitespaceAround = ",./()-+";
		internal static KeyValuePair<ScriptPart, string> VariablesFromScript(IEnumerable<ScriptPart> components, string script, Action<string, object> setVariable)
		{
			var parts = new[] { new WhitespacePart() }.Concat(components).ToArray();
			for (var i = 0; i < parts.Length; i++)
			{
				// if the previous part was an identifier or a whitespace part and this is a whitespace part
				if (i > 0 && (parts[i - 1] is IdentifierPart || parts[i - 1] is WhitespacePart) && parts[i] is WhitespacePart)
					continue; // the whitespace has already been consumed
				// if this part is a whitespace part and the next part is an identifier
				if (i < parts.Length - 1 && parts[i] is WhitespacePart && parts[i + 1] is IdentifierPart)
					continue; // the whitespace is optional, the identifier will consume it
				
				var remaining = script;
				script = parts[i].ConsumeScript(setVariable, remaining);
				if (script == null)
				{
					var skip = false;
					// if this part that failed to match is a whitespace part
					if (parts[i] is WhitespacePart)
					{
						if (i == 0) // if this was the first part, then it is optional, so try again with the next part
						{
							skip = true;
						}
						else
						{
							// if the next part is  a constant that starts with a character that means this whitespace is optional
							// or the prev part was a constant that ended  with a character that means this whitespace is optional
							if ((i < parts.Length - 1 && parts[i + 1] is ConstPart && optionalWhitespaceAround.Contains(((ConstPart)parts[i + 1]).Text.First())) ||
								(i > 0 && parts[i - 1] is ConstPart && optionalWhitespaceAround.Contains(((ConstPart)parts[i - 1]).Text.Last()))
							   )
							{
								skip = true;
							}
						}
					}
					if (skip)
					{
						script = remaining;
						continue;
					} else
						return new KeyValuePair<ScriptPart, string>(parts[i], remaining);
				}
			}
			return new KeyValuePair<ScriptPart, string>(null, script);
		}

		internal static void SetVariableIfNotDifferent(Dictionary<string, object> variables, string name, object value)
		{
			if (variables.ContainsKey(name))
			{
				if (!variables[name].GetType().Equals(value.GetType()))
					throw new FormatException(string.Format("Variable '{0}' is a {1}, yet part of the script is expecting it to be a {2}.", name, variables[name].GetType().Name, value.GetType().Name));
				var equals = variables[name].Equals(value);
				if (!equals) {
					if (value is string)
						equals = string.Equals((string)variables[name], (string)value, StringComparison.InvariantCultureIgnoreCase);
					else if (value is IEnumerable<string>)
						equals = ((IEnumerable<string>)value).SequenceEqual((IEnumerable<string>)variables[name]);
				}
				if (!equals)
					throw new FormatException(string.Format("Variable '{0}' is '{1}', yet part of the script is expecting it to be '{2}'.", name, variables[name] is IEnumerable<string> ? string.Join(", ", ((IEnumerable<string>)variables[name]).ToArray()) : variables[name], value is IEnumerable<string> ? string.Join(", ", ((IEnumerable<string>)value).ToArray()) : value));
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
				throw new FormatException(string.Format("Script does not match component.{2}Script component: '{0}'{2}Remaining script: '{1}'", result.Key, result.Value, Environment.NewLine));
			if (result.Value.Length > 0)
			{
				var trailing = WhitespacePart.ConsumeScript(result.Value); // ignore whitespace and comments
				if (trailing == null || trailing.Length > 0) // if it wasn't whitespace at the start of the remaining string, or there is still some non-whitespace left
					throw new FormatException(string.Format("Script contains some unexpected trailing text:{0}{1}", Environment.NewLine, trailing ?? result.Value));
			}
			return variables;
		}
	}

	public class ConstPart : ScriptPart
	{
		private string _text;

		public ConstPart (string Text)
		{
			if (string.IsNullOrEmpty(Text))
				throw new ArgumentNullException(nameof(Text));
			_text = Text;
		}

		public string Text { get { return _text; } }

		public override string ConsumeScript(Action<string, object> setVariable, string script)
		{
			if (script.StartsWith(_text, StringComparison.InvariantCultureIgnoreCase))
			{
				return script.Substring(_text.Length);
			} else
			{
				return null;
			}
		}

		public override string GenerateScript(Dictionary<string, object> variables)
		{
			return _text;
		}

		private static Regex ws = new Regex(@"\s+");
		public static IEnumerable<ScriptPart> FromString(string Text)
		{
			Text = Text.Replace(Environment.NewLine, "\n");
			var pos = 0;
			foreach (var match in ws.Matches(Text).OfType<Match>())
			{
				var text = Text.Substring(pos, match.Index - pos);
				if (text.Length > 0)
					yield return new ConstPart(text);
				char? prevChar = null;
				int count = 0;
				foreach (var chr in Text.Substring(match.Index, match.Length))
				{
					if (!prevChar.HasValue || prevChar.Value != chr)
					{
						if (count > 0)
						{
							yield return new WhitespacePart(PreferredCount: count, PreferredChar: prevChar.Value);
						}
						prevChar = chr;
						count = 1;
					}
					else
						count++;
				}
				if (count > 0)
				{
					yield return new WhitespacePart(PreferredCount: count, PreferredChar: prevChar.Value);
				}

				pos = match.Index + match.Length;
			}
			if (pos < Text.Length)
				yield return new ConstPart(Text.Substring(pos));
		}
	}

	public class WhitespacePart : ScriptPart
	{
		private char _PreferredChar;
		private int _PreferredCount;
		internal static Regex wsConsume = new Regex(@"\A" + Database.SqlWhitespaceOrCommentRegex + "+");
		private static Regex wsPeek = new Regex(@"\s");

		public WhitespacePart(char PreferredChar = ' ', int PreferredCount = 1)
		{
			if (PreferredCount < 0)
				throw new ArgumentOutOfRangeException(nameof(PreferredCount));
			if (!wsConsume.IsMatch(PreferredChar.ToString()))
				throw new ArgumentException("Value must be a whitespace character.", nameof(PreferredChar));
			_PreferredChar = PreferredChar;
			_PreferredCount = PreferredCount;
		}

		public static string ConsumeScript(string script)
		{
			var match = wsConsume.Match(script); // consume all whitespace, it doesn't matter about preferred amount/type here - allows more compatibility between SchemaZen versions in case of formatting changes etc.
			if (match.Success)
			{
				return script.Substring(match.Length);
			}
			else
			{
				return null;
			}
		}

		public override string ConsumeScript(Action<string, object> setVariable, string script)
		{
			return ConsumeScript(script);
		}

		public override string GenerateScript(Dictionary<string, object> variables)
		{
			if (_PreferredChar == '\n')
				return string.Join(string.Empty, Enumerable.Repeat(Environment.NewLine, _PreferredCount).ToArray());
			else
				return new string(Enumerable.Repeat(_PreferredChar, _PreferredCount).ToArray());
		}
	}

	public class IdentifierPart : ScriptPart
	{
		private string VarName;
		private static Regex identifier = new Regex(@"\A" + Database.SqlWhitespaceOrCommentRegex + "*(" + Database.SqlEnclosedIdentifierRegex + "|" + Database.SqlRegularIdentifierRegex + "|" + Database.SqlQuotedIdentifierRegex + ")" + Database.SqlWhitespaceOrCommentRegex + "*");
		
		public IdentifierPart(string VariableName)
		{
			if (string.IsNullOrEmpty(VariableName))
				throw new ArgumentNullException(nameof(VariableName));
			VarName = VariableName;
		}

		public string VariableName { get { return VarName; } }

		public override string ConsumeScript(Action<string, object> setVariable, string script)
		{
			var match = identifier.Match(script);
			if (!match.Success)
			{
				return null;
			}
			var identifierName = match.Groups[1].Value;
			if (identifierName.StartsWith("[") || identifierName.StartsWith("\""))
				identifierName = identifierName.Substring(1, identifierName.Length - 2);
			setVariable(VariableName, identifierName);
			return script.Substring(match.Length);
		}

		protected string GenerateScript(string identifierName)
		{
			return "[" + identifierName + "]";
		}

		public override string GenerateScript(Dictionary<string, object> variables)
		{
			if (!variables.ContainsKey(VariableName))
				throw new ArgumentOutOfRangeException(string.Format("Variable '{0}' does not exist.", VariableName));
			var value = variables[VariableName];
			if (!(value is string))
				throw new FormatException(string.Format("Variable '{0}' is not a string.", VariableName));

			return GenerateScript((string)value);
		}
	}

	public class OneOfPredefinedValuesPart : ScriptPart
	{
		private string _VariableName;
		internal string[] _PotentialValues;
		
		public OneOfPredefinedValuesPart(string VariableName, IEnumerable<string> PotentialValues)
		{
			if (string.IsNullOrEmpty(VariableName))
				throw new ArgumentNullException(nameof(VariableName));
			_VariableName = VariableName;
			if (PotentialValues == null)
				throw new ArgumentNullException(nameof(PotentialValues));
			_PotentialValues = PotentialValues.ToArray();
			if (!_PotentialValues.Any())
				throw new ArgumentNullException(nameof(PotentialValues));
		}

		public string VariableName { get { return _VariableName; } }

		public override string ConsumeScript(Action<string, object> setVariable, string script)
		{
			foreach (var value in _PotentialValues.OrderByDescending(s => s.Length)) // look for longest values first, in case a potential value starts with the entirety of another potential value
			{
				if (script.StartsWith(value, StringComparison.InvariantCultureIgnoreCase))
				{
					setVariable(_VariableName, script.Substring(0, value.Length)); // we could use the predefined value here, but we take it from the script instead so that it has the casing from there
					return script.Substring(value.Length);
				}
			}
			return null;
		}

		public override string GenerateScript(Dictionary<string, object> variables)
		{
			if (!variables.ContainsKey(_VariableName))
				throw new ArgumentOutOfRangeException(string.Format("Variable '{0}' does not exist.", _VariableName));
			var value = variables[_VariableName];
			if (!(value is string))
				throw new FormatException(string.Format("Variable '{0}' is not a string.", _VariableName));
			if (!_PotentialValues.Contains((string)value, StringComparer.InvariantCultureIgnoreCase))
				throw new ArgumentException(string.Format("Variable '{0}' does not match any of the expected values. Found value: '{1}' Allowed values: '{2}'", _VariableName, value, string.Join("|", _PotentialValues)));
			
			return (string)value;
		}
	}

	public class MultipleSeparatedIdentifiersPart : IdentifierPart
	{
		private string _separator;
		private bool _includeWS;

		public MultipleSeparatedIdentifiersPart(string VariableName, string Separator) : base(VariableName)
		{
			_separator = Separator;
			_includeWS = (Separator != ".");
		}

		public override string ConsumeScript(Action<string, object> setVariable, string script)
		{
			var values = new Dictionary<string, List<string>>();
			Action<string, object> setMultiVar = (name, value) => {
				if (!values.ContainsKey(name))
				{
					values[name] = new List<string>();
				}
				if (!(value is string))
					throw new FormatException(string.Format("Variable {0} is expected to be a list of strings.", VariableName));
				values[name].Add((string)value);
			};

			while ((script = base.ConsumeScript(setMultiVar, script)) != null)
			{
				if (!script.StartsWith(_separator))
					break;
				else {
					script = script.Substring(_separator.Length);
					var skipWS = WhitespacePart.ConsumeScript(script);
					if (skipWS != null)
						script = skipWS;
				}
			}
			foreach (var kvp in values)
			{
				setVariable(kvp.Key, kvp.Value);
			}
			
			return script;
		}

		public override string GenerateScript(Dictionary<string, object> variables)
		{
			if (!variables.ContainsKey(VariableName))
				throw new ArgumentOutOfRangeException(string.Format("Variable '{0}' does not exist.", VariableName));
			if (!(variables[VariableName] is IEnumerable<string>))
				throw new FormatException(string.Format("Variable '{0}' is not a string enumerable.", VariableName));

			var sb = new StringBuilder();
			var values = (IEnumerable<string>)variables[VariableName];
			var first = true;
			foreach (var value in values)
			{
				if (!first)
				{
					sb.Append(_separator);
					if (_includeWS)
						sb.Append(" ");
				}
				else
				{
					first = false;
				}
				sb.Append(GenerateScript(value));
			}
			return sb.ToString();
		}
	}

	public class MaybePart : ScriptPart
	{
		private string _Variable;
		private string[] _SkipGenerationIfVariableMatchesAny;
		private ScriptPart[] _Contents;

		public MaybePart (string Variable, IEnumerable<string> SkipGenerationIfVariableValueMatchesAny, IEnumerable<ScriptPart> Contents)
		{
			if (string.IsNullOrEmpty(Variable))
				throw new ArgumentNullException(nameof(Variable));
			if (SkipGenerationIfVariableValueMatchesAny == null)
				throw new ArgumentNullException(nameof(SkipGenerationIfVariableValueMatchesAny));
			if (Contents == null)
				throw new ArgumentNullException(nameof(Contents));
			_Variable = Variable;
			_SkipGenerationIfVariableMatchesAny = SkipGenerationIfVariableValueMatchesAny.ToArray();
			if (!_SkipGenerationIfVariableMatchesAny.Any())
				throw new ArgumentNullException(nameof(SkipGenerationIfVariableValueMatchesAny));
			_Contents = Contents.ToArray();
			if (!_Contents.Any())
				throw new ArgumentNullException(nameof(Contents));
		}

		public override string GenerateScript(Dictionary<string, object> variables)
		{
			if (!variables.ContainsKey(_Variable))
				throw new ArgumentOutOfRangeException(string.Format("Variable '{0}' does not exist.", _Variable));

			var value = variables[_Variable];
			if (!(value is string))
				throw new FormatException(string.Format("Variable '{0}' is not a string.", _Variable));

			if (_SkipGenerationIfVariableMatchesAny.Contains((string)value, StringComparer.InvariantCultureIgnoreCase))
				return string.Empty;

			return ScriptFromComponents(_Contents, variables);
		}

		public override string ConsumeScript(Action<string, object> setVariable, string script)
		{
			// look for the contents regardless of whether the variable is already set or not / whether or not the set value matches one to skip

			var values = new Dictionary<string, object>();
			Action<string, object> setVar = (name, value) => SetVariableIfNotDifferent(values, name, value);

			var result = VariablesFromScript(_Contents, script, setVar);
			if (result.Key != null)
			{
				return script; // unable to find contents, as it is a MaybePart, return the script unconsumed
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

	public class AnyOrderPart : ScriptPart
	{
		private ScriptPart[] _Contents;

		public AnyOrderPart(IEnumerable<ScriptPart> Contents)
		{
			if (Contents == null)
				throw new ArgumentNullException(nameof(Contents));
			_Contents = Contents.ToArray();
			if (!_Contents.Any())
				throw new ArgumentNullException(nameof(Contents));
		}

		public override string ConsumeScript(Action<string, object> setVariable, string script)
		{
			// try each content to see if it is consumable... stop when none are.
			var unconsumed = _Contents.ToList();
			
			while (unconsumed.Count > 0)
			{
				var consumed = 0;
				
				foreach (var component in unconsumed.ToArray())
				{
					var remaining = component.ConsumeScript(setVariable, script);
					if (remaining != script && remaining != null)
					{
						unconsumed.Remove(component);
						consumed++;
						script = remaining;
					}
				}
				if (consumed == 0)
					break;
			}
			if (!unconsumed.Any() || unconsumed.All(p => p is MaybePart))
				return script;
			else
				return null;
		}

		public override string GenerateScript(Dictionary<string, object> variables)
		{
			// output the contents in the order they were defined
			var sb = new StringBuilder();
			foreach (var component in _Contents)
				sb.Append(component.GenerateScript(variables));
			return sb.ToString();
		}
	}
}
