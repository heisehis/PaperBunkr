#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace cYo.Projects.ComicRack.Engine.IO.Provider.Books
{
	/// <summary>
	/// Small shared normalisers for the descriptive fields every book format hands back
	/// (docs/superpowers/specs/2026-10-26-books-slice1-data-foundation-design.md §4).
	/// </summary>
	public static class BookMetadataParsing
	{
		private static readonly Regex s_isbnCandidate = new Regex(@"(?<![0-9Xx])(?:97[89][\s\-]?)?(?:[0-9][\s\-]?){9}[0-9Xx](?![0-9Xx])", RegexOptions.Compiled);

		/// <summary>
		/// Extracts a valid ISBN-10 or ISBN-13 from an identifier string ("urn:isbn:978-0-441-17271-9", "ISBN 0441172717", a bare number).
		/// Returns the digits only, or null when nothing in the text passes the check digit.
		/// </summary>
		public static string? ExtractIsbn(string? text)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				return null;
			}

			foreach (Match match in s_isbnCandidate.Matches(text))
			{
				string digits = new string(match.Value.Where(c => char.IsDigit(c) || c == 'X' || c == 'x').ToArray()).ToUpperInvariant();
				if (digits.Length == 13 && IsValidIsbn13(digits))
				{
					return digits;
				}

				if (digits.Length == 10 && IsValidIsbn10(digits))
				{
					return digits;
				}
			}

			return null;
		}

		private static bool IsValidIsbn13(string digits)
		{
			if (!digits.All(char.IsDigit))
			{
				return false;
			}

			int sum = 0;
			for (int i = 0; i < 12; i++)
			{
				sum += (digits[i] - '0') * (i % 2 == 0 ? 1 : 3);
			}

			return (10 - (sum % 10)) % 10 == digits[12] - '0';
		}

		private static bool IsValidIsbn10(string digits)
		{
			int sum = 0;
			for (int i = 0; i < 10; i++)
			{
				char c = digits[i];
				int value;
				if (c == 'X' && i == 9)
				{
					value = 10;
				}
				else if (char.IsDigit(c))
				{
					value = c - '0';
				}
				else
				{
					return false;
				}

				sum += value * (10 - i);
			}

			return sum % 11 == 0;
		}

		/// <summary>Parses "2", "2.5", "02" (invariant culture) into a series index; null for blank or unparseable text.</summary>
		public static float? ParseIndex(string? text)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				return null;
			}

			return float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && value >= 0f && !float.IsInfinity(value)
				? value
				: null;
		}

		/// <summary>calibre:rating is stored 0-10; our scale is 0-5 in 0.5 steps. Out-of-range or zero (calibre's "unrated") returns null.</summary>
		public static float? FromCalibreRating(string? text)
		{
			if (!float.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float raw) || raw <= 0f || raw > 10f)
			{
				return null;
			}

			return (float)(Math.Round(raw) / 2d);
		}

		/// <summary>Trims, drops blanks and de-duplicates case-insensitively, keeping first-seen order and casing.</summary>
		public static IReadOnlyList<string> CleanList(IEnumerable<string?>? values)
		{
			if (values is null)
			{
				return Array.Empty<string>();
			}

			return values
				.Select(v => v?.Trim())
				.Where(v => !string.IsNullOrEmpty(v))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList()!;
		}

		/// <summary>Splits a "a, b; c" keyword string on commas/semicolons into a clean list.</summary>
		public static IReadOnlyList<string> SplitKeywords(string? text) =>
			string.IsNullOrWhiteSpace(text) ? Array.Empty<string>() : CleanList(text.Split(new[] { ',', ';' }));

		public static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
	}
}
