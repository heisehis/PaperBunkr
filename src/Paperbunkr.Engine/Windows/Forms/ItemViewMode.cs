namespace cYo.Common.Windows.Forms
{
	// Ported from ComicRackCE's cYo.Common.Windows/Forms (a WinForms-only project overall,
	// excluded per the porting brief). This one enum has zero WinForms dependency and is needed
	// by DisplayListConfig/StacksConfig (core saved-view config, not UI code) via ItemViewConfig.
	// See docs/retarget_spike_report.md.
	public enum ItemViewMode
	{
		Thumbnail,
		Tile,
		Detail
	}
}
