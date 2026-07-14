using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CrosshairY;

public partial class MainWindow : Window
{
    private readonly AppState       _s = new();
    private GlobalKeyboardHook?     _hook;

    private bool            _bindingMode;
    private Action<string>? _bindingCallback;
    private Button?         _bindingBtn;

    private bool            _slotBindingMode;
    private bool            _dragMode;

    private double           _scrollTarget;
    private DispatcherTimer? _scrollTimer;

    private Button? _activeNavBtn;

    private CrosshairOverlay? _crOverlay;

    public const string Version = "1.0.8";
    private string? _updateExeUrl;
    private string? _updateHtmlUrl;
    private long    _updateExeSize;
    private bool    _updateBusy;

    private string   _builderColor    = "#ffffff";
    private int      _builderSize     = 15;
    private string?[,] _builderGrid  = new string?[15, 15];
    private BuilderGridControl? _builderGridControl;

    private enum BuilderTool { Pencil, Eraser, Fill, Line, Rect, Ellipse }
    private BuilderTool _builderTool = BuilderTool.Pencil;
    private bool _strokeActive;
    private int  _strokeStartR, _strokeStartC;
    private string?[,]? _builderPreview;

    private enum BuilderSymmetry { None, MirrorX, MirrorY, Both }
    private BuilderSymmetry _builderSymmetry = BuilderSymmetry.None;

    private readonly Stack<string?[,]> _undoStack = new();
    private readonly Stack<string?[,]> _redoStack = new();

    private string? _activeCustomName;

    private static readonly string AppDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CrosshairY");

    private static readonly string ProfilesDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CrosshairY", "Configs");

    private static readonly string LastUsedFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CrosshairY", "Configs", ".lastused");

    private static readonly string LaunchesFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CrosshairY", "launches.dat");

    private static readonly string SettingsFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CrosshairY", "settings.dat");

    private static readonly string CustomTemplatesFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CrosshairY", "custom_crosshairs.json");

    private static readonly string ImagesDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CrosshairY", "Images");

    private static readonly (string id, string name)[] CrTemplates =
    {
        ("dot",              "Dot"),
        ("ring",             "Ring"),
        ("sq_dot",           "Square"),
        ("thin_cross",       "Thin +"),
        ("thick_cross",      "Thick +"),
        ("cross_dot_c",      "Cross·"),
        ("t_shape",          "T-Shape"),
        ("cross_circle",     "Circle+"),
        ("small_plus",       "S.Plus"),
        ("large_plus",       "L.Plus"),
        ("sniper",           "Sniper"),
        ("x_cross",          "X Cross"),
        ("x_dot",            "X·"),
        ("inward_arrows",    "Arrows"),
        ("outward_chevrons", "Chevrons"),
        ("triangle",         "Triangle"),
        ("diamond",          "Diamond"),
        ("dot_ring",         "Dot Ring"),
        ("double_ring",      "2 Rings"),
        ("plus_dot",         "Plus·"),
        ("brackets",         "Corners"),
        ("x_thick",          "X Thick")
    };

    private static readonly string[] CrColors =
    {
        "#ffffff", "#ff3333", "#33ff66", "#00ffff",
        "#ffff00", "#ff00ff", "#ff8800", "#000000"
    };

    private static readonly string[] BuilderPalette =
    {
        "#ffffff", "#ff3333", "#33ff66", "#00ffff",
        "#ffff00", "#ff00ff", "#ff8800", "#000000",
        "#aaaaaa", "#555555", "#ff6666", "#66ff99",
        "#66ccff", "#ffcc00", "#ff66ff", "#ff9944"
    };

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern int  SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HT_CAPTION       = 0x2;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
    private const uint WDA_NONE               = 0x00000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private const int GWL_EXSTYLE      = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW  = 0x00040000;

    public MainWindow()
    {
        RenderOptions.ProcessRenderMode = RenderMode.Default;
        InitializeComponent();

        SourceInitialized += (_, _) => ApplyCaptureAffinity();

        PreviewKeyDown += OnBuilderPreviewKeyDown;

        Loaded += (_, _) =>
        {
            _hook = new GlobalKeyboardHook();
            _hook.KeyDown += OnGlobalKeyDown;

            if (OuterBorder != null)
            {
                var clip = new RectangleGeometry { RadiusX = 0, RadiusY = 0, Rect = new Rect(0, 0, ActualWidth, ActualHeight) };
                OuterBorder.Clip = clip;
                SizeChanged += (_, e) => { clip.Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height); };
            }
        };

        Closing += (_, e) =>
        {
            if (!_forceClose)
            {
                e.Cancel = true;
                Hide();
            }
        };

