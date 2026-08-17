namespace ContactFlowCRM.Models
{
    /// <summary>
    /// Result summary of a single import operation, used to show the user
    /// exactly what happened (rows read, valid, invalid, duplicates, added).
    /// </summary>
    public sealed class ImportStats
    {
        public string FileName { get; set; } = string.Empty;
        public int RowsRead { get; set; }
        public int ValidPhone { get; set; }
        public int MissingOrInvalidPhone { get; set; }
        public int Added { get; set; }
        public int Updated { get; set; }
    }

    /// <summary>
    /// One row in the statistics table: "city" == the exact source file name
    /// each contact was imported from (matches the app's original convention
    /// of naming a batch after the file it came from).
    /// </summary>
    public sealed class SourceStat
    {
        public string SourceName { get; set; } = string.Empty; // e.g. "Tehran.csv"
        public int ContactCount { get; set; }
        public int WithPhone { get; set; }
        public int WithEmail { get; set; }
        public int WithTags { get; set; }
    }
}
