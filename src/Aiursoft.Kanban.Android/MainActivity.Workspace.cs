using ColorStateList = Android.Content.Res.ColorStateList;
using Android.OS;
using Android.Views;
using Android.Widget;
using Aiursoft.Kanban.SDK.Models;
using Google.Android.Material.Button;
using Google.Android.Material.Card;

namespace Aiursoft.Kanban.Android;

public sealed partial class MainActivity
{
    private View _home = null!;
    private LinearLayout _homeContent = null!;
    private View _boardPanel = null!;
    private LinearLayout _heading = null!;
    private bool _showingHome = true;
    private bool _sharedOnly;
    private string _homeQuery = string.Empty;
    private int _renderedBoardId;
    private int _boardPosition;
    private int _homePosition;
    private readonly Dictionary<int, ScrollView> _columnScrolls = [];
    private readonly Dictionary<int, int> _columnPositions = [];

    private void ConfigureWorkspace(Bundle? state)
    {
        _home = FindViewById<View>(Resource.Id.boards_home)!;
        _homeContent = FindViewById<LinearLayout>(Resource.Id.boards_home_content)!;
        _boardPanel = FindViewById<View>(Resource.Id.board_panel)!;
        _heading = FindViewById<LinearLayout>(Resource.Id.board_heading)!;
        _showingHome = state?.GetBoolean("boards-home", true) ?? true;
        if (Intent?.GetIntExtra("open-board", 0) > 0) _showingHome = false;
        _sharedOnly = state?.GetBoolean("shared-boards", false) ?? false;
        _homeQuery = state?.GetString("board-search") ?? string.Empty;
        _homePosition = state?.GetInt("home-position", 0) ?? 0;
        _boardPosition = state?.GetInt("board-position", 0) ?? 0;
        _renderedBoardId = state?.GetInt("rendered-board", 0) ?? 0;
        foreach (var id in state?.GetIntArray("column-ids") ?? [])
            _columnPositions[id] = state!.GetInt($"column-position-{id}");
        var navigation = FindViewById<LinearLayout>(Resource.Id.workspace_navigation)!;
        navigation.SetOnApplyWindowInsetsListener(new SystemBarInsetListener(Dp(4), Dp(8), Dp(4), Dp(8), false, true));
        AddDestination(navigation, UiText.Get("Boards"), Resource.Drawable.ic_board, ShowBoardsHome, true);
        AddDestination(navigation, UiText.Get("My tasks"), Resource.Drawable.ic_tasks, OpenMyTasks, false);
        AddDestination(navigation, UiText.Get("Workspace"), Resource.Drawable.ic_menu, () => _drawer.OpenDrawer(AndroidX.Core.View.GravityCompat.Start), false);
        navigation.LayoutChange += (_, _) =>
        {
            var workspace = FindViewById<View>(Resource.Id.workspace)!;
            if (workspace.LayoutParameters is ViewGroup.MarginLayoutParams layout && layout.BottomMargin != navigation.Height)
            {
                layout.BottomMargin = navigation.Height;
                workspace.LayoutParameters = layout;
            }
            if (_newCard.LayoutParameters is ViewGroup.MarginLayoutParams buttonLayout && buttonLayout.BottomMargin != navigation.Height + Dp(16))
            {
                buttonLayout.BottomMargin = navigation.Height + Dp(16);
                _newCard.LayoutParameters = buttonLayout;
            }
        };
        _boardScroll.ScrollChange += (_, _) => UpdateColumnSelection();
        var appearance = new MaterialButton(this, null, global::Android.Resource.Attribute.BorderlessButtonStyle) { Text = UiText.Get("Appearance"), Gravity = GravityFlags.Start | GravityFlags.CenterVertical };
        appearance.SetAllCaps(false);
        var parent = (LinearLayout)_profileSettings.Parent!;
        parent.AddView(appearance, parent.IndexOfChild(_profileSettings), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(48)));
        appearance.Click += (_, _) =>
        {
            var preferences = GetSharedPreferences("appearance", global::Android.Content.FileCreationMode.Private)!;
            var selected = preferences.GetInt("theme", 0);
            var dialog = new Google.Android.Material.Dialog.MaterialAlertDialogBuilder(this);
            dialog.SetTitle(UiText.Get("Appearance"));
            dialog.SetSingleChoiceItems(new[] { UiText.Get("Use device setting"), UiText.Get("Light"), UiText.Get("Dark") }, selected, (_, args) =>
            {
                preferences.Edit()!.PutInt("theme", args.Which)!.Apply();
                AndroidX.AppCompat.App.AppCompatDelegate.DefaultNightMode = args.Which switch { 1 => AndroidX.AppCompat.App.AppCompatDelegate.ModeNightNo, 2 => AndroidX.AppCompat.App.AppCompatDelegate.ModeNightYes, _ => AndroidX.AppCompat.App.AppCompatDelegate.ModeNightFollowSystem };
            });
            dialog.SetPositiveButton(UiText.Get("Done"), (_, _) => { });
            dialog.Show();
        };
        // Existing destinations remain available without a stack of primary buttons.
        foreach (var button in new[] { _archivedBoards, _dashboard, _globalSearch, _aiAssistant,
                     _dailyReports, _weeklyReports, _myTasks, _notifications, _profileSettings, _operationLogs, _signOut })
        {
            button.BackgroundTintList = ColorStateList.ValueOf(ColorOf(Resource.Color.surface));
            button.SetTextColor(ColorOf(Resource.Color.text_primary));
            button.IconTint = ColorStateList.ValueOf(ColorOf(Resource.Color.brand_primary));
            button.Gravity = GravityFlags.Start | GravityFlags.CenterVertical;
            button.TextSize = 14;
        }
    }

    protected override void OnNewIntent(global::Android.Content.Intent? intent)
    {
        base.OnNewIntent(intent);
        var boardId = intent?.GetIntExtra("open-board", 0) ?? 0;
        if (boardId > 0) { _showingHome = false; _ = LoadBoardAsync(boardId); }
    }

    protected override void OnSaveInstanceState(Bundle outState)
    {
        CaptureBoardPosition();
        outState.PutBoolean("boards-home", _showingHome);
        outState.PutInt("home-position", _home.ScrollY);
        outState.PutBoolean("shared-boards", _sharedOnly);
        outState.PutString("board-search", _homeQuery);
        outState.PutInt("board-position", _boardPosition);
        outState.PutInt("rendered-board", _renderedBoardId);
        outState.PutIntArray("column-ids", _columnPositions.Keys.ToArray());
        foreach (var (id, position) in _columnPositions) outState.PutInt($"column-position-{id}", position);
        base.OnSaveInstanceState(outState);
    }

    private void AddDestination(LinearLayout navigation, string label, int icon, Action action, bool selected)
    {
        var body = new LinearLayout(this) { Orientation = Orientation.Vertical, Clickable = true, Focusable = true };
        body.SetGravity(GravityFlags.Center);
        body.ContentDescription = label;
        var image = new ImageView(this) { ImportantForAccessibility = ImportantForAccessibility.No };
        image.SetImageResource(icon);
        image.ImageTintList = ColorStateList.ValueOf(ColorOf(Resource.Color.brand_primary));
        image.SetPadding(Dp(14), Dp(5), Dp(14), Dp(5));
        if (selected) image.Background = Rounded(ColorOf(Resource.Color.brand_container), 16);
        body.AddView(image, new LinearLayout.LayoutParams(Dp(56), Dp(32)));
        var text = Text(label, 12, selected ? Resource.Color.brand_primary : Resource.Color.text_secondary, selected);
        text.Gravity = GravityFlags.Center;
        text.ImportantForAccessibility = ImportantForAccessibility.No;
        body.AddView(text);
        body.SetMinimumHeight(Dp(56));
        navigation.AddView(body, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        body.Click += (_, _) => action();
    }

    private void ShowBoardsHome()
    {
        CaptureBoardPosition();
        if (_showingHome && _homeContent.ChildCount > 0) _homePosition = _home.ScrollY;
        _showingHome = true;
        _boardPanel.Visibility = ViewStates.Gone;
        _emptyState.Visibility = ViewStates.Gone;
        _home.Visibility = ViewStates.Visible;
        _newCard.Visibility = ViewStates.Gone;
        _toolbar.Title = "Kanban";
        _toolbar.Subtitle = null;
        _toolbar.SetNavigationIcon(Resource.Drawable.ic_menu);
        foreach (var id in new[] { Resource.Id.action_add_column, Resource.Id.action_board_settings,
                     Resource.Id.action_share_board, Resource.Id.action_gantt, Resource.Id.action_filter_board, Resource.Id.action_archive_board })
            _toolbar.Menu?.FindItem(id)?.SetVisible(false);
        _homeContent.RemoveAllViews();
        var hero = new MaterialCardView(this) { Radius = Dp(28), CardElevation = 0, StrokeWidth = 0 };
        var heroBody = new LinearLayout(this) { Orientation = Orientation.Vertical };
        heroBody.SetBackgroundResource(Resource.Drawable.workspace_hero);
        heroBody.SetPadding(Dp(24), Dp(24), Dp(56), Dp(24));
        heroBody.AddView(Text(UiText.Get("Your workspace"), 11, Resource.Color.hero_muted, true));
        var title = Text(UiText.Get("Make room for progress."), 32, Resource.Color.on_hero, true);
        title.SetLineSpacing(0, 1.04f);
        Place(heroBody, title, 12, 16);
        heroBody.AddView(Text(UiText.Format("{0} boards · one clear view", _boards.Count), 14, Resource.Color.hero_muted));
        hero.AddView(heroBody);
        Place(_homeContent, hero, 0, 24);

        var search = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        search.SetGravity(GravityFlags.CenterVertical);
        search.SetPadding(Dp(16), 0, Dp(16), 0);
        search.Background = Rounded(ColorOf(Resource.Color.surface_variant), 20);
        var searchIcon = new ImageView(this);
        searchIcon.SetImageResource(Resource.Drawable.ic_search);
        searchIcon.ImageTintList = ColorStateList.ValueOf(ColorOf(Resource.Color.brand_primary));
        search.AddView(searchIcon, new LinearLayout.LayoutParams(Dp(24), Dp(24)));
        var input = new EditText(this) { Text = _homeQuery, Hint = UiText.Get("Find a board"), TextSize = 16, Background = null };
        input.SetTextColor(ColorOf(Resource.Color.text_primary));
        input.SetHintTextColor(ColorOf(Resource.Color.text_secondary));
        input.SetPadding(Dp(12), 0, 0, 0);
        input.SetSingleLine(true);
        search.AddView(input, new LinearLayout.LayoutParams(0, Dp(56), 1));
        Place(_homeContent, search, 0, 20);
        var tabs = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        foreach (var (label, shared) in new[] { (UiText.Get("My boards"), false), (UiText.Get("Shared with me"), true) })
        {
            var tab = new MaterialButton(this) { Text = label, TextSize = 14, CornerRadius = Dp(16) };
            tab.SetAllCaps(false);
            tab.Elevation = 0;
            tab.SetTextColor(ColorOf(_sharedOnly == shared ? Resource.Color.brand_primary : Resource.Color.text_secondary));
            tab.BackgroundTintList = ColorStateList.ValueOf(ColorOf(_sharedOnly == shared ? Resource.Color.brand_container : Resource.Color.surface_canvas));
            tab.SetSingleLine(false);
            tab.SetMaxLines(2);
            tab.SetMinimumHeight(Dp(48));
            tabs.AddView(tab, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
            tab.Click += (_, _) => { _sharedOnly = shared; ShowBoardsHome(); };
        }
        Place(_homeContent, tabs, 0, 16);
        var cards = new LinearLayout(this) { Orientation = Orientation.Vertical };
        Place(_homeContent, cards, 0, 12);
        void RenderCards()
        {
            cards.RemoveAllViews();
            var boards = _boards.Where(b => b.IsOwner != _sharedOnly && b.Name.Contains(_homeQuery, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var board in boards) Place(cards, BoardTile(board), 0, 12);
            if (boards.Count == 0)
            {
                Place(cards, Text(UiText.Get("A little space for your next idea."), 20, Resource.Color.text_primary, true), 16, 8);
                Place(cards, Text(_homeQuery.Length > 0 ? UiText.Get("No matching boards. Try a different name.") : _sharedOnly ? UiText.Get("Boards shared with you will appear here.") : UiText.Get("Create a board to start organizing your work."), 14, Resource.Color.text_secondary), 0, 20);
            }
        }
        input.TextChanged += (_, _) => { _homeQuery = input.Text ?? string.Empty; RenderCards(); };
        RenderCards();
        var create = new MaterialButton(this) { Text = UiText.Get("Create a board"), CornerRadius = Dp(18) };
        create.SetIconResource(Resource.Drawable.ic_add);
        create.SetAllCaps(false);
        create.Click += (_, _) => ShowNewBoardDialog();
        Place(_homeContent, create, 0, 16, 56);
        _home.Post(() => _home.ScrollTo(0, _homePosition));
    }

    private View BoardTile(BoardSummaryDto board)
    {
        var shell = new MaterialCardView(this) { Radius = Dp(22), CardElevation = 0, StrokeWidth = Dp(1), Clickable = true, Focusable = true };
        shell.StrokeColor = GetColor(Resource.Color.outline);
        shell.SetCardBackgroundColor(GetColor(Resource.Color.surface));
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(Dp(18), Dp(20), Dp(18), Dp(20));
        var initial = Text(board.Name.Length > 0 ? System.Globalization.StringInfo.GetNextTextElement(board.Name).ToUpperInvariant() : UiText.Get("K"), 23, Resource.Color.brand_primary, true);
        initial.Gravity = GravityFlags.Center;
        initial.Background = Rounded(ColorOf(Resource.Color.brand_container), 16);
        row.AddView(initial, new LinearLayout.LayoutParams(Dp(52), Dp(56)));
        var labels = new LinearLayout(this) { Orientation = Orientation.Vertical };
        labels.AddView(Text(board.Name, 19, Resource.Color.text_primary, true));
        Place(labels, Text(UiText.Format("{0} cards · {1}", board.CardCount, board.CanEdit ? UiText.Get("Can edit") : UiText.Get("View only")), 13, Resource.Color.text_secondary), 7, 0);
        var lp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1);
        lp.SetMargins(Dp(16), 0, Dp(8), 0);
        row.AddView(labels, lp);
        row.AddView(Text("›", 28, Resource.Color.brand_primary));
        shell.AddView(row);
        shell.Click += async (_, _) => await LoadBoardAsync(board.Id);
        return shell;
    }

    private void RenderBoardHeading(BoardDto board)
    {
        _heading.RemoveAllViews();
        var hero = new LinearLayout(this) { Orientation = Orientation.Vertical };
        hero.SetBackgroundResource(Resource.Drawable.workspace_hero);
        hero.SetMinimumHeight(0);
        hero.SetMinimumWidth(0);
        hero.SetPadding(Dp(24), Dp(18), Dp(40), Dp(20));
        hero.AddView(Text(board.IsOwner ? UiText.Get("MY BOARD") : UiText.Get("SHARED BOARD"), 11, Resource.Color.hero_muted, true));
        var title = Text(board.Name, 26, Resource.Color.on_hero, true);
        title.SetMaxLines(3);
        title.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;
        Place(hero, title, 8, 10);
        hero.AddView(Text(UiText.Format("{0} cards · {1} columns", board.Columns.Sum(c => c.Cards.Count), board.Columns.Count) + (board.CanEdit ? "" : " · " + UiText.Get("View only")), 13, Resource.Color.hero_muted));
        _heading.AddView(hero);
        var scroll = new HorizontalScrollView(this) { HorizontalScrollBarEnabled = false };
        var tabs = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        tabs.SetPadding(Dp(12), Dp(8), Dp(12), 0);
        var index = 0;
        foreach (var column in board.Columns.OrderBy(c => c.Order))
        {
            var position = index++;
            var tab = new MaterialButton(this) { Text = column.Name, TextSize = 13, CornerRadius = Dp(16) };
            tab.SetAllCaps(false);
            tab.Elevation = 0;
            tab.SetMaxLines(1);
            tab.SetMaxWidth(Dp(240));
            tabs.AddView(tab, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, Dp(48)));
            tab.Click += (_, _) => _boardScroll.SmoothScrollTo(position * (ColumnWidth() + Dp(12)), 0);
        }
        scroll.AddView(tabs);
        _heading.AddView(scroll);
        _columnTabs = tabs;
    }

    private LinearLayout? _columnTabs;
    private void UpdateColumnSelection()
    {
        if (_columnTabs == null) return;
        var selected = (int)Math.Round((double)_boardScroll.ScrollX / (ColumnWidth() + Dp(12)));
        for (var i = 0; i < _columnTabs.ChildCount; i++)
        {
            if (_columnTabs.GetChildAt(i) is not MaterialButton tab) continue;
            tab.BackgroundTintList = ColorStateList.ValueOf(ColorOf(i == selected ? Resource.Color.brand_container : Resource.Color.surface_canvas));
            tab.SetTextColor(ColorOf(i == selected ? Resource.Color.brand_primary : Resource.Color.text_secondary));
            tab.Selected = i == selected;
        }
    }

    private int ColumnWidth() => Math.Min(Dp(380), Math.Max(Dp(260), Resources!.DisplayMetrics!.WidthPixels - Dp(44)));
    private void CaptureBoardPosition()
    {
        if (_renderedBoardId == 0 || _showingHome || _boardCanvas.ChildCount == 0) return;
        _boardPosition = _boardScroll.ScrollX;
        foreach (var (id, scroll) in _columnScrolls) _columnPositions[id] = scroll.ScrollY;
    }
    private void RestoreBoardPosition(int boardId)
    {
        if (_renderedBoardId != boardId) { _boardPosition = 0; _columnPositions.Clear(); }
        _renderedBoardId = boardId;
        _boardScroll.Post(() => { _boardScroll.ScrollTo(_boardPosition, 0); UpdateColumnSelection(); });
        foreach (var (id, scroll) in _columnScrolls)
            if (_columnPositions.TryGetValue(id, out var position)) scroll.Post(() => scroll.ScrollTo(0, position));
    }
    private void Place(ViewGroup parent, View view, int top, int bottom, int? height = null)
    {
        var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, height.HasValue ? Dp(height.Value) : ViewGroup.LayoutParams.WrapContent);
        lp.SetMargins(0, Dp(top), 0, Dp(bottom));
        parent.AddView(view, lp);
    }
}
