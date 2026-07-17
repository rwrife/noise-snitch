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
///
/// v0.2 (issue #7) adds "Mute-the-snitched": right-click a row to mute/unmute
/// that culprit's live audio session. The muting itself lives in the tray (which
/// owns the <c>SessionMuter</c> and the set of muted pids); the blotter just asks
/// via the injected callbacks and reflects muted rows (a 🔇 marker + struck-through
/// name).
/// </summary>
internal sealed class BlotterForm : Form
{
    private const int RowHeight = 44;
    private const int VisibleRows = 8;
    private const int IconSize = 24;
    private const int IconLeftPad = 10;
    private const int TextLeftPad = IconLeftPad + IconSize + 10; // text starts right of the icon

    private static readonly Color BackColorDark = Color.FromArgb(32, 34, 38);
    private static readonly Color RowAltColor = Color.FromArgb(38, 40, 45);
    private static readonly Color PrimaryText = Color.FromArgb(240, 240, 240);
    private static readonly Color SecondaryText = Color.FromArgb(150, 152, 158);
    private static readonly Color AccentText = Color.FromArgb(255, 196, 0);
    private static readonly Color IconPlaceholder = Color.FromArgb(80, 82, 90);
    private static readonly Color MutedText = Color.FromArgb(120, 122, 128);

    private readonly EventStore _events;
    private readonly AppIconProvider _icons = new(IconSize);
    private readonly ListBox _list;
    private readonly Label _header;
    private readonly System.Windows.Forms.Timer _refreshTimer;

    // v0.2 "Mute-the-snitched" (issue #7). The tray owns the actual muting; the
    // blotter asks whether a culprit is muted (to render it) and requests a
    // toggle when a row's context-menu item is clicked. Both default to no-ops so
    // the form still works (and stays testable) if muting isn't wired up.
    private readonly Func<NoiseEvent, bool> _isMuted;
    private readonly Func<NoiseEvent, MuteOutcome> _toggleMute;

    // The snapshot currently rendered. Held so owner-draw can index into it.
    private IReadOnlyList<NoiseEvent> _current = Array.Empty<NoiseEvent>();

    // Issue #24: the empty-state wording comes from the active personality pack.
    // Defaults to the neutral formatter constant so the form works stand-alone
    // (and in tests) without a personality wired up; the tray overrides it.
    private string _emptyStateText = BlotterFormatter.EmptyState;

    /// <summary>
    /// Issue #24: the message drawn when no events have been recorded yet. Set by
    /// the tray from the selected personality pack; a null/blank value snaps back
    /// to the default wording. Repaints if the form is visible.
    /// </summary>
    public string EmptyStateText
    {
        get => _emptyStateText;
        set
        {
            _emptyStateText = string.IsNullOrWhiteSpace(value)
                ? BlotterFormatter.EmptyState
                : value;
            if (IsHandleCreated)
            {
                _list.Invalidate();
            }
        }
    }

