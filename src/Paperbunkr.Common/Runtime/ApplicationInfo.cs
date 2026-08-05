namespace cYo.Common.Runtime
{
	/// <summary>
	/// Replaces the WinForms <c>System.Windows.Forms.Application.CompanyName</c>/<c>ProductName</c>
	/// static properties, which pulled these values from the entry assembly's attributes at runtime.
	/// Stripped as part of the net8 retarget (Paperbunkr does not depend on
	/// System.Windows.Forms). Values below mirror ComicRack CE's assembly attributes so that
	/// on-disk paths (e.g. backup archive naming, PDF document metadata) stay stable.
	/// TODO(Paperbunkr): reconsider these values (and how they're set) once Paperbunkr has its
	/// own branding/assembly metadata story.
	/// </summary>
	public static class ApplicationInfo
	{
		public const string CompanyName = "cYo";
		public const string ProductName = "ComicRack Community Edition";
	}
}
