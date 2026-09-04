using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using Aiursoft.Kanban.Android.Oidc;
using Aiursoft.Kanban.SDK;
using Google.Android.Material.AppBar;
using Google.Android.Material.Button;
using Google.Android.Material.Card;
using Google.Android.Material.Dialog;
using Google.Android.Material.ProgressIndicator;
using Google.Android.Material.Snackbar;

namespace Aiursoft.Kanban.Android;

[Activity(Label = "Report details", Exported = false, Theme = "@style/AppTheme")]
public sealed class ReportDetailActivity : AppCompatActivity
{
    private const string ReportIdExtra = "report_id";
    private const string WeeklyExtra = "weekly";

    private View _root = null!;
    private MaterialToolbar _toolbar = null!;
    private global::AndroidX.Core.Widget.NestedScrollView _scroll = null!;
    private LinearLayout _content = null!;
    private CircularProgressIndicator _progress = null!;
    private Guid _reportId;
    private bool _weekly;
    private bool _busy;

    private AppSession Session => ((KanbanApplication)Application!).Session;
    private KanbanApiClient Api => Session.RequireApi();

    public static Intent CreateIntent(Context context, Guid reportId, bool weekly) =>
        new Intent(context, typeof(ReportDetailActivity))
            .PutExtra(ReportIdExtra, reportId.ToString())
            .PutExtra(WeeklyExtra, weekly);

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (!Session.IsAuthenticated)
        {
            ReturnToLogin();
            return;
        }

        _weekly = Intent?.GetBooleanExtra(WeeklyExtra, false) ?? false;
        if (!Guid.TryParse(Intent?.GetStringExtra(ReportIdExtra), out _reportId))
        {
            Finish();
            return;
        }

        SetContentView(Resource.Layout.activity_report_detail);
        _root = FindViewById<View>(Resource.Id.report_detail_root)!;
        _toolbar = FindViewById<MaterialToolbar>(Resource.Id.report_detail_toolbar)!;
        _scroll = FindViewById<global::AndroidX.Core.Widget.NestedScrollView>(Resource.Id.report_detail_scroll)!;
        _content = FindViewById<LinearLayout>(Resource.Id.report_detail_content)!;
        _progress = FindViewById<CircularProgressIndicator>(Resource.Id.report_detail_progress)!;

