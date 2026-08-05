namespace cYo.Common.Drawing
{
	// TODO(Paperbunkr): portable stand-in for System.Windows.Forms.Padding (a WinForms-only
	// struct despite describing a plain geometric concept). RectangleExtensions used the WinForms
	// type purely for its four Left/Top/Right/Bottom ints plus Horizontal/Vertical convenience
	// properties -- nothing UI-specific. Defined locally to avoid pulling in System.Windows.Forms.
	public struct Padding
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;

		public Padding(int all)
		{
			Left = Top = Right = Bottom = all;
		}

		public Padding(int left, int top, int right, int bottom)
		{
			Left = left;
			Top = top;
			Right = right;
			Bottom = bottom;
		}

		public int Horizontal => Left + Right;

		public int Vertical => Top + Bottom;
	}
}
