using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using ClosedXML.Excel;
using ContactFlowCRM.Models;

namespace ContactFlowCRM.Services
{
    public enum TableFormat { Csv, Tsv, Txt, Json, Xlsx }

    /// <summary>
    /// Reads and writes real contact lists in whatever common format the user
    /// already has them in. Nothing here invents phone numbers - every value
    /// comes from a cell/line/field that existed in the user's own file.
    /// "Source" is set to the exact original file name (used later as the
    /// "city" grouping key in the statistics view, per the app's convention).
    /// </summary>
    public static class ContactTableIO
    {
        private static readonly Regex PhoneToken = new(@"[+]?\d[\d\-\s()]{5,}\d", RegexOptions.Compiled);

        public static TableFormat DetectFormat(string path)
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".tsv" => TableFormat.Tsv,
                ".txt" => TableFormat.Txt,
                ".json" => TableFormat.Json,
                ".xlsx" or ".xlsm" => TableFormat.Xlsx,
                _ => TableFormat.Csv // .csv and unknown extensions default to comma-separated
            };
        }

        // ---------------- IMPORT ----------------

        public static (List<Contact> contacts, ImportStats stats) Import(
            string path, IProgress<int>? progress, CancellationToken ct)
        {
            var format = DetectFormat(path);
            var fileName = Path.GetFileName(path);
            var stats = new ImportStats { FileName = fileName };
            List<Contact> result;

            switch (format)
            {
                case TableFormat.Xlsx:
                    result = ImportXlsx(path, fileName, stats, progress, ct);
                    break;
                case TableFormat.Json:
                    result = ImportJson(path, fileName, stats, ct);
                    break;
                case TableFormat.Tsv:
                    result = ImportDelimited(path, fileName, '\t', stats, progress, ct);
                    break;
                case TableFormat.Txt:
                    result = ImportFreeformTxt(path, fileName, stats, progress, ct);
                    break;
                default:
                    result = ImportDelimited(path, fileName, DetectDelimiter(path), stats, progress, ct);
                    break;
            }

            stats.RowsRead = result.Count + stats.MissingOrInvalidPhone;
            stats.ValidPhone = result.Count(c => !string.IsNullOrWhiteSpace(c.Phone));
            return (result, stats);
        }

        private static char DetectDelimiter(string path)
        {
            using var reader = new StreamReader(path, Encoding.UTF8, true);
            var firstLine = reader.ReadLine() ?? string.Empty;
            var candidates = new[] { ',', ';', '\t' };
            return candidates.OrderByDescending(c => firstLine.Count(ch => ch == c)).First();
        }

        private static List<Contact> ImportDelimited(
            string path, string fileName, char delimiter, ImportStats stats,
            IProgress<int>? progress, CancellationToken ct)
        {
            var results = new List<Contact>();
            using var reader = new StreamReader(path, Encoding.UTF8, true, 1 << 20);

            string? headerLine = reader.ReadLine();
            if (headerLine is null) return results;

            var headers = SplitLine(headerLine, delimiter);
            var idx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Count; i++) idx[headers[i].Trim()] = i;

            int iName = idx.GetValueOrDefault("name", idx.GetValueOrDefault("نام", -1));
            int iPhone = idx.GetValueOrDefault("phone", idx.GetValueOrDefault("number",
                idx.GetValueOrDefault("شماره", idx.GetValueOrDefault("موبایل", -1))));
            int iEmail = idx.GetValueOrDefault("email", idx.GetValueOrDefault("ایمیل", -1));
            int iTags = idx.GetValueOrDefault("tags", idx.GetValueOrDefault("برچسب", -1));
            int iNotes = idx.GetValueOrDefault("notes", idx.GetValueOrDefault("یادداشت", -1));

            // If the file has no phone column at all, fall back to free-form parsing per row.
            bool hasPhoneColumn = iPhone >= 0;

            string? line;
            int count = 0;
            while ((line = reader.ReadLine()) != null)
            {
                ct.ThrowIfCancellationRequested();
                if (line.Length == 0) continue;
                var fields = SplitLine(line, delimiter);

                var contact = new Contact { Source = fileName };
                if (hasPhoneColumn)
                {
                    contact.Name = Get(fields, iName);
                    contact.Phone = NormalizePhone(Get(fields, iPhone));
                    contact.Email = Get(fields, iEmail);
                    contact.Notes = Get(fields, iNotes);
                    var tagsRaw = Get(fields, iTags);
                    AddTags(contact, tagsRaw);
                }
                else
                {
                    FillFromFreeText(contact, string.Join(" ", fields));
                }

                count++;
                if (progress != null && count % 500 == 0) progress.Report(count);

                if (string.IsNullOrWhiteSpace(contact.Phone) && string.IsNullOrWhiteSpace(contact.Name)
                    && string.IsNullOrWhiteSpace(contact.Email))
                {
                    stats.MissingOrInvalidPhone++;
                    continue;
                }
                if (string.IsNullOrWhiteSpace(contact.Phone)) stats.MissingOrInvalidPhone++;

                results.Add(contact);
            }
            progress?.Report(count);
            return results;
        }

        private static List<Contact> ImportFreeformTxt(
            string path, string fileName, ImportStats stats, IProgress<int>? progress, CancellationToken ct)
        {
            var results = new List<Contact>();
            using var reader = new StreamReader(path, Encoding.UTF8, true, 1 << 20);
            string? line;
            int count = 0;

            // If the txt actually looks tabular (contains commas/tabs consistently), treat it as delimited.
            var firstNonEmpty = ReadFirstNonEmptyLine(path);
            if (firstNonEmpty != null && (firstNonEmpty.Contains(',') || firstNonEmpty.Contains('\t')))
            {
                var delim = firstNonEmpty.Contains('\t') ? '\t' : ',';
                return ImportDelimited(path, fileName, delim, stats, progress, ct);
            }

            while ((line = reader.ReadLine()) != null)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line)) continue;

                var contact = new Contact { Source = fileName };
                FillFromFreeText(contact, line);

                count++;
                if (progress != null && count % 500 == 0) progress.Report(count);

                if (string.IsNullOrWhiteSpace(contact.Phone) && string.IsNullOrWhiteSpace(contact.Name))
                {
                    stats.MissingOrInvalidPhone++;
                    continue;
                }
                if (string.IsNullOrWhiteSpace(contact.Phone)) stats.MissingOrInvalidPhone++;

                results.Add(contact);
            }
            progress?.Report(count);
            return results;
        }

        private static string? ReadFirstNonEmptyLine(string path)
        {
            using var reader = new StreamReader(path, Encoding.UTF8, true);
            string? l;
            while ((l = reader.ReadLine()) != null)
                if (!string.IsNullOrWhiteSpace(l)) return l;
            return null;
        }

        private static List<Contact> ImportJson(string path, string fileName, ImportStats stats, CancellationToken ct)
        {
            var results = new List<Contact>();
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);

            if (doc.RootElement.ValueKind != JsonValueKind.Array) return results;

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var contact = new Contact { Source = fileName };

                if (el.ValueKind == JsonValueKind.String)
                {
                    FillFromFreeText(contact, el.GetString() ?? string.Empty);
                }
                else if (el.ValueKind == JsonValueKind.Object)
                {
                    contact.Name = GetJsonString(el, "name", "Name", "نام");
                    contact.Phone = NormalizePhone(GetJsonString(el, "phone", "Phone", "number", "شماره"));
                    contact.Email = GetJsonString(el, "email", "Email", "ایمیل");
                    contact.Notes = GetJsonString(el, "notes", "Notes", "یادداشت");
                    var tags = GetJsonString(el, "tags", "Tags", "برچسب");
                    AddTags(contact, tags);
                }

                if (string.IsNullOrWhiteSpace(contact.Phone)) stats.MissingOrInvalidPhone++;
                if (!string.IsNullOrWhiteSpace(contact.Phone) || !string.IsNullOrWhiteSpace(contact.Name)
                    || !string.IsNullOrWhiteSpace(contact.Email))
                {
                    results.Add(contact);
                }
            }
            return results;
        }

        private static string GetJsonString(JsonElement el, params string[] keys)
        {
            foreach (var k in keys)
                if (el.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String)
                    return v.GetString() ?? string.Empty;
            return string.Empty;
        }

        private static List<Contact> ImportXlsx(
            string path, string fileName, ImportStats stats, IProgress<int>? progress, CancellationToken ct)
        {
            var results = new List<Contact>();
            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheets.First();
            var rows = ws.RowsUsed().ToList();
            if (rows.Count == 0) return results;

            var headerRow = rows[0];
            var idx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var cell in headerRow.CellsUsed())
                idx[cell.GetString().Trim()] = cell.Address.ColumnNumber;

            int cName = idx.GetValueOrDefault("name", idx.GetValueOrDefault("نام", -1));
            int cPhone = idx.GetValueOrDefault("phone", idx.GetValueOrDefault("number",
                idx.GetValueOrDefault("شماره", -1)));
            int cEmail = idx.GetValueOrDefault("email", idx.GetValueOrDefault("ایمیل", -1));
            int cTags = idx.GetValueOrDefault("tags", idx.GetValueOrDefault("برچسب", -1));
            int cNotes = idx.GetValueOrDefault("notes", idx.GetValueOrDefault("یادداشت", -1));

            int count = 0;
            for (int r = 1; r < rows.Count; r++)
            {
                ct.ThrowIfCancellationRequested();
                var row = rows[r];
                var contact = new Contact { Source = fileName };

                contact.Name = cName > 0 ? row.Cell(cName).GetString().Trim() : string.Empty;
                contact.Phone = cPhone > 0 ? NormalizePhone(row.Cell(cPhone).GetString().Trim()) : string.Empty;
                contact.Email = cEmail > 0 ? row.Cell(cEmail).GetString().Trim() : string.Empty;
                contact.Notes = cNotes > 0 ? row.Cell(cNotes).GetString().Trim() : string.Empty;
                if (cTags > 0) AddTags(contact, row.Cell(cTags).GetString().Trim());

                count++;
                if (progress != null && count % 500 == 0) progress.Report(count);

                if (string.IsNullOrWhiteSpace(contact.Phone)) stats.MissingOrInvalidPhone++;
                if (!string.IsNullOrWhiteSpace(contact.Phone) || !string.IsNullOrWhiteSpace(contact.Name))
                    results.Add(contact);
            }
            progress?.Report(count);
            return results;
        }

        private static void FillFromFreeText(Contact contact, string text)
        {
            var match = PhoneToken.Match(text);
            if (match.Success)
            {
                contact.Phone = NormalizePhone(match.Value);
                contact.Name = (text.Remove(match.Index, match.Length)).Trim(' ', ',', '-', '\t');
            }
            else
            {
                contact.Name = text.Trim();
            }
        }

        private static void AddTags(Contact contact, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            foreach (var t in raw.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
                contact.Tags.Add(t.Trim());
        }

        private static string Get(List<string> fields, int idx)
            => idx >= 0 && idx < fields.Count ? fields[idx].Trim() : string.Empty;

        private static string NormalizePhone(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var sb = new StringBuilder(raw.Length);
            foreach (var ch in raw)
                if (char.IsDigit(ch) || ch == '+') sb.Append(ch);
            return sb.ToString();
        }

        private static List<string> SplitLine(string line, char delimiter)
        {
            var result = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(c);
                }
                else
                {
                    if (c == '"') inQuotes = true;
                    else if (c == delimiter) { result.Add(sb.ToString()); sb.Clear(); }
                    else sb.Append(c);
                }
            }
            result.Add(sb.ToString());
            return result;
        }

        // ---------------- EXPORT ----------------

        public static void Export(string path, TableFormat format, IEnumerable<Contact> contacts)
        {
            switch (format)
            {
                case TableFormat.Xlsx: ExportXlsx(path, contacts); break;
                case TableFormat.Json: ExportJson(path, contacts); break;
                case TableFormat.Tsv: ExportDelimited(path, contacts, '\t'); break;
                case TableFormat.Txt: ExportTxt(path, contacts); break;
                default: ExportDelimited(path, contacts, ','); break;
            }
        }

        private static void ExportDelimited(string path, IEnumerable<Contact> contacts, char delimiter)
        {
            using var writer = new StreamWriter(path, false, Encoding.UTF8);
            writer.WriteLine(string.Join(delimiter, "name", "phone", "email", "tags", "notes"));
            foreach (var c in contacts)
                writer.WriteLine(string.Join(delimiter,
                    Escape(c.Name, delimiter), Escape(c.Phone, delimiter), Escape(c.Email, delimiter),
                    Escape(c.TagsDisplay, delimiter), Escape(c.Notes, delimiter)));
        }

        private static void ExportTxt(string path, IEnumerable<Contact> contacts)
        {
            using var writer = new StreamWriter(path, false, Encoding.UTF8);
            foreach (var c in contacts)
                writer.WriteLine(string.IsNullOrWhiteSpace(c.Name) ? c.Phone : $"{c.Name}\t{c.Phone}");
        }

        private static void ExportJson(string path, IEnumerable<Contact> contacts)
        {
            using var stream = File.Create(path);
            JsonSerializer.Serialize(stream, contacts.ToList(), new JsonSerializerOptions { WriteIndented = true });
        }

        private static void ExportXlsx(string path, IEnumerable<Contact> contacts)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Contacts");
            string[] headers = { "name", "phone", "email", "tags", "notes", "source" };
            for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];

            int row = 2;
            foreach (var c in contacts)
            {
                ws.Cell(row, 1).Value = c.Name;
                ws.Cell(row, 2).Value = c.Phone;
                ws.Cell(row, 3).Value = c.Email;
                ws.Cell(row, 4).Value = c.TagsDisplay;
                ws.Cell(row, 5).Value = c.Notes;
                ws.Cell(row, 6).Value = c.Source;
                row++;
            }
            ws.Columns().AdjustToContents();
            wb.SaveAs(path);
        }

        private static string Escape(string field, char delimiter)
        {
            if (field.Contains(delimiter) || field.Contains('"') || field.Contains('\n'))
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            return field;
        }
    }
}