        Closed += (_, _) =>
        {
            _hook?.Dispose();
            _crOverlay?.Close();
        };

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            var windowFade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(350)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            windowFade.Completed += (_, _) =>
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                t.Tick += (_, _) => { t.Stop(); StartTyping(); };
                t.Start();
            };
            BeginAnimation(OpacityProperty, windowFade);
        });
    }

    private bool _forceClose = false;

    internal void PrepareForExit() => _forceClose = true;

    private void ApplyCaptureAffinity()
    {
        bool active = _s.CaptureHidden;
        uint aff    = active ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE;
        var  hwnd   = new WindowInteropHelper(this).Handle;

        SetWindowDisplayAffinity(hwnd, aff);
        _crOverlay?.SetProof(active);

        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        if (active) { ex |= WS_EX_TOOLWINDOW; ex &= ~WS_EX_APPWINDOW; }
        else        { ex &= ~WS_EX_TOOLWINDOW; ex |= WS_EX_APPWINDOW; }
        SetWindowLong(hwnd, GWL_EXSTYLE, ex);
    }

    private void ToggleCaptureHide()
    {
        _s.CaptureHidden = !_s.CaptureHidden;
        ApplyCaptureAffinity();
    }

    private void StartTyping()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var fadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(600))) { EasingFunction = ease };
        AsciiText.BeginAnimation(OpacityProperty, fadeIn);

        var scaleX = new DoubleAnimation(0.92, 1, new Duration(TimeSpan.FromMilliseconds(600))) { EasingFunction = ease };
        var scaleY = new DoubleAnimation(0.92, 1, new Duration(TimeSpan.FromMilliseconds(600))) { EasingFunction = ease };
        var st = (ScaleTransform)AsciiText.RenderTransform;
        st.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);

        var lineExpand = new DoubleAnimation(0, 120, new Duration(TimeSpan.FromMilliseconds(500)))
        {
            BeginTime      = TimeSpan.FromMilliseconds(400),
            EasingFunction = ease
        };
        AccentLine.BeginAnimation(FrameworkElement.WidthProperty, lineExpand);

        var t2 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
        t2.Tick += (_, _) =>
        {
            t2.Stop();
            StatusText.Text = "starting CrosshairY...";
            var fade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(400)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            StatusText.BeginAnimation(OpacityProperty, fade);
        };
        t2.Start();

        var t3 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3000) };
        t3.Tick += (_, _) => { t3.Stop(); CheckAndShowSurvey(); };
        t3.Start();
    }

    private void CheckAndShowSurvey()
    {
        int launchCount   = IncrementLaunchCount(out var completedIds);
        var pendingSurvey = GetPendingSurvey(launchCount, completedIds);

        if (pendingSurvey.HasValue)
        {
            var (surveyId, question, options) = pendingSurvey.Value;
            var win = new SurveyWindow(question, options, launchCount) { Owner = this };
            win.ShowDialog();
            if (win.Submitted)
                MarkSurveyCompleted(surveyId);
        }

        TransitionToMain();
    }

    private static int IncrementLaunchCount(out HashSet<string> completedIds)
    {
        Directory.CreateDirectory(AppDir);

        int count    = 1;
        completedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (File.Exists(LaunchesFile))
            {
                var parts = File.ReadAllText(LaunchesFile).Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 && int.TryParse(parts[0].Trim(), out int stored))
                    count = stored + 1;
                foreach (var part in parts.Skip(1))
                    completedIds.Add(part.Trim());
            }
        }
        catch { }

        WriteLaunchesFile(count, completedIds);
        return count;
    }

    private static void MarkSurveyCompleted(string surveyId)
    {
        try
        {
            int count = 1;
            HashSet<string> completedIds;

            if (File.Exists(LaunchesFile))
            {
                var parts = File.ReadAllText(LaunchesFile).Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 && int.TryParse(parts[0].Trim(), out int stored))
                    count = stored;
                completedIds = new HashSet<string>(parts.Skip(1).Select(p => p.Trim()), StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                completedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            completedIds.Add(surveyId);
            WriteLaunchesFile(count, completedIds);
        }
        catch { }
    }

    private static void WriteLaunchesFile(int count, HashSet<string> completedIds)
    {
        try
        {
            var parts = new List<string> { count.ToString() };
            parts.AddRange(completedIds);
            File.WriteAllText(LaunchesFile, string.Join(",", parts));
        }
        catch { }
    }

    private static readonly (string id, string question, string[] options)[] Surveys =
    {
        (
            "survey_3",
            "How did you find us?",
            new[] { "TikTok", "GitHub", "Discord", "Friend", "Website", "Other" }
        ),
        (
            "survey_7",
            "What game do you mainly use CrosshairY for?",
            new[] { "Fortnite", "Blood Strike", "CS2", "Apex Legends", "Valorant", "Other" }
        ),
        (
            "survey_15",
            "What would you like to see added next?",
            new[] { "More crosshair templates", "Import of Images", "Multiple profiles active at once", "Animated / reactive crosshair", "Other" }
        ),
        (
            "survey_30",
            "How would you rate CrosshairY?",
            new[] { "1 star", "2 stars", "3 stars", "4 stars", "5 stars" }
        )
    };

    private static readonly int[] SurveyTriggers = { 3, 7, 15, 30 };

    private static (string id, string question, string[] options)? GetPendingSurvey(int launchCount, HashSet<string> completedIds)
    {
        for (int i = 0; i < SurveyTriggers.Length; i++)
            if (launchCount == SurveyTriggers[i] && !completedIds.Contains(Surveys[i].id))
                return Surveys[i];
        return null;
    }

    private void TransitionToMain()
    {
        var fade = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(350)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fade.Completed += (_, _) =>
        {
            StartupGrid.Visibility = Visibility.Collapsed;
            MainGrid.Visibility    = Visibility.Visible;
            MainGrid.Opacity       = 0;
            var fadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(300)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            MainGrid.BeginAnimation(OpacityProperty, fadeIn);
            _crOverlay = new CrosshairOverlay();
            _crOverlay.ImageDragged += OnImageDragged;
            LoadSettings();
            InitSettingsPanel();
            InitGamesPanel();
            SetupSmoothScroll(MainScrollViewer);
            AnimateNavSelect(BtnCrosshairs);
            TryLoadLastUsed();
            InitCrosshairsPanel();
            InitBuilderPanel();
            InitImagePanel();
            InitKeybindsPanel();
            UpdateMonitorButtons();
            ApplyMonitorToOverlay();
            VersionLabel.Text = $" v{Version}";
            _ = CheckForUpdatesAsync();
        };
        StartupGrid.BeginAnimation(OpacityProperty, fade);
    }

    private void SetupSmoothScroll(ScrollViewer sv)
    {
        _scrollTarget = 0;
        sv.PreviewMouseWheel += (s, e) =>
        {
            e.Handled     = true;
            _scrollTarget = Math.Clamp(_scrollTarget - e.Delta * 0.45, 0, sv.ScrollableHeight);
            if (_scrollTimer == null || !_scrollTimer.IsEnabled)
            {
                _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(7) };
                _scrollTimer.Tick += (_, _) =>
                {
                    double current = sv.VerticalOffset;
                    double diff    = _scrollTarget - current;
                    if (Math.Abs(diff) < 0.3) { sv.ScrollToVerticalOffset(_scrollTarget); _scrollTimer.Stop(); }
                    else sv.ScrollToVerticalOffset(current + diff * 0.18);
                };
                _scrollTimer.Start();
            }
        };
    }

    private void FadeInPanel(StackPanel panel)
    {
        panel.Opacity = 0;
        var anim = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(160)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        panel.BeginAnimation(OpacityProperty, anim);
    }

    private void InitSettingsPanel()
    {
        ProofKeyBtn.Content  = DisplayKey(_s.ProofKey);
        CycleKeyBtn.Content  = string.IsNullOrEmpty(_s.CycleKey) ? "NONE" : DisplayKey(_s.CycleKey);
        ToggleKeyBtn.Content = string.IsNullOrEmpty(_s.ToggleKey) ? "NONE" : DisplayKey(_s.ToggleKey);
        FollowKeyBtn.Content = string.IsNullOrEmpty(_s.FollowKey) ? "NONE" : DisplayKey(_s.FollowKey);
        UpdateNotifyToggle.IsChecked = _s.UpdateNotifications;
        StartupToggle.IsChecked = StartupManager.IsEnabled();
        UpdateLastCheckedLabel();
        BuildMonitorButtons();
    }

    private void BuildMonitorButtons()
    {
        if (MonitorPanel == null) return;
        MonitorPanel.Children.Clear();

        var screens = System.Windows.Forms.Screen.AllScreens;
        if (_s.MonitorIndex < 0 || _s.MonitorIndex >= screens.Length) _s.MonitorIndex = 0;

        for (int i = 0; i < screens.Length; i++)
        {
            var b       = screens[i].Bounds;
            var primary = screens[i].Primary ? "  (primary)" : "";
            var btn = new Button
            {
                Content             = $"DISPLAY {i + 1}  ·  {b.Width}×{b.Height}{primary}",
                Style               = (Style)FindResource("DarkBtn"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
                Margin              = new Thickness(0, 0, 0, 6),
                Tag                 = i
            };
            btn.Click += Monitor_Click;
            MonitorPanel.Children.Add(btn);
        }

        UpdateMonitorButtons();
    }

    private void Monitor_Click(object s, RoutedEventArgs e)
    {
        if (s is not Button btn || btn.Tag is not int idx) return;
        _s.MonitorIndex = idx;
        UpdateMonitorButtons();
        ApplyMonitorToOverlay();
        SaveSettings();
    }

    private void UpdateMonitorButtons()
    {
        if (MonitorPanel == null) return;
        foreach (UIElement el in MonitorPanel.Children)
        {
            if (el is not Button btn || btn.Tag is not int idx) continue;
            bool sel = idx == _s.MonitorIndex;
            btn.Background = new SolidColorBrush(sel ? Color.FromRgb(0x2a, 0x2a, 0x2a) : Color.FromRgb(0x14, 0x14, 0x14));
            btn.Foreground = new SolidColorBrush(sel ? Color.FromRgb(0xf5, 0xf5, 0xf5) : Color.FromRgb(0x8a, 0x8a, 0x8a));
        }
    }

    private void ApplyMonitorToOverlay()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        _crOverlay?.ApplyMonitor(_s.MonitorIndex, dpi.DpiScaleX, dpi.DpiScaleY);
        RefreshCrosshairOverlay();
    }

    private void InitCrosshairsPanel()
    {
        CrTemplatePanel.Children.Clear();
        foreach (var (id, name) in CrTemplates)
            CrTemplatePanel.Children.Add(BuildTemplateTile(id, name));

        CrColorPanel.Children.Clear();
        foreach (var hex in CrColors)
            CrColorPanel.Children.Add(BuildColorSwatch(hex));

        UpdateTemplateTileSelection();
        UpdateColorSwatchSelection();
        InitCustomTemplatesPanel();
        InitImagePanel();

        CrOutlineToggle.IsChecked   = _s.CrOutline;
        CrOutlineSizeSlider.Value   = _s.CrOutlineSize;
        CrSizeSlider.Value          = _s.CrSize;
        CrOpacitySlider.Value       = _s.CrOpacity;
        CrGapSlider.Value           = _s.CrGap;
        CrOutlineSizeLabel.Text     = _s.CrOutlineSize.ToString();
        CrSizeLabel.Text            = $"{_s.CrSize}%";
        CrOpacityLabel.Text         = $"{_s.CrOpacity}%";
        CrGapLabel.Text             = _s.CrGap.ToString();
        CrFollowToggle.IsChecked    = _s.CrFollowCursor;
        CrPosXBox.Text              = _s.CrOffsetX.ToString();
        CrPosYBox.Text              = _s.CrOffsetY.ToString();
    }

    private UIElement BuildTemplateTile(string id, string name)
    {
        var canvas = new Canvas
        {
            Width      = 64,
            Height     = 64,
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14))
        };
        CrDraw.Draw(canvas, 32, 32, 0.5, _s.CrColor, _s.CrOutline, _s.CrOutlineSize, id);

        var label = new TextBlock
        {
            Text                = name,
            FontFamily          = (FontFamily)FindResource("IBMPlexMono"),
            FontSize            = 8,
            Foreground          = new SolidColorBrush(Color.FromRgb(0x78, 0x78, 0x78)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin              = new Thickness(0, 4, 0, 0)
        };

        var inner = new StackPanel();
        inner.Children.Add(canvas);
        inner.Children.Add(label);

        var border = new Border
        {
            Width           = 74,
            Height          = 88,
            Padding         = new Thickness(4),
            BorderThickness = new Thickness(1),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x1e)),
            Margin          = new Thickness(0, 0, 6, 6),
            Cursor          = Cursors.Hand,
            Tag             = id,
            Child           = inner
        };

        border.MouseLeftButtonDown += (_, _) =>
        {
            _s.CrTemplate = _s.CrTemplate == id ? "" : id;
            _activeCustomName = null;
            UpdateTemplateTileSelection();
            InitCustomTemplatesPanel();
            InitImagePanel();
            RefreshCrosshairOverlay();
        };

        return border;
    }

    private UIElement BuildColorSwatch(string hex)
    {
        Color col;
        try   { col = (Color)ColorConverter.ConvertFromString(hex); }
        catch { col = Colors.White; }

        var border = new Border
        {
            Width           = 24,
            Height          = 24,
            Margin          = new Thickness(0, 0, 6, 0),
            BorderThickness = new Thickness(2),
            BorderBrush     = new SolidColorBrush(Colors.Transparent),
            Background      = new SolidColorBrush(col),
            Cursor          = Cursors.Hand,
            Tag             = hex
        };

        border.MouseLeftButtonDown += (_, _) =>
        {
            _s.CrColor = hex;
            UpdateColorSwatchSelection();
            RebuildTemplatePreviews();
            RefreshCrosshairOverlay();
        };

        return border;
    }

    private void UpdateTemplateTileSelection()
    {
        foreach (UIElement el in CrTemplatePanel.Children)
            if (el is Border b)
                b.BorderBrush = b.Tag as string == _s.CrTemplate
                    ? new SolidColorBrush(Color.FromRgb(0xf5, 0xf5, 0xf5))
                    : new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x1e));
    }

    private void UpdateColorSwatchSelection()
    {
        foreach (UIElement el in CrColorPanel.Children)
            if (el is Border b)
                b.BorderBrush = b.Tag as string == _s.CrColor
                    ? new SolidColorBrush(Color.FromRgb(0xf5, 0xf5, 0xf5))
                    : new SolidColorBrush(Colors.Transparent);
    }

    private void RebuildTemplatePreviews()
    {
        foreach (UIElement el in CrTemplatePanel.Children)
            if (el is Border b && b.Child is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is Canvas canvas)
                CrDraw.Draw(canvas, 32, 32, 0.5, _s.CrColor, _s.CrOutline, _s.CrOutlineSize, b.Tag as string ?? "");
    }

    private List<CustomTemplate> LoadCustomTemplates()
    {
        try
        {
            if (File.Exists(CustomTemplatesFile))
                return JsonSerializer.Deserialize<List<CustomTemplate>>(File.ReadAllText(CustomTemplatesFile)) ?? new();
        }
        catch { }
        return new();
    }

    private void SaveCustomTemplates(List<CustomTemplate> list)
    {
        try
        {
            Directory.CreateDirectory(AppDir);
            File.WriteAllText(CustomTemplatesFile, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void BuilderSaveTemplate_Click(object s, RoutedEventArgs e)
    {
        FlushBuilderToState();
        if (_s.CrCustomPixels.Count == 0) return;

        var name = BuilderTemplateName.Text.Trim();
        if (string.IsNullOrEmpty(name)) name = "custom";

        var list = LoadCustomTemplates();
        list.RemoveAll(t => string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase));
        list.Add(new CustomTemplate { name = name, size = _builderSize, pixels = new List<string>(_s.CrCustomPixels) });
        SaveCustomTemplates(list);

        _activeCustomName = name;
        InitCustomTemplatesPanel();

        if (s is Button btn)
        {
            var prev = btn.Content;
            btn.Content = "SAVED!";
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
            t.Tick += (_, _) => { t.Stop(); btn.Content = prev; };
            t.Start();
        }
    }

    private void InitCustomTemplatesPanel()
    {
        if (CustomTemplatePanel == null) return;
        CustomTemplatePanel.Children.Clear();

        var list = LoadCustomTemplates();
        CustomTemplateBorder.Visibility = list.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        foreach (var t in list)
            CustomTemplatePanel.Children.Add(BuildCustomTile(t));
    }

    private UIElement BuildCustomTile(CustomTemplate t)
    {
        var canvas = new Canvas
        {
            Width      = 64,
            Height     = 64,
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14))
        };
        DrawCustomPreview(canvas, t.pixels, t.size, 64);

        var label = new TextBlock
        {
            Text                = t.name,
            FontFamily          = (FontFamily)FindResource("IBMPlexMono"),
            FontSize            = 8,
            Foreground          = new SolidColorBrush(Color.FromRgb(0x78, 0x78, 0x78)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin              = new Thickness(0, 4, 0, 0),
            MaxWidth            = 70,
            TextTrimming        = TextTrimming.CharacterEllipsis
        };

        var inner = new StackPanel();
        inner.Children.Add(canvas);
        inner.Children.Add(label);

        var del = new Button
        {
            Content             = "×",
            Width               = 16,
            Height              = 16,
            Padding             = new Thickness(0),
            FontFamily          = (FontFamily)FindResource("IBMPlexMono"),
            FontSize            = 11,
            Foreground          = new SolidColorBrush(Color.FromRgb(0x8a, 0x8a, 0x8a)),
            Background          = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x1e)),
            BorderThickness     = new Thickness(0),
            Cursor              = Cursors.Hand,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment   = System.Windows.VerticalAlignment.Top,
            Margin              = new Thickness(0, 2, 2, 0)
        };
        del.Click += (_, ev) => { ev.Handled = true; DeleteCustomTemplate(t.name); };

        var grid = new Grid();
        grid.Children.Add(inner);
        grid.Children.Add(del);

        var border = new Border
        {
            Width           = 74,
            Height          = 88,
            Padding         = new Thickness(4),
            BorderThickness = new Thickness(1),
            BorderBrush     = new SolidColorBrush(_activeCustomName == t.name
                ? Color.FromRgb(0xf5, 0xf5, 0xf5)
                : Color.FromRgb(0x1e, 0x1e, 0x1e)),
            Margin          = new Thickness(0, 0, 6, 6),
            Cursor          = Cursors.Hand,
            Child           = grid
        };

        border.MouseLeftButtonDown += (_, _) => LoadCustomTemplate(t);
        return border;
    }

    private void DrawCustomPreview(Canvas canvas, List<string> pixels, int size, double box)
    {
        canvas.Children.Clear();
        if (size <= 0) return;

        double pad   = 4;
        double field = box - pad * 2;
        double cell  = field / size;

        foreach (var entry in pixels)
        {
            var parts = entry.Split(',');
            if (parts.Length < 3) continue;
            if (!int.TryParse(parts[0], out int row) || !int.TryParse(parts[1], out int col)) continue;

            Color color;
            try   { color = (Color)ColorConverter.ConvertFromString(parts[2]); }
            catch { color = Colors.White; }

            var rect = new System.Windows.Shapes.Rectangle
            {
                Width  = cell + 0.5,
                Height = cell + 0.5,
                Fill   = new SolidColorBrush(color)
            };
            Canvas.SetLeft(rect, pad + col * cell);
            Canvas.SetTop(rect,  pad + row * cell);
            canvas.Children.Add(rect);
        }
    }

    private void LoadCustomTemplate(CustomTemplate t)
    {
        _s.CrCustomPixels = new List<string>(t.pixels);
        int gs = t.size <= 15 ? 15 : t.size <= 30 ? 30 : 60;
        _s.CrBuilderSize = gs;
        _builderSize     = gs;
        _s.CrTemplate    = "custom";
        _activeCustomName = t.name;

        if (_builderGridControl != null)
        {
            RebuildBuilderGrid(gs);
            LoadBuilderGridFromState();
            UpdateBuilderSizeButtons();
        }

        UpdateTemplateTileSelection();
        InitCustomTemplatesPanel();
        InitImagePanel();
        RefreshCrosshairOverlay();
    }

    private void DeleteCustomTemplate(string name)
    {
        var list = LoadCustomTemplates();
        list.RemoveAll(t => string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase));
        SaveCustomTemplates(list);
        if (_activeCustomName == name) _activeCustomName = null;
        InitCustomTemplatesPanel();
    }

    private void RefreshCrosshairOverlay()
    {
        if (_dragMode && (!_crosshairOn || string.IsNullOrEmpty(_s.CrTemplate) || _s.CrFollowCursor))
        {
            _dragMode = false;
            _crOverlay?.SetDragMode(false);
            if (DragModeBtn != null)
            {
                DragModeBtn.Content = "DRAG TO POSITION";
                DragModeBtn.Background = (Brush)FindResource("BgBtn");
            }
        }

        if (!_crosshairOn)
        {
            _crOverlay?.Conceal();
            return;
        }

        if (_s.CrTemplate == "image")
            _crOverlay?.UpdateImageCrosshair(ImageFullPath(_s.CrImagePath), _s.CrSize, _s.CrOpacity, _s.CrOffsetX, _s.CrOffsetY, _s.CrFollowCursor);
        else if (_s.CrTemplate == "custom")
            _crOverlay?.UpdateCustomCrosshair(_s.CrCustomPixels, _s.CrSize, _s.CrOpacity, _s.CrBuilderSize, _s.CrOffsetX, _s.CrOffsetY, _s.CrFollowCursor);
        else
            _crOverlay?.UpdateCrosshair(_s.CrTemplate, _s.CrColor, _s.CrOutline, _s.CrOutlineSize, _s.CrSize, _s.CrOpacity, _s.CrGap, _s.CrOffsetX, _s.CrOffsetY, _s.CrFollowCursor);
    }

    private void CrToggle_Changed(object s, RoutedEventArgs e)
    {
        _s.CrOutline = CrOutlineToggle.IsChecked == true;
        if (CrTemplatePanel != null) RebuildTemplatePreviews();
        RefreshCrosshairOverlay();
    }

    private void CrOutlineSizeSlider_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        _s.CrOutlineSize = (int)e.NewValue;
        if (CrOutlineSizeLabel != null) CrOutlineSizeLabel.Text = _s.CrOutlineSize.ToString();
        if (CrTemplatePanel   != null) RebuildTemplatePreviews();
        RefreshCrosshairOverlay();
    }

    private void CrSizeSlider_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        _s.CrSize = (int)e.NewValue;
        if (CrSizeLabel != null) CrSizeLabel.Text = $"{_s.CrSize}%";
        RefreshCrosshairOverlay();
    }

    private void CrOpacitySlider_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        _s.CrOpacity = (int)e.NewValue;
        if (CrOpacityLabel != null) CrOpacityLabel.Text = $"{_s.CrOpacity}%";
        RefreshCrosshairOverlay();
    }

    private void CrGapSlider_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        _s.CrGap = (int)e.NewValue;
        if (CrGapLabel != null) CrGapLabel.Text = _s.CrGap.ToString();
        if (CrTemplatePanel != null) RebuildTemplatePreviews();
        RefreshCrosshairOverlay();
    }

    private int MaxOffsetX => (int)Math.Round((_crOverlay?.Width  ?? SystemParameters.PrimaryScreenWidth)  / 2.0);
    private int MaxOffsetY => (int)Math.Round((_crOverlay?.Height ?? SystemParameters.PrimaryScreenHeight) / 2.0);

    private void CrPosNudge_Click(object s, RoutedEventArgs e)
    {
        if (s is not FrameworkElement fe || fe.Tag is not string tag) return;

        switch (tag)
        {
            case "x-": _s.CrOffsetX = Math.Max(-MaxOffsetX, _s.CrOffsetX - 1); break;
            case "x+": _s.CrOffsetX = Math.Min( MaxOffsetX, _s.CrOffsetX + 1); break;
            case "y-": _s.CrOffsetY = Math.Max(-MaxOffsetY, _s.CrOffsetY - 1); break;
            case "y+": _s.CrOffsetY = Math.Min( MaxOffsetY, _s.CrOffsetY + 1); break;
        }

        UpdatePositionLabels();
        RefreshCrosshairOverlay();
    }

    private void CrPosReset_Click(object s, RoutedEventArgs e)
    {
        _s.CrOffsetX = 0;
        _s.CrOffsetY = 0;
        UpdatePositionLabels();
        RefreshCrosshairOverlay();
    }

    private void CrPosBox_Commit(object s, RoutedEventArgs e)
    {
        if (s is not System.Windows.Controls.TextBox tb) return;
        bool isX = (tb.Tag as string) == "x";

        if (int.TryParse(tb.Text.Trim(), out int val))
        {
            if (isX) _s.CrOffsetX = Math.Clamp(val, -MaxOffsetX, MaxOffsetX);
            else     _s.CrOffsetY = Math.Clamp(val, -MaxOffsetY, MaxOffsetY);
        }

        UpdatePositionLabels();
        RefreshCrosshairOverlay();
    }

    private void CrPosBox_KeyDown(object s, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter || s is not System.Windows.Controls.TextBox tb) return;
        CrPosBox_Commit(tb, e);
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    private void UpdatePositionLabels()
    {
        if (CrPosXBox != null) CrPosXBox.Text = _s.CrOffsetX.ToString();
        if (CrPosYBox != null) CrPosYBox.Text = _s.CrOffsetY.ToString();
    }

    private void CrFollowToggle_Changed(object s, RoutedEventArgs e)
    {
        _s.CrFollowCursor = CrFollowToggle.IsChecked == true;
        RefreshCrosshairOverlay();
    }

    private void UpdateNotifyToggle_Changed(object s, RoutedEventArgs e)
    {
        _s.UpdateNotifications = UpdateNotifyToggle.IsChecked == true;
        SaveSettings();
        if (!_s.UpdateNotifications) HideUpdateToast();
    }

    private void StartupToggle_Changed(object s, RoutedEventArgs e)
    {
        StartupManager.SetEnabled(StartupToggle.IsChecked == true);
    }

    private async System.Threading.Tasks.Task CheckForUpdatesAsync(bool manual = false)
    {
        if (!manual && !_s.UpdateNotifications) return;

        if (manual)
        {
            UpdateCheckNowBtn.IsEnabled = false;
            UpdateCheckNowBtn.Content   = "CHECKING…";
        }

        var rel = await Updater.GetLatestAsync();

        _s.LastUpdateCheck = DateTime.Now.ToString("o");
        SaveSettings();
        UpdateLastCheckedLabel();

        if (manual)
        {
            UpdateCheckNowBtn.IsEnabled = true;
            UpdateCheckNowBtn.Content   = "CHECK NOW";
        }

        if (rel == null)
        {
            if (manual && UpdateCheckStatus != null) UpdateCheckStatus.Text = "Check failed — try again later";
            return;
        }

        if (Updater.IsNewer(Version, rel.Tag))
        {
            _updateExeUrl  = rel.ExeUrl;
            _updateHtmlUrl = rel.HtmlUrl;
            _updateExeSize = rel.ExeSize;
            ShowUpdateToast(rel.Tag);
        }
        else if (manual && UpdateCheckStatus != null)
        {
            UpdateCheckStatus.Text = $"You're on the latest version (v{Version})";
        }
    }

    private async void UpdateCheckNow_Click(object s, RoutedEventArgs e) => await CheckForUpdatesAsync(manual: true);

    private void UpdateLastCheckedLabel()
    {
        if (UpdateCheckStatus == null) return;

        if (!string.IsNullOrEmpty(_s.LastUpdateCheck)
            && DateTime.TryParse(_s.LastUpdateCheck, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            UpdateCheckStatus.Text = $"Last checked: {dt.ToLocalTime():yyyy-MM-dd HH:mm}";
        else
            UpdateCheckStatus.Text = "Last checked: never";
    }

    private void ShowUpdateToast(string tag)
    {
        var v = tag.TrimStart('v', 'V');
        UpdateToastText.Text        = $"Version {v} is ready to install.";
        UpdateDownloadBtn.Content   = string.IsNullOrEmpty(_updateExeUrl) ? "VIEW RELEASE" : "DOWNLOAD & INSTALL";
        UpdateDownloadBtn.IsEnabled = true;
        UpdateDismissBtn.IsEnabled  = true;
        UpdateToast.Visibility      = Visibility.Visible;

        var fade  = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(280))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var slide = new DoubleAnimation(24, 0, new Duration(TimeSpan.FromMilliseconds(280))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        UpdateToast.BeginAnimation(OpacityProperty, fade);
        UpdateToastSlide.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private void HideUpdateToast()
    {
        if (UpdateToast.Visibility != Visibility.Visible) return;

        var fade  = new DoubleAnimation(UpdateToast.Opacity, 0, new Duration(TimeSpan.FromMilliseconds(200))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) => UpdateToast.Visibility = Visibility.Collapsed;
        var slide = new DoubleAnimation(0, 24, new Duration(TimeSpan.FromMilliseconds(200)));
        UpdateToast.BeginAnimation(OpacityProperty, fade);
        UpdateToastSlide.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private void UpdateDismiss_Click(object s, RoutedEventArgs e) => HideUpdateToast();

    private async void UpdateDownload_Click(object s, RoutedEventArgs e)
    {
        if (_updateBusy) return;

        if (string.IsNullOrEmpty(_updateExeUrl))
        {
            if (!string.IsNullOrEmpty(_updateHtmlUrl))
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_updateHtmlUrl) { UseShellExecute = true }); } catch { }
            return;
        }

        _updateBusy                 = true;
        UpdateDownloadBtn.IsEnabled = false;
        UpdateDismissBtn.IsEnabled  = false;
        UpdateDownloadBtn.Content   = "DOWNLOADING…";

        var path = await Updater.DownloadAsync(_updateExeUrl, _updateExeSize);

        if (path != null && Updater.LaunchSwapAndExit(path))
        {
            UpdateDownloadBtn.Content = "RESTARTING…";
            PrepareForExit();
            Application.Current.Shutdown(0);
            return;
        }

        UpdateDownloadBtn.Content   = "FAILED — RETRY";
        UpdateDownloadBtn.IsEnabled = true;
        UpdateDismissBtn.IsEnabled  = true;
        _updateBusy                 = false;
    }

    private static readonly Random _rng = new();

    private void Randomize_Click(object s, RoutedEventArgs e)
    {
        _s.CrTemplate = CrTemplates[_rng.Next(CrTemplates.Length)].id;
        _s.CrColor    = CrColors[_rng.Next(CrColors.Length)];
        UpdateTemplateTileSelection();
        UpdateColorSwatchSelection();
        RebuildTemplatePreviews();
        if (ColorPickerPopup?.IsOpen == true) SyncPickerFromColor();
        RefreshCrosshairOverlay();
    }

    // ── custom color picker (square HSV) ─────────────────────────────────────
    private const double SvW = 200, SvH = 150, HueW = 200;
    private double _pkH, _pkS, _pkV;   // current picker hue/saturation/value
    private bool _pkUpdating;          // guards programmatic text/UI updates
    private bool _svDrag, _hueDrag;

    private void OpenColorPicker_Click(object s, RoutedEventArgs e)
    {
        SyncPickerFromColor();
        ColorPickerPopup.IsOpen = true;
    }

    // read _s.CrColor into HSV state and refresh every part of the popup
    private void SyncPickerFromColor()
    {
        Color col;
        try   { col = (Color)ColorConverter.ConvertFromString(_s.CrColor); }
        catch { col = Colors.White; }
        RgbToHsv(col, out _pkH, out _pkS, out _pkV);
        SyncPickerUi();
    }

    // push the current HSV state onto the thumbs, hue base and hex field
    private void SyncPickerUi()
    {
        _pkUpdating = true;

        SvHueRect.Fill = new SolidColorBrush(HsvToRgb(_pkH, 1, 1));
        Canvas.SetLeft(SvThumb, _pkS * SvW - SvThumb.Width / 2);
        Canvas.SetTop(SvThumb, (1 - _pkV) * SvH - SvThumb.Height / 2);
        Canvas.SetLeft(HueThumb, _pkH / 360.0 * HueW - HueThumb.Width / 2);

        var col = HsvToRgb(_pkH, _pkS, _pkV);
        PickerPreview.Background = new SolidColorBrush(col);
        PickerHexInput.Text = HexOf(col);
        PickerHexInput.Foreground = new SolidColorBrush(Color.FromRgb(0xf5, 0xf5, 0xf5));

        _pkUpdating = false;
    }

    // commit the current HSV state as the active crosshair color
    private void ApplyPickerColor()
    {
        _s.CrColor = HexOf(HsvToRgb(_pkH, _pkS, _pkV));
        SyncPickerUi();
        UpdateColorSwatchSelection();
        RebuildTemplatePreviews();
        RefreshCrosshairOverlay();
    }

    private void SvSquare_MouseDown(object s, MouseButtonEventArgs e)
    {
        _svDrag = true;
        SvSquare.CaptureMouse();
        SetSvFromPoint(e.GetPosition(SvSquare));
    }

    private void SvSquare_MouseMove(object s, System.Windows.Input.MouseEventArgs e)
    {
        if (_svDrag) SetSvFromPoint(e.GetPosition(SvSquare));
    }

    private void SvSquare_MouseUp(object s, MouseButtonEventArgs e)
    {
        _svDrag = false;
        SvSquare.ReleaseMouseCapture();
    }

    private void SetSvFromPoint(Point p)
    {
        _pkS = System.Math.Clamp(p.X / SvW, 0, 1);
        _pkV = System.Math.Clamp(1 - p.Y / SvH, 0, 1);
        ApplyPickerColor();
    }

    private void HueBar_MouseDown(object s, MouseButtonEventArgs e)
    {
        _hueDrag = true;
        HueBar.CaptureMouse();
        SetHueFromPoint(e.GetPosition(HueBar));
    }

    private void HueBar_MouseMove(object s, System.Windows.Input.MouseEventArgs e)
    {
        if (_hueDrag) SetHueFromPoint(e.GetPosition(HueBar));
    }

    private void HueBar_MouseUp(object s, MouseButtonEventArgs e)
    {
        _hueDrag = false;
        HueBar.ReleaseMouseCapture();
    }

    private void SetHueFromPoint(Point p)
    {
        _pkH = System.Math.Clamp(p.X / HueW, 0, 1) * 360.0;
        ApplyPickerColor();
    }

    private void PickerHexInput_Changed(object s, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_pkUpdating || s is not System.Windows.Controls.TextBox tb) return;
        var raw = tb.Text.Trim();
        if (!raw.StartsWith('#')) raw = "#" + raw;
        try
        {
            var col = (Color)ColorConverter.ConvertFromString(raw);
            RgbToHsv(col, out _pkH, out _pkS, out _pkV);
            _s.CrColor = HexOf(col);

            _pkUpdating = true;
            SvHueRect.Fill = new SolidColorBrush(HsvToRgb(_pkH, 1, 1));
            Canvas.SetLeft(SvThumb, _pkS * SvW - SvThumb.Width / 2);
            Canvas.SetTop(SvThumb, (1 - _pkV) * SvH - SvThumb.Height / 2);
            Canvas.SetLeft(HueThumb, _pkH / 360.0 * HueW - HueThumb.Width / 2);
            PickerPreview.Background = new SolidColorBrush(col);
            tb.Foreground = new SolidColorBrush(Color.FromRgb(0xf5, 0xf5, 0xf5));
            _pkUpdating = false;

            UpdateColorSwatchSelection();
            RebuildTemplatePreviews();
            RefreshCrosshairOverlay();
        }
        catch
        {
            tb.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x33, 0x33));
        }
    }

    private static string HexOf(Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";

    private static Color HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - System.Math.Abs((h / 60.0) % 2 - 1));
        double m = v - c;
        double r, g, b;
        if      (h <  60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else              { r = c; g = 0; b = x; }
        return Color.FromRgb(
            (byte)System.Math.Round((r + m) * 255),
            (byte)System.Math.Round((g + m) * 255),
            (byte)System.Math.Round((b + m) * 255));
    }

    private static void RgbToHsv(Color col, out double h, out double s, out double v)
    {
        double r = col.R / 255.0, g = col.G / 255.0, b = col.B / 255.0;
        double max = System.Math.Max(r, System.Math.Max(g, b));
        double min = System.Math.Min(r, System.Math.Min(g, b));
        double d = max - min;

        h = 0;
        if (d > 0)
        {
            if      (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * (((b - r) / d) + 2);
            else               h = 60 * (((r - g) / d) + 4);
            if (h < 0) h += 360;
        }
        s = max <= 0 ? 0 : d / max;
        v = max;
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            ReleaseCapture();
            SendMessage(new WindowInteropHelper(this).Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
        }
    }

    private void Minimize_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object s, RoutedEventArgs e)
    {
        PlayGoodbyeAndShutdown();
    }

    private void PlayGoodbyeAndShutdown()
    {
        GoodbyeOverlay.Visibility = Visibility.Visible;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var overlayFade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(300))) { EasingFunction = ease };
        GoodbyeOverlay.BeginAnimation(OpacityProperty, overlayFade);

        var textFade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(600))) { EasingFunction = ease };
        GoodbyeText.BeginAnimation(OpacityProperty, textFade);

        var st = (ScaleTransform)GoodbyeText.RenderTransform;
        st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, 1, new Duration(TimeSpan.FromMilliseconds(600))) { EasingFunction = ease });
        st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, 1, new Duration(TimeSpan.FromMilliseconds(600))) { EasingFunction = ease });

        var lineExpand = new DoubleAnimation(0, 120, new Duration(TimeSpan.FromMilliseconds(500)))
        {
            BeginTime      = TimeSpan.FromMilliseconds(400),
            EasingFunction = ease
        };
        GoodbyeLine.BeginAnimation(FrameworkElement.WidthProperty, lineExpand);

        var subFade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(400)))
        {
            BeginTime      = TimeSpan.FromMilliseconds(800),
            EasingFunction = ease
        };
        GoodbyeSubText.BeginAnimation(OpacityProperty, subFade);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2800) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var fadeOut = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(350))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fadeOut.Completed += (_, _) =>
            {
                _forceClose = true;
                Application.Current.Shutdown();
            };
            BeginAnimation(OpacityProperty, fadeOut);
        };
        timer.Start();
    }

    private void AnimateNavSelect(Button activate)
    {
        if (_activeNavBtn != null && _activeNavBtn != activate)
        {
            var offBrush = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14));
            _activeNavBtn.Background = offBrush;
            offBrush.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(Color.FromRgb(0x14, 0x14, 0x14), Colors.Transparent, new Duration(TimeSpan.FromMilliseconds(200)))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            _activeNavBtn.Foreground      = new SolidColorBrush(Color.FromRgb(0x78, 0x78, 0x78));
            _activeNavBtn.BorderThickness = new Thickness(0);
        }
        activate.Background      = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14));
        activate.Foreground      = new SolidColorBrush(Color.FromRgb(0xf5, 0xf5, 0xf5));
        activate.BorderBrush     = new SolidColorBrush(Color.FromRgb(0xf5, 0xf5, 0xf5));
        activate.BorderThickness = new Thickness(2, 0, 0, 0);
        _activeNavBtn = activate;
    }

    private void BtnCrosshairs_Click(object s, RoutedEventArgs e)
    {
        CrosshairsPanel.Visibility = Visibility.Visible;
        SettingsPanel.Visibility   = Visibility.Collapsed;
        ProfilesPanel.Visibility   = Visibility.Collapsed;
        SupportPanel.Visibility    = Visibility.Collapsed;
        BuilderPanel.Visibility    = Visibility.Collapsed;
        KeybindsPanel.Visibility   = Visibility.Collapsed;
        GamesPanel.Visibility      = Visibility.Collapsed;
        FadeInPanel(CrosshairsPanel);
        AnimateNavSelect(BtnCrosshairs);
        _scrollTarget = 0;
        MainScrollViewer.ScrollToTop();
    }

    private void BtnSettings_Click(object s, RoutedEventArgs e)
    {
        SettingsPanel.Visibility   = Visibility.Visible;
        CrosshairsPanel.Visibility = Visibility.Collapsed;
        ProfilesPanel.Visibility   = Visibility.Collapsed;
        SupportPanel.Visibility    = Visibility.Collapsed;
        BuilderPanel.Visibility    = Visibility.Collapsed;
        KeybindsPanel.Visibility   = Visibility.Collapsed;
        GamesPanel.Visibility      = Visibility.Collapsed;
        FadeInPanel(SettingsPanel);
        AnimateNavSelect(BtnSettings);
        _scrollTarget = 0;
        MainScrollViewer.ScrollToTop();
    }

    private void BtnSupport_Click(object s, RoutedEventArgs e)
    {
        SupportPanel.Visibility    = Visibility.Visible;
        CrosshairsPanel.Visibility = Visibility.Collapsed;
        ProfilesPanel.Visibility   = Visibility.Collapsed;
        SettingsPanel.Visibility   = Visibility.Collapsed;
        BuilderPanel.Visibility    = Visibility.Collapsed;
        KeybindsPanel.Visibility   = Visibility.Collapsed;
        GamesPanel.Visibility      = Visibility.Collapsed;
        FadeInPanel(SupportPanel);
        AnimateNavSelect(BtnSupport);
        _scrollTarget = 0;
        MainScrollViewer.ScrollToTop();
    }

    private void BtnBuilder_Click(object s, RoutedEventArgs e)
    {
        BuilderPanel.Visibility    = Visibility.Visible;
        CrosshairsPanel.Visibility = Visibility.Collapsed;
        ProfilesPanel.Visibility   = Visibility.Collapsed;
        SettingsPanel.Visibility   = Visibility.Collapsed;
        SupportPanel.Visibility    = Visibility.Collapsed;
        KeybindsPanel.Visibility   = Visibility.Collapsed;
        GamesPanel.Visibility      = Visibility.Collapsed;
        FadeInPanel(BuilderPanel);
        AnimateNavSelect(BtnBuilder);
        _scrollTarget = 0;
        MainScrollViewer.ScrollToTop();
    }

    private void BtnKeybinds_Click(object s, RoutedEventArgs e)
    {
        KeybindsPanel.Visibility   = Visibility.Visible;
        CrosshairsPanel.Visibility = Visibility.Collapsed;
        ProfilesPanel.Visibility   = Visibility.Collapsed;
        SettingsPanel.Visibility   = Visibility.Collapsed;
        SupportPanel.Visibility    = Visibility.Collapsed;
        BuilderPanel.Visibility    = Visibility.Collapsed;
        GamesPanel.Visibility      = Visibility.Collapsed;
        FadeInPanel(KeybindsPanel);
        AnimateNavSelect(BtnKeybinds);
        _scrollTarget = 0;
        MainScrollViewer.ScrollToTop();
        BuildSlotList();
    }

    private void SocialLink_Click(object s, RoutedEventArgs e)
    {
        if (s is Button btn && btn.Tag is string url)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void StartBinding(Button btn, Action<string> callback)
    {
        if (_bindingMode) return;
        _bindingMode     = true;
        _bindingCallback = callback;
        _bindingBtn      = btn;
        btn.Content      = "...";
        btn.Background   = (Brush)FindResource("Red");
    }

    private void FinishBinding(string key)
    {
        if (!_bindingMode) return;

        bool   unbind = key == "Key.esc";
        string value  = unbind ? "" : key;

        _bindingCallback?.Invoke(value);
        if (_bindingBtn is { } b)
        {
            b.Content    = unbind ? "NONE" : DisplayKey(value);
            b.Background = (Brush)FindResource("BgBtn");
        }
        _bindingMode     = false;
        _bindingCallback = null;
        _bindingBtn      = null;
    }

    private void ProofKeyBtn_Click(object s, RoutedEventArgs e)  => StartBinding(ProofKeyBtn,  k => { _s.ProofKey  = k; SaveSettings(); });
    private void CycleKeyBtn_Click(object s, RoutedEventArgs e)  => StartBinding(CycleKeyBtn,  k => { _s.CycleKey  = k; SaveSettings(); });
    private void ToggleKeyBtn_Click(object s, RoutedEventArgs e) => StartBinding(ToggleKeyBtn, k => { _s.ToggleKey = k; SaveSettings(); });
    private void FollowKeyBtn_Click(object s, RoutedEventArgs e) => StartBinding(FollowKeyBtn, k => { _s.FollowKey = k; SaveSettings(); });

    private void OnGlobalKeyDown(string key)
    {
        if (_bindingMode)
        {
            Dispatcher.Invoke(() => FinishBinding(key));
            return;
        }

        if (_slotBindingMode)
        {
            Dispatcher.Invoke(() => FinishSlotBinding(key));
            return;
        }

        if (key == _s.ProofKey)
            Dispatcher.Invoke(ToggleCaptureHide);

        if (!string.IsNullOrEmpty(_s.CycleKey) && key == _s.CycleKey)
            Dispatcher.Invoke(CycleProfile);

        if (!string.IsNullOrEmpty(_s.ToggleKey) && key == _s.ToggleKey)
            Dispatcher.Invoke(ToggleCrosshairOn);

        if (!string.IsNullOrEmpty(_s.FollowKey) && key == _s.FollowKey)
            Dispatcher.Invoke(ToggleFollowViaHotkey);

        var slot = _s.CrSlots.FirstOrDefault(s => s.Key == key);
        if (slot != null)
            Dispatcher.Invoke(() => ApplySlot(slot));
    }

    private bool _crosshairOn = true;

    private void ToggleCrosshairOn()
    {
        _crosshairOn = !_crosshairOn;
        RefreshCrosshairOverlay();
    }

    private void ToggleFollowViaHotkey()
    {
        CrFollowToggle.IsChecked = !(CrFollowToggle.IsChecked == true);
    }

    internal void TrayToggleOverlay() => ToggleCrosshairOn();
    internal void TrayToggleFollow()  => ToggleFollowViaHotkey();
    internal void TrayToggleProof()   => ToggleCaptureHide();

    internal bool TrayOverlayOn => _crosshairOn;
    internal bool TrayFollowOn  => CrFollowToggle?.IsChecked == true;
    internal bool TrayProofOn   => _s.CaptureHidden;

    internal void TrayLoadProfile(string path) => LoadProfile(path);

    internal IReadOnlyList<(string name, string path)> TrayProfiles()
    {
        var list = new List<(string, string)>();
        try
        {
            Directory.CreateDirectory(ProfilesDir);
            foreach (var f in Directory.GetFiles(ProfilesDir, "*.json").OrderBy(f => f))
                list.Add((Path.GetFileNameWithoutExtension(f), f));
        }
        catch { }
        return list;
    }

    private void CycleProfile()
    {
        Directory.CreateDirectory(ProfilesDir);
        var files = Directory.GetFiles(ProfilesDir, "*.json").OrderBy(f => f).ToArray();
        if (files.Length == 0) return;

        string? lastUsed = null;
        try { if (File.Exists(LastUsedFile)) lastUsed = File.ReadAllText(LastUsedFile).Trim(); } catch { }

        int idx  = Array.FindIndex(files, f => string.Equals(f, lastUsed, StringComparison.OrdinalIgnoreCase));
        int next = (idx + 1) % files.Length;
        LoadProfile(files[next]);
    }

    private void BtnProfiles_Click(object s, RoutedEventArgs e)
    {
        ProfilesPanel.Visibility   = Visibility.Visible;
        CrosshairsPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility   = Visibility.Collapsed;
        SupportPanel.Visibility    = Visibility.Collapsed;
        BuilderPanel.Visibility    = Visibility.Collapsed;
        KeybindsPanel.Visibility   = Visibility.Collapsed;
        GamesPanel.Visibility      = Visibility.Collapsed;
        FadeInPanel(ProfilesPanel);
        AnimateNavSelect(BtnProfiles);
        _scrollTarget = 0;
        MainScrollViewer.ScrollToTop();
        LoadProfileList();
    }

    private void BtnGames_Click(object s, RoutedEventArgs e)
    {
        GamesPanel.Visibility      = Visibility.Visible;
        CrosshairsPanel.Visibility = Visibility.Collapsed;
        ProfilesPanel.Visibility   = Visibility.Collapsed;
        SettingsPanel.Visibility   = Visibility.Collapsed;
        SupportPanel.Visibility    = Visibility.Collapsed;
        BuilderPanel.Visibility    = Visibility.Collapsed;
        KeybindsPanel.Visibility   = Visibility.Collapsed;
        FadeInPanel(GamesPanel);
        AnimateNavSelect(BtnGames);
        _scrollTarget = 0;
        MainScrollViewer.ScrollToTop();
        BuildGamesList();
    }


    private void LoadProfileList()
    {
        ProfileListPanel.Children.Clear();

        Directory.CreateDirectory(ProfilesDir);
        var files = Directory.GetFiles(ProfilesDir, "*.json");

        if (files.Length == 0)
        {
            ProfileListPanel.Children.Add(new TextBlock
            {
                Text         = "No configs found. Save one or drop a .json file into the folder.",
                FontFamily   = (FontFamily)FindResource("IBMPlexMono"),
                FontSize     = 9,
                Foreground   = new SolidColorBrush(Color.FromRgb(0x78, 0x78, 0x78)),
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 4, 0, 4)
            });
            return;
        }

        string? lastUsed = null;
        try { if (File.Exists(LastUsedFile)) lastUsed = File.ReadAllText(LastUsedFile).Trim(); } catch { }

        foreach (var file in files.OrderBy(f => f))
        {
            var  name     = Path.GetFileNameWithoutExtension(file);
            var  path     = file;
            bool isActive = string.Equals(path, lastUsed, StringComparison.OrdinalIgnoreCase);

            var nameBlock = new TextBlock
            {
                Text              = isActive ? name + " ●" : name,
                FontFamily        = (FontFamily)FindResource("IBMPlexMono"),
                FontSize          = 11,
                FontWeight        = FontWeights.Bold,
                Foreground        = new SolidColorBrush(isActive
                    ? Color.FromRgb(0xf5, 0xf5, 0xf5)
                    : Color.FromRgb(0x8a, 0x8a, 0x8a)),
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            nameBlock.Margin = new Thickness(10, 0, 6, 0);

            var thumb = BuildProfileThumb(path);

            var loadBtn = new Button { Content = "LOAD", Style = (Style)FindResource("DarkBtn"), Width = 52, Tag = path };
            loadBtn.Click += (_, _) => { LoadProfile(path); LoadProfileList(); };

            var saveBtn = new Button { Content = "SAVE", Style = (Style)FindResource("DarkBtn"), Width = 52, Margin = new Thickness(6, 0, 0, 0), Tag = path };
            saveBtn.Click += (_, _) =>
            {
                File.WriteAllText(path, BuildProfileJson(), System.Text.Encoding.UTF8);
                LoadProfileList();
            };

            var dupBtn = new Button { Content = "DUP", Style = (Style)FindResource("DarkBtn"), Width = 46, Margin = new Thickness(6, 0, 0, 0), Tag = path };
            dupBtn.Click += (_, _) => DuplicateProfile(path);

            var deleteBtn = new Button { Content = "DEL", Style = (Style)FindResource("DarkBtn"), Width = 44, Margin = new Thickness(6, 0, 0, 0), Tag = path };
            deleteBtn.Click += (_, _) => { try { File.Delete(path); } catch { } LoadProfileList(); };

            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(thumb,     0);
            Grid.SetColumn(nameBlock, 1);
            Grid.SetColumn(loadBtn,   2);
            Grid.SetColumn(saveBtn,   3);
            Grid.SetColumn(dupBtn,    4);
            Grid.SetColumn(deleteBtn, 5);
            row.Children.Add(thumb);
            row.Children.Add(nameBlock);
            row.Children.Add(loadBtn);
            row.Children.Add(saveBtn);
            row.Children.Add(dupBtn);
            row.Children.Add(deleteBtn);

            ProfileListPanel.Children.Add(new Border
            {
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x1e)),
                BorderThickness = new Thickness(1),
                Background      = new SolidColorBrush(Color.FromRgb(0x0a, 0x0a, 0x0a)),
                Padding         = new Thickness(12, 10, 12, 10),
                Margin          = new Thickness(0, 0, 0, 6),
                Child           = row
            });
        }
    }

    private UIElement BuildProfileThumb(string path)
    {
        string template = "", color = "#ffffff", imagePath = "";
        bool   outline = false;
        int    outlineSize = 1, gap = 3, builderSize = 15;
        var    pixels = new List<string>();

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var r = doc.RootElement;
            if (r.TryGetProp("cr_template", out var v)) template = v;
            if (r.TryGetProp("cr_color",    out v))     color    = v;
            if (r.TryGetProp("cr_image_path", out v))   imagePath = v;
            if (r.TryGetProperty("cr_outline",      out var jv) && jv.ValueKind == JsonValueKind.True) outline = true;
            if (r.TryGetProperty("cr_outline_size", out jv) && jv.TryGetInt32(out var i)) outlineSize = i;
            if (r.TryGetProperty("cr_gap",          out jv) && jv.TryGetInt32(out i))     gap         = i;
            if (r.TryGetProperty("cr_builder_size", out jv) && jv.TryGetInt32(out i))     builderSize = i <= 15 ? 15 : i <= 30 ? 30 : 60;
            if (r.TryGetProperty("cr_custom_pixels", out jv) && jv.ValueKind == JsonValueKind.Array)
                foreach (var el in jv.EnumerateArray())
                    if (el.GetString() is { } px) pixels.Add(px);
        }
        catch { }

        var canvas = new Canvas
        {
            Width      = 42,
            Height     = 42,
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14))
        };

        if (template == "custom")
            DrawThumbCustom(canvas, pixels, builderSize);
        else if (template == "image" && !string.IsNullOrEmpty(imagePath))
            DrawThumbImage(canvas, imagePath);
        else if (!string.IsNullOrEmpty(template))
            CrDraw.Draw(canvas, 21, 21, 0.34, color, outline, outlineSize, template, gap);

        return new Border
        {
            Width           = 42,
            Height          = 42,
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x1e)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Child           = canvas
        };
    }

    private static void DrawThumbCustom(Canvas canvas, List<string> pixels, int gridSize)
    {
        if (pixels.Count == 0 || gridSize <= 0) return;

        double field = 38.0;
        double cell  = field / gridSize;
        double ox    = (42 - field) / 2.0;
        double oy    = (42 - field) / 2.0;

        foreach (var entry in pixels)
        {
            var parts = entry.Split(',');
            if (parts.Length < 3) continue;
            if (!int.TryParse(parts[0], out int row) || !int.TryParse(parts[1], out int col)) continue;
            if (row < 0 || row >= gridSize || col < 0 || col >= gridSize) continue;

            Color c;
            try   { c = (Color)ColorConverter.ConvertFromString(parts[2]); }
            catch { c = Colors.White; }

            var rect = new Rectangle { Width = cell, Height = cell, Fill = new SolidColorBrush(c) };
            RenderOptions.SetEdgeMode(rect, EdgeMode.Aliased);
            Canvas.SetLeft(rect, ox + col * cell);
            Canvas.SetTop(rect,  oy + row * cell);
            canvas.Children.Add(rect);
        }
    }

    private static void DrawThumbImage(Canvas canvas, string imageFileName)
    {
        var fullPath = ImageFullPath(imageFileName);
        if (!File.Exists(fullPath)) return;
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(fullPath);
            bi.EndInit();
            bi.Freeze();

            double maxDim = 38.0;
            double scale = System.Math.Min(maxDim / bi.PixelWidth, maxDim / bi.PixelHeight);
            double w = bi.PixelWidth * scale;
            double h = bi.PixelHeight * scale;

            var img = new System.Windows.Controls.Image { Source = bi, Width = w, Height = h, Stretch = Stretch.Fill };
            Canvas.SetLeft(img, (42 - w) / 2.0);
            Canvas.SetTop(img, (42 - h) / 2.0);
            canvas.Children.Add(img);
        }
        catch { }
    }

    private static string ImageFullPath(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return "";
        if (Path.IsPathRooted(fileName)) return fileName;
        return Path.Combine(ImagesDir, fileName);
    }

    private void DuplicateProfile(string path)
    {
        try
        {
            var dir      = Path.GetDirectoryName(path) ?? ProfilesDir;
            var baseName = Path.GetFileNameWithoutExtension(path);

            string candidate;
            int n = 2;
            do { candidate = Path.Combine(dir, $"{baseName} ({n}).json"); n++; }
            while (File.Exists(candidate));

            File.Copy(path, candidate);
        }
        catch { }

        LoadProfileList();
    }

    private void SaveProfile_Click(object s, RoutedEventArgs e)
    {
        var name = ProfileNameBox.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var invalid = Path.GetInvalidFileNameChars();
        name = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());

        Directory.CreateDirectory(ProfilesDir);
        var path = Path.Combine(ProfilesDir, name + ".json");

        File.WriteAllText(path, BuildProfileJson(), System.Text.Encoding.UTF8);
        LoadProfileList();
    }


    private void ExportProfile_Click(object s, RoutedEventArgs e)
    {
        try
        {
            var json    = BuildProfileJson();
            var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
            Clipboard.SetText("CY1:" + encoded);

            if (s is Button btn)
            {
                var prev = btn.Content;
                btn.Content = "COPIED!";
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
                t.Tick += (_, _) => { t.Stop(); btn.Content = prev; };
                t.Start();
            }
        }
        catch { }
    }

    private void ImportProfile_Click(object s, RoutedEventArgs e)
    {
        try
        {
            var text = Clipboard.GetText()?.Trim() ?? "";
            if (!text.StartsWith("CY1:")) return;

            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(text[4..]));

            using var test = JsonDocument.Parse(json);

            var name = ProfileNameBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) name = "imported";

            var invalid = Path.GetInvalidFileNameChars();
            name = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());

            Directory.CreateDirectory(ProfilesDir);
            var path = Path.Combine(ProfilesDir, name + ".json");
            File.WriteAllText(path, json, System.Text.Encoding.UTF8);

            LoadProfile(path);
            LoadProfileList();

            if (s is Button btn)
            {
                var prev = btn.Content;
                btn.Content = "IMPORTED!";
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
                t.Tick += (_, _) => { t.Stop(); btn.Content = prev; };
                t.Start();
            }
        }
        catch
        {
            if (s is Button btn)
            {
                var prev = btn.Content;
                btn.Content = "INVALID";
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
                t.Tick += (_, _) => { t.Stop(); btn.Content = prev; };
                t.Start();
            }
        }
    }


    private string BuildProfileJson()
    {
        var cfg = new
        {
            cr_template      = _s.CrTemplate,
            cr_color         = _s.CrColor,
            cr_outline       = _s.CrOutline,
            cr_outline_size  = _s.CrOutlineSize,
            cr_size          = _s.CrSize,
            cr_opacity       = _s.CrOpacity,
            cr_gap           = _s.CrGap,
            cr_offset_x      = _s.CrOffsetX,
            cr_offset_y      = _s.CrOffsetY,
            cr_follow_cursor = _s.CrFollowCursor,
            cr_custom_pixels = _s.CrCustomPixels,
            cr_builder_size  = _s.CrBuilderSize,
            cr_image_path    = _s.CrImagePath,
            proof_key        = _s.ProofKey,
            cycle_key        = _s.CycleKey
        };
        return JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
    }

    private void TryLoadLastUsed()
    {
        try
        {
            if (!File.Exists(LastUsedFile)) return;
            var path = File.ReadAllText(LastUsedFile).Trim();
            if (File.Exists(path)) LoadProfile(path, writeLastUsed: false);
        }
        catch { }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(AppDir);
            var cfg = new
            {
                proof_key            = _s.ProofKey,
                cycle_key            = _s.CycleKey,
                toggle_key           = _s.ToggleKey,
                follow_key           = _s.FollowKey,
                monitor_index        = _s.MonitorIndex,
                update_notifications = _s.UpdateNotifications,
                last_update_check    = _s.LastUpdateCheck,
                auto_switch_games    = _s.AutoSwitchGames,
                auto_revert_profile  = _s.AutoRevertProfile,
                game_profiles        = _s.GameProfiles,
                custom_games         = _s.CustomGames,
                cr_slots             = _s.CrSlots
            };
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(cfg));
        }
        catch { }
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsFile));
            var r = doc.RootElement;
            if (r.TryGetProp("proof_key", out var v) && !string.IsNullOrEmpty(v)) _s.ProofKey = v;
            if (r.TryGetProp("cycle_key", out v))                                  _s.CycleKey = v;
            if (r.TryGetProp("toggle_key", out v))                                 _s.ToggleKey = v;
            if (r.TryGetProp("follow_key", out v))                                 _s.FollowKey = v;
            if (r.TryGetProperty("monitor_index", out var mi) && mi.TryGetInt32(out var midx)) _s.MonitorIndex = midx;
            if (r.TryGetProperty("update_notifications", out var un) && un.ValueKind == JsonValueKind.False) _s.UpdateNotifications = false;
            if (r.TryGetProp("last_update_check", out v)) _s.LastUpdateCheck = v;

            if (r.TryGetProperty("auto_switch_games", out var asg) && asg.ValueKind == JsonValueKind.True) _s.AutoSwitchGames = true;
            if (r.TryGetProperty("auto_revert_profile", out var arp) && arp.ValueKind == JsonValueKind.True) _s.AutoRevertProfile = true;
            if (r.TryGetProperty("game_profiles", out var gp) && gp.ValueKind == JsonValueKind.Object)
            {
                _s.GameProfiles.Clear();
                foreach (var p in gp.EnumerateObject())
                    if (p.Value.GetString() is { } pn) _s.GameProfiles[p.Name] = pn;
            }
            if (r.TryGetProperty("custom_games", out var cg) && cg.ValueKind == JsonValueKind.Array)
            {
                _s.CustomGames.Clear();
                foreach (var el in cg.EnumerateArray())
                    if (el.GetString() is { } ex) _s.CustomGames.Add(ex);
            }

            if (r.TryGetProperty("cr_slots", out var cs) && cs.ValueKind == JsonValueKind.Array)
            {
                _s.CrSlots.Clear();
                foreach (var el in cs.EnumerateArray())
                {
                    try
                    {
                        var slot = System.Text.Json.JsonSerializer.Deserialize<CrosshairSlot>(el.GetRawText());
                        if (slot != null) _s.CrSlots.Add(slot);
                    }
                    catch { }
                }
            }

            if (UpdateNotifyToggle != null) UpdateNotifyToggle.IsChecked = _s.UpdateNotifications;
            if (ToggleKeyBtn != null) ToggleKeyBtn.Content = string.IsNullOrEmpty(_s.ToggleKey) ? "NONE" : DisplayKey(_s.ToggleKey);
            if (FollowKeyBtn != null) FollowKeyBtn.Content = string.IsNullOrEmpty(_s.FollowKey) ? "NONE" : DisplayKey(_s.FollowKey);
            UpdateLastCheckedLabel();
        }
        catch { }
    }

    private void LoadProfile(string path, bool writeLastUsed = true)
    {
        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;

            if (r.TryGetProp("cr_template", out var v)) _s.CrTemplate = v;
            if (r.TryGetProp("cr_color",    out v))     _s.CrColor    = v;
            if (r.TryGetProperty("cr_outline",      out var jv) && jv.ValueKind == JsonValueKind.True)  _s.CrOutline = true;
            if (r.TryGetProperty("cr_outline",      out jv)     && jv.ValueKind == JsonValueKind.False) _s.CrOutline = false;
            if (r.TryGetProperty("cr_outline_size", out jv) && jv.TryGetInt32(out var i)) _s.CrOutlineSize = i;
            if (r.TryGetProperty("cr_size",         out jv) && jv.TryGetInt32(out i))     _s.CrSize        = i;
            if (r.TryGetProperty("cr_opacity",      out jv) && jv.TryGetInt32(out i))     _s.CrOpacity     = i;
            if (r.TryGetProperty("cr_gap",          out jv) && jv.TryGetInt32(out i))     _s.CrGap         = i;
            if (r.TryGetProperty("cr_offset_x",     out jv) && jv.TryGetInt32(out i))     _s.CrOffsetX     = Math.Clamp(i, -MaxOffsetX, MaxOffsetX);
            if (r.TryGetProperty("cr_offset_y",     out jv) && jv.TryGetInt32(out i))     _s.CrOffsetY     = Math.Clamp(i, -MaxOffsetY, MaxOffsetY);
            if (r.TryGetProperty("cr_follow_cursor", out jv) && jv.ValueKind == JsonValueKind.True)  _s.CrFollowCursor = true;
            if (r.TryGetProperty("cr_follow_cursor", out jv) && jv.ValueKind == JsonValueKind.False) _s.CrFollowCursor = false;

            if (r.TryGetProp("cr_image_path", out v))
                _s.CrImagePath = v;

            if (r.TryGetProp("proof_key", out v) && !string.IsNullOrEmpty(v))
            {
                _s.ProofKey = v;
                if (ProofKeyBtn != null) ProofKeyBtn.Content = DisplayKey(v);
            }
            if (r.TryGetProp("cycle_key", out v))
            {
                _s.CycleKey = v;
                if (CycleKeyBtn != null) CycleKeyBtn.Content = string.IsNullOrEmpty(v) ? "NONE" : DisplayKey(v);
            }

            if (r.TryGetProperty("cr_custom_pixels", out jv) && jv.ValueKind == JsonValueKind.Array)
            {
                _s.CrCustomPixels.Clear();
                foreach (var el in jv.EnumerateArray())
                    if (el.GetString() is { } px) _s.CrCustomPixels.Add(px);
            }
            if (r.TryGetProperty("cr_builder_size", out jv) && jv.TryGetInt32(out i))
            {
                int gs = i <= 15 ? 15 : i <= 30 ? 30 : 60;
                _s.CrBuilderSize = gs;
                _builderSize = gs;
                if (_builderGridControl != null)
                {
                    RebuildBuilderGrid(gs);
                    LoadBuilderGridFromState();
                    UpdateBuilderSizeButtons();
                }
            }

            if (writeLastUsed)
            {
                Directory.CreateDirectory(ProfilesDir);
                File.WriteAllText(LastUsedFile, path);
            }

            InitCrosshairsPanel();
            RefreshCrosshairOverlay();
        }
        catch { }
    }

    private void ReloadProfiles_Click(object s, RoutedEventArgs e) => LoadProfileList();

    private static readonly (string key, string label, string[] exes)[] KnownGames =
    {
        ("bloodstrike",   "Blood Strike",        new[] { "bloodstrike.exe" }),
        ("valorant",      "Valorant",            new[] { "valorant-win64-shipping.exe" }),
        ("cs2",           "Counter-Strike 2",    new[] { "cs2.exe" }),
        ("apex",          "Apex Legends",        new[] { "r5apex.exe" }),
        ("fortnite",      "Fortnite",            new[] { "fortniteclient-win64-shipping.exe" }),
        ("overwatch",     "Overwatch 2",         new[] { "overwatch.exe" }),
        ("r6",            "Rainbow Six Siege",   new[] { "rainbowsix.exe", "rainbowsix_be.exe" }),
        ("pubg",          "PUBG: Battlegrounds", new[] { "tslgame.exe" }),
        ("cod",           "Call of Duty",        new[] { "cod.exe" }),
        ("marvelrivals",  "Marvel Rivals",       new[] { "marvel-win64-shipping.exe" }),
        ("thefinals",     "The Finals",          new[] { "discovery.exe" }),
        ("destiny2",      "Destiny 2",           new[] { "destiny2.exe" }),
        ("bf2042",        "Battlefield 2042",    new[] { "bf2042.exe" }),
    };

    private DispatcherTimer? _gameWatchTimer;
    private string?          _activeGameKey;
    private string?          _preGameProfile;

    private void InitGamesPanel()
    {
        AutoSwitchToggle.IsChecked = _s.AutoSwitchGames;
        AutoRevertToggle.IsChecked = _s.AutoRevertProfile;
        BuildGamesList();
        if (_s.AutoSwitchGames) StartGameWatcher();
    }

    private void ReloadGames_Click(object s, RoutedEventArgs e) => BuildGamesList();

    private void BuildGamesList()
    {
        if (GamesListPanel == null) return;
        GamesListPanel.Children.Clear();

        foreach (var (key, label, _) in KnownGames)
            GamesListPanel.Children.Add(BuildGameRow(key, label, custom: false));

        foreach (var exe in _s.CustomGames)
            GamesListPanel.Children.Add(BuildGameRow("custom:" + exe.ToLowerInvariant(), exe, custom: true));
    }

    private Border BuildGameRow(string key, string label, bool custom)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        if (custom)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        }

        var name = new TextBlock
        {
            Text                = label,
            FontFamily          = (FontFamily)FindResource("IBMPlexMono"),
            FontSize            = 11,
            Foreground          = new SolidColorBrush(Color.FromRgb(0xf5, 0xf5, 0xf5)),
            VerticalAlignment   = VerticalAlignment.Center,
            TextTrimming        = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(name, 0);
        grid.Children.Add(name);

        _s.GameProfiles.TryGetValue(key, out var current);
        var sel = new Button
        {
            Content             = string.IsNullOrEmpty(current) ? "OFF" : "ON",
            ToolTip             = string.IsNullOrEmpty(current) ? null : current,
            Style               = (Style)FindResource("DarkBtn"),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            Tag                 = key
        };
        sel.Click += GameProfileCycle_Click;
        Grid.SetColumn(sel, 2);
        grid.Children.Add(sel);

        if (custom)
        {
            var del = new Button
            {
                Content = "×",
                Style   = (Style)FindResource("DarkBtn"),
                Tag     = key
            };
            del.Click += RemoveCustomGame_Click;
            Grid.SetColumn(del, 4);
            grid.Children.Add(del);
        }

        return new Border
        {
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x1e)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding         = new Thickness(0, 0, 0, 6),
            Margin          = new Thickness(0, 0, 0, 6),
            Child           = grid
        };
    }

    private void GameProfileCycle_Click(object s, RoutedEventArgs e)
    {
        if (s is not Button btn || btn.Tag is not string key) return;

        var options = new List<string> { "" };
        try
        {
            Directory.CreateDirectory(ProfilesDir);
            foreach (var f in Directory.GetFiles(ProfilesDir, "*.json").OrderBy(f => f))
                options.Add(Path.GetFileNameWithoutExtension(f));
        }
        catch { }

        _s.GameProfiles.TryGetValue(key, out var current);
        int idx  = options.FindIndex(o => string.Equals(o, current ?? "", StringComparison.OrdinalIgnoreCase));
        int next = (idx + 1) % options.Count;
        string chosen = options[next];

        if (string.IsNullOrEmpty(chosen)) _s.GameProfiles.Remove(key);
        else                              _s.GameProfiles[key] = chosen;

        btn.Content = string.IsNullOrEmpty(chosen) ? "OFF" : "ON";
        btn.ToolTip = string.IsNullOrEmpty(chosen) ? null : chosen;
        SaveSettings();
    }

    private void AddCustomGame_Click(object s, RoutedEventArgs e)
    {
        var raw = CustomGameBox.Text?.Trim();
        if (string.IsNullOrEmpty(raw)) return;

        var exe = raw.ToLowerInvariant();
        if (!exe.EndsWith(".exe")) exe += ".exe";

        bool known  = KnownGames.Any(g => g.exes.Contains(exe));
        bool exists = _s.CustomGames.Any(c => string.Equals(c, exe, StringComparison.OrdinalIgnoreCase));
        if (!known && !exists) _s.CustomGames.Add(exe);

        CustomGameBox.Text = "";
        SaveSettings();
        BuildGamesList();
    }

    private void RemoveCustomGame_Click(object s, RoutedEventArgs e)
    {
        if (s is not Button btn || btn.Tag is not string key) return;
        if (!key.StartsWith("custom:")) return;
        var exe = key["custom:".Length..];
        _s.CustomGames.RemoveAll(c => string.Equals(c, exe, StringComparison.OrdinalIgnoreCase));
        _s.GameProfiles.Remove(key);
        SaveSettings();
        BuildGamesList();
    }

    private void AutoSwitchToggle_Changed(object s, RoutedEventArgs e)
    {
        _s.AutoSwitchGames = AutoSwitchToggle.IsChecked == true;
        SaveSettings();
        if (_s.AutoSwitchGames) StartGameWatcher();
        else                    StopGameWatcher();
    }

    private void AutoRevertToggle_Changed(object s, RoutedEventArgs e)
    {
        _s.AutoRevertProfile = AutoRevertToggle.IsChecked == true;
        SaveSettings();
    }

    private void StartGameWatcher()
    {
        if (_gameWatchTimer != null) return;
        _gameWatchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        _gameWatchTimer.Tick += (_, _) => GameWatchTick();
        _gameWatchTimer.Start();
    }

    private void StopGameWatcher()
    {
        _gameWatchTimer?.Stop();
        _gameWatchTimer = null;
        _activeGameKey  = null;
        _preGameProfile = null;
    }

    private void GameWatchTick()
    {
        string fg = ForegroundExeName();
        string? matchKey = ResolveGameKey(fg);

        if (matchKey == _activeGameKey) return;

        if (matchKey != null)
        {
            if (_activeGameKey == null)
            {
                try { _preGameProfile = File.Exists(LastUsedFile) ? File.ReadAllText(LastUsedFile).Trim() : null; }
                catch { _preGameProfile = null; }
            }
            _activeGameKey = matchKey;
            if (_s.GameProfiles.TryGetValue(matchKey, out var name)) LoadProfileByName(name);
        }
        else
        {
            _activeGameKey = null;
            if (_s.AutoRevertProfile && !string.IsNullOrEmpty(_preGameProfile) && File.Exists(_preGameProfile))
                LoadProfile(_preGameProfile);
            _preGameProfile = null;
        }
    }

    private string? ResolveGameKey(string exe)
    {
        if (string.IsNullOrEmpty(exe)) return null;

        foreach (var (key, _, exes) in KnownGames)
            if (exes.Contains(exe) && _s.GameProfiles.ContainsKey(key))
                return key;

        var customKey = "custom:" + exe;
        if (_s.CustomGames.Any(c => string.Equals(c, exe, StringComparison.OrdinalIgnoreCase))
            && _s.GameProfiles.ContainsKey(customKey))
            return customKey;

        return null;
    }

    private void LoadProfileByName(string name)
    {
        var path = Path.Combine(ProfilesDir, name + ".json");
        if (File.Exists(path)) LoadProfile(path);
    }

    private static string ForegroundExeName()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return "";
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return "";
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return p.ProcessName.ToLowerInvariant() + ".exe";
        }
        catch { return ""; }
    }

    private static string DisplayKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return "—";
        var d = key.Replace("Key.", "").ToUpper();
        return d.Length > 8 ? d[..8] : d;
    }


    private void InitBuilderPanel()
    {
        RebuildBuilderGrid(_builderSize);

        BuilderPalettePanel.Children.Clear();
        foreach (var hex in BuilderPalette)
            BuilderPalettePanel.Children.Add(BuildBuilderSwatch(hex));

        LoadBuilderGridFromState();
        UpdateBuilderSwatchSelection();
        UpdateBuilderModeLabel();
        UpdateBuilderSizeButtons();
        UpdateBuilderToolButtons();
        UpdateBuilderMirrorButton();
        UpdateUndoRedoButtons();
    }

    private void RebuildBuilderGrid(int size)
    {
        _builderSize = size;
        _builderGrid = new string?[size, size];
        _builderPreview = null;
        _undoStack.Clear();
        _redoStack.Clear();
        UpdateUndoRedoButtons();

        if (_builderGridControl == null)
        {
            _builderGridControl = new BuilderGridControl();
            _builderGridControl.StrokeStart += OnBuilderStrokeStart;
            _builderGridControl.StrokeMove  += OnBuilderStrokeMove;
            _builderGridControl.StrokeEnd   += OnBuilderStrokeEnd;
            BuilderGridHost.Children.Add(_builderGridControl);
        }

        _builderGridControl.SetGrid(size, _builderGrid);
        _builderGridControl.SetPreview(null);
    }

    private UIElement BuildBuilderSwatch(string hex)
    {
        Color col;
        try   { col = (Color)ColorConverter.ConvertFromString(hex); }
        catch { col = Colors.White; }

        var b = new Border
        {
            Width           = 20,
            Height          = 20,
            Margin          = new Thickness(0, 0, 4, 4),
            BorderThickness = new Thickness(2),
            BorderBrush     = new SolidColorBrush(Colors.Transparent),
            Background      = new SolidColorBrush(col),
            Cursor          = Cursors.Hand,
            Tag             = hex
        };

        b.MouseLeftButtonDown += (_, _) =>
        {
            _builderColor = hex;
            if (_builderTool == BuilderTool.Eraser) _builderTool = BuilderTool.Pencil;
            UpdateBuilderSwatchSelection();
            UpdateBuilderModeLabel();
            UpdateBuilderToolButtons();
        };

        return b;
    }

    private void UpdateBuilderSwatchSelection()
    {
        foreach (UIElement el in BuilderPalettePanel.Children)
            if (el is Border b)
                b.BorderBrush = (b.Tag as string) == _builderColor
                    ? new SolidColorBrush(Color.FromRgb(0xf5, 0xf5, 0xf5))
                    : new SolidColorBrush(Colors.Transparent);
    }

    private void UpdateBuilderModeLabel()
    {
        if (BuilderModeLabel == null) return;
        BuilderModeLabel.Text = _builderTool == BuilderTool.Eraser
            ? "tool: eraser"
            : $"tool: {_builderTool.ToString().ToLower()}  {_builderColor}";
    }

    private void SetBuilderCell(int row, int col, string? value)
    {
        if (row < 0 || row >= _builderSize || col < 0 || col >= _builderSize) return;
        if (_builderGrid[row, col] == value) return;
        _builderGrid[row, col] = value;
    }

    private void PaintCellSym(int row, int col, string? value)
    {
        foreach (var (r, c) in SymCells(row, col))
            SetBuilderCell(r, c, value);
    }

    private IEnumerable<(int r, int c)> SymCells(int row, int col)
    {
        yield return (row, col);
        int mr = _builderSize - 1 - row;
        int mc = _builderSize - 1 - col;
        if (_builderSymmetry is BuilderSymmetry.MirrorX or BuilderSymmetry.Both) yield return (row, mc);
        if (_builderSymmetry is BuilderSymmetry.MirrorY or BuilderSymmetry.Both) yield return (mr, col);
        if (_builderSymmetry is BuilderSymmetry.Both) yield return (mr, mc);
    }

    private string?[,] CloneBuilderGrid() => (string?[,])_builderGrid.Clone();

    private void PushUndo()
    {
        _undoStack.Push(CloneBuilderGrid());
        _redoStack.Clear();
        UpdateUndoRedoButtons();
    }

    private void OnBuilderStrokeStart(int row, int col)
    {
        _strokeActive = true;
        _strokeStartR = row;
        _strokeStartC = col;
        PushUndo();

        switch (_builderTool)
        {
            case BuilderTool.Pencil:  PaintCellSym(row, col, _builderColor); CommitGridChange(); break;
            case BuilderTool.Eraser:  PaintCellSym(row, col, null);          CommitGridChange(); break;
            case BuilderTool.Fill:    FloodFill(row, col);                   CommitGridChange(); break;
            default:                  ShowShapePreview(row, col);            break;
        }
    }

    private void OnBuilderStrokeMove(int row, int col)
    {
        if (!_strokeActive) return;

        switch (_builderTool)
        {
            case BuilderTool.Pencil:  PaintCellSym(row, col, _builderColor); CommitGridChange(); break;
            case BuilderTool.Eraser:  PaintCellSym(row, col, null);          CommitGridChange(); break;
            case BuilderTool.Fill:    break;
            default:                  ShowShapePreview(row, col);            break;
        }
    }

    private void OnBuilderStrokeEnd(int row, int col)
    {
        if (!_strokeActive) return;
        _strokeActive = false;

        if (_builderTool is BuilderTool.Line or BuilderTool.Rect or BuilderTool.Ellipse)
        {
            foreach (var (r, c) in ShapeCells(_strokeStartR, _strokeStartC, row, col, _builderTool))
                PaintCellSym(r, c, _builderColor);
            _builderPreview = null;
            _builderGridControl?.SetPreview(null);
            CommitGridChange();
        }
    }

    private void BuilderUndo()
    {
        if (_undoStack.Count == 0) return;
        _redoStack.Push(CloneBuilderGrid());
        _builderGrid = _undoStack.Pop();
        _builderGridControl?.SetGrid(_builderSize, _builderGrid);
        FlushBuilderToState();
        UpdateUndoRedoButtons();
    }

    private void BuilderRedo()
    {
        if (_redoStack.Count == 0) return;
        _undoStack.Push(CloneBuilderGrid());
        _builderGrid = _redoStack.Pop();
        _builderGridControl?.SetGrid(_builderSize, _builderGrid);
        FlushBuilderToState();
        UpdateUndoRedoButtons();
    }

    private void UpdateUndoRedoButtons()
    {
        if (BuilderUndoBtn != null) BuilderUndoBtn.IsEnabled = _undoStack.Count > 0;
        if (BuilderRedoBtn != null) BuilderRedoBtn.IsEnabled = _redoStack.Count > 0;
    }

    private void BuilderUndo_Click(object s, RoutedEventArgs e) => BuilderUndo();
    private void BuilderRedo_Click(object s, RoutedEventArgs e) => BuilderRedo();

    private void BuilderMirror_Click(object s, RoutedEventArgs e)
    {
        _builderSymmetry = (BuilderSymmetry)(((int)_builderSymmetry + 1) % 4);
        UpdateBuilderMirrorButton();
    }

    private void UpdateBuilderMirrorButton()
    {
        if (BuilderMirrorBtn == null) return;
        BuilderMirrorBtn.Content = _builderSymmetry switch
        {
            BuilderSymmetry.MirrorX => "MIRROR: LEFT/RIGHT",
            BuilderSymmetry.MirrorY => "MIRROR: UP/DOWN",
            BuilderSymmetry.Both    => "MIRROR: 4-WAY",
            _                       => "MIRROR: OFF"
        };
    }

    private void OnBuilderPreviewKeyDown(object s, System.Windows.Input.KeyEventArgs e)
    {
        if (BuilderPanel == null || BuilderPanel.Visibility != Visibility.Visible) return;
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.Z)      { BuilderUndo(); e.Handled = true; }
        else if (e.Key == Key.Y) { BuilderRedo(); e.Handled = true; }
    }

    private void CommitGridChange()
    {
        _builderGridControl?.Refresh();
        FlushBuilderToState();
    }

    private void ShowShapePreview(int row, int col)
    {
        _builderPreview = new string?[_builderSize, _builderSize];
        foreach (var (br, bc) in ShapeCells(_strokeStartR, _strokeStartC, row, col, _builderTool))
            foreach (var (r, c) in SymCells(br, bc))
                if (r >= 0 && r < _builderSize && c >= 0 && c < _builderSize)
                    _builderPreview[r, c] = _builderColor;
        _builderGridControl?.SetPreview(_builderPreview);
    }

    private void FloodFill(int row, int col)
    {
        if (row < 0 || row >= _builderSize || col < 0 || col >= _builderSize) return;
        var target = _builderGrid[row, col];
        if (target == _builderColor) return;

        var stack = new Stack<(int r, int c)>();
        stack.Push((row, col));
        while (stack.Count > 0)
        {
            var (r, c) = stack.Pop();
            if (r < 0 || r >= _builderSize || c < 0 || c >= _builderSize) continue;
            if (_builderGrid[r, c] != target) continue;
            _builderGrid[r, c] = _builderColor;
            stack.Push((r + 1, c));
            stack.Push((r - 1, c));
            stack.Push((r, c + 1));
            stack.Push((r, c - 1));
        }
    }

    private static IEnumerable<(int r, int c)> ShapeCells(int r0, int c0, int r1, int c1, BuilderTool tool) =>
        tool switch
        {
            BuilderTool.Line    => LineCells(r0, c0, r1, c1),
            BuilderTool.Rect    => RectCells(r0, c0, r1, c1),
            BuilderTool.Ellipse => EllipseCells(r0, c0, r1, c1),
            _                   => System.Array.Empty<(int, int)>()
        };

    private static IEnumerable<(int r, int c)> LineCells(int r0, int c0, int r1, int c1)
    {
        int dr = System.Math.Abs(r1 - r0), dc = System.Math.Abs(c1 - c0);
        int sr = r0 < r1 ? 1 : -1, sc = c0 < c1 ? 1 : -1;
        int err = dc - dr;
        while (true)
        {
            yield return (r0, c0);
            if (r0 == r1 && c0 == c1) break;
            int e2 = err * 2;
            if (e2 > -dr) { err -= dr; c0 += sc; }
            if (e2 <  dc) { err += dc; r0 += sr; }
        }
    }

    private static IEnumerable<(int r, int c)> RectCells(int r0, int c0, int r1, int c1)
    {
        int rt = System.Math.Min(r0, r1), rb = System.Math.Max(r0, r1);
        int cl = System.Math.Min(c0, c1), cr = System.Math.Max(c0, c1);
        for (int c = cl; c <= cr; c++) { yield return (rt, c); yield return (rb, c); }
        for (int r = rt; r <= rb; r++) { yield return (r, cl); yield return (r, cr); }
    }

    private static IEnumerable<(int r, int c)> EllipseCells(int r0, int c0, int r1, int c1)
    {
        int rt = System.Math.Min(r0, r1), rb = System.Math.Max(r0, r1);
        int cl = System.Math.Min(c0, c1), cr = System.Math.Max(c0, c1);
        double rcen = (rt + rb) / 2.0, ccen = (cl + cr) / 2.0;
        double ra = System.Math.Max(0.5, (rb - rt) / 2.0), rb2 = System.Math.Max(0.5, (cr - cl) / 2.0);
        for (int deg = 0; deg < 360; deg++)
        {
            double rad = deg * System.Math.PI / 180.0;
            int r = (int)System.Math.Round(rcen + ra  * System.Math.Sin(rad));
            int c = (int)System.Math.Round(ccen + rb2 * System.Math.Cos(rad));
            yield return (r, c);
        }
    }

    private void FlushBuilderToState()
    {
        _s.CrCustomPixels.Clear();
        for (int r = 0; r < _builderSize; r++)
            for (int c = 0; c < _builderSize; c++)
                if (_builderGrid[r, c] is { } hex)
                    _s.CrCustomPixels.Add($"{r},{c},{hex}");

        _s.CrTemplate = "custom";
        _activeCustomName = null;
        UpdateTemplateTileSelection();
        RefreshCrosshairOverlay();
    }

    private void LoadBuilderGridFromState()
    {
        if (_builderGridControl == null) return;

        Array.Clear(_builderGrid, 0, _builderGrid.Length);

        foreach (var entry in _s.CrCustomPixels)
        {
            var parts = entry.Split(',');
            if (parts.Length < 3) continue;
            if (!int.TryParse(parts[0], out int row) || !int.TryParse(parts[1], out int col)) continue;
            if (row < 0 || row >= _builderSize || col < 0 || col >= _builderSize) continue;
            _builderGrid[row, col] = parts[2];
        }

        _builderGridControl.Refresh();
    }

    private void BuilderTool_Click(object s, RoutedEventArgs e)
    {
        if (s is not Button btn || btn.Tag is not string tag) return;
        if (!System.Enum.TryParse<BuilderTool>(tag, true, out var tool)) return;
        _builderTool = tool;
        UpdateBuilderModeLabel();
        UpdateBuilderToolButtons();
    }

    private void UpdateBuilderToolButtons()
    {
        if (BuilderToolPanel == null) return;
        foreach (UIElement el in BuilderToolPanel.Children)
        {
            if (el is not Button btn || btn.Tag is not string tag) continue;
            bool selected = System.Enum.TryParse<BuilderTool>(tag, true, out var tool) && tool == _builderTool;
            btn.Background = selected
                ? new SolidColorBrush(Color.FromRgb(0x2a, 0x2a, 0x2a))
                : new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14));
        }
    }

    private void BuilderClear_Click(object s, RoutedEventArgs e)
    {
        if (_builderGridControl == null) return;
        PushUndo();
        Array.Clear(_builderGrid, 0, _builderGrid.Length);
        _builderPreview = null;
        _builderGridControl.SetPreview(null);
        _builderGridControl.Refresh();
        FlushBuilderToState();
    }

    private void BuilderApply_Click(object s, RoutedEventArgs e)
    {
        FlushBuilderToState();
    }

    private void BuilderGridSize_Click(object s, RoutedEventArgs e)
    {
        if (s is not Button btn) return;
        if (!int.TryParse(btn.Tag as string, out int newSize)) return;
        if (newSize == _builderSize) return;
        _s.CrBuilderSize = newSize;
        RebuildBuilderGrid(newSize);
        LoadBuilderGridFromState();
        UpdateBuilderSizeButtons();
    }

    private void UpdateBuilderSizeButtons()
    {
        if (BuilderSizePanel == null) return;
        foreach (UIElement el in BuilderSizePanel.Children)
        {
            if (el is not Button btn) continue;
            if (!int.TryParse(btn.Tag as string, out int sz)) continue;
            btn.Background = sz == _builderSize
                ? new SolidColorBrush(Color.FromRgb(0x2a, 0x2a, 0x2a))
                : new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14));
        }
    }

    private void InitImagePanel()
    {
        if (ImagePanel == null) return;
        ImagePanel.Children.Clear();
        try
        {
            Directory.CreateDirectory(ImagesDir);
            foreach (var f in Directory.GetFiles(ImagesDir)
                         .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                      f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                      f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                      f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ||
                                      f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(f => f))
                ImagePanel.Children.Add(BuildImageTile(Path.GetFileName(f)));
        }
        catch { }
    }

    private UIElement BuildImageTile(string fileName)
    {
        var canvas = new Canvas
        {
            Width      = 64,
            Height     = 64,
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14))
        };

        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(ImageFullPath(fileName));
            bi.EndInit();
            bi.Freeze();

            double maxDim = 56.0;
            double scale = System.Math.Min(maxDim / bi.PixelWidth, maxDim / bi.PixelHeight);
            double w = bi.PixelWidth * scale;
            double h = bi.PixelHeight * scale;

            var img = new System.Windows.Controls.Image { Source = bi, Width = w, Height = h, Stretch = Stretch.Fill };
            Canvas.SetLeft(img, (64 - w) / 2.0);
            Canvas.SetTop(img, (64 - h) / 2.0);
            canvas.Children.Add(img);
        }
        catch { }

        var label = new TextBlock
        {
            Text                = fileName,
            FontFamily          = (FontFamily)FindResource("IBMPlexMono"),
            FontSize            = 8,
            Foreground          = new SolidColorBrush(Color.FromRgb(0x78, 0x78, 0x78)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin              = new Thickness(0, 4, 0, 0),
            MaxWidth            = 70,
            TextTrimming        = TextTrimming.CharacterEllipsis
        };

        var inner = new StackPanel();
        inner.Children.Add(canvas);
        inner.Children.Add(label);

        var del = new Button
        {
            Content             = "×",
            Width               = 16,
            Height              = 16,
            Padding             = new Thickness(0),
            FontFamily          = (FontFamily)FindResource("IBMPlexMono"),
            FontSize            = 11,
            Foreground          = new SolidColorBrush(Color.FromRgb(0x8a, 0x8a, 0x8a)),
            Background          = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x1e)),
            BorderThickness     = new Thickness(0),
            Cursor              = Cursors.Hand,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment   = System.Windows.VerticalAlignment.Top,
            Margin              = new Thickness(0, 2, 2, 0)
        };
        del.Click += (_, ev) => { ev.Handled = true; DeleteImage(fileName); };

        var grid = new Grid();
        grid.Children.Add(inner);
        grid.Children.Add(del);

        var border = new Border
        {
            Width           = 74,
            Height          = 88,
            Padding         = new Thickness(4),
            BorderThickness = new Thickness(1),
            BorderBrush     = new SolidColorBrush(_s.CrTemplate == "image" && _s.CrImagePath == fileName
                ? Color.FromRgb(0xf5, 0xf5, 0xf5)
                : Color.FromRgb(0x1e, 0x1e, 0x1e)),
            Margin          = new Thickness(0, 0, 6, 6),
            Cursor          = Cursors.Hand,
            Child           = grid
        };

        border.MouseLeftButtonDown += (_, _) =>
        {
            _s.CrImagePath = fileName;
            _s.CrTemplate  = "image";
            _activeCustomName = null;
            UpdateTemplateTileSelection();
            InitCustomTemplatesPanel();
            InitImagePanel();
            RefreshCrosshairOverlay();
        };

        return border;
    }

    private void DeleteImage(string fileName)
    {
        try { File.Delete(ImageFullPath(fileName)); } catch { }
        if (_s.CrImagePath == fileName)
        {
            _s.CrImagePath = "";
            _s.CrTemplate  = "";
            RefreshCrosshairOverlay();
        }
        InitImagePanel();
    }

    private void ImportImage_Click(object s, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title          = "Import crosshair image",
            Filter         = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*"
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            Directory.CreateDirectory(ImagesDir);
            var fileName = Path.GetFileName(dlg.FileName);
            var dest = Path.Combine(ImagesDir, fileName);

            if (File.Exists(dest))
            {
                fileName = $"{Path.GetFileNameWithoutExtension(fileName)}_{DateTime.Now:HHmmss}{Path.GetExtension(fileName)}";
                dest = Path.Combine(ImagesDir, fileName);
            }

            File.Copy(dlg.FileName, dest);

            _s.CrImagePath = fileName;
            _s.CrTemplate  = "image";
            _activeCustomName = null;
            UpdateTemplateTileSelection();
            InitCustomTemplatesPanel();
            InitImagePanel();
            RefreshCrosshairOverlay();
        }
        catch { }
    }

    private void DragMode_Click(object s, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_s.CrTemplate)) return;
        if (_s.CrFollowCursor) return;
        _dragMode = !_dragMode;
        _crOverlay?.SetDragMode(_dragMode);
        DragModeBtn.Content = _dragMode ? "CLICK TO FINISH" : "DRAG TO POSITION";
        DragModeBtn.Background = _dragMode
            ? new SolidColorBrush(Color.FromRgb(0xaa, 0x20, 0x20))
            : (Brush)FindResource("BgBtn");

        if (!_dragMode)
            RefreshCrosshairOverlay();
    }

    private void OnImageDragged(int dx, int dy)
    {
        _s.CrOffsetX = Math.Clamp(dx, -MaxOffsetX, MaxOffsetX);
        _s.CrOffsetY = Math.Clamp(dy, -MaxOffsetY, MaxOffsetY);
        UpdatePositionLabels();
        if (_dragMode) RefreshCrosshairOverlay();
    }

    private void InitKeybindsPanel()
    {
        BuildSlotList();
    }

    private void BuildSlotList()
    {
        if (SlotListPanel == null) return;
        SlotListPanel.Children.Clear();

        if (_s.CrSlots.Count == 0)
        {
            SlotListPanel.Children.Add(new TextBlock
            {
                Text         = "No keybinds yet. Configure a crosshair, then press BIND CURRENT CROSSHAIR.",
                FontFamily   = (FontFamily)FindResource("IBMPlexMono"),
                FontSize     = 9,
                Foreground   = new SolidColorBrush(Color.FromRgb(0x78, 0x78, 0x78)),
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 4, 0, 4)
            });
            return;
        }

        foreach (var slot in _s.CrSlots.ToList())
            SlotListPanel.Children.Add(BuildSlotRow(slot));
    }

    private UIElement BuildSlotRow(CrosshairSlot slot)
    {
        var thumb = BuildSlotThumb(slot);

        var keyBlock = new TextBlock
        {
            Text              = DisplayKey(slot.Key),
            FontFamily        = (FontFamily)FindResource("IBMPlexMono"),
            FontSize          = 12,
            FontWeight        = FontWeights.Bold,
            Foreground        = new SolidColorBrush(Color.FromRgb(0xf5, 0xf5, 0xf5)),
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        keyBlock.Margin = new Thickness(10, 0, 6, 0);

        var typeBlock = new TextBlock
        {
            Text              = SlotTypeLabel(slot),
            FontFamily        = (FontFamily)FindResource("IBMPlexMono"),
            FontSize          = 9,
            Foreground        = new SolidColorBrush(Color.FromRgb(0x78, 0x78, 0x78)),
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };

        var delBtn = new Button { Content = "×", Style = (Style)FindResource("DarkBtn"), Width = 36, Tag = slot.Key };
        delBtn.Click += (_, ev) => { ev.Handled = true; DeleteSlot(slot.Key); };

        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(thumb, 0);
        Grid.SetColumn(keyBlock, 1);
        Grid.SetColumn(typeBlock, 2);
        Grid.SetColumn(delBtn, 3);
        row.Children.Add(thumb);
        row.Children.Add(keyBlock);
        row.Children.Add(typeBlock);
        row.Children.Add(delBtn);

        return new Border
        {
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x1e)),
            BorderThickness = new Thickness(1),
            Background      = new SolidColorBrush(Color.FromRgb(0x0a, 0x0a, 0x0a)),
            Padding         = new Thickness(12, 10, 12, 10),
            Margin          = new Thickness(0, 0, 0, 6),
            Child           = row
        };
    }

    private static string SlotTypeLabel(CrosshairSlot slot)
    {
        if (slot.Template == "image") return "image";
        if (slot.Template == "custom") return "drawn";
        return slot.Template;
    }

    private UIElement BuildSlotThumb(CrosshairSlot slot)
    {
        var canvas = new Canvas
        {
            Width      = 42,
            Height     = 42,
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14))
        };

        if (slot.Template == "image")
            DrawThumbImage(canvas, slot.ImagePath);
        else if (slot.Template == "custom")
            DrawThumbCustom(canvas, slot.CustomPixels, slot.BuilderSize);
        else if (!string.IsNullOrEmpty(slot.Template))
            CrDraw.Draw(canvas, 21, 21, 0.34, slot.Color, slot.Outline, slot.OutlineSize, slot.Template, slot.Gap);

        return new Border
        {
            Width           = 42,
            Height          = 42,
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x1e)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Child           = canvas
        };
    }

    private void DeleteSlot(string key)
    {
        _s.CrSlots.RemoveAll(s => s.Key == key);
        SaveSettings();
        BuildSlotList();
    }

    private void BindSlot_Click(object s, RoutedEventArgs e)
    {
        if (_slotBindingMode) return;
        _slotBindingMode = true;
        BindSlotBtn.Content = "PRESS A KEY… (ESC TO CANCEL)";
        BindSlotBtn.Background = (Brush)FindResource("Red");
    }

    private void FinishSlotBinding(string key)
    {
        _slotBindingMode = false;
        BindSlotBtn.Content = "BIND CURRENT CROSSHAIR";
        BindSlotBtn.Background = (Brush)FindResource("BgBtn");

        if (key == "Key.esc") return;

        _s.CrSlots.RemoveAll(s => s.Key == key);

        var slot = new CrosshairSlot
        {
            Key          = key,
            Template     = _s.CrTemplate,
            Color        = _s.CrColor,
            Outline      = _s.CrOutline,
            OutlineSize  = _s.CrOutlineSize,
            Size         = _s.CrSize,
            Opacity      = _s.CrOpacity,
            Gap          = _s.CrGap,
            OffsetX      = _s.CrOffsetX,
            OffsetY      = _s.CrOffsetY,
            BuilderSize  = _s.CrBuilderSize,
            ImagePath    = _s.CrImagePath,
            CustomPixels = new List<string>(_s.CrCustomPixels)
        };

        _s.CrSlots.Add(slot);
        SaveSettings();
        BuildSlotList();
    }

    private void ApplySlot(CrosshairSlot slot)
    {
        _s.CrTemplate     = slot.Template;
        _s.CrColor        = slot.Color;
        _s.CrOutline      = slot.Outline;
        _s.CrOutlineSize  = slot.OutlineSize;
        _s.CrSize         = slot.Size;
        _s.CrOpacity      = slot.Opacity;
        _s.CrGap          = slot.Gap;
        _s.CrOffsetX      = slot.OffsetX;
        _s.CrOffsetY      = slot.OffsetY;
        _s.CrBuilderSize  = slot.BuilderSize;
        _s.CrImagePath    = slot.ImagePath;
        _s.CrCustomPixels = new List<string>(slot.CustomPixels);

        _builderSize = slot.BuilderSize;

        if (IsLoaded)
        {
            InitCrosshairsPanel();

            if (slot.Template == "custom" && _builderGridControl != null)
            {
                RebuildBuilderGrid(slot.BuilderSize);
                LoadBuilderGridFromState();
                UpdateBuilderSizeButtons();
            }

            RefreshCrosshairOverlay();
        }
    }
}

