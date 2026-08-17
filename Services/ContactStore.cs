using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ContactFlowCRM.Models;

namespace ContactFlowCRM.Services
{
    /// <summary>
    /// Simple, fast, local-only storage. No network calls, no external
    /// database server. Data lives in a single JSON file under the user's
    /// AppData folder so the app stays a true single-exe, no-install tool.
    /// </summary>
    public sealed class ContactStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false
        };

        public string FilePath { get; }
        public List<Contact> Contacts { get; private set; } = new();

        public ContactStore()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ContactFlowCRM");
            Directory.CreateDirectory(dir);
            FilePath = Path.Combine(dir, "contacts.json");
        }

        public async Task LoadAsync()
        {
            if (!File.Exists(FilePath))
            {
                Contacts = new List<Contact>();
                return;
            }

            await using var stream = File.OpenRead(FilePath);
            var loaded = await JsonSerializer.DeserializeAsync<List<Contact>>(stream, JsonOptions);
            Contacts = loaded ?? new List<Contact>();
        }

        public async Task SaveAsync()
        {
            var tmpPath = FilePath + ".tmp";
            await using (var stream = File.Create(tmpPath))
            {
                await JsonSerializer.SerializeAsync(stream, Contacts, JsonOptions);
            }
            File.Copy(tmpPath, FilePath, overwrite: true);
            File.Delete(tmpPath);
        }

        /// <summary>
        /// Merge-imports contacts, de-duplicating by phone number (last one wins for name/email/tags update).
        /// Returns (added, updated) counts.
        /// </summary>
        public (int added, int updated) MergeImport(IEnumerable<Contact> incoming)
        {
            var byPhone = Contacts
                .Where(c => !string.IsNullOrWhiteSpace(c.Phone))
                .ToDictionary(c => c.Phone, c => c);

            int added = 0, updated = 0;

            foreach (var c in incoming)
            {
                if (string.IsNullOrWhiteSpace(c.Phone))
                {
                    // No phone number - still add as a standalone contact.
                    Contacts.Add(c);
                    added++;
                    continue;
                }

                if (byPhone.TryGetValue(c.Phone, out var existing))
                {
                    if (!string.IsNullOrWhiteSpace(c.Name)) existing.Name = c.Name;
                    if (!string.IsNullOrWhiteSpace(c.Email)) existing.Email = c.Email;
                    foreach (var t in c.Tags)
                        if (!existing.Tags.Contains(t)) existing.Tags.Add(t);
                    if (!string.IsNullOrWhiteSpace(c.Notes)) existing.Notes = c.Notes;
                    existing.UpdatedAtUtc = DateTime.UtcNow;
                    updated++;
                }
                else
                {
                    Contacts.Add(c);
                    byPhone[c.Phone] = c;
                    added++;
                }
            }

            return (added, updated);
        }

        public void Remove(IEnumerable<string> ids)
        {
            var set = new HashSet<string>(ids);
            Contacts.RemoveAll(c => set.Contains(c.Id));
        }

        /// <summary>
        /// Groups contacts by their Source file name ("city" in the app's convention)
        /// and reports a small breakdown per group. No artificial cap on group count
        /// or size - every imported file shows up here.
        /// </summary>
        public List<SourceStat> GetStatsBySource()
        {
            return Contacts
                .GroupBy(c => string.IsNullOrWhiteSpace(c.Source) ? "(دستی / بدون فایل)" : c.Source)
                .Select(g => new SourceStat
                {
                    SourceName = g.Key,
                    ContactCount = g.Count(),
                    WithPhone = g.Count(c => !string.IsNullOrWhiteSpace(c.Phone)),
                    WithEmail = g.Count(c => !string.IsNullOrWhiteSpace(c.Email)),
                    WithTags = g.Count(c => c.Tags.Count > 0)
                })
                .OrderByDescending(s => s.ContactCount)
                .ToList();
        }
    }
}
