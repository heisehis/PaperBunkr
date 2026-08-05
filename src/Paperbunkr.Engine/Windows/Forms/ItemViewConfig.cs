using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Xml.Serialization;

namespace cYo.Common.Windows.Forms
{
	// Ported from ComicRackCE's cYo.Common.Windows/Forms -- see ItemViewMode.cs for the rationale.
	// TODO(Paperbunkr): the original also had a GroupsStatus (ItemViewGroupsStatus) property
	// tracking which ListView group headers are collapsed, keyed off GroupHeaderInformation/
	// IViewableItem -- genuine live-UI state, and IViewableItem pulls in the excluded
	// cYo.Common.Windows.Forms ListView item-model chain. Dropped here rather than porting that
	// whole chain for a config record; group-collapse state will need a new home once there's an
	// Avalonia grouped view to track it for.
	[Serializable]
	public class ItemViewConfig
	{
		private List<ItemViewColumnInfo> columns = new List<ItemViewColumnInfo>();

		private ItemViewMode itemViewMode = ItemViewMode.Detail;

		private SortOrder itemSortOrder = SortOrder.Ascending;

		private SortOrder groupSortOrder = SortOrder.Ascending;

		[XmlArrayItem("Column")]
		public List<ItemViewColumnInfo> Columns
		{
			get => columns;
			set => columns = value;
		}

		[XmlAttribute]
		[DefaultValue(ItemViewMode.Detail)]
		public ItemViewMode ItemViewMode
		{
			get => itemViewMode;
			set => itemViewMode = value;
		}

		[XmlAttribute]
		[DefaultValue(false)]
		public bool Grouping { get; set; }

		[XmlAttribute]
		[DefaultValue(null)]
		public string SortKey { get; set; }

		[XmlAttribute]
		[DefaultValue(null)]
		public string GrouperId { get; set; }

		[XmlAttribute]
		[DefaultValue(null)]
		public string StackerId { get; set; }

		[XmlAttribute]
		[DefaultValue(SortOrder.Ascending)]
		public SortOrder ItemSortOrder
		{
			get => itemSortOrder;
			set => itemSortOrder = value;
		}

		[XmlAttribute]
		[DefaultValue(SortOrder.Ascending)]
		public SortOrder GroupSortOrder
		{
			get => groupSortOrder;
			set => groupSortOrder = value;
		}

		public Size ThumbnailSize { get; set; }

		public Size TileSize { get; set; }

		public int ItemRowHeight { get; set; }
	}
}
