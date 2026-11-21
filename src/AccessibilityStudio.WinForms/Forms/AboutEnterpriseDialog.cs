// ================================================================================
// FILE: Forms/AboutEnterpriseDialog.cs
// PROJECT: AccessibilityStudio.WinForms
// LICENSE: MIT License
// ================================================================================
// PLAIN-ENGLISH OVERVIEW (100-LEVEL LEGAL & ENTERPRISE ABOUT DIALOG):
//   Displays version information, MIT open-source license notice, Third-Party
//   component attributions (WebView2, Monaco Editor, MPL-2.0 compatibility note),
//   and RaggieSoft's mandatory Fictitious Entities Notice for sample fixtures.
// ================================================================================

using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace AccessibilityStudio.WinForms.Forms;

public sealed class AboutEnterpriseDialog : Form
{
    public AboutEnterpriseDialog()
    {
        Text = "About RaggieSoft Accessibility Studio";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(620, 440);
        MinimumSize = new Size(560, 400);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 4
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Label lblTitle = new()
        {
            Text = "RaggieSoft Accessibility Studio — Enterprise Edition v1.0.0",
            AutoSize = true,
            Font = new Font("Segoe UI", 13F, FontStyle.Bold)
        };

        Label lblSub = new()
        {
            Text = "Source-Level WCAG 2.2 AA & Revised Section 508 Web Engineering IDE • Created by Michael P. Ragsdale",
            AutoSize = true,
            Padding = new Padding(0, 4, 0, 10)
        };

        TextBox txtLegal = new()
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            AccessibleName = "Software License, Third-Party Attributions, and Legal Notices",
            Text =
                "OPEN-SOURCE CODE LICENSE (MIT):\r\n" +
                "Copyright © 2026 Michael P. Ragsdale (RaggieSoft Media). Source code is licensed under the MIT License.\r\n\r\n" +
                "THIRD-PARTY COMPONENT ATTRIBUTIONS:\r\n" +
                "• .NET 10 Runtime & Windows Forms — Copyright © .NET Foundation (MIT License)\r\n" +
                "• Microsoft Edge WebView2 SDK — Copyright © Microsoft Corporation (BSD License)\r\n" +
                "• Monaco Editor — Copyright © Microsoft Corporation (MIT License)\r\n" +
                "• Optional Rule-Pack Compatibility: Unmodified Deque axe-core rule bundles placed in RulePacks/ remain governed by Mozilla Public License 2.0 (MPL-2.0).\r\n\r\n" +
                "FICTITIOUS ENTITIES DISCLAIMER:\r\n" +
                "While RaggieSoft Accessibility Studio is real engineering software by Michael P. Ragsdale, any sample organizational names, universities (CPI, VDU), corporations (OmniCorp, Apex Records, Prairie Coachworks, Sainte-Claire), transit agencies (BRT, CVTA), or entities bearing 'LLC', 'Trust', or 'Holdings' in sample test fixtures across the RaggieSoft ecosystem are works of fiction. Any resemblance to actual persons or real-world entities is purely coincidental."
        };

        FlowLayoutPanel footer = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(0, 10, 0, 0)
        };

        Button btnOk = new() { Text = "OK", DialogResult = DialogResult.OK, Width = 96 };
        Button btnWebsite = new() { Text = "Open Product Portal (a11y.raggiesoft.com)", AutoSize = true };
        btnWebsite.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://a11y.raggiesoft.com") { UseShellExecute = true });
            }
            catch { }
        };

        footer.Controls.Add(btnOk);
        footer.Controls.Add(btnWebsite);

        AcceptButton = btnOk;
        CancelButton = btnOk;

        layout.Controls.Add(lblTitle, 0, 0);
        layout.Controls.Add(lblSub, 0, 1);
        layout.Controls.Add(txtLegal, 0, 2);
        layout.Controls.Add(footer, 0, 3);
        Controls.Add(layout);
    }
}
