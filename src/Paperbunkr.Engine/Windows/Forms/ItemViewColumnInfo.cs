using System;
using System.ComponentModel;
using System.Xml.Serialization;

namespace cYo.Common.Windows.Forms
{
	// Ported from ComicRackCE's cYo.Common.Windows/Forms -- see ItemViewMode.cs for the rationale.
	// TODO(Paperbunkr): the original also had a constructor taking an IColumn (a live WinForms
	// ListView column header) to snapshot its state into this serializable record. That
	// constructor is UI glue, not needed by DisplayListConfig/StacksConfig persistence, and IColumn
	// itself lives in the excluded cYo.Common.Windows.Forms UI layer -- so it was dropped here
	// rather than dragging that whole dependency chain in. Re-add once there's an Avalonia-side
	// column-header type to snapshot from.
	[Serializable]
	public class ItemViewColumnInfo
	{
		private bool visible = true;

		private int width = 80;

		private readonly string name;

		[NonSerialized]
		private readonly object tag;

		private DateTime lastTimeVisible = DateTime.MinValue;

		[XmlAttribute]
		[DefaultValue(0)]
		public int Id { get; set; }

		[XmlAttribute]
		[DefaultValue(0)]
		public int FormatId { get; set; }

		[XmlAttribute]
		[DefaultValue(true)]
		public bool Visible
		{
			get => visible;
			set => visible = value;
		}

		[XmlAttribute]
		[DefaultValue(80)]
		public int Width
		{
			get => width;
			set => width = value;
		}

		public string Name => name;

		public object Tag => tag;

		[DefaultValue(typeof(DateTime), "0001-01-01T00:00:00")]
		public DateTime LastTimeVisible
		{
			get => lastTimeVisible;
			set => lastTimeVisible = value;
		}

		public ItemViewColumnInfo()
		{
		}

		public override string ToString()
		{
			if (!string.IsNullOrEmpty(name))
			{
				return name;
			}
			return string.Empty;
		}
	}
}
