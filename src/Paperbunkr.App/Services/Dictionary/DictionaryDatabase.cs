using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace Paperbunkr.App.Services.Dictionary;

/// <summary>
/// The offline dictionary file (docs/superpowers/specs/2026-10-27-books-slice4b-dictionary-design.md §2): one SQLite file with
/// <c>entries(lemma, pos, sense_no, definition, example)</c> and <c>exceptions(form, lemma)</c>, built once from WordNet and then only read.
/// </summary>
public sealed class DictionaryDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    private DictionaryDatabase(SqliteConnection connection) => _connection = connection;

    public static void Build(string path, IEnumerable<DictionaryEntry> entries, IEnumerable<(string Form, string Lemma)> exceptions)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using (var create = connection.CreateCommand())
        {
            create.CommandText = """
                create table entries (lemma text not null, pos text not null, sense_no integer not null, definition text not null, example text);
                create table exceptions (form text not null, lemma text not null);
                """;
            create.ExecuteNonQuery();
        }

        using var transaction = connection.BeginTransaction();
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "insert into entries (lemma, pos, sense_no, definition, example) values ($lemma, $pos, $sense, $definition, $example)";
            var lemma = insert.Parameters.Add("$lemma", SqliteType.Text);
            var pos = insert.Parameters.Add("$pos", SqliteType.Text);
            var sense = insert.Parameters.Add("$sense", SqliteType.Integer);
            var definition = insert.Parameters.Add("$definition", SqliteType.Text);
            var example = insert.Parameters.Add("$example", SqliteType.Text);
            var counts = new Dictionary<(string, string), int>();
            foreach (var entry in entries)
            {
                var key = (entry.Lemma.ToLowerInvariant(), entry.Pos);
                counts[key] = counts.GetValueOrDefault(key) + 1;
                lemma.Value = key.Item1;
                pos.Value = entry.Pos;
                sense.Value = counts[key];
                definition.Value = entry.Definition;
                example.Value = (object?)entry.Example ?? DBNull.Value;
                insert.ExecuteNonQuery();
            }
        }

        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "insert into exceptions (form, lemma) values ($form, $lemma)";
            var form = insert.Parameters.Add("$form", SqliteType.Text);
            var lemma = insert.Parameters.Add("$lemma", SqliteType.Text);
            foreach (var (f, l) in exceptions)
            {
                form.Value = f.ToLowerInvariant();
                lemma.Value = l.ToLowerInvariant();
                insert.ExecuteNonQuery();
            }
        }

        using (var index = connection.CreateCommand())
        {
            index.Transaction = transaction;
            index.CommandText = "create index entries_lemma on entries (lemma); create index exceptions_form on exceptions (form);";
            index.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public static DictionaryDatabase Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        return new DictionaryDatabase(connection);
    }

    /// <summary>Senses of a lemma, parts of speech in noun, verb, adjective, adverb order and senses in WordNet's own (most common first) order.</summary>
    public IReadOnlyList<DictionaryEntry> Entries(string lemma)
    {
        var list = new List<DictionaryEntry>();
        using var command = _connection.CreateCommand();
        command.CommandText = """
            select lemma, pos, definition, example from entries where lemma = $lemma
            order by case pos when 'noun' then 0 when 'verb' then 1 when 'adjective' then 2 else 3 end, sense_no
            """;
        command.Parameters.AddWithValue("$lemma", lemma);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new DictionaryEntry(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return list;
    }

    public IReadOnlyList<string> Exceptions(string form)
    {
        var list = new List<string>();
        using var command = _connection.CreateCommand();
        command.CommandText = "select distinct lemma from exceptions where form = $form";
        command.Parameters.AddWithValue("$form", form);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(reader.GetString(0));
        }

        return list;
    }

    public DictionaryLookup AsLookup() => new(Entries, Exceptions);

    public void Dispose() => _connection.Dispose();
}
