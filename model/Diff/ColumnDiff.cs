using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SchemaZen.model
{
	public class ColumnDiff
	{
		public Column Source;
		public Column Target;

		public ColumnDiff(Column target, Column source)
		{
			Source = source;
			Target = target;
		}

		public bool IsDiff
		{
			get { return IsDiffBase || DefaultIsDiff; }
		}

		private bool IsDiffBase
		{
			get
			{
				return Source.IsNullable != Target.IsNullable || Source.Length != Target.Length ||
					   Source.Position != Target.Position || Source.Type != Target.Type || Source.Precision != Target.Precision ||
					   Source.Scale != Target.Scale || Source.ComputedDefinition != Target.ComputedDefinition;
			}
		}

		public bool DefaultIsDiff
		{
			get { return Source.DefaultText != Target.DefaultText; }
		}

		public bool OnlyDefaultIsDiff
		{
			get { return DefaultIsDiff && !IsDiffBase; }
		}
	}
}
