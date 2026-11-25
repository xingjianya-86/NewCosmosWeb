using System.Drawing;
using Forms = System.Windows.Forms;

namespace NewCosmos.Agent.Services;

/// <summary>
/// 托盘「绑定本机打印机」输入框：只收 6 位数字绑定码。
/// 无设计器，避免引入额外的 resx/设计器文件。
/// </summary>
public sealed class PrinterBindForm : Forms.Form
{
    private readonly Forms.TextBox _codeBox;
    private readonly Forms.Button _okButton;

    public PrinterBindForm()
    {
        Text = "绑定本机打印机";
        FormBorderStyle = Forms.FormBorderStyle.FixedDialog;
        StartPosition = Forms.FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        TopMost = true;
        Font = SystemFonts.MessageBoxFont;
        ClientSize = new Size(360, 164);

        var title = new Forms.Label
        {
            Text = "请输入 6 位绑定码：",
            AutoSize = true,
            Location = new Point(18, 18)
        };

        _codeBox = new Forms.TextBox
        {
            Location = new Point(18, 46),
            Size = new Size(324, 27),
            MaxLength = 6,
            TabIndex = 0
        };
        _codeBox.KeyPress += OnCodeKeyPress;

        var hint = new Forms.Label
        {
            Text = "绑定码由服务端生成，仅用于本次绑定。",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Location = new Point(18, 82)
        };

        _okButton = new Forms.Button
        {
            Text = "确定",
            DialogResult = Forms.DialogResult.None,
            Location = new Point(174, 118),
            Size = new Size(82, 28),
            TabIndex = 1,
            Enabled = false
        };
        _okButton.Click += (_, _) => Confirm();
        _codeBox.TextChanged += (_, _) => _okButton.Enabled = _codeBox.Text.Trim().Length == 6;

        var cancelButton = new Forms.Button
        {
            Text = "取消",
            DialogResult = Forms.DialogResult.Cancel,
            Location = new Point(262, 118),
            Size = new Size(82, 28),
            TabIndex = 2
        };

        AcceptButton = _okButton;
        CancelButton = cancelButton;

        Controls.Add(title);
        Controls.Add(_codeBox);
        Controls.Add(hint);
        Controls.Add(_okButton);
        Controls.Add(cancelButton);

        Shown += (_, _) => _codeBox.Focus();
    }

    /// <summary>用户输入的 6 位绑定码（已去首尾空白）。</summary>
    public string Code => _codeBox.Text.Trim();

    private void OnCodeKeyPress(object? sender, Forms.KeyPressEventArgs e)
    {
        if (char.IsControl(e.KeyChar))
            return;

        if (!char.IsAsciiDigit(e.KeyChar))
        {
            e.Handled = true;
            return;
        }

        // MaxLength 已限制长度；这里兜住粘贴/输入法带来的超长字符。
        if (_codeBox.Text.Length >= 6 && _codeBox.SelectionLength == 0)
            e.Handled = true;
    }

    private void Confirm()
    {
        if (!PrinterBindRunner.TryNormalizeCode(_codeBox.Text, out _))
        {
            Forms.MessageBox.Show(
                this,
                "绑定码必须是 6 位数字。",
                "绑定码格式错误",
                Forms.MessageBoxButtons.OK,
                Forms.MessageBoxIcon.Warning);
            _codeBox.Focus();
            _codeBox.SelectAll();
            return;
        }

        DialogResult = Forms.DialogResult.OK;
        Close();
    }
}
