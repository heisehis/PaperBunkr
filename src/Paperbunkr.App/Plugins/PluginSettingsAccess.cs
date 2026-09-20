using System;
using System.Linq;
using System.Text;
using Paperbunkr.Data;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins;

namespace Paperbunkr.App.Plugins;

/// <summary>
/// Windows DPAPI as an <see cref="ISecretProtector"/> for plugin <c>secret</c> settings
/// (docs/superpowers/specs/2026-09-20-plugin-api-4-1-design.md §6.2). Built on the same
/// <see cref="DpapiSecrets"/> <see cref="CredentialStore"/> uses, with its own entropy so the two
/// stores' ciphertext isn't interchangeable. At-rest protection only: bound to this Windows user on
/// this machine, and a full-trust native plugin can still unprotect its own values.
/// </summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Paperbunkr.PluginSettings.v1");

    public static DpapiSecretProtector Instance { get; } = new();

    public bool IsAvailable => DpapiSecrets.IsSupported;

    public string Protect(string plaintext) => DpapiSecrets.Protect(plaintext, Entropy);

    public string? TryUnprotect(string stored) => DpapiSecrets.TryUnprotect(stored, Entropy);

    public bool IsProtected(string stored) => DpapiSecrets.IsProtected(stored);
}

/// <summary>
/// The schema-aware view of the per-plugin settings store (<see cref="PluginSettingState"/>), shared by
/// what a plugin sees through <c>GetSetting</c>/<c>SetSetting</c> and what the settings overlay shows and
/// edits (docs/superpowers/specs/2026-09-20-plugin-api-4-1-design.md §6.2). Storage is unchanged - one
/// string per key - so a plugin that declares no schema behaves exactly as it did before this existed.
/// <para>
/// For a key the plugin <em>declared</em>:
/// <list type="bullet">
/// <item><see cref="Get"/> is the sanitization layer: unset resolves to the declared default, and a stored
/// value that violates the schema (a bad choice, an out-of-range number) resolves to the default too, so a
/// plugin never sees a schema-violating value. The raw stored string is <b>never rewritten</b> by a read.</item>
/// <item>A <c>secret</c> is DPAPI-encrypted before it's written and decrypted on read; one that won't decrypt
/// (another Windows user or machine, corruption) reads as unset. Where DPAPI isn't available, a secret can't
/// be written (it fails closed rather than being stored as plain text). A secret found stored as plain text -
/// written before the schema existed - is encrypted in place the first time it's read.</item>
/// </list>
/// Undeclared keys are read and written verbatim.
/// </para>
/// </summary>
public sealed class PluginSettingsAccess
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly Func<string, PluginSettingsSchema?> _schemaFor;

    public PluginSettingsAccess(Func<PaperbunkrDbContext> contextFactory, Func<string, PluginSettingsSchema?> schemaFor, ISecretProtector protector)
    {
        _contextFactory = contextFactory;
        _schemaFor = schemaFor;
        Protector = protector;
    }

    public ISecretProtector Protector { get; }

    public PluginSettingsSchema? SchemaFor(string pluginKey) => _schemaFor(pluginKey);

    /// <summary>The value a plugin should see for <paramref name="key"/>.</summary>
    public string? Get(string pluginKey, string key)
    {
        string? raw = GetRaw(pluginKey, key);
        PluginSettingDefinition? definition = _schemaFor(pluginKey)?.Find(key);
        if (definition is null)
        {
            return raw;
        }

        if (definition.Type == PluginSettingType.Secret && raw is { Length: > 0 } && !Protector.IsProtected(raw) && Protector.IsAvailable)
        {
            WriteRaw(pluginKey, key, Protector.Protect(raw));
        }

        return PluginSettingsSchema.Resolve(definition, raw, Protector);
    }

    /// <summary>Stores <paramref name="value"/> for <paramref name="key"/>, encrypting it first if the plugin declared the key a secret.</summary>
    public void Set(string pluginKey, string key, string value)
    {
        PluginSettingDefinition? definition = _schemaFor(pluginKey)?.Find(key);
        if (definition?.Type == PluginSettingType.Secret)
        {
            if (!Protector.IsAvailable)
            {
                throw new InvalidOperationException($"Setting '{key}' is a secret, and secrets can't be stored safely on this platform.");
            }

            WriteRaw(pluginKey, key, value.Length == 0 ? value : Protector.Protect(value));
            return;
        }

        WriteRaw(pluginKey, key, value);
    }

    /// <summary>The stored string exactly as persisted (ciphertext for a secret), or null if never set. What the settings overlay flags when it isn't valid.</summary>
    public string? GetRaw(string pluginKey, string key)
    {
        using var context = _contextFactory();
        return context.PluginSettingStates
            .Where(s => s.PluginKey == pluginKey && s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefault();
    }

    /// <summary>
    /// What the settings overlay should show in the editor for <paramref name="definition"/>: the stored
    /// value as-is even when it's invalid (so the user can see and correct it - <see cref="Get"/> is what
    /// hides it from the plugin), the default when unset, and for a secret the decrypted text, or empty when
    /// it won't decrypt. <paramref name="unreadableSecret"/> is true in that last case.
    /// </summary>
    public string GetForEditing(string pluginKey, PluginSettingDefinition definition, out bool unreadableSecret)
    {
        unreadableSecret = false;
        string? raw = GetRaw(pluginKey, definition.Key);
        if (raw is null)
        {
            return definition.Default ?? string.Empty;
        }

        if (definition.Type == PluginSettingType.Secret && Protector.IsProtected(raw))
        {
            string? plain = Protector.TryUnprotect(raw);
            unreadableSecret = plain is null;
            return plain ?? string.Empty;
        }

        return raw;
    }

    private void WriteRaw(string pluginKey, string key, string value)
    {
        using var context = _contextFactory();
        var row = context.PluginSettingStates.FirstOrDefault(s => s.PluginKey == pluginKey && s.Key == key);
        if (row is null)
        {
            context.PluginSettingStates.Add(new PluginSettingState { PluginKey = pluginKey, Key = key, Value = value });
        }
        else
        {
            row.Value = value;
        }

        context.SaveChanges();
    }
}
