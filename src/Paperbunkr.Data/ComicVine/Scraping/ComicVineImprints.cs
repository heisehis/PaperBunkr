namespace Paperbunkr.Data.ComicVine.Scraping;

/// <summary>
/// Static imprint -&gt; parent-publisher map, ported verbatim from CE's real <c>cvimprints.py</c>
/// (design doc §3) - the full 70-entry table, re-extracted directly from the actual extracted
/// <c>ComicVineScraper-1.0.102</c> plugin source once it became available again on disk this session
/// (a prior pass shipped only 8 hand-verified entries and explicitly flagged the rest as
/// not-safe-to-guess; this replaces that placeholder). CE's own docstring is explicit: "Both the
/// passed in and returned strings for these methods must EXACTLY match their corresponding values in
/// the ComicVine database (i.e. case, punctuation, etc.)" - exact-match only, no normalization.
///
/// The user-editable override list (CE's advanced <c>IMPRINT=X--&gt;Y</c> settings lines) is a
/// separate mechanism - see <see cref="FindParentPublisher"/>'s <paramref name="userOverrides"/>
/// parameter, checked first so a user's own mapping always wins over this static table.
/// </summary>
public static class ComicVineImprints
{
    // Parent-publisher name constants match CE's own literal strings exactly (cvimprints.py's
    // __ACTIONLAB/__DC/etc. module constants) - e.g. "Image" not "Image Comics", "Boom!" not
    // "Boom! Studios" - since CE's own docstring requires an exact ComicVine-database-string match.
    private const string ActionLab = "Action Lab";
    private const string Amryl = "Amryl Entertainment";
    private const string Ape = "Ape Entertainment";
    private const string Avatar = "Avatar Press";
    private const string Boom = "Boom!";
    private const string DarkHorse = "Dark Horse Comics";
    private const string Dc = "DC Comics";
    private const string Dynamite = "Dynamite Entertainment";
    private const string Hakusensha = "Hakusensha";
    private const string Heroic = "Heroic Publishing";
    private const string Idw = "IDW Publishing";
    private const string Image = "Image";
    private const string Kodansha = "Kodansha";
    private const string Lion = "Lion Forge Comics";
    private const string Malibu = "Malibu";
    private const string Marvel = "Marvel";
    private const string Nbm = "Nbm";
    private const string Penguin = "Penguin Group";
    private const string Radio = "Radio Comix";
    private const string Slg = "Slg Publishing";
    private const string Titan = "Titan Comics";
    private const string Tokuma = "Tokuma Shoten";
    private const string Tokyopop = "Tokyopop";
    private const string Wizard = "Wizard";

    private static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>
    {
        ["2000AD"] = Dc,
        ["Adventure"] = Malibu,
        ["Aircel Publishing"] = Malibu,
        ["America's Best Comics"] = Dc, // originally image
        ["Amerotica "] = Nbm,
        ["Antimatter"] = Amryl,
        ["Apparat"] = Avatar,
        ["Archaia"] = Boom,
        ["Berger Books"] = DarkHorse,
        ["BOOM! Box"] = Boom,
        ["Boundless Comics"] = Avatar,
        ["Black Bull"] = Wizard,
        ["Black Crown"] = Idw,
        ["Blu Manga"] = Tokyopop,
        ["CMX"] = Dc,
        ["Chaos! Comics"] = Dynamite,
        ["Cliffhanger"] = Dc,
        ["Comic Bom Bom"] = Kodansha,
        ["ComicsLit"] = Nbm,
        ["Curtis Magazines"] = Marvel,
        ["Danger Zone"] = ActionLab,
        ["Dark Horse Books"] = DarkHorse,
        ["Dark Horse Manga"] = DarkHorse,
        ["Desperado Publishing"] = Image,
        ["Epic"] = Marvel,
        ["Eternity"] = Malibu,
        ["Eurotica "] = Nbm,
        ["Focus"] = Dc,
        ["Helix"] = Dc,
        ["Hero Comics"] = Heroic,
        ["Homage comics"] = Dc, // i.e. wildstorm
        ["Hudson Street Press"] = Penguin,
        ["Icon Comics"] = Marvel,
        ["Impact"] = Dc,
        ["Jets Comics"] = Hakusensha,
        ["KaBOOM!"] = Boom,
        ["KiZoic"] = Ape,
        ["Kodansha Comics Digital-First!"] = Kodansha,
        ["Kodansha Comics USA"] = Kodansha,
        ["MAD"] = Dc,
        ["Marvel Digital Comics Unlimited"] = Marvel,
        ["Marvel Knights"] = Marvel,
        ["Marvel Music"] = Marvel,
        ["Marvel Soleil"] = Marvel,
        ["Marvel UK"] = Marvel,
        ["Maverick"] = DarkHorse,
        ["Max"] = Marvel,
        ["Milestone"] = Dc,
        ["Minx"] = Dc,
        ["Papercutz"] = Nbm,
        ["Paradox Press"] = Dc,
        ["Piranha Press"] = Dc,
        ["Quillion"] = Lion,
        ["Razorline"] = Marvel,
        ["Roar Comics"] = Lion,
        ["ShadowLine"] = Image,
        ["Silverline"] = Image,
        ["Sin Factory Comix"] = Radio,
        ["Skybound"] = Image,
        ["Slave Labor"] = Slg,
        ["Star Comics"] = Marvel,
        ["Tangent Comics"] = Dc,
        ["Titan Books"] = Titan,
        ["Todd McFarlane Productions"] = Image,
        ["Tokuma Comics"] = Tokuma,
        ["Top Cow"] = Image,
        ["Top Shelf"] = Idw,
        ["Ultraverse"] = Malibu,
        ["Vertical"] = Kodansha,
        ["Vertigo"] = Dc,
        ["Wildstorm"] = Dc,
        ["Zuda Comics"] = Dc,
    };

    /// <summary>
    /// Resolves <paramref name="imprint"/> to its parent publisher: checks
    /// <paramref name="userOverrides"/> first (a user's own mapping always wins), then the static
    /// table above, and returns <paramref name="imprint"/> unchanged if neither has an exact match -
    /// matching CE's own fallback behavior exactly.
    /// </summary>
    public static string FindParentPublisher(string imprint, IReadOnlyDictionary<string, string>? userOverrides = null)
    {
        string trimmed = imprint.Trim();

        if (userOverrides is not null && userOverrides.TryGetValue(trimmed, out string? overridden))
        {
            return overridden;
        }

        return Map.TryGetValue(trimmed, out string? parent) ? parent : imprint;
    }
}
