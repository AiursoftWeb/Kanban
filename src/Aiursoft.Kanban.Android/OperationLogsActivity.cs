using Android.App;
using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
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
using Color = Android.Graphics.Color;

namespace Aiursoft.Kanban.Android;

[Activity(Label = "My Operation Logs", Exported = false, Theme = "@style/AppTheme")]
public sealed class OperationLogsActivity : AppCompatActivity
{
    private View _root = null!;
    private global::AndroidX.Core.Widget.NestedScrollView _scroll = null!;
    private LinearLayout _content = null!;
    private CircularProgressIndicator _progress = null!;
    private int _page = 1;
    private bool _busy;

    private AppSession Session => ((KanbanApplication)Application!).Session;
    private KanbanApiClient Api => Session.RequireApi();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (!Session.IsAuthenticated)
        {
            ReturnToLogin();
            return;
        }

        SetContentView(Resource.Layout.activity_operation_logs);
        _root = FindViewById<View>(Resource.Id.operation_logs_root)!;
        _scroll = FindViewById<global::AndroidX.Core.Widget.NestedScrollView>(
            Resource.Id.operation_logs_scroll)!;
        _content = FindViewById<LinearLayout>(Resource.Id.operation_logs_content)!;
        _progress = FindViewById<CircularProgressIndicator>(Resource.Id.operation_logs_progress)!;
        var toolbar = FindViewById<MaterialToolbar>(Resource.Id.operation_logs_toolbar)!;
        toolbar.SetOnApplyWindowInsetsListener(new ToolbarInsetListener(Dp(64)));
        toolbar.NavigationContentDescription = "Back to Kanban";
        toolbar.NavigationClick += (_, _) => Finish();
        _content.SetOnApplyWindowInsetsListener(
            new SystemBarInsetListener(Dp(16), Dp(18), Dp(16), Dp(32), false, true));
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
            Render(await Api.GetMyOperationLogsAsync(_page));
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

    private void Render(OperationLogListResponse response)
    {
        _page = response.CurrentPage;
        _content.RemoveAllViews();
        Add(Text("My Operation Logs", 26, Resource.Color.text_primary, true), 0, 5);
        Add(Text(
            response.Enabled
                ? $"Your latest activity across Kanban · {response.TotalCount} total"
                : "Operation logging is not enabled on this server.",
            14,
            Resource.Color.text_secondary), 0, 18);

        if (!response.Enabled)
        {
            Add(MessageCard(
                "Logging is disabled",
                "Ask the server administrator to enable operation logging if you need this history."),
                0,
                0);
            return;
        }

        if (response.Logs.Count == 0)
        {
            Add(MessageCard("No operation logs", "Your recorded activity will appear here."), 0, 18);
        }
        else
        {
            foreach (var log in response.Logs)
            {
                Add(LogCard(log), 0, 12);
            }
        }

        Add(Pagination(response.CurrentPage, response.TotalPages), 6, 0);
        _scroll.Post(() => _scroll.ScrollTo(0, 0));
    }

    private View LogCard(OperationLogDto log)
    {
        var card = SurfaceCard();
        var body = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        body.SetPadding(Dp(16), Dp(15), Dp(16), Dp(16));

        var header = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        header.SetGravity(GravityFlags.CenterVertical);
        header.AddView(Badge(string.IsNullOrWhiteSpace(log.Category)
            ? "OPERATION"
            : log.Category.ToUpperInvariant()));
        var time = Text(log.EventTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            12,
            Resource.Color.text_secondary);
        time.Gravity = GravityFlags.End;
        header.AddView(time, new LinearLayout.LayoutParams(
            0, ViewGroup.LayoutParams.WrapContent, 1));
        body.AddView(header);

        AddTo(body,
            Text(string.IsNullOrWhiteSpace(log.Summary) ? log.Action : log.Summary,
                16,
                Resource.Color.text_primary,
                true),
            13,
            0);
        if (!string.IsNullOrWhiteSpace(log.Action))
        {
            AddTo(body, Text(log.Action, 13, Resource.Color.text_secondary), 5, 0);
        }

        var context = new[]
        {
            string.IsNullOrWhiteSpace(log.Source) ? null : $"Source: {log.Source}",
            string.IsNullOrWhiteSpace(log.IpAddress) ? null : $"IP: {log.IpAddress}"
        }.OfType<string>().ToList();
        if (context.Count > 0)
        {
            AddTo(body, Text(string.Join("  ·  ", context), 12, Resource.Color.text_secondary), 10, 0);
        }
        card.AddView(body);
        card.ContentDescription = $"{log.Summary}. {log.Action}. {time.Text}";
        return card;
    }

    private View MessageCard(string title, string message)
    {
        var card = SurfaceCard();
        var body = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        body.SetGravity(GravityFlags.Center);
        body.SetPadding(Dp(22), Dp(34), Dp(22), Dp(34));
        body.AddView(Text(title, 19, Resource.Color.text_primary, true));
        var detail = Text(message, 14, Resource.Color.text_secondary);
        detail.Gravity = GravityFlags.Center;
        AddTo(body, detail, 8, 0);
        card.AddView(body);
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
        var status = Text($"Page {currentPage} of {totalPages}",
            13,
            Resource.Color.text_secondary,
            true);
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
        var button = new MaterialButton(this, null,
            global::Android.Resource.Attribute.BorderlessButtonStyle)
        {
            Text = text,
            TextSize = 13,
            Enabled = enabled
        };
        button.SetAllCaps(false);
        button.SetTextColor(ColorStateList.ValueOf(ColorOf(Resource.Color.brand_primary)));
        return button;
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

    private TextView Badge(string value)
    {
        var badge = Text(value, 11, Resource.Color.on_brand_container, true);
        badge.Gravity = GravityFlags.Center;
        badge.SetPadding(Dp(11), Dp(6), Dp(11), Dp(6));
        badge.Background = Rounded(ColorOf(Resource.Color.brand_container), 16);
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

    private void Add(View view, int top, int bottom) => AddTo(_content, view, top, bottom);

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

    private static string FriendlyMessage(Exception exception) =>
        exception.Message.Length > 160 ? exception.Message[..160] : exception.Message;

    private Color ColorOf(int colorResource) => new(GetColor(colorResource));

    private GradientDrawable Rounded(Color color, int radius)
    {
        var drawable = new GradientDrawable();
        drawable.SetColor(color);
        drawable.SetCornerRadius(Dp(radius));
        return drawable;
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
