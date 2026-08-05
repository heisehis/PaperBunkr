using System;
using System.Drawing;

namespace cYo.Projects.ComicRack.Engine.Display
{
	// TODO(Paperbunkr): replaces System.Windows.Forms.MouseButtons (Left/Right/Middle/XButton1/XButton2/None)
	// so this event-args type doesn't drag in a WinForms dependency. Values match the WinForms enum's flags.
	[Flags]
	public enum MouseButton
	{
		None = 0,
		Left = 0x00100000,
		Right = 0x00200000,
		Middle = 0x00400000,
		XButton1 = 0x00800000,
		XButton2 = 0x01000000
	}

	public class GestureEventArgs : EventArgs
	{
		private readonly GestureType gesture;

		public GestureType Gesture => gesture;

		public ContentAlignment Area
		{
			get;
			set;
		}

		public Rectangle AreaBounds
		{
			get;
			set;
		}

		public Point Location
		{
			get;
			set;
		}

		public MouseButton MouseButton
		{
			get;
			set;
		}

		public bool Double
		{
			get;
			set;
		}

		public bool Handled
		{
			get;
			set;
		}

		public bool IsTouch
		{
			get;
			set;
		}

		public GestureEventArgs(GestureType gesture)
		{
			this.gesture = gesture;
		}
	}
}
