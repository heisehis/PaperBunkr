// ComicInfoHtml hook - proves the Book payload arrives. Result is shown as plain text with tags
// stripped, never rendered as real HTML (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md
// §5) - the tags here exist to prove the stripping actually happens App-side.
return "<b>HTML</b> info for issue " + Book.Id;
