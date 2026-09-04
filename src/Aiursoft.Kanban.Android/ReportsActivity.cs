using Android.App;
using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using Aiursoft.AiurProtocol.Models;
using Aiursoft.Kanban.Android.Oidc;
using Aiursoft.Kanban.SDK;
using Aiursoft.Kanban.SDK.Models;
using Google.Android.Material.AppBar;
using Google.Android.Material.Button;
using Google.Android.Material.Card;
using Google.Android.Material.Dialog;
using Google.Android.Material.ProgressIndicator;
using Google.Android.Material.Snackbar;
using Color = Android.Graphics.Color;

namespace Aiursoft.Kanban.Android;

[Activity(Label = "Reports", Exported = false, Theme = "@style/AppTheme")]
public sealed class ReportsActivity : AppCompatActivity
{
    private const string WeeklyExtra = "weekly";

    private View _root = null!;
    private MaterialToolbar _toolbar = null!;
    private global::AndroidX.Core.Widget.NestedScrollView _scroll = null!;
    private LinearLayout _content = null!;
    private CircularProgressIndicator _progress = null!;
    private bool _weekly;
    private int _page = 1;
    private bool _busy;

    private AppSession Session => ((KanbanApplication)Application!).Session;
    private KanbanApiClient Api => Session.RequireApi();

    public static Intent CreateIntent(Context context, bool weekly) =>
        new Intent(context, typeof(ReportsActivity)).PutExtra(WeeklyExtra, weekly);

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (!Session.IsAuthenticated)
        {
            ReturnToLogin();
            return;
        }

        _weekly = Intent?.GetBooleanExtra(WeeklyExtra, false) ?? false;
        SetContentView(Resource.Layout.activity_reports);
        _root = FindViewById<View>(Resource.Id.reports_root)!;
        _toolbar = FindViewById<MaterialToolbar>(Resource.Id.reports_toolbar)!;
        _scroll = FindViewById<global::AndroidX.Core.Widget.NestedScrollView>(Resource.Id.reports_scroll)!;
        _content = FindViewById<LinearLayout>(Resource.Id.reports_content)!;
        _progress = FindViewById<CircularProgressIndicator>(Resource.Id.reports_progress)!;

