using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace OmsiMapEditor.Setup
{
    /// <summary>
    /// The setup's window in the editor's design (ADR 055, CorporateDesign): a borderless dark window with its own title
    /// bar, the --mf colour tokens, Inter and the logo. It installs when the program carries MapWorks and uninstalls
    /// when it is the uninstaller in the program folder.
    /// </summary>
    internal sealed class SetupForm : Form
    {
        private enum Page
        {
            Choose,
            Working,
            Done,
            Failed
        }

        private const int LogicalWidth = 720;
        private const int LogicalHeight = 470;
        private const int TitleHeight = 40;
        private const int FooterHeight = 64;
        private const int Edge = 40;

        private readonly bool installing;
        private readonly string version = SetupInstallation.Version;
        private readonly string installedVersion = SetupInstallation.InstalledVersion;
        private readonly Image logo = Theme.Logo();
        private readonly FlatButton closeButton;
        private readonly FlatButton browseButton;
        private readonly FlatButton secondaryButton;
        private readonly FlatButton primaryButton;
        private readonly FieldBox folderField;
        private readonly CheckOption startMenuOption;
        private readonly CheckOption desktopOption;
        private readonly CheckOption launchOption;
        private Page page = Page.Choose;
        private string folder;
        private string footerMessage;
        private string failure;
        private double progress;
        private string progressText = string.Empty;
        private bool busy;
        private bool sample;

        public SetupForm(bool install)
        {
            installing = install;
            Text = installing ? "OMSI MapWorks Setup" : "Uninstall OMSI MapWorks";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Theme.S(LogicalWidth), Theme.S(LogicalHeight));
            BackColor = Theme.Background;
            ForeColor = Theme.TextPrimary;
            Font = Theme.Regular(13);
            DoubleBuffered = true;
            KeyPreview = true;
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (ArgumentException)
            {
            }

            closeButton = new FlatButton(FlatButton.Kind.Close, string.Empty, Theme.Panel) { Bounds = Scaled(LogicalWidth - 46, 0, 46, TitleHeight), TabStop = false };
            closeButton.Click += (sender, args) => Close();
            folderField = new FieldBox(Theme.Background) { Bounds = Scaled(Edge, 300, 524, 34) };
            browseButton = new FlatButton(FlatButton.Kind.Secondary, "Browse...", Theme.Background) { Bounds = Scaled(Edge + 534, 300, 106, 34) };
            browseButton.Click += (sender, args) => Browse();
            startMenuOption = new CheckOption("Start menu shortcut", Theme.Background) { Checked = true, Bounds = Scaled(Edge, 368, 200, 24) };
            desktopOption = new CheckOption("Desktop shortcut", Theme.Background) { Checked = true, Bounds = Scaled(Edge + 210, 368, 200, 24) };
            launchOption = new CheckOption("Start OMSI MapWorks now", Theme.Background) { Checked = true, Bounds = Scaled(Edge, 300, 300, 24) };
            var footerTop = LogicalHeight - FooterHeight;
            secondaryButton = new FlatButton(FlatButton.Kind.Secondary, "Cancel", Theme.Panel) { Bounds = Scaled(LogicalWidth - Edge - 230, footerTop + 15, 100, 34) };
            primaryButton = new FlatButton(installing ? FlatButton.Kind.Primary : FlatButton.Kind.Danger, string.Empty, Theme.Panel)
            {
                Bounds = Scaled(LogicalWidth - Edge - 120, footerTop + 15, 120, 34)
            };
            secondaryButton.Click += (sender, args) => Close();
            primaryButton.Click += (sender, args) => Primary();
            Controls.AddRange(new Control[] { closeButton, folderField, browseButton, startMenuOption, desktopOption, launchOption, secondaryButton, primaryButton });

            folder = installing ? SetupInstallation.DefaultFolder : Path.GetDirectoryName(Application.ExecutablePath);
            folderField.TextBox.Text = folder;
            folderField.TextBox.TextChanged += (sender, args) =>
            {
                footerMessage = null;
                Invalidate();
            };
            ShowPage(Page.Choose);
        }

        /// <summary>Shows a page as it looks while working, for a screenshot of the design.</summary>
        public void ShowSample(string name)
        {
            sample = true;
            switch (name)
            {
                case "working":
                    progress = 0.42;
                    progressText = "OMSIMapWorks_Data/sharedassets0.assets";
                    ShowPage(Page.Working);
                    break;
                case "done":
                    ShowPage(Page.Done);
                    break;
                case "failed":
                    failure = "The folder could not be written: access is denied.";
                    ShowPage(Page.Failed);
                    break;
                case "message":
                    footerMessage = "OMSI MapWorks is running from this folder. Close it first.";
                    Invalidate();
                    break;
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                // A shadow around the borderless window.
                const int dropShadow = 0x20000;
                var parameters = base.CreateParams;
                parameters.ClassStyle |= dropShadow;
                return parameters;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Rounded corners on Windows 11; earlier versions ignore it.
            var round = 2;
            try
            {
                DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Half-copied files help nobody: the window stays until the work is done.
            if (busy)
                e.Cancel = true;
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // The uninstaller removes its own folder once it has quit, however the window was closed.
            if (!installing && page == Page.Done && !sample)
                SetupInstallation.RemoveAfterExit(folder);
            base.OnFormClosed(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && !busy)
                Close();
            base.OnKeyDown(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            // The title bar moves the window.
            if (e.Button == MouseButtons.Left && e.Y < Theme.S(TitleHeight))
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var width = ClientSize.Width;
            var height = ClientSize.Height;

            // Title bar and footer as raised panels, like the editor's menu and status bars.
            using (var panel = new SolidBrush(Theme.Panel))
            {
                g.FillRectangle(panel, 0, 0, width, Theme.S(TitleHeight));
                g.FillRectangle(panel, 0, height - Theme.S(FooterHeight), width, Theme.S(FooterHeight));
            }
            using (var line = new Pen(Theme.BorderSubtle))
            {
                g.DrawLine(line, 0, Theme.S(TitleHeight) - 1, width, Theme.S(TitleHeight) - 1);
                g.DrawLine(line, 0, height - Theme.S(FooterHeight), width, height - Theme.S(FooterHeight));
            }
            using (var border = new Pen(Theme.Border))
                g.DrawRectangle(border, 0, 0, width - 1, height - 1);
            Theme.Text(g, Text, Theme.SemiBold(13), Theme.TextSecondary, Scaled(16, 0, 400, TitleHeight), false, StringAlignment.Center);

            // The logo, then the page.
            var logoHeight = page == Page.Choose ? 104 : 120;
            var logoWidth = logo == null ? 0 : logoHeight * logo.Width / logo.Height;
            if (logo != null)
                g.DrawImage(logo, Scaled((LogicalWidth - logoWidth) / 2, 60, logoWidth, logoHeight));
            var top = page == Page.Choose ? 184 : 204;
            var heading = Heading();
            Theme.Text(g, heading, Theme.SemiBold(20), page == Page.Failed ? Theme.Error : Theme.TextPrimary, Scaled(Edge, top, LogicalWidth - 2 * Edge, 30), false, StringAlignment.Near);
            Theme.Text(g, Description(), Theme.Regular(13), Theme.TextSecondary, Scaled(Edge, top + 34, LogicalWidth - 2 * Edge, 60), true, StringAlignment.Near);

            if (page == Page.Choose && installing)
            {
                Theme.Text(g, "Install to", Theme.Regular(12), Theme.TextSecondary, Scaled(Edge, 278, 300, 18), false, StringAlignment.Near);
                Theme.Text(g, "Installs for you alone and needs no administrator rights. Updates go into the same folder.", Theme.Regular(11), Theme.TextMuted,
                    Scaled(Edge, 340, LogicalWidth - 2 * Edge, 18), false, StringAlignment.Near);
            }
            if (page == Page.Working)
            {
                var track = Scaled(Edge, 290, LogicalWidth - 2 * Edge, 6);
                using (var brush = new SolidBrush(Theme.Field))
                using (var path = Theme.Rounded(track, Theme.S(3)))
                    g.FillPath(brush, path);
                var fill = new Rectangle(track.X, track.Y, (int)Math.Round(track.Width * Math.Max(0, Math.Min(1, progress))), track.Height);
                if (fill.Width > 0)
                    using (var brush = new SolidBrush(Theme.Accent))
                    using (var path = Theme.Rounded(fill, Theme.S(3)))
                        g.FillPath(brush, path);
                Theme.Text(g, progressText, Theme.Regular(11), Theme.TextMuted, Scaled(Edge, 306, LogicalWidth - 2 * Edge - 60, 18), false, StringAlignment.Near);
                Theme.Text(g, ((int)Math.Round(progress * 100)) + " %", Theme.Regular(11), Theme.TextSecondary, Scaled(LogicalWidth - Edge - 60, 306, 60, 18), false,
                    StringAlignment.Near, StringAlignment.Far);
            }

            // Messages for the step at hand go left of the buttons.
            if (footerMessage != null)
                Theme.Text(g, footerMessage, Theme.Regular(12), Theme.Error, Scaled(Edge - 16, LogicalHeight - FooterHeight, LogicalWidth - Edge - 230 - 16, FooterHeight), true,
                    StringAlignment.Center);
            else if (page == Page.Choose && installing)
                Theme.Text(g, "OMSI 2 itself is not included; MapWorks reads maps from your installation.", Theme.Regular(11), Theme.TextMuted,
                    Scaled(Edge - 16, LogicalHeight - FooterHeight, LogicalWidth - Edge - 230 - 16, FooterHeight), true, StringAlignment.Center);
        }

        private string Heading()
        {
            switch (page)
            {
                case Page.Working:
                    return installing ? "Installing OMSI MapWorks " + version : "Removing OMSI MapWorks";
                case Page.Done:
                    return installing ? "OMSI MapWorks is installed" : "OMSI MapWorks was removed";
                case Page.Failed:
                    return installing ? "MapWorks could not be installed" : "MapWorks could not be removed";
                default:
                    return installing ? "Install OMSI MapWorks " + version : "Uninstall OMSI MapWorks" + (installedVersion != null ? " " + installedVersion : string.Empty);
            }
        }

        private string Description()
        {
            switch (page)
            {
                case Page.Working:
                    return installing ? "Copying the program into " + folder + "." : "Removing the program from " + folder + ".";
                case Page.Done:
                    return installing
                        ? "It lies in " + folder + ". You find it in the Start menu as OMSI MapWorks, and Windows' list of apps can remove it again."
                        : "Your projects, maps and settings are still there; only the program is gone.";
                case Page.Failed:
                    return failure + (installing ? " Nothing else was changed; run the setup again once the cause is gone." : string.Empty);
                default:
                    if (!installing)
                        return "Removes the program from " + folder + ", its shortcuts and its entry in Windows' list of apps. Your projects, maps and settings stay.";
                    return installedVersion != null
                        ? "A modern map editor for OMSI 2. Version " + installedVersion + " is installed; Install replaces it with " + version + ". Projects, maps and settings stay."
                        : "A modern map editor for OMSI 2: open maps, build roads and junctions, place objects and export your work as a map of its own.";
            }
        }

        private void ShowPage(Page shown)
        {
            page = shown;
            var choose = page == Page.Choose;
            folderField.Visible = choose && installing;
            browseButton.Visible = choose && installing;
            startMenuOption.Visible = choose && installing;
            desktopOption.Visible = choose && installing;
            launchOption.Visible = page == Page.Done && installing;
            closeButton.Visible = page != Page.Working;
            secondaryButton.Visible = choose;
            primaryButton.Visible = page != Page.Working;
            primaryButton.Text = choose ? (installing ? "Install" : "Uninstall") : page == Page.Done && installing ? "Finish" : "Close";
            primaryButton.ButtonKind = choose && !installing ? FlatButton.Kind.Danger : FlatButton.Kind.Primary;
            if (primaryButton.Visible)
                primaryButton.Focus();
            Invalidate();
        }

        private void Primary()
        {
            switch (page)
            {
                case Page.Choose:
                    Begin();
                    break;
                case Page.Done:
                    if (installing && launchOption.Checked)
                        try
                        {
                            SetupInstallation.Start(folder);
                        }
                        catch (System.ComponentModel.Win32Exception)
                        {
                        }
                    Close();
                    break;
                default:
                    Close();
                    break;
            }
        }

        private void Begin()
        {
            string problem;
            try
            {
                if (installing)
                {
                    folder = SetupInstallation.ResolveFolder(folderField.TextBox.Text);
                    problem = SetupInstallation.CheckFolder(folder);
                }
                else
                    problem = SetupInstallation.CheckUninstall(folder);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException ||
                                              exception is IOException || exception is UnauthorizedAccessException)
            {
                problem = installing ? "This is no folder MapWorks can use: " + exception.Message : exception.Message;
            }
            if (problem != null)
            {
                footerMessage = problem;
                Invalidate();
                return;
            }
            footerMessage = null;
            folderField.TextBox.Text = folder;
            busy = true;
            progress = 0;
            progressText = string.Empty;
            ShowPage(Page.Working);
            var target = folder;
            var startMenu = startMenuOption.Checked;
            var desktop = desktopOption.Checked;
            var shown = DateTime.MinValue;
            Action<double, string> report = (share, name) =>
            {
                // At most twenty updates a second reach the window.
                var now = DateTime.UtcNow;
                if (share < 1 && (now - shown).TotalMilliseconds < 50)
                    return;
                shown = now;
                BeginInvoke((Action)(() =>
                {
                    progress = share;
                    progressText = name;
                    Invalidate();
                }));
            };
            new Thread(() =>
            {
                string error = null;
                try
                {
                    if (installing)
                        SetupInstallation.Install(target, startMenu, desktop, report);
                    else
                        SetupInstallation.Uninstall(target, report);
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                }
                BeginInvoke((Action)(() =>
                {
                    busy = false;
                    failure = error;
                    ShowPage(error == null ? Page.Done : Page.Failed);
                }));
            }) { IsBackground = true }.Start();
        }

        private void Browse()
        {
            using (var dialog = new FolderBrowserDialog { Description = "Choose the folder for OMSI MapWorks", ShowNewFolderButton = true })
            {
                var current = folderField.TextBox.Text;
                if (Directory.Exists(current))
                    dialog.SelectedPath = current;
                else if (Directory.Exists(Path.GetDirectoryName(current) ?? string.Empty))
                    dialog.SelectedPath = Path.GetDirectoryName(current);
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    folderField.TextBox.Text = SetupInstallation.ResolveFolder(dialog.SelectedPath);
            }
        }

        private static Rectangle Scaled(int x, int y, int width, int height)
        {
            return new Rectangle(Theme.S(x), Theme.S(y), Theme.S(width), Theme.S(height));
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    }

    /// <summary>The editor's design tokens (EditorShell.uss), its fonts and the logo, scaled for the screen.</summary>
    internal static class Theme
    {
        public static readonly Color Background = ColorTranslator.FromHtml("#171C23");
        public static readonly Color Panel = ColorTranslator.FromHtml("#202630");
        public static readonly Color PanelRaised = ColorTranslator.FromHtml("#29313D");
        public static readonly Color PanelHover = ColorTranslator.FromHtml("#35475A");
        public static readonly Color Field = ColorTranslator.FromHtml("#141920");
        public static readonly Color Border = ColorTranslator.FromHtml("#414B59");
        public static readonly Color BorderSubtle = ColorTranslator.FromHtml("#2E3642");
        public static readonly Color TextPrimary = ColorTranslator.FromHtml("#F2F4F7");
        public static readonly Color TextSecondary = ColorTranslator.FromHtml("#AEB8C5");
        public static readonly Color TextMuted = ColorTranslator.FromHtml("#7E8A99");
        public static readonly Color Accent = ColorTranslator.FromHtml("#F08A32");
        public static readonly Color AccentHover = ColorTranslator.FromHtml("#FF9B45");
        public static readonly Color Error = ColorTranslator.FromHtml("#E06464");

        private static readonly PrivateFontCollection Fonts = new PrivateFontCollection();
        private static FontFamily regular;
        private static FontFamily semiBold;
        private static float scale = 1f;

        /// <summary>Loads Inter for drawing and, through GDI, for the text field; measures the screen's scale.</summary>
        public static void Load()
        {
            using (var screen = Graphics.FromHwnd(IntPtr.Zero))
                scale = Math.Max(1f, screen.DpiX / 96f);
            foreach (var name in new[] { "Inter-Regular.ttf", "Inter-SemiBold.ttf" })
            {
                var bytes = Resource(name);
                if (bytes == null)
                    continue;
                // The memory stays with the fonts for the life of the program.
                var memory = Marshal.AllocCoTaskMem(bytes.Length);
                Marshal.Copy(bytes, 0, memory, bytes.Length);
                Fonts.AddMemoryFont(memory, bytes.Length);
                uint count = 0;
                AddFontMemResourceEx(memory, (uint)bytes.Length, IntPtr.Zero, ref count);
            }
            foreach (var family in Fonts.Families)
                if (family.Name.IndexOf("SemiBold", StringComparison.OrdinalIgnoreCase) >= 0)
                    semiBold = family;
                else
                    regular = family;
        }

        public static int S(int value)
        {
            return (int)Math.Round(value * scale);
        }

        public static Font Regular(float pixels)
        {
            return new Font(regular ?? FontFamily.GenericSansSerif, pixels * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        public static Font SemiBold(float pixels)
        {
            return semiBold != null
                ? new Font(semiBold, pixels * scale, FontStyle.Regular, GraphicsUnit.Pixel)
                : new Font(regular ?? FontFamily.GenericSansSerif, pixels * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        public static Image Logo()
        {
            var bytes = Resource("Logo.png");
            if (bytes == null)
                return null;
            using (var stream = new MemoryStream(bytes))
            using (var image = Image.FromStream(stream))
                return new Bitmap(image);
        }

        public static void Text(Graphics g, string text, Font font, Color color, Rectangle bounds, bool wrap, StringAlignment vertical,
            StringAlignment horizontal = StringAlignment.Near)
        {
            using (font)
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat { LineAlignment = vertical, Alignment = horizontal, Trimming = StringTrimming.EllipsisCharacter })
            {
                if (!wrap)
                    format.FormatFlags |= StringFormatFlags.NoWrap;
                g.DrawString(text ?? string.Empty, font, brush, bounds, format);
            }
        }

        public static GraphicsPath Rounded(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            var diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static byte[] Resource(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null)
                    return null;
                using (var copy = new MemoryStream())
                {
                    stream.CopyTo(copy);
                    return copy.ToArray();
                }
            }
        }

        [DllImport("gdi32.dll")]
        private static extern IntPtr AddFontMemResourceEx(IntPtr font, uint length, IntPtr reserved, ref uint fonts);
    }

    /// <summary>A button drawn like the editor's: orange for the main action, red for removing, outlined otherwise.</summary>
    internal sealed class FlatButton : Control
    {
        public enum Kind
        {
            Primary,
            Danger,
            Secondary,
            Close
        }

        private readonly Color surface;
        private bool hover;
        private bool pressed;

        public FlatButton(Kind kind, string text, Color surface)
        {
            ButtonKind = kind;
            Text = text;
            this.surface = surface;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public Kind ButtonKind { get; set; }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            pressed = true;
            Focus();
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
                OnClick(EventArgs.Empty);
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            Invalidate();
            base.OnTextChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(surface);
            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            if (ButtonKind == Kind.Close)
            {
                if (hover)
                    using (var brush = new SolidBrush(pressed ? ControlPaint.Dark(Theme.Error) : Theme.Error))
                        g.FillRectangle(brush, ClientRectangle);
                var size = Theme.S(10);
                var x = (Width - size) / 2;
                var y = (Height - size) / 2;
                using (var pen = new Pen(hover ? Theme.TextPrimary : Theme.TextSecondary, Math.Max(1f, Theme.S(1) * 1.4f)))
                {
                    g.DrawLine(pen, x, y, x + size, y + size);
                    g.DrawLine(pen, x + size, y, x, y + size);
                }
                return;
            }

            Color fill;
            Color border;
            Color text;
            switch (ButtonKind)
            {
                case Kind.Primary:
                    fill = hover ? Theme.AccentHover : Theme.Accent;
                    border = fill;
                    text = Theme.Background;
                    break;
                case Kind.Danger:
                    fill = hover ? ControlPaint.Light(Theme.Error, 0.2f) : Theme.Error;
                    border = fill;
                    text = Theme.Background;
                    break;
                default:
                    fill = hover ? Theme.PanelHover : Theme.PanelRaised;
                    border = Theme.Border;
                    text = Theme.TextPrimary;
                    break;
            }
            if (!Enabled)
            {
                fill = Theme.PanelRaised;
                border = Theme.BorderSubtle;
                text = Theme.TextMuted;
            }
            if (pressed)
                fill = ControlPaint.Dark(fill, 0.05f);
            using (var path = Theme.Rounded(bounds, Theme.S(4)))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(Focused && ButtonKind == Kind.Secondary ? Theme.Accent : border))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
            if (Focused && ButtonKind != Kind.Secondary)
                using (var path = Theme.Rounded(Rectangle.Inflate(bounds, -Theme.S(2), -Theme.S(2)), Theme.S(3)))
                using (var pen = new Pen(Color.FromArgb(110, Theme.Background)))
                    g.DrawPath(pen, path);
            Theme.Text(g, Text, ButtonKind == Kind.Secondary ? Theme.Regular(13) : Theme.SemiBold(13), text, ClientRectangle, false, StringAlignment.Center,
                StringAlignment.Center);
        }
    }

    /// <summary>A check box drawn like the editor's toggles: a field-coloured box that turns orange with a dark tick.</summary>
    internal sealed class CheckOption : Control
    {
        private readonly Color surface;
        private bool isChecked;
        private bool hover;

        public CheckOption(string text, Color surface)
        {
            Text = text;
            this.surface = surface;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public bool Checked
        {
            get { return isChecked; }
            set
            {
                isChecked = value;
                Invalidate();
            }
        }

        protected override void OnClick(EventArgs e)
        {
            Focus();
            Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
                Checked = !Checked;
            base.OnKeyDown(e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(surface);
            var size = Theme.S(16);
            var box = new Rectangle(0, (Height - size) / 2, size, size);
            using (var path = Theme.Rounded(box, Theme.S(3)))
            using (var brush = new SolidBrush(isChecked ? Theme.Accent : Theme.Field))
            using (var pen = new Pen(isChecked ? Theme.Accent : hover || Focused ? Theme.Accent : Theme.Border))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
            if (isChecked)
                using (var pen = new Pen(Theme.Background, Math.Max(1.5f, Theme.S(2))) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(pen, new[]
                    {
                        new PointF(box.X + size * 0.24f, box.Y + size * 0.52f),
                        new PointF(box.X + size * 0.43f, box.Y + size * 0.70f),
                        new PointF(box.X + size * 0.76f, box.Y + size * 0.32f)
                    });
            Theme.Text(g, Text, Theme.Regular(13), Theme.TextPrimary, new Rectangle(size + Theme.S(8), 0, Width - size - Theme.S(8), Height), false,
                StringAlignment.Center);
        }
    }

    /// <summary>A text field like the editor's: a dark field with a border that turns orange while typing.</summary>
    internal sealed class FieldBox : Control
    {
        private readonly Color surface;

        public FieldBox(Color surface)
        {
            this.surface = surface;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TextBox = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Field,
                ForeColor = Theme.TextPrimary,
                Font = Theme.Regular(13)
            };
            TextBox.GotFocus += (sender, args) => Invalidate();
            TextBox.LostFocus += (sender, args) => Invalidate();
            Controls.Add(TextBox);
            Cursor = Cursors.IBeam;
        }

        public TextBox TextBox { get; private set; }

        protected override void OnClick(EventArgs e)
        {
            TextBox.Focus();
            base.OnClick(e);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            var inset = Theme.S(10);
            TextBox.SetBounds(inset, (Height - TextBox.PreferredHeight) / 2, Width - 2 * inset, TextBox.PreferredHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(surface);
            using (var path = Theme.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), Theme.S(4)))
            using (var brush = new SolidBrush(Theme.Field))
            using (var pen = new Pen(TextBox.Focused ? Theme.Accent : Theme.Border))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
        }
    }

    internal static class Program
    {
        /// <summary>
        /// Without arguments the window. --silent [--dir folder] [--no-startmenu] [--no-desktop] installs (or, as the
        /// uninstaller, removes) without it and ends with 0 or 1, the reason in --log file; --screenshot file.png
        /// [--page choose|working|done|failed|message] saves the window as a picture for a check of its design.
        /// </summary>
        [STAThread]
        private static int Main(string[] args)
        {
            SetProcessDPIAware();
            Theme.Load();
            var installing = SetupInstallation.HasPayload;
            if (Has(args, "--silent"))
                return Silent(installing, Value(args, "--dir"), !Has(args, "--no-startmenu"), !Has(args, "--no-desktop"), Value(args, "--log"));

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var form = new SetupForm(installing))
            {
                var screenshot = Value(args, "--screenshot");
                if (screenshot != null)
                {
                    form.ShowSample(Value(args, "--page") ?? "choose");
                    form.Shown += (sender, e) => form.BeginInvoke((Action)(() =>
                    {
                        using (var bitmap = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                            bitmap.Save(screenshot, System.Drawing.Imaging.ImageFormat.Png);
                        }
                        form.Close();
                    }));
                }
                Application.Run(form);
            }
            return 0;
        }

        private static int Silent(bool installing, string chosen, bool startMenu, bool desktop, string log)
        {
            try
            {
                if (installing)
                {
                    var folder = SetupInstallation.ResolveFolder(chosen ?? SetupInstallation.DefaultFolder);
                    var problem = SetupInstallation.CheckFolder(folder);
                    if (problem != null)
                        throw new InvalidOperationException(problem);
                    SetupInstallation.Install(folder, startMenu, desktop, (share, name) => { });
                    Log(log, "Installed " + SetupInstallation.Version + " into " + folder + ".");
                }
                else
                {
                    var folder = Path.GetDirectoryName(Application.ExecutablePath);
                    var problem = SetupInstallation.CheckUninstall(folder);
                    if (problem != null)
                        throw new InvalidOperationException(problem);
                    SetupInstallation.Uninstall(folder, (share, name) => { });
                    SetupInstallation.RemoveAfterExit(folder);
                    Log(log, "Removed " + folder + ".");
                }
                return 0;
            }
            catch (Exception exception)
            {
                Log(log, "Failed: " + exception.Message);
                return 1;
            }
        }

        private static void Log(string file, string text)
        {
            if (file != null)
                File.AppendAllText(file, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + text + Environment.NewLine);
        }

        private static bool Has(string[] args, string name)
        {
            return Array.IndexOf(args, name) >= 0;
        }

        private static string Value(string[] args, string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
    }
}
