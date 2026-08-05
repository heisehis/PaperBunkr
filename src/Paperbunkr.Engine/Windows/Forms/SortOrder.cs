namespace cYo.Common.Windows.Forms
{
	// TODO(Paperbunkr): portable stand-in for System.Windows.Forms.SortOrder, used here purely as
	// a saved-config value (ascending/descending/none), not for any actual ListView sort-glyph
	// rendering. See docs/retarget_spike_report.md.
	public enum SortOrder
	{
		None,
		Ascending,
		Descending
	}
}
