using Android.App;
using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using Aiursoft.Kanban.Android.Oidc;
using Aiursoft.Kanban.SDK;
using Aiursoft.Kanban.SDK.Models;
using Google.Android.Material.AppBar;
using Google.Android.Material.Button;
using Google.Android.Material.Card;
using Google.Android.Material.ProgressIndicator;
using Google.Android.Material.Snackbar;

namespace Aiursoft.Kanban.Android;

[Activity(Label = "Notifications", Exported = false, Theme = "@style/AppTheme")]
public sealed class NotificationsActivity : AppCompatActivity
{
    private View _root = null!;
    private global::AndroidX.Core.Widget.NestedScrollView _scroll = null!;
    private LinearLayout _content = null!;
    private CircularProgressIndicator _progress = null!;
    private NotificationListResponse? _model;
    private bool _busy;
    private AppSession Session => ((KanbanApplication)Application!).Session;
    private KanbanApiClient Api => Session.RequireApi();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (!Session.IsAuthenticated) { ReturnToLogin(); return; }
        SetContentView(Resource.Layout.activity_notifications);
        _root = FindViewById<View>(Resource.Id.notifications_root)!;
        _scroll = FindViewById<global::AndroidX.Core.Widget.NestedScrollView>(Resource.Id.notifications_scroll)!;
        _content = FindViewById<LinearLayout>(Resource.Id.notifications_content)!;
        _progress = FindViewById<CircularProgressIndicator>(Resource.Id.notifications_progress)!;
        var toolbar = FindViewById<MaterialToolbar>(Resource.Id.notifications_toolbar)!;
        toolbar.SetOnApplyWindowInsetsListener(new ToolbarInsetListener(Dp(64)));
        _content.SetOnApplyWindowInsetsListener(new SystemBarInsetListener(Dp(16), Dp(18), Dp(16), Dp(32), false, true));
        toolbar.NavigationClick += (_, _) => Finish();
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_busy) return;
        try { SetBusy(true); _model = await Api.GetNotificationsAsync(); Render(); }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); }
    }

    private void Render()
    {
        var model = _model; if (model == null) return;
        _content.RemoveAllViews();
        var header = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Horizontal };
        header.SetGravity(GravityFlags.CenterVertical);
        var title = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Vertical };
        title.AddView(Text("Notifications", 26, Resource.Color.text_primary, true));
        title.AddView(Text(model.UnreadCount == 0 ? "No unread notifications." : $"You have {model.UnreadCount} unread notification{(model.UnreadCount == 1 ? "" : "s")}.", 14, Resource.Color.text_secondary));
        header.AddView(title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        if (model.UnreadCount > 0)
        {
            var all = Button("Mark all read"); header.AddView(all, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, Dp(46)));
            all.Click += async (_, _) => await MarkAllReadAsync();
        }
        Add(header, 0, 20);
        if (model.Notifications.Count == 0) { Add(Message(), 0, 0); return; }
        foreach (var item in model.Notifications) Add(Notification(item), 0, 12);
    }

    private View Notification(NotificationDto item)
    {
        var card = Surface(); var body = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Vertical }; body.SetPadding(Dp(16), Dp(14), Dp(16), Dp(15));
        var top = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Horizontal }; top.SetGravity(GravityFlags.CenterVertical);
        var avatar = Text(string.IsNullOrWhiteSpace(item.ActorUserName) ? "?" : item.ActorUserName.Trim()[0].ToString().ToUpperInvariant(), 15, Resource.Color.on_brand_container, true); avatar.Gravity = GravityFlags.Center; avatar.SetBackgroundColor(new global::Android.Graphics.Color(GetColor(Resource.Color.brand_container)));
        top.AddView(avatar, new LinearLayout.LayoutParams(Dp(38), Dp(38)));
        var meta = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Vertical }; meta.AddView(Text(item.ActorUserName, 14, Resource.Color.text_primary, true)); meta.AddView(Text(Context(item), 12, Resource.Color.text_secondary));
        var metaParams = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1); metaParams.SetMargins(Dp(10), 0, 0, 0); top.AddView(meta, metaParams);
        top.AddView(Text(RelativeTime(item.CreationTime), 11, Resource.Color.text_secondary)); body.AddView(top);
        AddTo(body, Text(item.Message, 15, Resource.Color.text_primary), 12, 0);
        if (!string.IsNullOrWhiteSpace(item.CommentContent)) { var comment = Text(item.CommentContent, 14, Resource.Color.text_primary); comment.SetPadding(Dp(12), Dp(10), Dp(12), Dp(10)); comment.SetBackgroundColor(new global::Android.Graphics.Color(GetColor(Resource.Color.surface_variant))); AddTo(body, comment, 10, 0); }
        var actions = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Horizontal };
        var read = Button("Mark as read"); actions.AddView(read, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, Dp(44))); read.Click += async (_, _) => await MarkReadAsync(item.Id);
        if (item.CardId.HasValue || item.BoardId.HasValue) { var open = Button(item.CardId.HasValue ? "Open card" : "Open board"); var p = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, Dp(44)); p.SetMargins(Dp(8), 0, 0, 0); actions.AddView(open, p); open.Click += (_, _) => Open(item); }
        AddTo(body, actions, 12, 0); card.AddView(body); return card;
    }

    private async Task MarkReadAsync(int id)
    {
        try { await Api.MarkNotificationReadAsync(id); _model?.Notifications.RemoveAll(n => n.Id == id); if (_model != null) _model.UnreadCount = _model.Notifications.Count; Render(); }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task MarkAllReadAsync()
    {
        try { await Api.MarkAllNotificationsReadAsync(); if (_model != null) { _model.Notifications.Clear(); _model.UnreadCount = 0; } Render(); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void Open(NotificationDto item)
    {
        if (item.CardId.HasValue) { StartActivity(CardDetailActivity.CreateIntent(this, item.CardId.Value)); return; }
        if (!item.BoardId.HasValue) return; Session.SelectedBoardId = item.BoardId.Value; var intent = new Intent(this, typeof(MainActivity)); intent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop); StartActivity(intent); Finish();
    }

    private View Message() { var card = Surface(); var body = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Vertical }; body.SetGravity(GravityFlags.Center); body.SetPadding(Dp(20), Dp(36), Dp(20), Dp(36)); body.AddView(Text("All caught up!", 20, Resource.Color.text_primary, true)); body.AddView(Text("You have no unread notifications.", 14, Resource.Color.text_secondary)); card.AddView(body); return card; }
    private MaterialButton Button(string value) { var button = new MaterialButton(this, null, global::Android.Resource.Attribute.BorderlessButtonStyle) { Text = value, TextSize = 12 }; button.SetAllCaps(false); button.SetTextColor(ColorStateList.ValueOf(new global::Android.Graphics.Color(GetColor(Resource.Color.brand_primary)))); return button; }
    private MaterialCardView Surface() { var card = new MaterialCardView(this) { Radius = Dp(16), CardElevation = 0 }; card.SetCardBackgroundColor(GetColor(Resource.Color.surface)); card.StrokeColor = GetColor(Resource.Color.outline); card.StrokeWidth = Dp(1); return card; }
    private TextView Text(string value, float size, int color, bool bold = false) { var text = new TextView(this) { Text = value, TextSize = size, Typeface = bold ? Typeface.DefaultBold : Typeface.Default }; text.SetTextColor(new global::Android.Graphics.Color(GetColor(color))); return text; }
    private void Add(View view, int top, int bottom) => AddTo(_content, view, top, bottom);
    private void AddTo(ViewGroup parent, View view, int top, int bottom) { var p = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent); p.SetMargins(0, Dp(top), 0, Dp(bottom)); parent.AddView(view, p); }
    private static string Context(NotificationDto item) => string.Join(" / ", new[] { item.BoardName, item.ColumnName, item.CardTitle }.Where(v => !string.IsNullOrWhiteSpace(v))!);
    private static string RelativeTime(DateTime value) { var diff = DateTime.UtcNow - value; if (diff.TotalMinutes < 1) return "just now"; if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago"; if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago"; if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago"; return value.ToLocalTime().ToString("MMM dd"); }
    private void SetBusy(bool busy) { _busy = busy; _progress.Visibility = busy ? ViewStates.Visible : ViewStates.Gone; _scroll.Alpha = busy ? .55f : 1f; }
    private void ShowError(Exception ex) { if (ex is KanbanAuthenticationRequiredException) { ReturnToLogin(); return; } var bar = Snackbar.Make(_root, ex.Message, Snackbar.LengthLong); bar.SetAction("Retry", view => { _ = LoadAsync(); }); bar.Show(); }
    private void ReturnToLogin() { var i = new Intent(this, typeof(LoginActivity)); i.AddFlags(ActivityFlags.ClearTop | ActivityFlags.NewTask | ActivityFlags.ClearTask); StartActivity(i); Finish(); }
    private int Dp(int value) => (int)Math.Round(value * Resources!.DisplayMetrics!.Density);
    private sealed class ToolbarInsetListener(int height) : Java.Lang.Object, View.IOnApplyWindowInsetsListener { public WindowInsets OnApplyWindowInsets(View view, WindowInsets insets) { var top = OperatingSystem.IsAndroidVersionAtLeast(30) ? insets.GetInsets(WindowInsets.Type.SystemBars()).Top : insets.SystemWindowInsetTop; var p = view.LayoutParameters!; p.Height = height + top; view.LayoutParameters = p; view.SetPadding(view.PaddingLeft, top, view.PaddingRight, view.PaddingBottom); return insets; } }
}
