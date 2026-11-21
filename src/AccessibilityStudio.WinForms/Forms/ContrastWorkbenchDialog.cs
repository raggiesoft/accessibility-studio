// ================================================================================
// FILE: Forms/ContrastWorkbenchDialog.cs
// PROJECT: AccessibilityStudio.WinForms
// LICENSE: MIT License
// ================================================================================
// PLAIN-ENGLISH OVERVIEW (100-LEVEL WINFORMS DIALOG GUIDE):
//   Interactive WCAG 2.1 / 2.2 Color Contrast & CSS Token Calculator dialog
//   accessible from `Tools -> Color Contrast Workbench...` (`Ctrl+Shift+C`).
//   Lets engineers test Foreground/Background hex pairs live, preview Normal &
//   Large sample text, check AA (`4.5:1` / `3.0:1`) and AAA (`7.0:1` / `4.5:1`)
//   badges, and click "Auto-Tune Nearest AA Color" to solve failing contrast!
// ================================================================================

using System.Drawing;
using System.Windows.Forms;
using AccessibilityStudio.Core.Analysis;
using Microsoft.Web.WebView2.Core;

namespace AccessibilityStudio.WinForms.Forms;

public sealed class ContrastWorkbenchDialog : Form
{
    private readonly CoreWebView2PreferredColorScheme _colorScheme;
    private readonly TextBox _txtForeground = new() { Text = "#6F7785", Width = 130, AccessibleName = "Foreground Hex Color" };
    private readonly TextBox _txtBackground = new() { Text = "#FFFFFF", Width = 130, AccessibleName = "Background Hex Color" };
    private readonly Label _lblRatio = new() { AutoSize = true, Font = new Font("Segoe UI", 16F, FontStyle.Bold) };
    private readonly Label _lblVerdicts = new() { AutoSize = true, Font = new Font("Segoe UI", 9.5F, FontStyle.Regular) };
    private readonly Panel _previewPanel = new() { Height = 92, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(14) };
    private readonly Label _lblPreviewSample = new() { Dock = DockStyle.Fill, Text = "Sample Heading (18pt Bold) — The quick brown fox jumps over the lazy dog (14px regular body copy).", Font = new Font("Segoe UI", 11F, FontStyle.Regular) };

    public ContrastWorkbenchDialog(CoreWebView2PreferredColorScheme colorScheme = CoreWebView2PreferredColorScheme.Auto)
    {
        _colorScheme = colorScheme;

        Text = "WCAG 2.2 Color Contrast & CSS Token Workbench";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(580, 390);
        MinimumSize = new Size(520, 360);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        // Enable standard Windows title-bar `?` Help button and `F1` context-sensitive Help!
        HelpButton = true;
        HelpRequested += (_, hlpevent) =>
        {
            hlpevent.Handled = true;
            OpenWorkbenchHelp();
        };

        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 5
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        FlowLayoutPanel inputsRow = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        Button btnPickFg = new() { Text = "Pick…", AutoSize = true };
        Button btnPickBg = new() { Text = "Pick…", AutoSize = true };
        Button btnSwap = new() { Text = "⇄ Swap", AutoSize = true };
        Button btnAutoFix = new() { Text = "⚡ Auto-Tune Nearest 4.5:1 AA Hex", AutoSize = true };

        btnPickFg.Click += (_, _) => PickColorInto(_txtForeground);
        btnPickBg.Click += (_, _) => PickColorInto(_txtBackground);
        btnSwap.Click += (_, _) =>
        {
            (_txtForeground.Text, _txtBackground.Text) = (_txtBackground.Text, _txtForeground.Text);
            Recalculate();
        };
        btnAutoFix.Click += (_, _) =>
        {
            if (ColorContrastEngine.TryParseCssColor(_txtForeground.Text, out RgbColor fg) &&
                ColorContrastEngine.TryParseCssColor(_txtBackground.Text, out RgbColor bg))
            {
                RgbColor tuned = ColorContrastEngine.SuggestNearestPassingForeground(fg, bg, 4.55);
                _txtForeground.Text = tuned.ToHex();
                Recalculate();
            }
        };

        _txtForeground.TextChanged += (_, _) => Recalculate();
        _txtBackground.TextChanged += (_, _) => Recalculate();

        inputsRow.Controls.Add(new Label { Text = "Foreground:", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        inputsRow.Controls.Add(_txtForeground);
        inputsRow.Controls.Add(btnPickFg);
        inputsRow.Controls.Add(new Label { Text = "   Background:", AutoSize = true, Padding = new Padding(8, 6, 4, 0) });
        inputsRow.Controls.Add(_txtBackground);
        inputsRow.Controls.Add(btnPickBg);

        FlowLayoutPanel toolsRow = new() { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 6, 0, 8) };
        Button btnHelp = new() { Text = "❓ Luminance Help (F1)", AutoSize = true };
        btnHelp.Click += (_, _) => OpenWorkbenchHelp();
        toolsRow.Controls.Add(btnSwap);
        toolsRow.Controls.Add(btnAutoFix);
        toolsRow.Controls.Add(btnHelp);

        _previewPanel.Controls.Add(_lblPreviewSample);

        FlowLayoutPanel metricsPanel = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, Padding = new Padding(0, 8, 0, 8) };
        metricsPanel.Controls.Add(_lblRatio);
        metricsPanel.Controls.Add(_lblVerdicts);

        Button btnClose = new() { Text = "Close", DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right, Width = 96 };
        AcceptButton = btnClose;
        CancelButton = btnClose;

        root.Controls.Add(inputsRow, 0, 0);
        root.Controls.Add(toolsRow, 0, 1);
        root.Controls.Add(_previewPanel, 0, 2);
        root.Controls.Add(metricsPanel, 0, 3);
        root.Controls.Add(btnClose, 0, 4);

        Controls.Add(root);
        Recalculate();
    }

    private void PickColorInto(TextBox targetBox)
    {
        using ColorDialog dlg = new() { FullOpen = true };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            targetBox.Text = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
        }
    }

    private void Recalculate()
    {
        if (!ColorContrastEngine.TryParseCssColor(_txtForeground.Text, out RgbColor fg) ||
            !ColorContrastEngine.TryParseCssColor(_txtBackground.Text, out RgbColor bg))
        {
            _lblRatio.Text = "Enter valid CSS Hex (#RRGGBB) or rgb(r,g,b)";
            _lblVerdicts.Text = string.Empty;
            return;
        }

        ContrastEvaluation eval = ColorContrastEngine.Evaluate(fg, bg);
        _previewPanel.BackColor = Color.FromArgb(bg.R, bg.G, bg.B);
        _lblPreviewSample.ForeColor = Color.FromArgb(fg.R, fg.G, fg.B);

        _lblRatio.Text = $"Contrast Ratio: {eval.Ratio:F2} : 1";
        _lblVerdicts.Text =
            $"Normal Text AA (4.5:1): {(eval.PassesNormalAA ? "PASS" : "FAIL")}   |   " +
            $"Normal Text AAA (7.0:1): {(eval.PassesNormalAAA ? "PASS" : "FAIL")}   |   " +
            $"Large Text & UI 1.4.11 (3.0:1): {(eval.PassesLargeTextOrUiAA ? "PASS" : "FAIL")}";
    }

    private void OpenWorkbenchHelp()
    {
        using HelpViewerForm helpDlg = new(_colorScheme, "contrast-workbench");
        helpDlg.ShowDialog(this);
    }
}
