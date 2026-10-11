using System.IO;
using cYo.Common.Xml;

namespace cYo.Projects.ComicRack.Engine
{
	/// <summary>
	/// Paperbunkr's additions to CE's generated <c>MetronInfo.cs</c> (docs/superpowers/specs/2026-10-05-
	/// metroninfo-write-back-design.md). Kept in this partial so the generated file stays CE's own v1.0
	/// copy: the two elements MetronInfo v1.1 added (both optional, so a document without them is still
	/// valid v1.0), and the serialization CE never needed because it only reads the format.
	/// </summary>
	public partial class MetronInfo
	{
		[System.Xml.Serialization.XmlElementAttribute("AlternativeNumber")]
		public string AlternativeNumber { get; set; }

		[System.Xml.Serialization.XmlElementAttribute("CommunityRating")]
		public CommunityRatingType CommunityRating { get; set; }

		public byte[] ToArray()
		{
			using (MemoryStream memoryStream = new MemoryStream())
			{
				XmlUtility.GetSerializer<MetronInfo>().Serialize(memoryStream, this);
				return memoryStream.ToArray();
			}
		}

		/// <summary>Returns null for anything that does not parse as a MetronInfo document.</summary>
		public static MetronInfo TryRead(Stream inStream)
		{
			try
			{
				return XmlUtility.GetSerializer<MetronInfo>().Deserialize(inStream) as MetronInfo;
			}
			catch (System.Exception)
			{
				return null;
			}
		}
	}

	[System.SerializableAttribute()]
	[System.Xml.Serialization.XmlTypeAttribute("communityRatingType", Namespace = "")]
	public partial class CommunityRatingType
	{
		[System.Xml.Serialization.XmlElementAttribute("AverageRating")]
		public decimal AverageRating { get; set; }

		[System.Xml.Serialization.XmlElementAttribute("RatingCount")]
		public int RatingCount { get; set; }

		[System.Xml.Serialization.XmlIgnoreAttribute()]
		public bool RatingCountSpecified { get; set; }
	}
}