internal sealed class CustomTemplate
{
    public string name { get; set; } = "";
    public int size { get; set; } = 15;
    public List<string> pixels { get; set; } = new();
}

internal static class JsonExt
{
    public static bool TryGetProp(this JsonElement el, string name, out string value)
    {
        if (el.TryGetProperty(name, out var prop) && prop.GetString() is { } s)
        { value = s; return true; }
        value = "";
        return false;
    }
}

internal sealed class BuilderGridControl : FrameworkElement
{
    private const double FieldSize = 330.0;

    private static readonly Brush BgBrush  = Frozen(Color.FromRgb(0x0a, 0x0a, 0x0a));
    private static readonly Pen   LinePen  = FrozenPen(Color.FromRgb(0x33, 0x33, 0x33), 1.0);

    private readonly Dictionary<string, Brush> _brushCache = new();

    private int        _size;
    private string?[,]? _cells;
    private string?[,]? _preview;
    private bool       _drawing;
    private int        _lastR = -1, _lastC = -1;

    public event Action<int, int>? StrokeStart;
    public event Action<int, int>? StrokeMove;
    public event Action<int, int>? StrokeEnd;

    public BuilderGridControl()
    {
        Width  = FieldSize;
        Height = FieldSize;
        Cursor = System.Windows.Input.Cursors.Hand;
    }

