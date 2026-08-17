using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ContactFlowCRM.Models;
using ContactFlowCRM.Services;

namespace ContactFlowCRM
{
    public sealed class MainForm : Form
    {
        private readonly ContactStore _store = new();
        private List<Contact> _view = new(); // currently filtered/displayed subset (references into _store.Contacts)

        private readonly DataGridView _grid = new();
        private readonly TextBox _searchBox = new();
        private readonly Label _statusLabel = new();
        private readonly ToolStrip _toolbar = new();
        private readonly ProgressBar _progressBar = new();

        private CancellationTokenSource? _importCts;
        private System.Windows.Forms.Timer? _searchDebounce;

        public MainForm()
        {
            Text = "ContactFlow CRM";
            Width = 1100;
            Height = 700;
            StartPosition = FormStartPosition.CenterScreen;

            BuildToolbar();
            BuildGrid();
            BuildStatusBar();

            Load += async (_, _) => await InitialLoadAsync();
        }

        // ---------- UI construction ----------

        private void BuildToolbar()
        {
            _toolbar.Dock = DockStyle.Top;
            _toolbar.ImageScalingSize = new System.Drawing.Size(20, 20);

            var btnImport = new ToolStripButton("Import (any format)...") { DisplayStyle = ToolStripItemDisplayStyle.Text };
            btnImport.Click += async (_, _) => await ImportAsync();

            var btnExportAll = new ToolStripButton("Export All...") { DisplayStyle = ToolStripItemDisplayStyle.Text };
            btnExportAll.Click += (_, _) => Export(_store.Contacts);

            var btnExportView = new ToolStripButton("Export Filtered...") { DisplayStyle = ToolStripItemDisplayStyle.Text };
            btnExportView.Click += (_, _) => Export(_view);

            var btnStats = new ToolStripButton("Statistics (by city)") { DisplayStyle = ToolStripItemDisplayStyle.Text };
            btnStats.Click += (_, _) => ShowStatistics();

            var btnAdd = new ToolStripButton("Add Contact") { DisplayStyle = ToolStripItemDisplayStyle.Text };
            btnAdd.Click += (_, _) => AddContact();

            var btnTag = new ToolStripButton("Tag Selected...") { DisplayStyle = ToolStripItemDisplayStyle.Text };
            btnTag.Click += (_, _) => TagSelected();

            var btnDelete = new ToolStripButton("Delete Selected") { DisplayStyle = ToolStripItemDisplayStyle.Text };
            btnDelete.Click += (_, _) => DeleteSelected();

            var searchLabel = new ToolStripLabel("  Search:");
            var searchHost = new ToolStripControlHost(_searchBox) { Width = 260 };
            _searchBox.TextChanged += (_, _) => DebounceSearch();

            _toolbar.Items.AddRange(new ToolStripItem[]
            {
                btnImport, btnExportAll, btnExportView, btnStats, new ToolStripSeparator(),
                btnAdd, btnTag, btnDelete, new ToolStripSeparator(),
                searchLabel, searchHost
            });

            Controls.Add(_toolbar);
        }

        private void BuildGrid()
        {
            _grid.Dock = DockStyle.Fill;
            _grid.ReadOnly = true;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = true;
            _grid.VirtualMode = true; // key to staying fast with very large contact lists
            _grid.AutoGenerateColumns = false;
            _grid.RowHeadersVisible = false;
            _grid.EditMode = DataGridViewEditMode.EditProgrammatically;

            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Name", Width = 220 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Phone", HeaderText = "Phone", Width = 160 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Email", HeaderText = "Email", Width = 220 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Tags", HeaderText = "Tags", Width = 200 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Notes", HeaderText = "Notes", Width = 260 });

            _grid.CellValueNeeded += (_, e) =>
            {
                if (e.RowIndex < 0 || e.RowIndex >= _view.Count) return;
                var c = _view[e.RowIndex];
                e.Value = e.ColumnIndex switch
                {
                    0 => c.Name,
                    1 => c.Phone,
                    2 => c.Email,
                    3 => c.TagsDisplay,
                    4 => c.Notes,
                    _ => string.Empty
                };
            };

            _grid.CellDoubleClick += (_, e) =>
            {
                if (e.RowIndex >= 0 && e.RowIndex < _view.Count)
                    EditContact(_view[e.RowIndex]);
            };

            Controls.Add(_grid);
            _grid.BringToFront();
        }

        private void BuildStatusBar()
        {
            var statusStrip = new StatusStrip();
            var statusHost = new ToolStripControlHost(_statusLabel) { AutoSize = true };
            _statusLabel.Text = "Ready";
            _statusLabel.AutoSize = true;

            _progressBar.Width = 200;
            _progressBar.Visible = false;
            var progressHost = new ToolStripControlHost(_progressBar);

            statusStrip.Items.Add(statusHost);
            statusStrip.Items.Add(new ToolStripStatusLabel { Spring = true });
            statusStrip.Items.Add(progressHost);

            Controls.Add(statusStrip);
        }

        // ---------- Data operations ----------

        private async Task InitialLoadAsync()
        {
            SetStatus("Loading...");
            await _store.LoadAsync();
            RefreshView();
            SetStatus($"{_store.Contacts.Count:N0} contacts loaded");
        }

