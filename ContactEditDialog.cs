using System;
using System.Drawing;
using System.Windows.Forms;
using ContactFlowCRM.Models;

namespace ContactFlowCRM
{
    public sealed class ContactEditDialog : Form
    {
        private readonly TextBox _name = new();
        private readonly TextBox _phone = new();
        private readonly TextBox _email = new();
        private readonly TextBox _tags = new();
        private readonly TextBox _notes = new();

        public Contact Result { get; }

        public ContactEditDialog(Contact contact)
        {
            Result = contact;
            Text = "Contact";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            Width = 420;
            Height = 340;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Padding = new Padding(12),
                AutoSize = true
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            AddRow(layout, "Name", _name, contact.Name);
            AddRow(layout, "Phone", _phone, contact.Phone);
            AddRow(layout, "Email", _email, contact.Email);
            AddRow(layout, "Tags", _tags, contact.TagsDisplay);
            AddRow(layout, "Notes", _notes, contact.Notes);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 44
            };
            var ok = new Button { Text = "Save", DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
            ok.Click += (_, _) => Commit();
            buttonPanel.Controls.Add(ok);
            buttonPanel.Controls.Add(cancel);

            Controls.Add(layout);
            Controls.Add(buttonPanel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private static void AddRow(TableLayoutPanel layout, string label, TextBox box, string value)
        {
            box.Text = value;
            box.Width = 260;
            layout.RowCount++;
            layout.Controls.Add(new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, layout.RowCount - 1);
            layout.Controls.Add(box, 1, layout.RowCount - 1);
        }

        private void Commit()
        {
            Result.Name = _name.Text.Trim();
            Result.Phone = _phone.Text.Trim();
            Result.Email = _email.Text.Trim();
            Result.Notes = _notes.Text.Trim();
            Result.Tags.Clear();
            foreach (var t in _tags.Text.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
                Result.Tags.Add(t.Trim());
        }
    }

    public sealed class TagInputDialog : Form
    {
        private readonly TextBox _input = new();
        public string Tag => _input.Text;

        public TagInputDialog()
        {
            Text = "Add tag";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            Width = 320;
            Height = 140;

            _input.Dock = DockStyle.Top;
            _input.Margin = new Padding(12);

            var panel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44 };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
            panel.Controls.Add(ok);
            panel.Controls.Add(cancel);

            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
            host.Controls.Add(_input);

            Controls.Add(host);
            Controls.Add(panel);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
