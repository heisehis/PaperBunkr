// DrawThumbnailOverlay hook - proves the Book payload arrives and a byte[] result round-trips.
// A real plugin would return real overlay PNG bytes (read from a bundled file, since scripts have
// no System.Drawing/Avalonia reference to generate one on the fly - see CSharpCommand's fixed
// assembly-reference list); arbitrary bytes are enough to prove the wiring here.
return new byte[] { 1, 2, 3, (byte)(Book.Id % 256) };