        _toolbar.SetOnApplyWindowInsetsListener(new ToolbarInsetListener(Dp(64)));
        _content.SetOnApplyWindowInsetsListener(
            new SystemBarInsetListener(Dp(16), Dp(18), Dp(16), Dp(32), false, true));
        _toolbar.NavigationContentDescription = "Back to Kanban";
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
                RenderWeekly(await Api.GetWeeklyReportsAsync(_page));
            }
            else
            {
                RenderDaily(await Api.GetDailyReportsAsync(_page));
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

    private void RenderDaily(DailyReportListResponse response)
    {
        _page = response.CurrentPage;
        var reports = response.Reports.Select(report => new ReportListItem(
            report.Id,
            report.ReportType.Equals("Plan", StringComparison.OrdinalIgnoreCase) ? "PLAN" : "SUMMARY",
            report.Date.ToString("yyyy-MM-dd"),
            $"Generated {FormatDateTime(report.GeneratedAt)}",
            report.Content,
            false,
            report.ReportType.Equals("Plan", StringComparison.OrdinalIgnoreCase))).ToList();
        Render(
            "Daily Assistant",
            "Plans and summaries generated from your accessible boards.",
            reports,
            response.CurrentPage,
            response.TotalPages,
            response.TotalCount,
            DailyActionCard(response));
    }

    private void RenderWeekly(WeeklyReportListResponse response)
    {
        _page = response.CurrentPage;
        var reports = response.Reports.Select(report => new ReportListItem(
            report.Id,
            "WEEKLY",
            $"{report.WeekStart:yyyy-MM-dd} — {report.WeekStart.AddDays(6):yyyy-MM-dd}",
            $"Generated {FormatDateTime(report.GeneratedAt)}",
            report.Content,
            true,
            false)).ToList();
        Render(
            "Weekly Report",
            "A weekly view of the work completed across your boards.",
            reports,
            response.CurrentPage,
            response.TotalPages,
            response.TotalCount,
            WeeklyActionCard(response));
    }

    private void Render(
        string title,
        string description,
        IReadOnlyList<ReportListItem> reports,
        int currentPage,
        int totalPages,
        int totalCount,
        View? actionCard)
    {
        _content.RemoveAllViews();
        _toolbar.Title = title;
        _toolbar.Subtitle = totalCount == 1 ? "1 report" : $"{totalCount} reports";

        var heading = Text(title, 26, Resource.Color.text_primary, true);
        Add(heading, 0, 5);
        Add(Text(description, 14, Resource.Color.text_secondary), 0, 16);
        Add(ReportTabs(), 0, 20);
        if (actionCard != null)
        {
            Add(actionCard, 0, 18);
        }

        if (reports.Count == 0)
        {
            var empty = SurfaceCard();
            var emptyContent = new LinearLayout(this)
            {
                Orientation = global::Android.Widget.Orientation.Vertical
            };
            emptyContent.SetGravity(GravityFlags.Center);
            emptyContent.SetPadding(Dp(24), Dp(34), Dp(24), Dp(34));
            emptyContent.AddView(Text("No reports yet", 19, Resource.Color.text_primary, true));
            var message = Text(
                _weekly
                    ? "Your generated weekly reports will appear here."
                    : "Your generated plans and summaries will appear here.",
                14,
                Resource.Color.text_secondary);
            message.Gravity = GravityFlags.Center;
            var messageLayout = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);
            messageLayout.SetMargins(0, Dp(8), 0, 0);
            emptyContent.AddView(message, messageLayout);
            empty.AddView(emptyContent);
            Add(empty, 0, 18);
        }
        else
        {
            foreach (var report in reports)
            {
                Add(ReportCard(report), 0, 12);
            }
        }

        Add(Pagination(currentPage, totalPages), 6, 0);
        _scroll.Post(() => _scroll.ScrollTo(0, 0));
    }

    private View? DailyActionCard(DailyReportListResponse response)
    {
        var isPlan = response.CanGeneratePlan;
        var isSummary = response.CanGenerateSummary;
        if (!isPlan && !isSummary)
        {
            return null;
        }

        var current = isPlan ? response.TodayPlan : response.TodaySummary;
        var type = isPlan ? "plan" : "summary";
        var title = isPlan ? "Today's Plan" : "Today's Summary";
        var message = current == null
            ? isPlan
                ? "Generate a morning plan from your accessible cards."
                : "Generate an afternoon summary of today's work."
            : $"Generated {FormatDateTime(current.GeneratedAt)}";
        var card = ActionSurface(title, message);
        var body = (LinearLayout)card.GetChildAt(0)!;
        var actions = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        if (current != null)
        {
            var view = ActionButton("View", primary: false);
            actions.AddView(view, new LinearLayout.LayoutParams(0, Dp(48), 1));
            view.Click += (_, _) => StartActivity(
                ReportDetailActivity.CreateIntent(this, current.Id, weekly: false));
        }
        var generate = ActionButton(current == null ? "Generate" : "Regenerate", primary: true);
        var generateLayout = new LinearLayout.LayoutParams(0, Dp(48), 1);
        if (current != null)
        {
            generateLayout.SetMargins(Dp(10), 0, 0, 0);
        }
        actions.AddView(generate, generateLayout);
        generate.Click += async (_, _) => await GenerateDailyAsync(type);
        AddTo(body, actions, 14, 0);
        return card;
    }

    private View WeeklyActionCard(WeeklyReportListResponse response)
    {
        var current = response.CurrentWeekReport;
        var period = $"{response.CurrentWeekStart:yyyy-MM-dd} — {response.CurrentWeekStart.AddDays(6):yyyy-MM-dd}";
        var message = current != null
            ? period
            : response.CanGenerate
                ? "Your report is ready to generate."
                : "Weekly reports become available every Friday afternoon (UTC+8).";
        var card = ActionSurface("This Week's Report", message);
        if (current == null && !response.CanGenerate)
        {
            return card;
        }

        var body = (LinearLayout)card.GetChildAt(0)!;
        var actions = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        if (current != null)
        {
            var view = ActionButton("View", primary: false);
            actions.AddView(view, new LinearLayout.LayoutParams(0, Dp(48), 1));
            view.Click += (_, _) => StartActivity(
                ReportDetailActivity.CreateIntent(this, current.Id, weekly: true));
            var discard = ActionButton("Discard", primary: false);
            var discardLayout = new LinearLayout.LayoutParams(0, Dp(48), 1);
            discardLayout.SetMargins(Dp(10), 0, 0, 0);
            actions.AddView(discard, discardLayout);
            discard.Click += (_, _) => ShowDiscardDialog(current.Id);
        }
        else
        {
            var generate = ActionButton("Generate report", primary: true);
            actions.AddView(generate, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                Dp(48)));
            generate.Click += async (_, _) => await GenerateWeeklyAsync();
        }
        AddTo(body, actions, 14, 0);
        return card;
    }

    private MaterialCardView ActionSurface(string title, string message)
    {
        var card = SurfaceCard();
        var body = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        body.SetPadding(Dp(16), Dp(15), Dp(16), Dp(16));
        body.AddView(Text(title, 18, Resource.Color.text_primary, true));
        AddTo(body, Text(message, 13, Resource.Color.text_secondary), 6, 0);
        card.AddView(body);
        return card;
    }

    private MaterialButton ActionButton(string text, bool primary)
    {
        var button = primary
            ? new MaterialButton(this)
            : new MaterialButton(this, null, global::Android.Resource.Attribute.BorderlessButtonStyle);
        button.Text = text;
        button.TextSize = 13;
        button.CornerRadius = Dp(14);
        button.SetAllCaps(false);
        if (!primary)
        {
            button.SetTextColor(ColorStateList.ValueOf(ColorOf(Resource.Color.brand_primary)));
        }
        return button;
    }

    private View ReportTabs()
    {
        var row = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        var daily = TabButton("Daily Assistant", !_weekly);
        var weekly = TabButton("Weekly Report", _weekly);
        row.AddView(daily, new LinearLayout.LayoutParams(0, Dp(48), 1));
        var weeklyLayout = new LinearLayout.LayoutParams(0, Dp(48), 1);
        weeklyLayout.SetMargins(Dp(10), 0, 0, 0);
        row.AddView(weekly, weeklyLayout);
        daily.Click += (_, _) => SwitchReportType(weekly: false);
        weekly.Click += (_, _) => SwitchReportType(weekly: true);
        return row;
    }

    private MaterialButton TabButton(string text, bool selected)
    {
        var button = new MaterialButton(this)
        {
            Text = text,
            TextSize = 13,
            CornerRadius = Dp(14)
        };
        button.SetAllCaps(false);
        button.BackgroundTintList = ColorStateList.ValueOf(ColorOf(
            selected ? Resource.Color.brand_container : Resource.Color.surface_variant));
        button.SetTextColor(ColorStateList.ValueOf(ColorOf(
            selected ? Resource.Color.on_brand_container : Resource.Color.text_primary)));
        return button;
    }

    private View ReportCard(ReportListItem report)
    {
        var card = SurfaceCard();
        card.Clickable = true;
        card.Focusable = true;
        card.ContentDescription = $"{report.Badge}, {report.Period}. Open report.";

        var content = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        content.SetPadding(Dp(16), Dp(15), Dp(16), Dp(16));

        var header = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        header.SetGravity(GravityFlags.CenterVertical);
        var badge = Badge(
            report.Badge,
            report.IsPlan ? Resource.Color.brand_container : Resource.Color.success_container,
            report.IsPlan ? Resource.Color.on_brand_container : Resource.Color.on_success_container);
        header.AddView(badge);
        var period = Text(report.Period, 15, Resource.Color.text_primary, true);
        period.Gravity = GravityFlags.End;
        header.AddView(period, new LinearLayout.LayoutParams(
            0, ViewGroup.LayoutParams.WrapContent, 1));
        content.AddView(header);

        var preview = Text(Preview(report.Content), 15, Resource.Color.text_primary);
        preview.SetMaxLines(5);
        preview.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;
        var previewLayout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        previewLayout.SetMargins(0, Dp(14), 0, Dp(12));
        content.AddView(preview, previewLayout);
        content.AddView(Text(report.GeneratedLabel, 12, Resource.Color.text_secondary));
        card.AddView(content);
        card.Click += (_, _) => StartActivity(
            ReportDetailActivity.CreateIntent(this, report.Id, report.IsWeekly));
        return card;
    }

    private View Pagination(int currentPage, int totalPages)
    {
        var row = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        row.SetGravity(GravityFlags.CenterVertical);
        var previous = PageButton("Previous", currentPage > 1);
        var next = PageButton("Next", currentPage < totalPages);
        var status = Text($"Page {currentPage} of {totalPages}", 13, Resource.Color.text_secondary, true);
        status.Gravity = GravityFlags.Center;
        row.AddView(previous, new LinearLayout.LayoutParams(0, Dp(48), 1));
        row.AddView(status, new LinearLayout.LayoutParams(0, Dp(48), 1));
        row.AddView(next, new LinearLayout.LayoutParams(0, Dp(48), 1));
        previous.Click += (_, _) => ChangePage(currentPage - 1);
        next.Click += (_, _) => ChangePage(currentPage + 1);
        return row;
    }

    private MaterialButton PageButton(string text, bool enabled)
    {
        var button = new MaterialButton(this, null, global::Android.Resource.Attribute.BorderlessButtonStyle)
        {
            Text = text,
            TextSize = 13,
            Enabled = enabled
        };
        button.SetAllCaps(false);
        button.SetTextColor(ColorStateList.ValueOf(ColorOf(Resource.Color.brand_primary)));
        return button;
    }

    private void SwitchReportType(bool weekly)
    {
        if (_weekly == weekly || _busy)
        {
            return;
        }
        _weekly = weekly;
        _page = 1;
        _ = LoadAsync();
    }

    private void ChangePage(int page)
    {
        if (page < 1 || page == _page || _busy)
        {
            return;
        }
        _page = page;
        _ = LoadAsync();
    }

    private async Task GenerateDailyAsync(string type) =>
        await ExecuteAndReloadAsync(() => Api.GenerateDailyReportAsync(type));

    private async Task GenerateWeeklyAsync() =>
        await ExecuteAndReloadAsync(Api.GenerateWeeklyReportAsync);

    private void ShowDiscardDialog(Guid reportId)
    {
        var builder = new MaterialAlertDialogBuilder(this);
        builder.SetTitle("Discard weekly report?");
        builder.SetMessage("The report will be removed. The scheduled job may generate it again later.");
        builder.SetNegativeButton("Cancel", (_, _) => { });
        builder.SetPositiveButton("Discard", (_, _) => _ = DiscardWeeklyAsync(reportId));
        builder.Show();
    }

    private async Task DiscardWeeklyAsync(Guid reportId) =>
        await ExecuteAndReloadAsync(() => Api.DeleteWeeklyReportAsync(reportId));

    private async Task ExecuteAndReloadAsync<TResponse>(Func<Task<TResponse>> action)
        where TResponse : AiurResponse
    {
        if (_busy)
        {
            return;
        }

        TResponse? response = null;
        try
        {
            SetBusy(true);
            response = await action();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            SetBusy(false);
        }

        if (response == null)
        {
            return;
        }
        _page = 1;
        await LoadAsync();
        Snackbar.Make(_root, response.Message ?? "Report updated.", Snackbar.LengthLong).Show();
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

    private TextView Badge(string value, int background, int foreground)
    {
        var badge = Text(value, 11, foreground, true);
        badge.Gravity = GravityFlags.Center;
        badge.SetPadding(Dp(11), Dp(6), Dp(11), Dp(6));
        badge.Background = Rounded(ColorOf(background), 16);
        return badge;
    }

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

    private void Add(View view, int top, int bottom)
    {
        AddTo(_content, view, top, bottom);
    }

    private void AddTo(ViewGroup parent, View view, int top, int bottom)
    {
        var layout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        layout.SetMargins(0, Dp(top), 0, Dp(bottom));
        parent.AddView(view, layout);
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
        var bar = Snackbar.Make(_root, FriendlyMessage(exception), Snackbar.LengthLong);
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

    private static string Preview(string content)
    {
        var preview = content.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return preview.Length > 260 ? preview[..260] + "…" : preview;
    }

    private static string FormatDateTime(DateTime value) =>
        value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    private static string FriendlyMessage(Exception exception)
    {
        var message = exception.Message;
        return message.Length > 160 ? message[..160] : message;
    }

    private Color ColorOf(int colorResource) => new(GetColor(colorResource));

    private GradientDrawable Rounded(Color color, int radius)
    {
        var drawable = new GradientDrawable();
        drawable.SetColor(color);
        drawable.SetCornerRadius(Dp(radius));
        return drawable;
    }

    private int Dp(int value) => (int)Math.Round(value * Resources!.DisplayMetrics!.Density);

    private sealed record ReportListItem(
        Guid Id,
        string Badge,
        string Period,
        string GeneratedLabel,
        string Content,
        bool IsWeekly,
        bool IsPlan);

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
