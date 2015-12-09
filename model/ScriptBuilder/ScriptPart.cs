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
		public abstract int PeekNextOccurrence(string script);

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
					if (first && current is WhitespacePart && script == null) // if the first script part is whitespace, then it is optional
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
				var equals = variables[name].Equals(value);
				if (!equals && value is string)
				{
					equals = string.Equals((string)variables[name], (string)value, StringComparison.InvariantCultureIgnoreCase);
				}
				if (!equals)
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
				throw new FormatException(string.Format("Script does not match component.{2}Script component: '{0}'{2}Remaining script: '{1}'", result.Key, result.Value, Environment.NewLine));
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

		public override string ConsumeScript(Action<string, object> setVariable, string script, ScriptPart next)
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

		public override int PeekNextOccurrence(string script)
		{
			return script.IndexOf(_text, StringComparison.InvariantCultureIgnoreCase);
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
		private static Regex wsConsume = new Regex(@"\A\s+");
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

		public override string ConsumeScript(Action<string, object> setVariable, string script, ScriptPart next)
		{
			var match = wsConsume.Match(script); // consume all whitespace, it doesn't matter about preferred amount/type here - allows more compatibility between SchemaZen versions in case of formatting changes etc.
			if (match.Success)
			{
				return script.Substring(match.Length);
			} else
			{
				if (next == null) // if it is the last of the script tokens, it is optional trailing whitespace
					return script;
				if (next is ConstPart)
				{
					// make whitespace optional if next character is a bracket or a comma
					// TODO: should this also work if the previous character was one of these?
					// TODO: probably should expand the range of characters to include all operators etc.
					var nextText = ((ConstPart)next).Text;
					var optionalIfFollowedBy = "()[],";
					if (optionalIfFollowedBy.Contains(nextText.Substring(0, 1)))
						return script;
				}
				return null;
			}
		}

		public override string GenerateScript(Dictionary<string, object> variables)
		{
			return new string(Enumerable.Repeat(_PreferredChar, _PreferredCount).ToArray());
		}

		public override int PeekNextOccurrence(string script)
		{
			var match = wsPeek.Match(script);
			return match.Index;
		}
	}

	public class VariablePart : ScriptPart
	{
		private string _Name;
		internal string[] _PotentialValues;

		public VariablePart(string Name, string[] PotentialValues = null)
		{
			if (string.IsNullOrEmpty(Name))
				throw new ArgumentNullException(nameof(Name));
			_Name = Name;
			_PotentialValues = PotentialValues;
		}

		public string Name { get { return _Name; } }

		public override string ConsumeScript(Action<string, object> setVariable, string script, ScriptPart next)
		{
			var length = 0;
			if (_PotentialValues != null && _PotentialValues.Any())
			{
				foreach (var value in _PotentialValues.OrderByDescending(s => s.Length)) // look for longest values first, in case a potential value starts with the entirety of another potential value
				{
					if (script.StartsWith(value, StringComparison.InvariantCultureIgnoreCase))
					{
						length = value.Length;
						break;
					}
				}
			}
			// attempt to find the next script part
			var nextPos = next == null ? script.Length : next.PeekNextOccurrence(script.Substring(length));
			if (nextPos == -1)
			{
				throw new FormatException(string.Format("Unable to find script part {0} after variable '{1}'.", next, _Name));
			}
			else
			{
				var value = script.Substring(0, nextPos + length);
				if (_PotentialValues != null && _PotentialValues.Any() && !_PotentialValues.Any(v => v.Equals(value, StringComparison.InvariantCultureIgnoreCase)))
				{
					throw new FormatException(string.Format("Variable '{0}', with value '{1}' in script does not match any potential values: {2}", _Name, value, string.Join("|", _PotentialValues)));
				}
				else
				{
					setVariable(_Name, value);
					return script.Substring(nextPos + length);
				}
			}
		}

		public override string GenerateScript(Dictionary<string, object> variables)
		{
			if (!variables.ContainsKey(_Name))
				throw new ArgumentOutOfRangeException(string.Format("Variable '{0}' does not exist.", _Name));
			var value = variables[_Name];
			if (!(value is string))
				throw new FormatException(string.Format("Variable '{0}' is not a string.", _Name));
			if (_PotentialValues != null && _PotentialValues.Any() && !_PotentialValues.Contains((string)value, StringComparer.InvariantCultureIgnoreCase))
				throw new ArgumentException(string.Format("Variable '{0}' does not match any of the expected values. Found value: '{1}' Allowed values: '{2}'", _Name, value, string.Join("|", _PotentialValues)));
			
			return (string)value;
		}

		public override int PeekNextOccurrence(string script)
		{
			throw new NotSupportedException();
		}
	}

	public class MultipleOccurancesPart : ScriptPart
	{
		private ScriptPart[] _Prefix;
		private VariablePart _Variable;
		private ScriptPart[] _Suffix;
		private ScriptPart[] _Separator;

		public MultipleOccurancesPart(VariablePart Variable, IEnumerable<ScriptPart> Separator, IEnumerable<ScriptPart> Prefix = null, IEnumerable<ScriptPart> Suffix = null)
		{
			if (Variable == null)
				throw new ArgumentNullException(nameof(Variable));
			if (Separator == null)
				throw new ArgumentNullException(nameof(Separator));
			_Prefix = (Prefix ?? Enumerable.Empty<ScriptPart>()).ToArray();
			_Variable = Variable;
			_Suffix = (Suffix ?? Enumerable.Empty<ScriptPart>()).ToArray();
			_Separator = Separator.ToArray();
			if (!_Separator.Any())
				throw new ArgumentNullException(nameof(Separator));
		}

		public override string ConsumeScript(Action<string, object> setVariable, string script, ScriptPart next)
		{
			if (_Variable._PotentialValues != null && _Variable._PotentialValues.Any())
				throw new NotSupportedException(string.Format("Variable {0} is expected to have multiple values, so it cannot have any potential values defined.", _Variable.Name));

			var values = new Dictionary<string, List<string>>();
			Action<string, object> setMultiVar = (name, value) => {
				if (!values.ContainsKey(name))
				{
					values[name] = new List<string>();
				}
				if (!(value is string))
					throw new FormatException(string.Format("Variable {0} is expected to be a list of strings.", _Variable.Name));
				values[name].Add((string)value);
			};

			var first = true;
			var components = _Prefix.Concat(new[] { _Variable }).Concat(_Suffix);
			while (true)
			{
				var remaining = script;
				var result = VariablesFromScript(first ? components : _Separator.Concat(components), remaining, setMultiVar);
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
			if (!variables.ContainsKey(_Variable.Name))
				throw new ArgumentOutOfRangeException(string.Format("Variable '{0}' does not exist.", _Variable.Name));
			if (!(variables[_Variable.Name] is IEnumerable<string>))
				throw new FormatException(string.Format("Variable '{0}' is not a string enumerable.", _Variable.Name));

			var sb = new StringBuilder();
			var values = (IEnumerable<string>)variables[_Variable.Name];
			var first = true;
			foreach (var value in values)
			{
				if (!first)
				{
					foreach (var part in _Separator)
						sb.Append(part.GenerateScript(variables));
				}
				else
				{
					first = false;
				}
				foreach (var part in _Prefix)
					sb.Append(part.GenerateScript(variables));
				sb.Append(value);
				foreach (var part in _Suffix)
					sb.Append(part.GenerateScript(variables));
			}
			return sb.ToString();
		}

		public override int PeekNextOccurrence(string script)
		{
			throw new NotSupportedException();
		}
	}

	public class MaybePart : ScriptPart
	{
		private string _Variable;
		private string _SkipIfRegexMatch;
		private ScriptPart[] _Contents;

		public MaybePart (string Variable, string SkipIfRegexMatch, IEnumerable<ScriptPart> Contents)
		{
			if (string.IsNullOrEmpty(Variable))
				throw new ArgumentNullException(nameof(Variable));
			if (string.IsNullOrEmpty(SkipIfRegexMatch))
				throw new ArgumentNullException(nameof(SkipIfRegexMatch));
			if (Contents == null)
				throw new ArgumentNullException(nameof(Contents));
			_Variable = Variable;
			_SkipIfRegexMatch = SkipIfRegexMatch;
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

			if (Regex.IsMatch((string)value, _SkipIfRegexMatch))
				return string.Empty;

			return ScriptFromComponents(_Contents, variables);
		}

		public override string ConsumeScript(Action<string, object> setVariable, string script, ScriptPart next)
		{
			// look for the contents regardless of whether the variable is already set or not / whether or not the set value matches the Skip Regex

			var values = new Dictionary<string, object>();
			Action<string, object> setVar = (name, value) => SetVariableIfNotDifferent(values, name, value);

			var result = VariablesFromScript(_Contents, script, setVar);
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

		public override int PeekNextOccurrence(string script)
		{
			throw new NotSupportedException();
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

		public override string ConsumeScript(Action<string, object> setVariable, string script, ScriptPart next)
		{
			// try each content to see if it is consumable... stop when none are.
			var unconsumed = _Contents.ToList();
			
			while (unconsumed.Count > 0)
			{
				var consumed = 0;
				
				foreach (var component in unconsumed.ToArray())
				{
					var remaining = component.ConsumeScript(setVariable, script, null);
					if (remaining != script)
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

		public override int PeekNextOccurrence(string script)
		{
			throw new NotSupportedException();
		}
	}
}
