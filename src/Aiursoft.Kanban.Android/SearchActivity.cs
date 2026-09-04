using Android.App;
using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
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
using Google.Android.Material.TextField;
using Color = Android.Graphics.Color;

namespace Aiursoft.Kanban.Android;

[Activity(Label = "Global Search", Exported = false, Theme = "@style/AppTheme")]
public sealed class SearchActivity : AppCompatActivity
{
    private View _root = null!;
    private MaterialToolbar _toolbar = null!;
    private global::AndroidX.Core.Widget.NestedScrollView _scroll = null!;
    private LinearLayout _content = null!;
    private CircularProgressIndicator _progress = null!;
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

        SetContentView(Resource.Layout.activity_search);
        _root = FindViewById<View>(Resource.Id.search_root)!;
        _toolbar = FindViewById<MaterialToolbar>(Resource.Id.search_toolbar)!;
        _scroll = FindViewById<global::AndroidX.Core.Widget.NestedScrollView>(Resource.Id.search_scroll)!;
        _content = FindViewById<LinearLayout>(Resource.Id.search_content)!;
        _progress = FindViewById<CircularProgressIndicator>(Resource.Id.search_progress)!;

        _toolbar.SetOnApplyWindowInsetsListener(new ToolbarInsetListener(Dp(64)));
        _content.SetOnApplyWindowInsetsListener(
            new SystemBarInsetListener(Dp(16), Dp(18), Dp(16), Dp(32), false, true));
        _toolbar.NavigationContentDescription = "Back to Kanban";
        _toolbar.NavigationClick += (_, _) => Finish();
        Render(string.Empty, null);
    }

    private void Render(string query, CardSearchResponse? response)
    {
        _content.RemoveAllViews();
        _toolbar.Title = "Global Search";
        _toolbar.Subtitle = response == null
            ? "Cards across accessible boards"
            : response.TotalCount == 1 ? "1 result" : $"{response.TotalCount} results";

        Add(Text("Search Cards", 26, Resource.Color.text_primary, true), 0, 5);
        Add(Text(
            "Search titles and descriptions across every board you can read.",
            14,
            Resource.Color.text_secondary), 0, 16);

        var searchRow = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        var inputBox = new TextInputLayout(this)
        {
            Hint = "Search cards, tasks, or documents",
            BoxBackgroundMode = TextInputLayout.BoxBackgroundOutline
        };
        inputBox.SetBoxCornerRadii(Dp(16), Dp(16), Dp(16), Dp(16));
        var input = new TextInputEditText(this)
        {
            Text = query,
            InputType = InputTypes.ClassText | InputTypes.TextFlagCapSentences,
            ImeOptions = ImeAction.Search
        };
        input.SetSingleLine(true);
        inputBox.AddView(input);
        searchRow.AddView(inputBox, new LinearLayout.LayoutParams(
            0, ViewGroup.LayoutParams.WrapContent, 1));
        var searchButton = new MaterialButton(this)
        {
            Text = "Search",
            TextSize = 14,
            CornerRadius = Dp(16)
        };
        searchButton.SetAllCaps(false);
        var buttonLayout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, Dp(58));
        buttonLayout.SetMargins(Dp(10), 1, 0, 0);
        searchRow.AddView(searchButton, buttonLayout);
        Add(searchRow, 0, 22);

        async Task SearchAsync()
        {
            var value = input.Text?.Trim() ?? string.Empty;
            if (value.Length == 0)
            {
                inputBox.Error = "Enter a search query";
                input.RequestFocus();
                return;
            }
            inputBox.Error = null;
            await LoadAsync(value);
        }

        searchButton.Click += async (_, _) => await SearchAsync();
        input.EditorAction += async (_, args) =>
        {
            if (args.ActionId == ImeAction.Search)
            {
                args.Handled = true;
                await SearchAsync();
            }
        };

        if (response == null)
        {
            Add(SearchPrompt(), 0, 0);
            input.Post(() => input.RequestFocus());
            return;
        }

        var header = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        header.SetGravity(GravityFlags.CenterVertical);
        header.AddView(Text(
            $"Results for “{response.Query}”",
            18,
            Resource.Color.text_primary,
            true), new LinearLayout.LayoutParams(
            0, ViewGroup.LayoutParams.WrapContent, 1));
        if (response.UsedAi)
        {
            header.AddView(Badge(
                "AI POWERED",
                Resource.Color.success_container,
                Resource.Color.on_success_container));
        }
        Add(header, 0, 14);

        if (response.Cards.Count == 0)
        {
            Add(EmptyResults(response.Query), 0, 0);
        }
        else
        {
            foreach (var card in response.Cards)
            {
                Add(SearchResultCard(card), 0, 12);
            }
        }
        _scroll.Post(() => _scroll.ScrollTo(0, 0));
    }

    private async Task LoadAsync(string query)
    {
        if (_busy)
        {
            return;
        }
        try
        {
            SetBusy(true);
            Render(query, await Api.SearchCardsAsync(query));
        }
        catch (Exception exception)
        {
            ShowError(exception, query);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private View SearchResultCard(TaskCardDto card)
    {
        var shell = SurfaceCard();
        shell.Clickable = true;
        shell.Focusable = true;
        shell.ContentDescription = $"{card.Title}, {card.BoardName}, {card.ColumnName}";
        var content = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        content.SetPadding(Dp(16), Dp(15), Dp(16), Dp(16));

        var context = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        context.SetGravity(GravityFlags.CenterVertical);
        context.AddView(Badge(
            card.Priority.ToUpperInvariant(),
            PriorityBackground(card.Priority),
            PriorityForeground(card.Priority)));
        var location = Text($"{card.BoardName}  /  {card.ColumnName}", 12, Resource.Color.text_secondary, true);
        location.Gravity = GravityFlags.End;
        context.AddView(location, new LinearLayout.LayoutParams(
            0, ViewGroup.LayoutParams.WrapContent, 1));
        content.AddView(context);

        var title = Text(card.Title, 18, Resource.Color.text_primary, true);
        var titleLayout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        titleLayout.SetMargins(0, Dp(13), 0, 0);
        content.AddView(title, titleLayout);
        var description = Text(
            string.IsNullOrWhiteSpace(card.Description) ? "No description provided." : card.Description,
            14,
            Resource.Color.text_secondary);
        description.SetMaxLines(3);
        description.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;
        var descriptionLayout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        descriptionLayout.SetMargins(0, Dp(9), 0, 0);
        content.AddView(description, descriptionLayout);

        if (card.Labels.Count > 0)
        {
            var labels = new LinearLayout(this)
            {
                Orientation = global::Android.Widget.Orientation.Horizontal
            };
            foreach (var label in card.Labels.Take(4))
            {
                var chip = Text(label.Name, 11, Resource.Color.text_primary, true);
                chip.SetPadding(Dp(9), Dp(4), Dp(9), Dp(4));
                chip.Background = Rounded(ColorOf(Resource.Color.surface_variant), 13);
                var chipLayout = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);
                chipLayout.SetMargins(0, 0, Dp(7), 0);
                labels.AddView(chip, chipLayout);
            }
            AddTo(content, labels, 12, 0);
        }

        shell.AddView(content);
        shell.Click += (_, _) => StartActivity(CardDetailActivity.CreateIntent(this, card.Id));
        return shell;
    }

    private View SearchPrompt() => MessageCard(
        "Find work anywhere",
        "Enter a title, description, task, or document phrase to search all accessible boards.");

    private View EmptyResults(string query) => MessageCard(
        "No results found",
        $"Nothing matched “{query}”. Try a broader phrase.");

    private View MessageCard(string title, string message)
    {
        var shell = SurfaceCard();
        var content = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        content.SetGravity(GravityFlags.Center);
        content.SetPadding(Dp(24), Dp(34), Dp(24), Dp(34));
        content.AddView(Text(title, 19, Resource.Color.text_primary, true));
        var body = Text(message, 14, Resource.Color.text_secondary);
        body.Gravity = GravityFlags.Center;
        AddTo(content, body, 8, 0);
        shell.AddView(content);
        return shell;
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

    private void AddTo(ViewGroup container, View view, int top, int bottom)
    {
        var layout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        layout.SetMargins(0, Dp(top), 0, Dp(bottom));
        container.AddView(view, layout);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _progress.Visibility = busy ? ViewStates.Visible : ViewStates.Gone;
        _scroll.Enabled = !busy;
        _scroll.Alpha = busy ? 0.55f : 1f;
    }

    private void ShowError(Exception exception, string query)
    {
        if (exception is KanbanAuthenticationRequiredException)
        {
            ReturnToLogin();
            return;
        }
        var message = exception.Message.Length > 160 ? exception.Message[..160] : exception.Message;
        var bar = Snackbar.Make(_root, message, Snackbar.LengthLong);
        bar.SetAction("Retry", ignoredView => _ = LoadAsync(query));
        bar.Show();
    }

    private void ReturnToLogin()
    {
        var intent = new Intent(this, typeof(LoginActivity));
        intent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.NewTask | ActivityFlags.ClearTask);
        StartActivity(intent);
        Finish();
    }

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