        _toolbar.SetOnApplyWindowInsetsListener(new ToolbarInsetListener(Dp(64)));
        _content.SetOnApplyWindowInsetsListener(
            new SystemBarInsetListener(Dp(16), Dp(18), Dp(16), Dp(32), false, true));
        _toolbar.NavigationContentDescription = "Back to reports";
        _toolbar.NavigationClick += (_, _) => Finish();
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_busy)
        {
            return;
        }

        try
        {
            SetBusy(true);
            if (_weekly)
            {
                var report = (await Api.GetWeeklyReportAsync(_reportId)).Report;
                Render(
                    "Weekly Report",
                    $"{report.WeekStart:yyyy-MM-dd} — {report.WeekStart.AddDays(6):yyyy-MM-dd}",
                    report.GeneratedAt,
                    report.Content,
                    false);
            }
            else
            {
                var report = (await Api.GetDailyReportAsync(_reportId)).Report;
                var isPlan = report.ReportType.Equals("Plan", StringComparison.OrdinalIgnoreCase);
                Render(
                    isPlan ? "Daily Plan" : "Daily Summary",
                    report.Date.ToString("yyyy-MM-dd"),
                    report.GeneratedAt,
                    report.Content,
                    isPlan);
            }
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Render(string title, string period, DateTime generatedAt, string reportContent, bool isPlan)
    {
        _content.RemoveAllViews();
        _toolbar.Title = title;
        _toolbar.Subtitle = period;

        var meta = SurfaceCard();
        var metaContent = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        metaContent.SetPadding(Dp(16), Dp(14), Dp(16), Dp(14));
        metaContent.AddView(Text(
            _weekly ? "WEEKLY REPORT" : isPlan ? "DAILY PLAN" : "DAILY SUMMARY",
            12,
            isPlan ? Resource.Color.brand_primary : Resource.Color.on_success_container,
            true));
        var periodView = Text(period, 19, Resource.Color.text_primary, true);
        var periodLayout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        periodLayout.SetMargins(0, Dp(7), 0, Dp(5));
        metaContent.AddView(periodView, periodLayout);
        metaContent.AddView(Text(
            $"Generated {generatedAt.ToLocalTime():yyyy-MM-dd HH:mm}",
            12,
            Resource.Color.text_secondary));
        meta.AddView(metaContent);
        Add(meta, 0, 14);

        var bodyCard = SurfaceCard();
        var body = Text(reportContent, 16, Resource.Color.text_primary);
        body.SetTextIsSelectable(true);
        body.SetLineSpacing(Dp(4), 1f);
        body.SetPadding(Dp(18), Dp(18), Dp(18), Dp(22));
        bodyCard.AddView(body);
        Add(bodyCard, 0, 0);
        if (_weekly)
        {
            var discard = new MaterialButton(this)
            {
                Text = "Discard report",
                TextSize = 14,
                CornerRadius = Dp(14)
            };
            discard.SetAllCaps(false);
            discard.Click += (_, _) => ShowDiscardDialog();
            Add(discard, 14, 0);
        }
    }

    private void ShowDiscardDialog()
    {
        var builder = new MaterialAlertDialogBuilder(this);
        builder.SetTitle("Discard weekly report?");
        builder.SetMessage("The report will be removed. The scheduled job may generate it again later.");
        builder.SetNegativeButton("Cancel", (_, _) => { });
        builder.SetPositiveButton("Discard", (_, _) => _ = DiscardAsync());
        builder.Show();
    }

    private async Task DiscardAsync()
    {
        try
        {
            SetBusy(true);
            await Api.DeleteWeeklyReportAsync(_reportId);
            Toast.MakeText(this, "Weekly report discarded.", ToastLength.Long)?.Show();
            Finish();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private MaterialCardView SurfaceCard()
    {
        var card = new MaterialCardView(this)
        {
            Radius = Dp(16),
            CardElevation = 0
        };
        card.SetCardBackgroundColor(GetColor(Resource.Color.surface));
        card.StrokeColor = GetColor(Resource.Color.outline);
        card.StrokeWidth = Dp(1);
        return card;
    }

    private TextView Text(string value, float size, int colorResource, bool bold = false)
    {
        var view = new TextView(this)
        {
            Text = value,
            TextSize = size,
            Typeface = bold ? Typeface.DefaultBold : Typeface.Default
        };
        view.SetTextColor(new global::Android.Graphics.Color(GetColor(colorResource)));
        return view;
    }

    private void Add(View view, int top, int bottom)
    {
        var layout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        layout.SetMargins(0, Dp(top), 0, Dp(bottom));
        _content.AddView(view, layout);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _progress.Visibility = busy ? ViewStates.Visible : ViewStates.Gone;
        _scroll.Enabled = !busy;
        _scroll.Alpha = busy ? 0.55f : 1f;
    }

    private void ShowError(Exception exception)
    {
        if (exception is KanbanAuthenticationRequiredException)
        {
            ReturnToLogin();
            return;
        }
        var message = exception.Message.Length > 160 ? exception.Message[..160] : exception.Message;
        var bar = Snackbar.Make(_root, message, Snackbar.LengthLong);
        bar.SetAction("Retry", ignoredView => _ = LoadAsync());
        bar.Show();
    }

    private void ReturnToLogin()
    {
        var intent = new Intent(this, typeof(LoginActivity));
        intent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.NewTask | ActivityFlags.ClearTask);
        StartActivity(intent);
        Finish();
    }

    private int Dp(int value) => (int)Math.Round(value * Resources!.DisplayMetrics!.Density);

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
