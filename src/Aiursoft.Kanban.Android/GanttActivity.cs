using Android.App;
using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using AndroidX.Core.Content;
using Aiursoft.Kanban.Android.Oidc;
using Aiursoft.Kanban.SDK;
using Aiursoft.Kanban.SDK.Models;
using Google.Android.Material.AppBar;
using Google.Android.Material.Button;
using Google.Android.Material.Card;
using Google.Android.Material.ProgressIndicator;
using Google.Android.Material.Snackbar;
using Color = Android.Graphics.Color;

namespace Aiursoft.Kanban.Android;

[Activity(Label = "Gantt chart", Exported = false, Theme = "@style/AppTheme")]
public sealed class GanttActivity : AppCompatActivity
{
    private const string BoardIdExtra = "board_id";
    private View _root = null!;
    private global::AndroidX.Core.Widget.NestedScrollView _scroll = null!;
    private MaterialToolbar _toolbar = null!;
    private MaterialButton _defaultMode = null!;
    private MaterialButton _plannedMode = null!;
    private MaterialButton _actualMode = null!;
    private MaterialButton _export = null!;
    private TextView _summary = null!;
    private HorizontalScrollView _horizontal = null!;
    private FrameLayout _chartHost = null!;
    private TextView _missingHeading = null!;
    private LinearLayout _missing = null!;
    private CircularProgressIndicator _progress = null!;
    private GanttResponse? _model;
    private NativeGanttView? _chart;
    private int _boardId;
    private GanttMode _mode = GanttMode.Default;
    private bool _loaded;
    private bool _busy;

    private AppSession Session => ((KanbanApplication)Application!).Session;
    private KanbanApiClient Api => Session.RequireApi();

    public static Intent CreateIntent(Context context, int boardId) =>
        new Intent(context, typeof(GanttActivity)).PutExtra(BoardIdExtra, boardId);

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (!Session.IsAuthenticated)
        {
            ReturnToLogin();
            return;
        }
        _boardId = Intent?.GetIntExtra(BoardIdExtra, 0) ?? 0;
        if (_boardId <= 0)
        {
            Finish();
            return;
        }

