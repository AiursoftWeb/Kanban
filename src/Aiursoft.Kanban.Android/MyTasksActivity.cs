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

[Activity(Label = "My Tasks", Exported = false, Theme = "@style/AppTheme")]
public sealed class MyTasksActivity : AppCompatActivity
{
    private static readonly (string Label, string Value)[] Statuses =
    [
        ("Incomplete", "incomplete"),
        ("Not started", "not-started"),
        ("In progress", "in-progress"),
        ("Completed", "completed"),
        ("All", "all")
    ];

    private static readonly (string Label, string Value)[] Sorts =
    [
        ("Planned end · overdue first", "planned-end-desc"),
        ("Planned end · earliest first", "planned-end-asc"),
        ("Planned start · latest first", "planned-start-desc"),
        ("Planned start · earliest first", "planned-start-asc"),
        ("Priority · urgent first", "priority-asc"),
        ("Priority · low first", "priority-desc"),
        ("Actual start · latest first", "actual-start-desc"),
        ("Actual start · earliest first", "actual-start-asc"),
        ("Actual end · latest first", "actual-end-desc"),
        ("Actual end · earliest first", "actual-end-asc"),
        ("Newest first", "creation-desc"),
        ("Oldest first", "creation-asc"),
        ("Title · A–Z", "title-asc"),
        ("Title · Z–A", "title-desc")
    ];

    private View _root = null!;
    private MaterialToolbar _toolbar = null!;
    private global::AndroidX.Core.Widget.NestedScrollView _scroll = null!;
    private LinearLayout _content = null!;
    private CircularProgressIndicator _progress = null!;
    private string? _targetUserId;
    private string _status = "incomplete";
    private string _labelMode = "any";
    private string _sort = "planned-end-desc";
    private readonly HashSet<int> _labelIds = [];
    private bool _loaded;
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

        SetContentView(Resource.Layout.activity_my_tasks);
        _root = FindViewById<View>(Resource.Id.my_tasks_root)!;
        _toolbar = FindViewById<MaterialToolbar>(Resource.Id.my_tasks_toolbar)!;
        _scroll = FindViewById<global::AndroidX.Core.Widget.NestedScrollView>(Resource.Id.my_tasks_scroll)!;
        _content = FindViewById<LinearLayout>(Resource.Id.my_tasks_content)!;
        _progress = FindViewById<CircularProgressIndicator>(Resource.Id.my_tasks_progress)!;

