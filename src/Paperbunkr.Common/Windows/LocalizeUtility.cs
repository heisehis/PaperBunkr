using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using cYo.Common.Localize;
using cYo.Common.Text;

namespace cYo.Common.Windows
{
	// Ported from ComicRackCE's cYo.Common.Windows.LocalizeUtility, which is otherwise a deeply
	// WinForms-coupled Control/ToolStrip/ListView/DataGridView tree-walking localizer (genuine UI
	// code, correctly excluded along with the rest of cYo.Common.Windows -- see
	// docs/retarget_spike_report.md). LocalizeEnum is the one member Engine actually calls
	// (ComicPageInfo.cs, MetronInfoProvider.cs) and has zero WinForms dependency: it just maps an
	// enum value to a localized display string via TR. Ported alone rather than dragging the rest
	// of the WinForms-coupled class in.
	public static class LocalizeUtility
	{
		public static string LocalizeEnum(Type enumType, int value)
		{
			string name = Enum.GetName(enumType, value);
			return TR.Load(enumType.Name)[name, GetEnumDescription(enumType, name).PascalToSpaced()];
		}

		private static string GetEnumDescription(Type enumType, string name)
		{
			FieldInfo field = enumType.GetField(name);
			DescriptionAttribute descriptionAttribute = field.GetCustomAttributes(typeof(DescriptionAttribute), inherit: false).Cast<DescriptionAttribute>().FirstOrDefault();
			if (descriptionAttribute != null)
			{
				return descriptionAttribute.Description;
			}
			return name;
		}
	}
}
