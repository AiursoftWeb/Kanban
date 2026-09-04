using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using Aiursoft.Kanban.Android.Oidc;
using Aiursoft.Kanban.SDK;
using Aiursoft.Kanban.SDK.Models;
using Google.Android.Material.AppBar;
using Google.Android.Material.Card;
using Google.Android.Material.ProgressIndicator;
using Google.Android.Material.Snackbar;

namespace Aiursoft.Kanban.Android;

[Activity(Label = "Overview", Exported = false, Theme = "@style/AppTheme")]
public sealed class DashboardActivity : AppCompatActivity
{
    private View _root = null!;
    private global::AndroidX.Core.Widget.NestedScrollView _scroll = null!;
    private LinearLayout _content = null!;
    private CircularProgressIndicator _progress = null!;
    private bool _loaded;
    private bool _busy;
    private AppSession Session => ((KanbanApplication)Application!).Session;
    private KanbanApiClient Api => Session.RequireApi();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (!Session.IsAuthenticated) { ReturnToLogin(); return; }
        SetContentView(Resource.Layout.activity_dashboard);
        _root = FindViewById<View>(Resource.Id.dashboard_root)!;
        _scroll = FindViewById<global::AndroidX.Core.Widget.NestedScrollView>(Resource.Id.dashboard_scroll)!;
        _content = FindViewById<LinearLayout>(Resource.Id.dashboard_content)!;
        _progress = FindViewById<CircularProgressIndicator>(Resource.Id.dashboard_progress)!;
        var toolbar = FindViewById<MaterialToolbar>(Resource.Id.dashboard_toolbar)!;
        toolbar.SetOnApplyWindowInsetsListener(new ToolbarInsetListener(Dp(64)));
        _content.SetOnApplyWindowInsetsListener(new SystemBarInsetListener(Dp(16), Dp(18), Dp(16), Dp(32), false, true));
        toolbar.NavigationClick += (_, _) => Finish();
        _ = LoadAsync();
    }

    protected override void OnResume()
    {
        base.OnResume();
        if (_loaded && !_busy) _ = LoadAsync(false);
    }

    private async Task LoadAsync(bool showProgress = true)
    {
        if (_busy) return;
        try { SetBusy(true, showProgress); Render(await Api.GetDashboardAsync()); _loaded = true; }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false, showProgress); }
    }

    private void Render(DashboardResponse model)
    {
        _content.RemoveAllViews();
        Add(Text("Kanban Dashboard", 26, Resource.Color.text_primary, true), 0, 16);
        var stats1 = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Horizontal };
        stats1.AddView(Stat("My boards", model.OwnedBoardCount), Weighted());
        stats1.AddView(Stat("Shared boards", model.SharedBoardCount), Weighted(10));
        Add(stats1, 0, 10);
        var stats2 = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Horizontal };
        stats2.AddView(Stat("Assigned tasks", model.AssignedTaskCount), Weighted());
        stats2.AddView(Stat("Overdue", model.OverdueTaskCount, model.OverdueTaskCount > 0), Weighted(10));
        Add(stats2, 0, 18);

        if (model.LatestPlan != null || model.LatestSummary != null)
        {
            Add(Section("TODAY'S REPORTS"), 0, 8);
            if (model.LatestPlan != null) Add(Report(model.LatestPlan, "Today's Plan"), 0, 10);
            if (model.LatestSummary != null) Add(Report(model.LatestSummary, "Today's Summary"), 0, 14);
        }

        Add(Section($"MY ACTIVE TASKS  ·  {model.InProgressTaskCount} IN PROGRESS"), 0, 8);
        if (model.AssignedTasks.Count == 0) Add(Message("No active tasks assigned to you right now."), 0, 16);
        foreach (var task in model.AssignedTasks) Add(Task(task), 0, 9);

        Add(Section("MY BOARDS"), 10, 8);
        if (model.OwnedBoards.Count == 0) Add(Message("No boards created yet."), 0, 10);
        foreach (var board in model.OwnedBoards.Take(6)) Add(Board(board, false), 0, 9);
        Add(Section("SHARED WITH ME"), 12, 8);
        if (model.SharedBoards.Count == 0) Add(Message("No boards are shared with you yet."), 0, 0);
        foreach (var board in model.SharedBoards.Take(6)) Add(Board(board, true), 0, 9);
    }

    private View Stat(string label, int value, bool danger = false)
    {
        var card = Surface();
        var body = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Vertical };
        body.SetPadding(Dp(15), Dp(13), Dp(15), Dp(13));
        body.AddView(Text(label, 12, Resource.Color.text_secondary));
        body.AddView(Text(value.ToString(), 27, danger ? Resource.Color.on_danger_container : Resource.Color.text_primary, true));
        card.AddView(body); return card;
    }

    private View Report(DailyReportDto report, string title)
    {
        var card = Surface(); card.Clickable = true;
        var body = VerticalBody(); body.AddView(Text(title, 17, Resource.Color.text_primary, true));
        var preview = Text(Preview(report.Content), 14, Resource.Color.text_secondary); preview.SetMaxLines(4);
        AddTo(body, preview, 8, 6); body.AddView(Text($"Generated {report.GeneratedAt.ToLocalTime():yyyy-MM-dd HH:mm}", 12, Resource.Color.text_secondary));
        card.AddView(body); card.Click += (_, _) => StartActivity(ReportDetailActivity.CreateIntent(this, report.Id, false)); return card;
    }

    private View Task(TaskCardDto task)
    {
        var card = Surface(); card.Clickable = true;
        var body = VerticalBody(); body.AddView(Text(task.Title, 16, Resource.Color.text_primary, true));
        body.AddView(Text($"{task.BoardName} / {task.ColumnName}  ·  {task.Priority}", 12, Resource.Color.text_secondary));
        body.AddView(Text(task.DueDate.HasValue ? $"Due {task.DueDate.Value.ToLocalTime():yyyy-MM-dd HH:mm}" : "No due date", 12,
            task.DueDate < DateTime.UtcNow ? Resource.Color.on_danger_container : Resource.Color.text_secondary));
        card.AddView(body); card.Click += (_, _) => StartActivity(CardDetailActivity.CreateIntent(this, task.Id)); return card;
    }

    private View Board(DashboardBoardDto board, bool shared)
    {
        var card = Surface(); card.Clickable = true;
        var body = VerticalBody(); body.AddView(Text(board.Name, 16, Resource.Color.text_primary, true));
        var detail = shared
            ? $"{board.Permission}  ·  {board.TotalCards} cards  ·  {board.IncompleteCards} open"
            : $"{board.TotalCards} cards  ·  {board.InProgressCards} in progress  ·  {board.CompletedCards} done";
        body.AddView(Text(detail, 12, Resource.Color.text_secondary));
        if (board.OverdueCards > 0) body.AddView(Text($"{board.OverdueCards} overdue", 12, Resource.Color.on_danger_container, true));
        card.AddView(body); card.Click += (_, _) => OpenBoard(board.BoardId); return card;
    }

    private void OpenBoard(int boardId)
    {
        Session.SelectedBoardId = boardId;
        var intent = new Intent(this, typeof(MainActivity));
        intent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        StartActivity(intent); Finish();
    }

    private View Message(string value) { var card = Surface(); var text = Text(value, 14, Resource.Color.text_secondary); text.Gravity = GravityFlags.Center; text.SetPadding(Dp(18), Dp(24), Dp(18), Dp(24)); card.AddView(text); return card; }
    private LinearLayout VerticalBody() { var body = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Vertical }; body.SetPadding(Dp(15), Dp(13), Dp(15), Dp(13)); return body; }
    private MaterialCardView Surface() { var card = new MaterialCardView(this) { Radius = Dp(14), CardElevation = 0 }; card.SetCardBackgroundColor(GetColor(Resource.Color.surface)); card.StrokeColor = GetColor(Resource.Color.outline); card.StrokeWidth = Dp(1); return card; }
    private TextView Section(string value) { var text = Text(value, 12, Resource.Color.text_secondary, true); text.LetterSpacing = .08f; return text; }
    private TextView Text(string value, float size, int color, bool bold = false) { var text = new TextView(this) { Text = value, TextSize = size, Typeface = bold ? Typeface.DefaultBold : Typeface.Default }; text.SetTextColor(new global::Android.Graphics.Color(GetColor(color))); return text; }
    private void Add(View view, int top, int bottom) => AddTo(_content, view, top, bottom);
    private void AddTo(ViewGroup parent, View view, int top, int bottom) { var p = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent); p.SetMargins(0, Dp(top), 0, Dp(bottom)); parent.AddView(view, p); }
    private LinearLayout.LayoutParams Weighted(int left = 0) { var p = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1); p.SetMargins(Dp(left), 0, 0, 0); return p; }
    private static string Preview(string value) { var text = value.Replace('\r', ' ').Replace('\n', ' ').Trim(); return text.Length > 260 ? text[..260] + "…" : text; }
    private void SetBusy(bool busy, bool visible) { _busy = busy; _progress.Visibility = busy && visible ? ViewStates.Visible : ViewStates.Gone; _scroll.Alpha = busy && visible ? .55f : 1f; }
    private void ShowError(Exception ex) { if (ex is KanbanAuthenticationRequiredException) { ReturnToLogin(); return; } var bar = Snackbar.Make(_root, ex.Message, Snackbar.LengthLong); bar.SetAction("Retry", view => { _ = LoadAsync(); }); bar.Show(); }
    private void ReturnToLogin() { var i = new Intent(this, typeof(LoginActivity)); i.AddFlags(ActivityFlags.ClearTop | ActivityFlags.NewTask | ActivityFlags.ClearTask); StartActivity(i); Finish(); }
    private int Dp(int value) => (int)Math.Round(value * Resources!.DisplayMetrics!.Density);
    private sealed class ToolbarInsetListener(int height) : Java.Lang.Object, View.IOnApplyWindowInsetsListener { public WindowInsets OnApplyWindowInsets(View view, WindowInsets insets) { var top = OperatingSystem.IsAndroidVersionAtLeast(30) ? insets.GetInsets(WindowInsets.Type.SystemBars()).Top : insets.SystemWindowInsetTop; var p = view.LayoutParameters!; p.Height = height + top; view.LayoutParameters = p; view.SetPadding(view.PaddingLeft, top, view.PaddingRight, view.PaddingBottom); return insets; } }
}