        _toolbar.SetOnApplyWindowInsetsListener(new ToolbarInsetListener(Dp(64)));
        _content.SetOnApplyWindowInsetsListener(
            new SystemBarInsetListener(Dp(16), Dp(18), Dp(16), Dp(32), false, true));
        _toolbar.NavigationContentDescription = "Back to Kanban";
        _toolbar.NavigationClick += (_, _) => Finish();
        _ = LoadAsync();
    }

    protected override void OnResume()
    {
        base.OnResume();
        if (_loaded && !_busy && Session.IsAuthenticated)
        {
            _ = LoadAsync(showProgress: false);
        }
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
            var response = await Api.GetMyTasksAsync(
                _targetUserId,
                _status,
                _labelIds,
                _labelMode,
                _sort);
            _targetUserId = response.TargetUser.Id;
            _status = response.SelectedStatus;
            _labelMode = response.SelectedLabelMode;
            _sort = response.SelectedSort;
            _labelIds.Clear();
            _labelIds.UnionWith(response.SelectedLabelIds);
            Render(response);
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

    private void Render(MyTasksResponse response)
    {
        _content.RemoveAllViews();
        _toolbar.Title = response.IsViewingOtherUser
            ? $"{response.TargetUser.DisplayName}'s Tasks"
            : "My Tasks";
        _toolbar.Subtitle = response.Cards.Count == 1 ? "1 task" : $"{response.Cards.Count} tasks";

        Add(Text(
            response.IsViewingOtherUser ? response.TargetUser.DisplayName : "My Tasks",
            26,
            Resource.Color.text_primary,
            true), 0, 4);
        Add(Text(
            "Assigned cards across your Kanban boards.",
            14,
            Resource.Color.text_secondary), 0, 16);

        var filters = SurfaceCard();
        var filterContent = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        filterContent.SetPadding(Dp(14), Dp(14), Dp(14), Dp(16));
        if (response.CanViewAnyUserTasks && response.AvailableUsers.Count > 0)
        {
            filterContent.AddView(SectionTitle("ASSIGNEE"));
            var users = response.AvailableUsers;
            var userSpinner = Spinner(users.Select(user => user.DisplayName).ToArray());
            var selectedUser = Math.Max(0, users.FindIndex(user => user.Id == response.TargetUser.Id));
            userSpinner.SetSelection(selectedUser);
            userSpinner.ItemSelected += (_, args) =>
            {
                var selectedId = users[args.Position].Id;
                if (selectedId == _targetUserId || _busy)
                {
                    return;
                }
                _targetUserId = selectedId;
                _labelIds.Clear();
                _ = LoadAsync();
            };
            AddTo(filterContent, userSpinner, 4, 12, Dp(52));
        }

        filterContent.AddView(SectionTitle("STATUS"));
        AddTo(filterContent, StatusFilter(), 7, 12);
        filterContent.AddView(SectionTitle("SORT"));
        var sortSpinner = Spinner(Sorts.Select(item => item.Label).ToArray());
        var selectedSort = Math.Max(0, Array.FindIndex(Sorts, item => item.Value == _sort));
        sortSpinner.SetSelection(selectedSort);
        sortSpinner.ItemSelected += (_, args) =>
        {
            var selected = Sorts[args.Position].Value;
            if (selected == _sort || _busy)
            {
                return;
            }
            _sort = selected;
            _ = LoadAsync();
        };
        AddTo(filterContent, sortSpinner, 4, response.AvailableLabels.Count > 0 ? 12 : 0, Dp(52));

        if (response.AvailableLabels.Count > 0)
        {
            var labelHeader = new LinearLayout(this)
            {
                Orientation = global::Android.Widget.Orientation.Horizontal
            };
            labelHeader.SetGravity(GravityFlags.CenterVertical);
            labelHeader.AddView(SectionTitle("LABELS"), new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WrapContent, 1));
            if (_labelIds.Count > 1)
            {
                var mode = SmallButton(_labelMode == "all" ? "Match all" : "Match any");
                labelHeader.AddView(mode, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WrapContent, Dp(40)));
                mode.Click += (_, _) =>
                {
                    _labelMode = _labelMode == "all" ? "any" : "all";
                    _ = LoadAsync();
                };
            }
            filterContent.AddView(labelHeader);
            AddTo(filterContent, LabelFilter(response.AvailableLabels), 7, 0);
        }
        filters.AddView(filterContent);
        Add(filters, 0, 18);

        if (response.Cards.Count == 0)
        {
            Add(EmptyState(), 0, 0);
        }
        else
        {
            foreach (var card in response.Cards)
            {
                Add(TaskCard(card), 0, 12);
            }
        }
    }

    private View StatusFilter()
    {
        var scroll = new HorizontalScrollView(this)
        {
            HorizontalScrollBarEnabled = false
        };
        var row = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        foreach (var status in Statuses)
        {
            var button = FilterButton(status.Label, status.Value == _status);
            var layout = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, Dp(44));
            layout.SetMargins(0, 0, Dp(8), 0);
            row.AddView(button, layout);
            button.Click += (_, _) =>
            {
                if (_status == status.Value || _busy)
                {
                    return;
                }
                _status = status.Value;
                _ = LoadAsync();
            };
        }
        scroll.AddView(row);
        return scroll;
    }

    private View LabelFilter(IReadOnlyList<TaskLabelFilterDto> labels)
    {
        var scroll = new HorizontalScrollView(this)
        {
            HorizontalScrollBarEnabled = false
        };
        var row = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        foreach (var label in labels)
        {
            var selected = _labelIds.Contains(label.Id);
            var button = FilterButton($"●  {label.Name}  {label.UsageCount}", selected);
            button.ContentDescription = $"{label.Name}, used by {label.UsageCount} tasks";
            var layout = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, Dp(44));
            layout.SetMargins(0, 0, Dp(8), 0);
            row.AddView(button, layout);
            button.Click += (_, _) =>
            {
                if (_busy)
                {
                    return;
                }
                if (!_labelIds.Add(label.Id))
                {
                    _labelIds.Remove(label.Id);
                }
                _ = LoadAsync();
            };
        }
        scroll.AddView(row);
        return scroll;
    }

    private View TaskCard(TaskCardDto card)
    {
        var shell = SurfaceCard();
        shell.Clickable = true;
        shell.Focusable = true;
        shell.ContentDescription = $"{card.Title}, {StatusLabel(card.Status)}, {card.Priority} priority";
        var content = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        content.SetPadding(Dp(16), Dp(15), Dp(16), Dp(16));

        var badges = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        badges.AddView(Badge(
            StatusLabel(card.Status).ToUpperInvariant(),
            card.Status == "Completed" ? Resource.Color.success_container : Resource.Color.surface_variant,
            card.Status == "Completed" ? Resource.Color.on_success_container : Resource.Color.text_secondary));
        var priority = Badge(
            card.Priority.ToUpperInvariant(),
            PriorityBackground(card.Priority),
            PriorityForeground(card.Priority));
        var priorityLayout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);
        priorityLayout.SetMargins(Dp(8), 0, 0, 0);
        badges.AddView(priority, priorityLayout);
        content.AddView(badges);

        var title = Text(card.Title, 18, Resource.Color.text_primary, true);
        var titleLayout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        titleLayout.SetMargins(0, Dp(13), 0, Dp(5));
        content.AddView(title, titleLayout);
        content.AddView(Text(
            $"{card.BoardName}  ·  {card.ColumnName}",
            13,
            Resource.Color.text_secondary));

        if (!string.IsNullOrWhiteSpace(card.Description))
        {
            var description = Text(card.Description, 14, Resource.Color.text_primary);
            description.SetMaxLines(3);
            description.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;
            var descriptionLayout = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
            descriptionLayout.SetMargins(0, Dp(11), 0, 0);
            content.AddView(description, descriptionLayout);
        }

        if (card.Labels.Count > 0)
        {
            var labels = new HorizontalScrollView(this)
            {
                HorizontalScrollBarEnabled = false
            };
            var labelRow = new LinearLayout(this)
            {
                Orientation = global::Android.Widget.Orientation.Horizontal
            };
            foreach (var label in card.Labels)
            {
                var chip = Text(label.Name, 12, Resource.Color.text_primary, true);
                chip.SetPadding(Dp(10), Dp(5), Dp(10), Dp(5));
                chip.Background = Rounded(ColorOf(Resource.Color.surface_variant), 14);
                var chipLayout = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);
                chipLayout.SetMargins(0, 0, Dp(7), 0);
                labelRow.AddView(chip, chipLayout);
            }
            labels.AddView(labelRow);
            AddTo(content, labels, 12, 0);
        }

        var timeline = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        timeline.AddView(Text(
            $"Start  {FormatDate(card.PlannedStartTime)}",
            12,
            Resource.Color.text_secondary), new LinearLayout.LayoutParams(
            0, ViewGroup.LayoutParams.WrapContent, 1));
        var overdue = card.DueDate.HasValue && card.DueDate.Value < DateTime.UtcNow && card.Status != "Completed";
        var due = Text(
            $"Due  {FormatDate(card.DueDate)}",
            12,
            overdue ? Resource.Color.on_danger_container : Resource.Color.text_secondary,
            overdue);
        due.Gravity = GravityFlags.End;
        timeline.AddView(due, new LinearLayout.LayoutParams(
            0, ViewGroup.LayoutParams.WrapContent, 1));
        AddTo(content, timeline, 13, 0);

        shell.AddView(content);
        shell.Click += (_, _) => StartActivity(CardDetailActivity.CreateIntent(this, card.Id));
        return shell;
    }

    private View EmptyState()
    {
        var shell = SurfaceCard();
        var content = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        content.SetGravity(GravityFlags.Center);
        content.SetPadding(Dp(24), Dp(34), Dp(24), Dp(34));
        content.AddView(Text("No matching tasks", 19, Resource.Color.text_primary, true));
        var message = Text("Try another status, label, or assignee filter.", 14, Resource.Color.text_secondary);
        message.Gravity = GravityFlags.Center;
        AddTo(content, message, 8, 0);
        shell.AddView(content);
        return shell;
    }

    private Spinner Spinner(IReadOnlyList<string> items)
    {
        var spinner = new Spinner(this, SpinnerMode.Dialog);
        var adapter = new ArrayAdapter<string>(
            this,
            global::Android.Resource.Layout.SimpleSpinnerItem,
            items.ToArray());
        adapter.SetDropDownViewResource(global::Android.Resource.Layout.SimpleSpinnerDropDownItem);
        spinner.Adapter = adapter;
        return spinner;
    }

    private MaterialButton FilterButton(string text, bool selected)
    {
        var button = new MaterialButton(this)
        {
            Text = text,
            TextSize = 12,
            CornerRadius = Dp(14)
        };
        button.SetAllCaps(false);
        button.BackgroundTintList = ColorStateList.ValueOf(ColorOf(
            selected ? Resource.Color.brand_container : Resource.Color.surface_variant));
        button.SetTextColor(ColorStateList.ValueOf(ColorOf(
            selected ? Resource.Color.on_brand_container : Resource.Color.text_primary)));
        return button;
    }

    private MaterialButton SmallButton(string text)
    {
        var button = new MaterialButton(this, null, global::Android.Resource.Attribute.BorderlessButtonStyle)
        {
            Text = text,
            TextSize = 12
        };
        button.SetAllCaps(false);
        button.SetTextColor(ColorStateList.ValueOf(ColorOf(Resource.Color.brand_primary)));
        return button;
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
        badge.SetPadding(Dp(10), Dp(5), Dp(10), Dp(5));
        badge.Background = Rounded(ColorOf(background), 15);
        return badge;
    }

    private TextView SectionTitle(string value)
    {
        var title = Text(value, 12, Resource.Color.text_secondary, true);
        title.LetterSpacing = 0.08f;
        return title;
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

    private void AddTo(ViewGroup container, View view, int top, int bottom, int? height = null)
    {
        var layout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            height ?? ViewGroup.LayoutParams.WrapContent);
        layout.SetMargins(0, Dp(top), 0, Dp(bottom));
        container.AddView(view, layout);
    }

    private void SetBusy(bool busy, bool showProgress)
    {
        _busy = busy;
        _progress.Visibility = busy && showProgress ? ViewStates.Visible : ViewStates.Gone;
        _scroll.Enabled = !busy;
        _scroll.Alpha = busy && showProgress ? 0.55f : 1f;
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

    private static string StatusLabel(string status) => status switch
    {
        "NotStarted" => "Not started",
        "InProgress" => "In progress",
        "Completed" => "Completed",
        _ => status
    };

    private int PriorityBackground(string priority) => priority switch
    {
        "Urgent" => Resource.Color.danger_container,
        "High" => Resource.Color.warning_container,
        "Medium" => Resource.Color.brand_container,
        "Low" => Resource.Color.success_container,
        _ => Resource.Color.surface_variant
    };

    private int PriorityForeground(string priority) => priority switch
    {
        "Urgent" => Resource.Color.on_danger_container,
        "High" => Resource.Color.on_warning_container,
        "Medium" => Resource.Color.on_brand_container,
        "Low" => Resource.Color.on_success_container,
        _ => Resource.Color.text_secondary
    };

    private static string FormatDate(DateTime? date) =>
        date.HasValue ? date.Value.ToLocalTime().ToString("yyyy-MM-dd") : "—";

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