        SetContentView(Resource.Layout.activity_gantt);
        BindViews();
        ConfigureChrome();
        WireEvents();
        _ = LoadAsync();
    }

    protected override void OnResume()
    {
        base.OnResume();
        if (_loaded && !_busy)
        {
            _ = LoadAsync(showProgress: false);
        }
    }

    private void BindViews()
    {
        _root = FindViewById<View>(Resource.Id.gantt_root)!;
        _scroll = FindViewById<global::AndroidX.Core.Widget.NestedScrollView>(Resource.Id.gantt_scroll)!;
        _toolbar = FindViewById<MaterialToolbar>(Resource.Id.gantt_toolbar)!;
        _defaultMode = FindViewById<MaterialButton>(Resource.Id.gantt_default_button)!;
        _plannedMode = FindViewById<MaterialButton>(Resource.Id.gantt_planned_button)!;
        _actualMode = FindViewById<MaterialButton>(Resource.Id.gantt_actual_button)!;
        _export = FindViewById<MaterialButton>(Resource.Id.gantt_export_button)!;
        _summary = FindViewById<TextView>(Resource.Id.gantt_summary)!;
        _horizontal = FindViewById<HorizontalScrollView>(Resource.Id.gantt_horizontal_scroll)!;
        _chartHost = FindViewById<FrameLayout>(Resource.Id.gantt_chart_host)!;
        _missingHeading = FindViewById<TextView>(Resource.Id.gantt_missing_heading)!;
        _missing = FindViewById<LinearLayout>(Resource.Id.gantt_missing_list)!;
        _progress = FindViewById<CircularProgressIndicator>(Resource.Id.gantt_progress)!;
    }

    private void ConfigureChrome()
    {
        _toolbar.SetOnApplyWindowInsetsListener(new ToolbarInsetListener(Dp(64)));
        _scroll.SetOnApplyWindowInsetsListener(
            new SystemBarInsetListener(0, 0, 0, Dp(12), false, true));
        _toolbar.NavigationContentDescription = "Back to board";
        _toolbar.NavigationClick += (_, _) => Finish();
    }

    private void WireEvents()
    {
        _defaultMode.Click += (_, _) => SetMode(GanttMode.Default);
        _plannedMode.Click += (_, _) => SetMode(GanttMode.Planned);
        _actualMode.Click += (_, _) => SetMode(GanttMode.Actual);
        _export.Click += async (_, _) => await ExportAsync();
    }

    private async Task LoadAsync(bool showProgress = true)
    {
        if (_busy)
        {
            return;
        }
        try
        {
            SetBusy(true, showProgress);
            _model = await Api.GetGanttAsync(_boardId);
            _toolbar.Subtitle = _model.BoardName;
            Render();
            _loaded = true;
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            SetBusy(false, showProgress);
        }
    }

    private void SetMode(GanttMode mode)
    {
        if (_mode == mode)
        {
            return;
        }
        _mode = mode;
        Render();
    }

    private void Render()
    {
        var model = _model;
        if (model == null)
        {
            return;
        }
        StyleModeButton(_defaultMode, _mode == GanttMode.Default);
        StyleModeButton(_plannedMode, _mode == GanttMode.Planned);
        StyleModeButton(_actualMode, _mode == GanttMode.Actual);

        var bars = new List<GanttBar>();
        var missing = new List<(TaskCardDto Card, string Reason)>();
        foreach (var card in model.Cards)
        {
            var dates = ResolveDates(card, _mode);
            if (dates.HasValue)
            {
                bars.Add(new GanttBar(card, dates.Value.Start.Date, dates.Value.End.Date));
            }
            else
            {
                missing.Add((card, MissingReason(card, _mode)));
            }
        }
        bars = bars.OrderBy(bar => bar.Start).ThenByDescending(bar => bar.End).ToList();

        _chartHost.RemoveAllViews();
        _chart = null;
        if (bars.Count == 0)
        {
            _horizontal.Visibility = ViewStates.Gone;
            _export.Enabled = false;
            _summary.Text = model.Cards.Count == 0
                ? "This board has no cards."
                : "No cards have complete dates in this mode.";
        }
        else
        {
            _horizontal.Visibility = ViewStates.Visible;
            _summary.Text = $"{bars.Count} scheduled · {missing.Count} without complete dates";
            var chart = new NativeGanttView(this, OpenCard);
            chart.SetBars(bars);
            _chart = chart;
            _export.Enabled = !_busy;
            _chartHost.AddView(chart, new FrameLayout.LayoutParams(chart.ChartWidth, chart.ChartHeight));
            _horizontal.Post(() => ScrollChartToToday(chart));
        }

        _missing.RemoveAllViews();
        _missingHeading.Visibility = missing.Count == 0 ? ViewStates.Gone : ViewStates.Visible;
        _missing.Visibility = missing.Count == 0 ? ViewStates.Gone : ViewStates.Visible;
        foreach (var item in missing)
        {
            var layout = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent);
            layout.SetMargins(0, Dp(5), 0, Dp(5));
            _missing.AddView(MissingCard(item.Card, item.Reason), layout);
        }
    }

    private View MissingCard(TaskCardDto card, string reason)
    {
        var shell = new MaterialCardView(this)
        {
            Radius = Dp(14),
            CardElevation = 0,
            Clickable = true,
            Focusable = true
        };
        shell.SetCardBackgroundColor(GetColor(Resource.Color.surface));
        shell.StrokeColor = GetColor(Resource.Color.outline);
        shell.StrokeWidth = Dp(1);
        var body = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        body.SetPadding(Dp(14), Dp(12), Dp(14), Dp(12));
        body.AddView(Text(card.Title, 15, Resource.Color.text_primary, true));
        body.AddView(Text($"{card.ColumnName} · {reason}", 12, Resource.Color.text_secondary));
        shell.AddView(body);
        shell.Click += (_, _) => OpenCard(card.Id);
        return shell;
    }

    private void StyleModeButton(MaterialButton button, bool selected)
    {
        button.BackgroundTintList = ColorStateList.ValueOf(ColorOf(
            selected ? Resource.Color.brand_container : Resource.Color.surface));
        button.SetTextColor(ColorStateList.ValueOf(ColorOf(
            selected ? Resource.Color.on_brand_container : Resource.Color.text_primary)));
        button.StrokeColor = ColorStateList.ValueOf(ColorOf(
            selected ? Resource.Color.brand_primary : Resource.Color.outline));
        button.StrokeWidth = Dp(selected ? 2 : 1);
    }

    private void ScrollChartToToday(NativeGanttView chart)
    {
        var todayX = chart.TodayX;
        if (todayX <= 0)
        {
            return;
        }
        _horizontal.SmoothScrollTo(Math.Max(0, todayX - _horizontal.Width / 2), 0);
    }

    private void OpenCard(int cardId) => StartActivity(CardDetailActivity.CreateIntent(this, cardId));

    private async Task ExportAsync()
    {
        var chart = _chart;
        var model = _model;
        if (_busy || chart == null || model == null)
        {
            Snackbar.Make(_root, "No exportable chart is available in this mode.", Snackbar.LengthLong).Show();
            return;
        }
        const long maxPixels = 12_000_000;
        if (chart.ChartWidth > 16_384 || chart.ChartHeight > 16_384 ||
            (long)chart.ChartWidth * chart.ChartHeight > maxPixels)
        {
            Snackbar.Make(_root,
                "The chart is too large to export as one PNG image.",
                Snackbar.LengthLong).Show();
            return;
        }

        Bitmap? bitmap = null;
        try
        {
            SetBusy(true, showProgress: true);
            _export.Text = "Exporting…";
            bitmap = Bitmap.CreateBitmap(
                chart.ChartWidth,
                chart.ChartHeight,
                Bitmap.Config.Argb8888!);
            using (var canvas = new Canvas(bitmap))
            {
                chart.Draw(canvas);
            }

            var exportDirectory = new Java.IO.File(CacheDir, "exports");
            if (!exportDirectory.Exists() && !exportDirectory.Mkdirs())
            {
                throw new IOException("Could not create the export directory.");
            }
            var modeName = _mode.ToString().ToLowerInvariant();
            var fileName = $"{SafeFileName(model.BoardName)}-{modeName}-gantt.png";
            var file = new Java.IO.File(exportDirectory, fileName);
            await Task.Run(async () =>
            {
                await using var stream = new FileStream(file.AbsolutePath!, FileMode.Create, FileAccess.Write);
                if (!bitmap.Compress(Bitmap.CompressFormat.Png!, 100, stream))
                {
                    throw new IOException("Could not encode the Gantt chart as PNG.");
                }
                await stream.FlushAsync();
            });

            var uri = FileProvider.GetUriForFile(
                this,
                $"{PackageName}.fileprovider",
                file);
            var share = new Intent(Intent.ActionSend);
            share.SetType("image/png");
            share.PutExtra(Intent.ExtraStream, uri);
            share.ClipData = ClipData.NewRawUri("Gantt chart", uri);
            share.AddFlags(ActivityFlags.GrantReadUriPermission);
            StartActivity(Intent.CreateChooser(share, "Share Gantt chart"));
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            bitmap?.Recycle();
            bitmap?.Dispose();
            _export.Text = "Export PNG";
            SetBusy(false, showProgress: true);
        }
    }

    private static string SafeFileName(string value)
    {
        var safe = new string(value
            .Trim()
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_'
                ? character
                : '-')
            .ToArray())
            .Trim('-');
        return string.IsNullOrWhiteSpace(safe) ? "kanban" : safe;
    }

    private static (DateTime Start, DateTime End)? ResolveDates(TaskCardDto card, GanttMode mode)
    {
        DateTime? start;
        DateTime? end;
        switch (mode)
        {
            case GanttMode.Planned:
                start = card.PlannedStartTime;
                end = card.DueDate;
                break;
            case GanttMode.Actual:
                start = card.ActualStartTime;
                end = card.ActualEndTime;
                break;
            default:
                if (card.ActualStartTime.HasValue && card.ActualEndTime.HasValue)
                {
                    start = card.ActualStartTime;
                    end = card.ActualEndTime;
                }
                else
                {
                    start = card.PlannedStartTime;
                    end = card.DueDate;
                }
                break;
        }
        if (!start.HasValue || !end.HasValue)
        {
            return null;
        }
        return end.Value < start.Value
            ? (start.Value, start.Value)
            : (start.Value, end.Value);
    }

    private static string MissingReason(TaskCardDto card, GanttMode mode) => mode switch
    {
        GanttMode.Planned when !card.PlannedStartTime.HasValue && !card.DueDate.HasValue =>
            "Missing planned start and due date",
        GanttMode.Planned when !card.PlannedStartTime.HasValue => "Missing planned start date",
        GanttMode.Planned => "Missing due date",
        GanttMode.Actual when !card.ActualStartTime.HasValue && !card.ActualEndTime.HasValue =>
            "Missing actual start and end date",
        GanttMode.Actual when !card.ActualStartTime.HasValue => "Missing actual start date",
        GanttMode.Actual => "Missing actual end date",
        _ => "Missing complete planned or actual dates"
    };

    private TextView Text(string value, float size, int colorResource, bool bold = false)
    {
        var view = new TextView(this)
        {
            Text = value,
            TextSize = size,
            Typeface = bold ? Typeface.DefaultBold : Typeface.Default
        };
        view.SetTextColor(ColorOf(colorResource));
        return view;
    }

    private void SetBusy(bool busy, bool showProgress)
    {
        _busy = busy;
        _progress.Visibility = busy && showProgress ? ViewStates.Visible : ViewStates.Gone;
        _scroll.Alpha = busy ? 0.55f : 1f;
        _defaultMode.Enabled = !busy;
        _plannedMode.Enabled = !busy;
        _actualMode.Enabled = !busy;
        _export.Enabled = !busy && _chart != null;
    }

    private void ShowError(Exception exception)
    {
        if (exception is KanbanAuthenticationRequiredException)
        {
            ReturnToLogin();
            return;
        }
        Snackbar.Make(_root, FriendlyMessage(exception), Snackbar.LengthLong).Show();
    }

    private void ReturnToLogin()
    {
        var intent = new Intent(this, typeof(LoginActivity));
        intent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.NewTask | ActivityFlags.ClearTask);
        StartActivity(intent);
        Finish();
    }

    private static string FriendlyMessage(Exception exception) =>
        exception.Message.Length > 180 ? exception.Message[..180] : exception.Message;

    private Color ColorOf(int colorResource) => new(GetColor(colorResource));

    private int Dp(int value) => (int)Math.Round(value * Resources!.DisplayMetrics!.Density);

    private enum GanttMode
    {
        Default,
        Planned,
        Actual
    }

    private sealed record GanttBar(TaskCardDto Card, DateTime Start, DateTime End);

    private sealed class NativeGanttView : View
    {
        private readonly Action<int> _openCard;
        private readonly Context _context;
        private readonly Paint _paint = new(PaintFlags.AntiAlias);
        private readonly int _labelWidth;
        private readonly int _rowHeight;
        private readonly int _headerHeight;
        private List<GanttBar> _bars = [];
        private DateTime _minimum;
        private DateTime _maximum;
        private int _timelineWidth;

        public NativeGanttView(Context context, Action<int> openCard) : base(context)
        {
            _openCard = openCard;
            _context = context;
            _labelWidth = Dp(context, 168);
            _rowHeight = Dp(context, 62);
            _headerHeight = Dp(context, 50);
            Clickable = true;
        }

        public int ChartWidth { get; private set; }
        public int ChartHeight { get; private set; }

        public int TodayX => DateTime.Today < _minimum || DateTime.Today > _maximum
            ? -1
            : _labelWidth + DayX(DateTime.Today);

        public void SetBars(List<GanttBar> bars)
        {
            _bars = bars;
            var today = DateTime.Today;
            _minimum = bars.Min(bar => bar.Start).AddDays(-2);
            _maximum = bars.Max(bar => bar.End).AddDays(3);
            if (today < _minimum)
            {
                _minimum = today.AddDays(-2);
            }
            if (today > _maximum)
            {
                _maximum = today.AddDays(3);
            }
            var days = Math.Max(1, (_maximum - _minimum).Days + 1);
            var preferredWidth = days * Dp(_context, 28);
            _timelineWidth = Math.Clamp(preferredWidth, Dp(_context, 420), Dp(_context, 28000));
            ChartWidth = _labelWidth + _timelineWidth;
            ChartHeight = _headerHeight + _bars.Count * _rowHeight;
            RequestLayout();
            Invalidate();
        }

        protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec) =>
            SetMeasuredDimension(ChartWidth, ChartHeight);

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            _paint.SetStyle(Paint.Style.Fill);
            _paint.Color = new Color(_context.GetColor(Resource.Color.surface));
            canvas.DrawRect(0, 0, Width, Height, _paint);

            _paint.Color = new Color(_context.GetColor(Resource.Color.surface_variant));
            canvas.DrawRect(0, 0, Width, _headerHeight, _paint);
            DrawText(canvas, "Cards", Dp(_context, 12), Dp(_context, 30), 13, true,
                Resource.Color.text_primary);

            var days = Math.Max(1, (_maximum - _minimum).Days + 1);
            var step = days switch
            {
                <= 45 => 5,
                <= 120 => 14,
                <= 400 => 30,
                <= 1200 => 90,
                _ => 365
            };
            for (var day = 0; day < days; day += step)
            {
                var date = _minimum.AddDays(day);
                var x = _labelWidth + DayX(date);
                _paint.Color = new Color(_context.GetColor(Resource.Color.outline));
                _paint.StrokeWidth = Dp(_context, 1);
                canvas.DrawLine(x, 0, x, Height, _paint);
                DrawText(canvas, date.ToString(days > 400 ? "yyyy" : "MMM d"),
                    x + Dp(_context, 4), Dp(_context, 30), 11, false, Resource.Color.text_secondary);
            }

            if (TodayX > 0)
            {
                _paint.Color = Color.ParseColor("#EF4444");
                _paint.StrokeWidth = Dp(_context, 2);
                canvas.DrawLine(TodayX, 0, TodayX, Height, _paint);
            }

            for (var index = 0; index < _bars.Count; index++)
            {
                var item = _bars[index];
                var top = _headerHeight + index * _rowHeight;
                if (index % 2 == 1)
                {
                    _paint.Color = new Color(_context.GetColor(Resource.Color.surface_variant));
                    canvas.DrawRect(0, top, Width, top + _rowHeight, _paint);
                }
                _paint.Color = new Color(_context.GetColor(Resource.Color.outline));
                _paint.StrokeWidth = Dp(_context, 1);
                canvas.DrawLine(0, top + _rowHeight, Width, top + _rowHeight, _paint);
                DrawText(canvas, Ellipsize(item.Card.Title, 22), Dp(_context, 12), top + Dp(_context, 25),
                    13, true, Resource.Color.text_primary);
                DrawText(canvas, Ellipsize(item.Card.ColumnName, 24), Dp(_context, 12), top + Dp(_context, 46),
                    11, false, Resource.Color.text_secondary);

                var left = _labelWidth + DayX(item.Start);
                var right = _labelWidth + DayX(item.End.AddDays(1));
                if (right - left < Dp(_context, 12))
                {
                    right = left + Dp(_context, 12);
                }
                _paint.Color = StatusColor(item.Card.Status);
                var rect = new RectF(left, top + Dp(_context, 13), right, top + _rowHeight - Dp(_context, 13));
                canvas.DrawRoundRect(rect, Dp(_context, 8), Dp(_context, 8), _paint);
                if (right - left > Dp(_context, 70))
                {
                    DrawText(canvas, Ellipsize(item.Card.Title, 26), left + Dp(_context, 8), top + Dp(_context, 38),
                        11, true, global::Android.Resource.Color.White);
                }
            }
        }

        public override bool OnTouchEvent(MotionEvent? motionEvent)
        {
            if (motionEvent?.Action == MotionEventActions.Up)
            {
                var index = ((int)motionEvent.GetY() - _headerHeight) / _rowHeight;
                if (index >= 0 && index < _bars.Count)
                {
                    PerformClick();
                    _openCard(_bars[index].Card.Id);
                    return true;
                }
            }
            return true;
        }

        public override bool PerformClick()
        {
            base.PerformClick();
            return true;
        }

        private int DayX(DateTime date)
        {
            var totalDays = Math.Max(1, (_maximum - _minimum).TotalDays + 1);
            return (int)Math.Round((date - _minimum).TotalDays / totalDays * _timelineWidth);
        }

        private void DrawText(
            Canvas canvas,
            string value,
            float x,
            float y,
            float sp,
            bool bold,
            int colorResource)
        {
            _paint.SetStyle(Paint.Style.Fill);
            _paint.Color = new Color(_context.GetColor(colorResource));
            _paint.TextSize = Sp(_context, sp);
            _paint.SetTypeface(bold ? Typeface.DefaultBold : Typeface.Default);
            canvas.DrawText(value, x, y, _paint);
        }

        private Color StatusColor(string status) => status switch
        {
            "Completed" => Color.ParseColor("#16A34A"),
            "InProgress" => Color.ParseColor("#2563EB"),
            _ => Color.ParseColor("#64748B")
        };

        private static string Ellipsize(string value, int maxLength) => value.Length <= maxLength
            ? value
            : value[..(maxLength - 1)] + "…";

        private static int Dp(Context context, int value) =>
            (int)Math.Round(value * context.Resources!.DisplayMetrics!.Density);

        private static float Sp(Context context, float value) =>
            value * context.Resources!.DisplayMetrics!.Density * context.Resources.Configuration!.FontScale;
    }

    private sealed class ToolbarInsetListener(int contentHeight)
        : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View view, WindowInsets insets)
        {
            var top = OperatingSystem.IsAndroidVersionAtLeast(30)
                ? insets.GetInsets(WindowInsets.Type.SystemBars()).Top
                : insets.SystemWindowInsetTop;
            var parameters = view.LayoutParameters!;
            parameters.Height = contentHeight + top;
            view.LayoutParameters = parameters;
            view.SetPadding(view.PaddingLeft, top, view.PaddingRight, view.PaddingBottom);
            return insets;
        }
    }
}
