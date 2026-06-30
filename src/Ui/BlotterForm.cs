using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using NoiseSnitch.AudioWatcher;
using NoiseSnitch.Model;

namespace NoiseSnitch.Ui;

/// <summary>
/// The M4 "blotter": a borderless tray flyout that lists the most recent
/// <see cref="NoiseEvent"/>s, newest first, with relative timestamps
/// ("3s ago — chrome"). It auto-refreshes while open — both on a short timer (so
/// the relative times keep ticking) and immediately whenever the
/// <see cref="EventStore"/> records a new onset — and shows a friendly empty
/// state when nothing has made noise yet.
///
/// Behaves like a typical tray flyout: it appears near the cursor/tray, sits
/// above other windows, and hides itself when it loses focus (click elsewhere)
/// rather than closing, so re-opening from the tray is instant.
/// </summary>
internal sealed class BlotterForm : Form
{
    private const int RowHeight = 44;
    private const int VisibleRows = 8;

    private static readonly Color BackColorDark = Color.FromArgb(32, 34, 38);
    private static readonly Color RowAltColor = Color.FromArgb(38, 40, 45);
    private static readonly Color PrimaryText = Color.FromArgb(240, 240, 240);
    private static readonly Color SecondaryText = Color.FromArgb(150, 152, 158);
    private static readonly Color AccentText = Color.FromArgb(255, 196, 0);

    private readonly EventStore _events;
    private readonly ListBox _list;
    private readonly Label _header;
    private readonly System.Windows.Forms.Timer _refreshTimer;

    // The snapshot currently rendered. Held so owner-draw can index into it.
    private IReadOnlyList<NoiseEvent> _current = Array.Empty<NoiseEvent>();

    public BlotterForm(EventStore events)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = BackColorDark;
        Width = 340;
        Height = RowHeight * VisibleRows + 40;
        TopMost = true;
        // Subtle rounded-ish border via a 1px padding frame.
        Padding = new Padding(1);

        _header = new Label
        {
            Dock = DockStyle.Top,
            Height = 32,
            Text = "noise-snitch · recent",
            ForeColor = AccentText,
            BackColor = BackColorDark,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
        };

        _list = new ListBox
        {
            Dock = DockStyle.Fill,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = RowHeight,
            BorderStyle = BorderStyle.None,
            BackColor = BackColorDark,
            ForeColor = PrimaryText,
            IntegralHeight = false,
            SelectionMode = SelectionMode.None,
            Font = new Font("Segoe UI", 9f),
        };
        _list.DrawItem += OnDrawItem;

        Controls.Add(_list);
        Controls.Add(_header);

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _refreshTimer.Tick += (_, _) => Reload();
    }

    /// <summary>
    /// Positions the flyout near the given screen point (typically the tray /
    /// cursor), clamped to the working area, then shows and refreshes it.
    /// </summary>
    public void ShowNear(Point anchor)
    {
        Rectangle wa = Screen.FromPoint(anchor).WorkingArea;

        // Prefer to sit up-and-left of the anchor (tray is bottom-right by default).
        int x = Math.Min(anchor.X, wa.Right - Width);
        int y = anchor.Y - Height;
        if (y < wa.Top)
        {
            y = Math.Min(anchor.Y, wa.Bottom - Height);
        }

        x = Math.Max(wa.Left, x);
        y = Math.Max(wa.Top, y);
        Location = new Point(x, y);

        Reload();
        _refreshTimer.Start();

        if (!Visible)
        {
            Show();
        }

        Activate();
        BringToFront();
    }

    /// <summary>Hides (does not dispose) and stops the refresh timer.</summary>
    public void HideFlyout()
    {
        _refreshTimer.Stop();
        if (Visible)
        {
            Hide();
        }
    }

    /// <summary>
    /// Hook the store so new onsets refresh the list immediately while the flyout
    /// is open. Marshals onto the UI thread since the watcher may add from its
    /// timer/STA tick.
    /// </summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _events.Added += OnEventAdded;
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _events.Added -= OnEventAdded;
        base.OnHandleDestroyed(e);
    }

    private void OnEventAdded(object? sender, NoiseEvent e)
    {
        if (!Visible)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(Reload));
        }
        else
        {
            Reload();
        }
    }

    // Flyout convention: vanish when focus moves elsewhere.
    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        HideFlyout();
    }

    /// <summary>Pulls the latest events and repaints. Cheap; safe to call often.</summary>
    private void Reload()
    {
        _current = _events.Recent();

        _list.BeginUpdate();
        try
        {
            // We don't store real items (owner-draw reads _current); just keep the
            // count in sync so the ListBox raises DrawItem the right number of times.
            int target = Math.Max(_current.Count, 1); // 1 => empty-state row
            while (_list.Items.Count > target)
            {
                _list.Items.RemoveAt(_list.Items.Count - 1);
            }
            while (_list.Items.Count < target)
            {
                _list.Items.Add(string.Empty);
            }
        }
        finally
        {
            _list.EndUpdate();
        }

        _header.Text = _current.Count == 0
            ? "noise-snitch · recent"
            : $"noise-snitch · recent ({_current.Count})";

        _list.Invalidate();
    }

    private void OnDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0)
        {
            return;
        }

        Rectangle b = e.Bounds;
        Color rowBg = (e.Index % 2 == 0) ? BackColorDark : RowAltColor;
        using (var bg = new SolidBrush(rowBg))
        {
            e.Graphics.FillRectangle(bg, b);
        }

        // Empty state: no events recorded yet.
        if (_current.Count == 0)
        {
            TextRenderer.DrawText(
                e.Graphics, BlotterFormatter.EmptyState, _list.Font, b,
                SecondaryText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        if (e.Index >= _current.Count)
        {
            return;
        }

        NoiseEvent ev = _current[e.Index];
        DateTime now = DateTime.UtcNow;

        var primaryRect = new Rectangle(b.X + 12, b.Y + 6, b.Width - 16, 20);
        var detailRect = new Rectangle(b.X + 12, b.Y + 24, b.Width - 16, 16);

        using var primaryFont = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        using var detailFont = new Font("Segoe UI", 8f, FontStyle.Regular);

        TextRenderer.DrawText(
            e.Graphics, BlotterFormatter.Line(ev, now), primaryFont, primaryRect,
            PrimaryText, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

        TextRenderer.DrawText(
            e.Graphics, BlotterFormatter.Detail(ev), detailFont, detailRect,
            SecondaryText, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    // Paint a faint 1px frame so the borderless flyout reads as a distinct panel.
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Color.FromArgb(70, 72, 78));
        var r = ClientRectangle;
        e.Graphics.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Dispose();
            _header.Dispose();
            _list.Dispose();
        }

        base.Dispose(disposing);
    }
}