    /// <param name="events">The recent-events store this flyout renders.</param>
    /// <param name="isMuted">
    /// Returns whether the culprit behind an event is currently muted, so its row
    /// can be drawn struck-through. Optional; defaults to "never muted".
    /// </param>
    /// <param name="toggleMute">
    /// Mutes/unmutes the culprit behind an event and returns the outcome, invoked
    /// when the row's context-menu toggle is clicked. Optional; defaults to a no-op.
    /// </param>
    public BlotterForm(
        EventStore events,
        Func<NoiseEvent, bool>? isMuted = null,
        Func<NoiseEvent, MuteOutcome>? toggleMute = null)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _isMuted = isMuted ?? (_ => false);
        _toggleMute = toggleMute ?? (_ => MuteOutcome.NoSession);

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
        // v0.2: right-click a row to mute/unmute that culprit (issue #7).
        _list.MouseUp += OnListMouseUp;

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
    /// Issue #28: toggles the flyout for a global-hotkey press — if it's already
    /// visible, hide it; otherwise show it near <paramref name="anchor"/>. Lets the
    /// same shortcut both summon and dismiss the blotter.
    /// </summary>
    public void ToggleNear(Point anchor)
    {
        if (Visible)
        {
            HideFlyout();
        }
        else
        {
            ShowNear(anchor);
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
                e.Graphics, EmptyStateText, _list.Font, b,
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
        bool muted = SafeIsMuted(ev);

        DrawRowIcon(e.Graphics, b, ev);

        var primaryRect = new Rectangle(b.X + TextLeftPad, b.Y + 6, b.Width - TextLeftPad - 8, 20);
        var detailRect = new Rectangle(b.X + TextLeftPad, b.Y + 24, b.Width - TextLeftPad - 8, 16);

        // A muted culprit (v0.2 / issue #7) is drawn dimmed and struck through,
        // with a 🔇 marker, so the blotter reflects that you've silenced it.
        using var primaryFont = new Font("Segoe UI", 9.5f,
            muted ? FontStyle.Strikeout : FontStyle.Regular);
        using var detailFont = new Font("Segoe UI", 8f, FontStyle.Regular);

        string primary = BlotterFormatter.Line(ev, now);
        if (muted)
        {
            primary = "🔇 " + primary;
        }

        TextRenderer.DrawText(
            e.Graphics, primary, primaryFont, primaryRect,
            muted ? MutedText : PrimaryText, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

        TextRenderer.DrawText(
            e.Graphics, BlotterFormatter.Detail(ev), detailFont, detailRect,
            SecondaryText, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    /// <summary>
    /// v0.2 (issue #7): on right-click, work out which row (if any) is under the
    /// cursor and pop a context menu offering to mute/unmute that culprit. The
    /// system-sounds session is deliberately not offered (shared shell audio).
    /// </summary>
    private void OnListMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
        {
            return;
        }

        int index = _list.IndexFromPoint(e.Location);
        if (index < 0 || index >= _current.Count)
        {
            return;
        }

        NoiseEvent ev = _current[index];
        if (!MuteActionFormatter.CanOfferToggle(ev.ProcessId))
        {
            return;
        }

        bool muted = SafeIsMuted(ev);
        string label = MuteActionFormatter.ToggleLabel(ev.ProcessId, ev.ProcessName, muted);

        var menu = new ContextMenuStrip();
        menu.Items.Add(label, null, (_, _) => RequestToggle(ev));
        // Dispose the transient menu once it closes (item clicked or dismissed).
        // A ContextMenuStrip shown as a child of the list doesn't deactivate the
        // flyout, so the blotter stays up while the menu is open.
        menu.Closed += (_, _) => menu.Dispose();
        menu.Show(_list, e.Location);
    }

    /// <summary>
    /// Invokes the injected mute toggle for an event, then repaints so the row's
    /// struck-through/marker state updates immediately. Exceptions from the
    /// callback are swallowed to a debug note — a failed mute must never take down
    /// the flyout — and the outcome is surfaced via the callback owner (tray).
    /// </summary>
    private void RequestToggle(NoiseEvent ev)
    {
        try
        {
            _toggleMute(ev);
        }
        catch (Exception)
        {
            // The tray-owned callback already logs/handles failure; never crash the UI.
        }

        _list.Invalidate();
    }

    /// <summary>
    /// Asks the injected predicate whether a culprit is muted, treating any thrown
    /// exception as "not muted" so a rendering pass can never fail.
    /// </summary>
    private bool SafeIsMuted(NoiseEvent ev)
    {
        try
        {
            return _isMuted(ev);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Paints the per-app icon (M5) at the left of a row, vertically centered.
    /// Falls back to a small muted dot when no icon could be resolved (system
    /// sounds, exited/locked-down process, or extraction failure) so rows stay
    /// visually aligned whether or not art is available.
    /// </summary>
    private void DrawRowIcon(Graphics g, Rectangle rowBounds, NoiseEvent ev)
    {
        int iconY = rowBounds.Y + (rowBounds.Height - IconSize) / 2;
        var iconRect = new Rectangle(rowBounds.X + IconLeftPad, iconY, IconSize, IconSize);

        Image? icon = _icons.Get(ev.ExecutablePath);
        if (icon is not null)
        {
            g.DrawImage(icon, iconRect);
            return;
        }

        // Fallback: a small centered dot as a neutral placeholder.
        const int dot = 8;
        var dotRect = new Rectangle(
            iconRect.X + (IconSize - dot) / 2,
            iconRect.Y + (IconSize - dot) / 2,
            dot, dot);
        using var brush = new SolidBrush(IconPlaceholder);
        g.FillEllipse(brush, dotRect);
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
            _icons.Dispose();
            _header.Dispose();
            _list.Dispose();
        }

        base.Dispose(disposing);
    }
}