        private void RefreshView(string? filter = null)
        {
            filter = string.IsNullOrWhiteSpace(filter) ? _searchBox.Text : filter;

            if (string.IsNullOrWhiteSpace(filter))
            {
                _view = _store.Contacts;
            }
            else
            {
                var f = filter.Trim();
                _view = _store.Contacts.Where(c =>
                    (c.Name?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (c.Phone?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (c.Email?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    c.Tags.Any(t => t.Contains(f, StringComparison.OrdinalIgnoreCase))
                ).ToList();
            }

            _grid.RowCount = 0;
            _grid.RowCount = _view.Count;
            _grid.Invalidate();
            SetStatus($"{_view.Count:N0} / {_store.Contacts.Count:N0} contacts");
        }

        private void DebounceSearch()
        {
            _searchDebounce?.Stop();
            _searchDebounce?.Dispose();
            _searchDebounce = new System.Windows.Forms.Timer { Interval = 180 };
            _searchDebounce.Tick += (_, _) =>
            {
                _searchDebounce?.Stop();
                RefreshView();
            };
            _searchDebounce.Start();
        }

        private async Task ImportAsync()
        {
            using var ofd = new OpenFileDialog
            {
                Filter = "All supported files (*.csv;*.tsv;*.txt;*.json;*.xlsx)|*.csv;*.tsv;*.txt;*.json;*.xlsx|" +
                         "CSV (*.csv)|*.csv|TSV (*.tsv)|*.tsv|Text / phone list (*.txt)|*.txt|" +
                         "JSON (*.json)|*.json|Excel (*.xlsx)|*.xlsx|All files (*.*)|*.*",
                Title = "Import contacts (any format)"
            };
            if (ofd.ShowDialog(this) != DialogResult.OK) return;

            _importCts = new CancellationTokenSource();
            var progress = new Progress<int>(n => SetStatus($"Importing... {n:N0} rows read"));
            _progressBar.Visible = true;
            _progressBar.Style = ProgressBarStyle.Marquee;

            try
            {
                var (incoming, importStats) = await Task.Run(
                    () => ContactTableIO.Import(ofd.FileName, progress, _importCts.Token),
                    _importCts.Token);

                var (added, updated) = _store.MergeImport(incoming);
                importStats.Added = added;
                importStats.Updated = updated;
                await _store.SaveAsync();
                RefreshView();

                SetStatus($"Import complete: {added:N0} added, {updated:N0} updated, " +
                          $"{importStats.ValidPhone:N0} with valid phone, " +
                          $"{importStats.MissingOrInvalidPhone:N0} without a usable phone");
            }
            catch (OperationCanceledException)
            {
                SetStatus("Import cancelled");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Import failed: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("Import failed");
            }
            finally
            {
                _progressBar.Visible = false;
                _progressBar.Style = ProgressBarStyle.Blocks;
            }
        }

        private void Export(IReadOnlyCollection<Contact> contacts)
        {
            if (contacts.Count == 0)
            {
                MessageBox.Show(this, "Nothing to export.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var sfd = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv|TSV (*.tsv)|*.tsv|Text / phone list (*.txt)|*.txt|" +
                         "JSON (*.json)|*.json|Excel (*.xlsx)|*.xlsx",
                FileName = "contacts_export.csv"
            };
            if (sfd.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                var format = sfd.FilterIndex switch
                {
                    1 => TableFormat.Csv,
                    2 => TableFormat.Tsv,
                    3 => TableFormat.Txt,
                    4 => TableFormat.Json,
                    5 => TableFormat.Xlsx,
                    _ => ContactTableIO.DetectFormat(sfd.FileName)
                };
                ContactTableIO.Export(sfd.FileName, format, contacts);
                SetStatus($"Exported {contacts.Count:N0} contacts to {sfd.FileName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Export failed: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowStatistics()
        {
            var stats = _store.GetStatsBySource();
            using var dlg = new StatisticsDialog(stats, _store.Contacts.Count);
            dlg.ShowDialog(this);
        }

        private void AddContact()
        {
            using var dlg = new ContactEditDialog(new Contact());
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            _store.Contacts.Add(dlg.Result);
            _ = _store.SaveAsync();
            RefreshView();
        }

        private void EditContact(Contact contact)
        {
            using var dlg = new ContactEditDialog(contact);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            contact.UpdatedAtUtc = DateTime.UtcNow;
            _ = _store.SaveAsync();
            RefreshView();
        }

        private void TagSelected()
        {
            var selected = GetSelectedContacts();
            if (selected.Count == 0) return;

            using var dlg = new TagInputDialog();
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            var tag = dlg.Tag.Trim();
            if (tag.Length == 0) return;

            foreach (var c in selected)
            {
                if (!c.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                    c.Tags.Add(tag);
                c.UpdatedAtUtc = DateTime.UtcNow;
            }
            _ = _store.SaveAsync();
            RefreshView();
            SetStatus($"Tagged {selected.Count:N0} contacts with '{tag}'");
        }

        private void DeleteSelected()
        {
            var selected = GetSelectedContacts();
            if (selected.Count == 0) return;

            var confirm = MessageBox.Show(this,
                $"Delete {selected.Count:N0} selected contact(s)? This cannot be undone.",
                "Confirm delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;

            _store.Remove(selected.Select(c => c.Id));
            _ = _store.SaveAsync();
            RefreshView();
        }

        private List<Contact> GetSelectedContacts()
        {
            var result = new List<Contact>();
            foreach (DataGridViewRow row in _grid.SelectedRows)
            {
                if (row.Index >= 0 && row.Index < _view.Count)
                    result.Add(_view[row.Index]);
            }
            return result;
        }

        private void SetStatus(string text) => _statusLabel.Text = text;
    }
}
