using System;
using System.Collections.Generic;

namespace ContactFlowCRM.Models
{
    /// <summary>
    /// A single real contact record. No phone-number generation logic exists
    /// anywhere in this project - every record here must originate from a
    /// file the user imports (their own real contact list).
    /// </summary>
    public sealed class Contact
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new();
        public string Notes { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty; // e.g. filename it was imported from
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public string TagsDisplay => Tags.Count == 0 ? string.Empty : string.Join(", ", Tags);
    }
}
