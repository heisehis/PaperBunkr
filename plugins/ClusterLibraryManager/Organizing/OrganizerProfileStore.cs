using ClusterLibraryManager.Persistence;
using LiteDB;

namespace ClusterLibraryManager.Organizing;

/// <summary>LiteDB-backed CRUD for <see cref="OrganizerProfile"/> (design doc §7, grilling Q16=B -
/// full CE-parity multiple named profiles, not a single active configuration).</summary>
public sealed class OrganizerProfileStore
{
    private const string CollectionName = "organizer_profiles";
    private readonly PluginDatabase _database;

    public OrganizerProfileStore(PluginDatabase database)
    {
        _database = database;
    }

    public IReadOnlyList<OrganizerProfile> GetAll() => Collection().FindAll().OrderBy(p => p.Name).ToList();

    public OrganizerProfile? Get(int id) => Collection().FindById(id);

    /// <summary>Inserts a new profile (Id 0) or updates an existing one, returning it with its
    /// assigned Id.</summary>
    public OrganizerProfile Save(OrganizerProfile profile)
    {
        if (profile.Id == 0)
        {
            int newId = Collection().Insert(profile).AsInt32;
            profile.Id = newId;
        }
        else
        {
            Collection().Update(profile);
        }

        return profile;
    }

    public void Delete(int id) => Collection().Delete(id);

    private ILiteCollection<OrganizerProfile> Collection() => _database.GetCollection<OrganizerProfile>(CollectionName);
}
