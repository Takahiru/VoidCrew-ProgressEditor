using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace VoidCrewProgressEditor
{
    [Serializable] internal sealed class SavedGenePoint { public string id; public int level; }
    [Serializable] internal sealed class SavedGeneLoadout { public List<SavedGenePoint> points = new List<SavedGenePoint>(); }
    [Serializable] internal sealed class ProgressUndoEntry
    {
        public string savedUtc;
        public long xp;
        public int rank, favor;
        public List<SavedGeneLoadout> loadouts = new List<SavedGeneLoadout>();
    }
    [Serializable] internal sealed class ProgressUndoAccount
    {
        public string account;
        public List<ProgressUndoEntry> entries = new List<ProgressUndoEntry>();
    }
    [Serializable] internal sealed class ProgressUndoData
    {
        public int schema = 1;
        public List<ProgressUndoAccount> accounts = new List<ProgressUndoAccount>();
    }
    internal sealed class ProgressUndoHistory
    {
        private readonly string path;
        private readonly ProgressUndoData data;
        internal ProgressUndoHistory(string filename)
        {
            path = filename;
            data = File.Exists(path) ? JsonConvert.DeserializeObject<ProgressUndoData>(File.ReadAllText(path)) : new ProgressUndoData();
            if (data == null || data.schema != 1 || data.accounts == null)
                throw new InvalidOperationException("Rank undo history is invalid; edits disabled to preserve it.");
            var ids = new HashSet<string>();
            foreach (var account in data.accounts)
            {
                if (account == null || string.IsNullOrEmpty(account.account) || !ids.Add(account.account) || account.entries == null || account.entries.Count > 10)
                    throw new InvalidOperationException("Rank undo history contains an invalid account.");
                foreach (var entry in account.entries)
                {
                    if (entry == null || entry.rank < 0 || entry.rank > 30 || entry.xp < 0 || entry.favor < 0 || (entry.rank < 30 && entry.favor > 0) || entry.loadouts == null)
                        throw new InvalidOperationException("Rank undo history contains an invalid snapshot.");
                    foreach (var loadout in entry.loadouts)
                    {
                        if (loadout == null || loadout.points == null) throw new InvalidOperationException("Invalid gene snapshot.");
                        var genes = new HashSet<string>();
                        foreach (var point in loadout.points)
                            if (point == null || string.IsNullOrEmpty(point.id) || point.level < 0 || !genes.Add(point.id))
                                throw new InvalidOperationException("Invalid gene snapshot.");
                    }
                }
            }
        }
        private ProgressUndoAccount Find(string id)
        {
            if (string.IsNullOrEmpty(id)) throw new InvalidOperationException("Player account is not ready.");
            return data.accounts.Find(delegate(ProgressUndoAccount a) { return a.account == id; });
        }
        internal ProgressUndoEntry Peek(string account)
        {
            var a = Find(account);
            return a == null || a.entries.Count == 0 ? null : a.entries[a.entries.Count - 1];
        }
        internal void Push(string account, ProgressUndoEntry entry)
        {
            var a = Find(account);
            bool created = a == null;
            if (created) { a = new ProgressUndoAccount { account = account }; data.accounts.Add(a); }
            var previous = new List<ProgressUndoEntry>(a.entries);
            a.entries.Add(entry);
            if (a.entries.Count > 10) a.entries.RemoveAt(0);
            try { Save(); }
            catch { a.entries = previous; if (created) data.accounts.Remove(a); throw; }
        }
        internal void Pop(string account, ProgressUndoEntry expected)
        {
            var a = Find(account);
            if (a == null || a.entries.Count == 0 || !ReferenceEquals(Peek(account), expected))
                throw new InvalidOperationException("Rank undo history changed; entry was not removed.");
            a.entries.RemoveAt(a.entries.Count - 1);
            try { Save(); }
            catch { a.entries.Add(expected); throw; }
        }
        private void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(data, Formatting.Indented));
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
    }
}