    public void SetGrid(int size, string?[,] cells)
    {
        _size  = size;
        _cells = cells;
        InvalidateVisual();
    }

    public void SetPreview(string?[,]? preview)
    {
        _preview = preview;
        InvalidateVisual();
    }

    public void Refresh() => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(BgBrush, null, new Rect(0, 0, FieldSize, FieldSize));

        if (_cells == null || _size <= 0) return;

        double cell = FieldSize / _size;

        for (int r = 0; r < _size; r++)
            for (int c = 0; c < _size; c++)
                if (_cells[r, c] is { } hex)
                    dc.DrawRectangle(BrushFor(hex), null,
                        new Rect(c * cell, r * cell, cell, cell));

        if (_preview != null)
            for (int r = 0; r < _size; r++)
                for (int c = 0; c < _size; c++)
                    if (_preview[r, c] is { } phex)
                        dc.DrawRectangle(PreviewBrushFor(phex), null,
                            new Rect(c * cell, r * cell, cell, cell));

        var gl = new GuidelineSet();
        for (int i = 0; i <= _size; i++)
        {
            double p = Math.Round(i * cell) + 0.5;
            gl.GuidelinesX.Add(p);
            gl.GuidelinesY.Add(p);
        }
        gl.Freeze();
        dc.PushGuidelineSet(gl);
        for (int i = 0; i <= _size; i++)
        {
            double p = Math.Round(i * cell) + 0.5;
            dc.DrawLine(LinePen, new Point(p, 0), new Point(p, FieldSize));
            dc.DrawLine(LinePen, new Point(0, p), new Point(FieldSize, p));
        }
        dc.Pop();
    }

    protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!CellAt(e.GetPosition(this), out int r, out int c)) return;
        _drawing = true;
        _lastR = r; _lastC = c;
        CaptureMouse();
        StrokeStart?.Invoke(r, c);
        e.Handled = true;
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        if (!_drawing) return;
        if (!CellAt(e.GetPosition(this), out int r, out int c)) return;
        if (r == _lastR && c == _lastC) return;
        _lastR = r; _lastC = c;
        StrokeMove?.Invoke(r, c);
    }

    protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_drawing) return;
        _drawing = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (CellAt(e.GetPosition(this), out int r, out int c)) StrokeEnd?.Invoke(r, c);
        else StrokeEnd?.Invoke(_lastR, _lastC);
    }

    private bool CellAt(Point p, out int r, out int c)
    {
        r = c = -1;
        if (_size <= 0) return false;
        double cell = FieldSize / _size;
        c = System.Math.Clamp((int)(p.X / cell), 0, _size - 1);
        r = System.Math.Clamp((int)(p.Y / cell), 0, _size - 1);
        return true;
    }

    private Brush BrushFor(string hex)
    {
        if (_brushCache.TryGetValue(hex, out var b)) return b;
        Brush nb;
        try   { nb = Frozen((Color)ColorConverter.ConvertFromString(hex)); }
        catch { nb = Brushes.White; }
        _brushCache[hex] = nb;
        return nb;
    }

    private Brush PreviewBrushFor(string hex)
    {
        var key = "p:" + hex;
        if (_brushCache.TryGetValue(key, out var b)) return b;
        Brush nb;
        try
        {
            var col = (Color)ColorConverter.ConvertFromString(hex);
            nb = Frozen(Color.FromArgb(0x88, col.R, col.G, col.B));
        }
        catch { nb = Frozen(Color.FromArgb(0x88, 0xff, 0xff, 0xff)); }
        _brushCache[key] = nb;
        return nb;
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Pen FrozenPen(Color c, double thickness)
    {
        var p = new Pen(Frozen(c), thickness);
        p.Freeze();
        return p;
    }
}
