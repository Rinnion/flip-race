using System;
using System.Collections.Generic;
using System.Text;
using FlipRace.Core;
using UnityEngine;

namespace FlipRace.Presentation
{
    // A cache entry is only a replay candidate. Core checks compatibility and
    // plays it through the race before Presentation can use it.
    sealed class PrestartBotCache
    {
        internal const string StorageKey = "flip-race-prestart-bot-routes-v1";
        const string Header = "1\n";
        const int MaxStoredChars = 700 * 1024;
        const int MaxEncodedChars = ((PrestartBotPreparation.MaximumCacheCharacters + 2) / 3) * 4;
        const int MaxEntries = 24;
        readonly List<Entry> entries = new List<Entry>();

        sealed class Entry
        {
            internal string Key, Encoded, Text;
        }

        internal PrestartBotCache()
        {
            string stored;
            try { stored = PlayerPrefs.GetString(StorageKey, ""); }
            catch (Exception) { return; }
            if (string.IsNullOrEmpty(stored) || stored.Length > MaxStoredChars || !stored.StartsWith(Header, StringComparison.Ordinal)) return;
            foreach (string line in stored.Substring(Header.Length).Split('\n'))
            {
                if (entries.Count == MaxEntries) break;
                int separator = line.IndexOf('\t');
                if (separator <= 0 || separator > 256 || line.Length - separator > MaxEncodedChars + 1) continue;
                try
                {
                    string encoded = line.Substring(separator + 1);
                    string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                    if (decoded.Length != 0 && decoded.Length <= PrestartBotPreparation.MaximumCacheCharacters)
                        entries.Add(new Entry { Key = line.Substring(0, separator), Text = decoded, Encoded = encoded });
                }
                catch (FormatException) { }
            }
        }

        internal string Candidate(string key)
        {
            foreach (var entry in entries)
                if (entry.Key == key) return entry.Text;
            return null;
        }

        internal void Remember(string key, string route)
        {
            if (string.IsNullOrEmpty(route) || route.Length > PrestartBotPreparation.MaximumCacheCharacters || key.Length > 256) return;
            if (entries.Count > 0 && entries[0].Key == key && entries[0].Text == route) return;
            string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(route));
            if (encoded.Length > MaxEncodedChars) return;
            entries.RemoveAll(entry => entry.Key == key);
            entries.Insert(0, new Entry { Key = key, Encoded = encoded, Text = route });
            Save();
        }

        internal void Forget(string key)
        {
            if (entries.RemoveAll(entry => entry.Key == key) > 0) Save();
        }

        void Save()
        {
            var stored = new StringBuilder(Header);
            int count = 0;
            while (count < entries.Count && count < MaxEntries)
            {
                var entry = entries[count];
                if (stored.Length + entry.Key.Length + entry.Encoded.Length + 2 > MaxStoredChars) break;
                stored.Append(entry.Key).Append('\t').Append(entry.Encoded).Append('\n');
                count++;
            }
            entries.RemoveRange(count, entries.Count - count);
            try
            {
                PlayerPrefs.SetString(StorageKey, stored.ToString());
                PlayerPrefs.Save();
            }
            catch (Exception) { }
        }
    }
}
