using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using ContactFlowCRM.Models;

namespace ContactFlowCRM
{
    public sealed class StatisticsDialog : Form
    {
        public StatisticsDialog(List<SourceStat> stats, int totalContacts)
        {
            Text = "آمار بر اساس فایل (شهر)";
            StartPosition = FormStartPosition.CenterParent;
            Width = 640;
            Height = 480;

            var summary = new Label
            {
                Dock = DockStyle.Top,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = $"مجموع مخاطبین: {totalContacts:N0}   |   تعداد فایل/شهر: {stats.Count:N0}",
                Font = new Font(Font, FontStyle.Bold)
            };

            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AutoGenerateColumns = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };

            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "City", HeaderText = "شهر / نام فایل", Width = 220, DataPropertyName = "SourceName" });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Count", HeaderText = "تعداد مخاطب", Width = 110, DataPropertyName = "ContactCount" });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Phone", HeaderText = "دارای شماره", Width = 110, DataPropertyName = "WithPhone" });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Email", HeaderText = "دارای ایمیل", Width = 110, DataPropertyName = "WithEmail" });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Tags", HeaderText = "دارای برچسب", Width = 110, DataPropertyName = "WithTags" });

            grid.DataSource = stats;

            Controls.Add(grid);
            Controls.Add(summary);
        }
    }
}
