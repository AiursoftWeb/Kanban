using Android.App;
using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Provider;
using Android.Text;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using Aiursoft.Kanban.Android.Oidc;
using Aiursoft.Kanban.SDK;
using Aiursoft.Kanban.SDK.Models;
using Google.Android.Material.AppBar;
using Google.Android.Material.Button;
using Google.Android.Material.Card;
using Google.Android.Material.Dialog;
using Google.Android.Material.ProgressIndicator;
using Google.Android.Material.Snackbar;
using Google.Android.Material.TextField;
using Color = Android.Graphics.Color;

namespace Aiursoft.Kanban.Android;

[Activity(Label = "Card details", Exported = false, Theme = "@style/AppTheme")]
public sealed class CardDetailActivity : AppCompatActivity
{
    public const string CardIdExtra = "card_id";
    private const int ImagePickerRequestCode = 7301;
    private const int MaxPendingImages = 10;
    private const long MaxImageBytes = 10L * 1024 * 1024;
    private const string CommentDraftState = "comment_draft";
    private const string PendingImageUrisState = "pending_image_uris";

    private static readonly string[] Priorities = ["Urgent", "High", "Medium", "Low", "None"];
    private static readonly string[] RecurrenceUnits = ["Day", "Week", "Month", "Year"];

    private View _root = null!;
    private MaterialToolbar _toolbar = null!;
    private global::AndroidX.Core.Widget.NestedScrollView _scroll = null!;
    private LinearLayout _content = null!;
    private CircularProgressIndicator _progress = null!;
    private readonly List<PendingImageAttachment> _pendingImages = [];
    private readonly Dictionary<string, Task<Bitmap?>> _remoteImageLoads = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _imageLoadCancellation = new();
    private LinearLayout? _pendingImagePreviews;
    private TextView? _pendingImageSummary;
    private CardDetailsDto? _card;
    private string _commentDraft = string.Empty;
    private int _cardId;
    private bool _busy;

    private AppSession Session => ((KanbanApplication)Application!).Session;
    private KanbanApiClient Api => Session.RequireApi();

    public static Intent CreateIntent(Context context, int cardId) =>
        new Intent(context, typeof(CardDetailActivity)).PutExtra(CardIdExtra, cardId);

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (!Session.IsAuthenticated)
        {
            ReturnToLogin();
            return;
        }

        _cardId = Intent?.GetIntExtra(CardIdExtra, 0) ?? 0;
        if (_cardId <= 0)
        {
            Finish();
            return;
        }
        RestoreCommentComposer(savedInstanceState);

        SetContentView(Resource.Layout.activity_card_detail);
        _root = FindViewById<View>(Resource.Id.card_detail_root)!;
        _toolbar = FindViewById<MaterialToolbar>(Resource.Id.card_detail_toolbar)!;
        _scroll = FindViewById<global::AndroidX.Core.Widget.NestedScrollView>(Resource.Id.card_detail_scroll)!;
        _content = FindViewById<LinearLayout>(Resource.Id.card_detail_content)!;
        _progress = FindViewById<CircularProgressIndicator>(Resource.Id.card_detail_progress)!;

        _toolbar.SetOnApplyWindowInsetsListener(new ToolbarInsetListener(Dp(64)));
        _content.SetOnApplyWindowInsetsListener(
            new SystemBarInsetListener(Dp(16), Dp(18), Dp(16), Dp(32), false, true));
        _toolbar.NavigationContentDescription = "Back to board";
        _toolbar.NavigationClick += (_, _) => Finish();
        _ = LoadAsync();
    }

    protected override void OnSaveInstanceState(Bundle outState)
    {
        outState.PutString(CommentDraftState, _commentDraft);
        outState.PutStringArray(
            PendingImageUrisState,
            _pendingImages
                .Select(image => image.Uri.ToString() ?? string.Empty)
                .Where(value => value.Length > 0)
                .ToArray());
        base.OnSaveInstanceState(outState);
    }

    private void RestoreCommentComposer(Bundle? savedInstanceState)
    {
        if (savedInstanceState == null)
        {
            return;
        }
        _commentDraft = savedInstanceState.GetString(CommentDraftState) ?? string.Empty;
        foreach (var value in savedInstanceState.GetStringArray(PendingImageUrisState) ?? [])
        {
            var uri = global::Android.Net.Uri.Parse(value);
            if (uri == null)
            {
                continue;
            }
            var attachment = ReadPendingImage(uri);
            if (attachment != null)
            {
                _pendingImages.Add(attachment);
            }
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != ImagePickerRequestCode || resultCode != Result.Ok || data == null)
        {
            return;
        }

        var selectedUris = new List<global::Android.Net.Uri>();
        if (data.ClipData != null)
        {
            for (var index = 0; index < data.ClipData.ItemCount; index++)
            {
                var uri = data.ClipData.GetItemAt(index)?.Uri;
                if (uri != null)
                {
                    selectedUris.Add(uri);
                }
            }
        }
        else if (data.Data != null)
        {
            selectedUris.Add(data.Data);
        }

        var rejected = 0;
        foreach (var uri in selectedUris)
        {
            if (_pendingImages.Count >= MaxPendingImages)
            {
                rejected++;
                continue;
            }
            if (_pendingImages.Any(image => image.Uri.ToString() == uri.ToString()))
            {
                continue;
            }

            var attachment = ReadPendingImage(uri);
            if (attachment == null)
            {
                rejected++;
                continue;
            }
            TryPersistReadPermission(uri);
            _pendingImages.Add(attachment);
        }

        RenderPendingImagePreviews();
        if (rejected > 0)
        {
            Snackbar.Make(
                _root,
                $"{rejected} image(s) were skipped. Use PNG, JPEG, GIF, WebP, or BMP up to 10 MB.",
                Snackbar.LengthLong).Show();
        }
    }

    protected override void OnDestroy()
    {
        _imageLoadCancellation.Cancel();
        foreach (var attachment in _pendingImages)
        {
            attachment.Thumbnail?.Dispose();
        }
        foreach (var load in _remoteImageLoads.Values.Where(load => load.IsCompletedSuccessfully))
        {
            load.Result?.Dispose();
        }
        _imageLoadCancellation.Dispose();
        base.OnDestroy();
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
            var response = await Api.GetCardDetailsAsync(_cardId);
            _card = response.Card;
            Render();
        }
        catch (Exception exception)
        {
            ShowError(exception, retry: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Render()
    {
        var card = _card;
        if (card == null)
        {
            return;
        }

        var previousScroll = _scroll.ScrollY;
        _content.RemoveAllViews();
        _toolbar.Title = card.Title;
        _toolbar.Subtitle = $"{card.BoardName}  ·  {card.ColumnName}";

        var contextRow = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        contextRow.SetGravity(GravityFlags.CenterVertical);
        var access = Badge(card.CanEdit ? "CAN EDIT" : "VIEW ONLY",
            card.CanEdit ? Resource.Color.success_container : Resource.Color.warning_container,
            card.CanEdit ? Resource.Color.on_success_container : Resource.Color.on_warning_container);
        contextRow.AddView(access);
        var subscribe = SecondaryButton(card.IsSubscribed ? "Subscribed" : "Subscribe");
        var subscribeLayout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, Dp(44));
        subscribeLayout.SetMargins(Dp(10), 0, 0, 0);
        contextRow.AddView(subscribe, subscribeLayout);
        Add(contextRow, 0, 18);
        subscribe.Click += async (_, _) => await ToggleSubscriptionAsync(subscribe);

        var (titleBox, titleInput) = Input("Title", card.Title, multiline: false);
        titleInput.Enabled = card.CanEdit;
        titleBox.Enabled = card.CanEdit;
        Add(titleBox, 0, 12);

        var (descriptionBox, descriptionInput) = Input(
            "Description", card.Description ?? string.Empty, multiline: true);
        descriptionInput.Enabled = card.CanEdit;
        descriptionBox.Enabled = card.CanEdit;
        Add(descriptionBox, 0, 16);

        var choices = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        var priorityGroup = LabeledSpinner("PRIORITY", Priorities, card.Priority, card.CanEdit);
        choices.AddView(priorityGroup.Container,
            new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));

        var assignees = card.AvailableAssignees.ToList();
        if (card.AssignedUser != null && assignees.All(user => user.Id != card.AssignedUser.Id))
        {
            assignees.Add(card.AssignedUser);
        }
        var assigneeNames = new[] { "Unassigned" }
            .Concat(assignees.Select(user => user.DisplayName))
            .ToArray();
        var selectedAssignee = card.AssignedUser == null
            ? "Unassigned"
            : card.AssignedUser.DisplayName;
        var assigneeGroup = LabeledSpinner("ASSIGNEE", assigneeNames, selectedAssignee, card.CanEdit);
        var assigneeLayout = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1);
        assigneeLayout.SetMargins(Dp(12), 0, 0, 0);
        choices.AddView(assigneeGroup.Container, assigneeLayout);
        Add(choices, 0, 16);

        var schedule = ScheduleEditor(card);
        Add(schedule.Container, 0, 16);
        Add(LabelsCard(card), 0, 18);

        var save = PrimaryButton("Save changes");
        save.Visibility = card.CanEdit ? ViewStates.Visible : ViewStates.Gone;
        Add(save, 0, 24, Dp(56));
        save.Click += async (_, _) =>
        {
            var title = titleInput.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(title))
            {
                titleBox.Error = "Enter a card title";
                titleInput.RequestFocus();
                return;
            }
            titleBox.Error = null;
            var selectedAssigneeIndex = assigneeGroup.Spinner.SelectedItemPosition;
            var assignedUserId = selectedAssigneeIndex <= 0
                ? null
                : assignees[selectedAssigneeIndex - 1].Id;
            if (!TryReadRecurrence(schedule, out var recurrenceInterval, out var recurrenceUnit))
            {
                return;
            }
            await SaveAsync(save, new UpdateCardRequest
            {
                Title = title,
                Description = descriptionInput.Text,
                Priority = Priorities[priorityGroup.Spinner.SelectedItemPosition],
                AssignedUserId = assignedUserId,
                PlannedStartTime = schedule.PlannedStartTime,
                DueDate = schedule.DueDate,
                RecurrenceInterval = recurrenceInterval,
                RecurrenceUnit = recurrenceUnit
            });
        };

        if (card.CanEdit)
        {
            Add(CardActions(card), 0, 24);
        }

        var creatorName = card.CreatorUser?.DisplayName ?? "Unknown";
        Add(Text($"Created by {creatorName} · {FormatDateTime(card.CreationTime)}",
            13, Resource.Color.text_secondary), 0, 24);

        _content.AddView(SectionTitle($"COMMENTS  {card.Comments.Count}"));
        if (card.Comments.Count == 0)
        {
            Add(Text("No replies yet.", 14, Resource.Color.text_secondary), 10, 14);
        }
        else
        {
            foreach (var comment in card.Comments)
            {
                Add(CommentCard(comment), 8, 2);
            }
        }

        if (card.CanEdit)
        {
            var (commentBox, commentInput) = Input("Write a reply", _commentDraft, multiline: true, minLines: 2);
            commentInput.TextChanged += (_, _) => _commentDraft = commentInput.Text ?? string.Empty;
            Add(commentBox, 16, 10);
            Add(CommentAttachmentComposer(), 0, 10);
            var addComment = PrimaryButton("Reply");
            Add(addComment, 0, 24, Dp(50));
            addComment.Click += async (_, _) =>
                await AddCommentAsync(commentBox, commentInput, addComment);
        }
        else
        {
            Add(Text("This board is view-only, so replies and edits are disabled.",
                13, Resource.Color.text_secondary), 12, 24);
        }

        if (card.CanDelete)
        {
            var delete = DangerButton("Delete card");
            Add(delete, 0, 8, Dp(52));
            delete.Click += (_, _) => ConfirmDeleteCard();
        }

        _scroll.Post(() => _scroll.ScrollTo(0, previousScroll));
    }

    private ScheduleEditorState ScheduleEditor(CardDetailsDto card)
    {
        var state = new ScheduleEditorState
        {
            PlannedStartTime = card.PlannedStartTime?.Date,
            DueDate = card.DueDate?.Date,
            Recurring = new CheckBox(this)
            {
                Text = "Recurring task",
                Checked = card.RecurrenceInterval.HasValue,
                Enabled = card.CanEdit
            },
            IntervalInput = new EditText(this)
            {
                Hint = "Interval (1–365)",
                Text = card.RecurrenceInterval?.ToString() ?? "1",
                TextSize = 14,
                Enabled = card.CanEdit,
                InputType = InputTypes.ClassNumber
            },
            UnitSpinner = new Spinner(this, SpinnerMode.Dialog)
            {
                Enabled = card.CanEdit
            }
        };
        var shell = SurfaceCard();
        var content = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        content.SetPadding(Dp(16), Dp(14), Dp(16), Dp(14));
        content.AddView(SectionTitle("TIMELINE"));
        content.AddView(DateEditorRow("Planned start", state, dueDate: false, card.CanEdit));
        content.AddView(DateEditorRow("Due", state, dueDate: true, card.CanEdit));
        if (card.ActualStartTime.HasValue)
        {
            content.AddView(Metadata("Started", FormatDateTime(card.ActualStartTime.Value)));
        }
        if (card.ActualEndTime.HasValue)
        {
            content.AddView(Metadata("Completed", FormatDateTime(card.ActualEndTime.Value)));
        }
        state.Recurring.SetTextColor(ColorOf(Resource.Color.text_primary));
        content.AddView(state.Recurring);

        var recurrenceFields = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        recurrenceFields.AddView(state.IntervalInput,
            new LinearLayout.LayoutParams(0, Dp(52), 1));
        var unitAdapter = new ArrayAdapter<string>(this,
            global::Android.Resource.Layout.SimpleSpinnerItem, RecurrenceUnits);
        unitAdapter.SetDropDownViewResource(global::Android.Resource.Layout.SimpleSpinnerDropDownItem);
        state.UnitSpinner.Adapter = unitAdapter;
        var unitIndex = Array.FindIndex(RecurrenceUnits,
            unit => string.Equals(unit, card.RecurrenceUnit, StringComparison.OrdinalIgnoreCase));
        state.UnitSpinner.SetSelection(Math.Max(0, unitIndex));
        var unitLayout = new LinearLayout.LayoutParams(0, Dp(52), 1);
        unitLayout.SetMargins(Dp(10), 0, 0, 0);
        recurrenceFields.AddView(state.UnitSpinner, unitLayout);
        recurrenceFields.Visibility = state.Recurring.Checked ? ViewStates.Visible : ViewStates.Gone;
        content.AddView(recurrenceFields);
        var recurrenceHint = Text("Recurring cards require a due date.", 12, Resource.Color.text_secondary);
        recurrenceHint.Visibility = recurrenceFields.Visibility;
        content.AddView(recurrenceHint);
        state.Recurring.CheckedChange += (_, args) =>
        {
            recurrenceFields.Visibility = args.IsChecked ? ViewStates.Visible : ViewStates.Gone;
            recurrenceHint.Visibility = recurrenceFields.Visibility;
        };
        shell.AddView(content);
        state.Container = shell;
        return state;
    }

    private View DateEditorRow(string label, ScheduleEditorState state, bool dueDate, bool enabled)
    {
        var row = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(0, Dp(6), 0, Dp(6));
        row.AddView(Text(label, 13, Resource.Color.text_secondary),
            new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));

        DateTime? Current() => dueDate ? state.DueDate : state.PlannedStartTime;
        void Update(DateTime? value)
        {
            if (dueDate)
            {
                state.DueDate = value;
            }
            else
            {
                state.PlannedStartTime = value;
            }
        }

        var clear = SecondaryButton("Clear");
        clear.Enabled = enabled;
        clear.Visibility = Current().HasValue ? ViewStates.Visible : ViewStates.Gone;
        var choose = SecondaryButton(FormatDate(Current()));
        choose.Enabled = enabled;
        choose.Click += (_, _) => ShowDatePicker(Current(), value =>
        {
            Update(value);
            choose.Text = FormatDate(value);
            clear.Visibility = ViewStates.Visible;
        });
        clear.Click += (_, _) =>
        {
            Update(null);
            choose.Text = FormatDate(null);
            clear.Visibility = ViewStates.Gone;
        };
        row.AddView(choose, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, Dp(44)));
        row.AddView(clear, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, Dp(44)));
        return row;
    }

    private View LabelsCard(CardDetailsDto card)
    {
        var shell = SurfaceCard();
        var content = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        content.SetPadding(Dp(16), Dp(14), Dp(16), Dp(14));
        content.AddView(SectionTitle("LABELS"));
        if (card.Labels.Count == 0)
        {
            content.AddView(Text("No labels", 13, Resource.Color.text_secondary));
        }
        else
        {
            content.AddView(LabelStrip(card.Labels, card.CanEdit));
        }

        if (card.CanEdit)
        {
            var row = new LinearLayout(this)
            {
                Orientation = global::Android.Widget.Orientation.Horizontal
            };
            var box = new TextInputLayout(this)
            {
                Hint = "Search or create a label",
                BoxBackgroundMode = TextInputLayout.BoxBackgroundOutline
            };
            box.SetBoxCornerRadii(Dp(14), Dp(14), Dp(14), Dp(14));
            var input = new AutoCompleteTextView(this)
            {
                Threshold = 0,
                TextSize = 14,
                InputType = InputTypes.ClassText | InputTypes.TextFlagCapWords
            };
            input.SetSingleLine(true);
            var attachedIds = card.Labels.Select(label => label.Id).ToHashSet();
            var suggestions = card.AvailableLabels
                .Where(label => !attachedIds.Contains(label.Id))
                .Select(label => label.Name)
                .ToArray();
            input.Adapter = new ArrayAdapter<string>(this,
                global::Android.Resource.Layout.SimpleDropDownItem1Line, suggestions);
            input.Click += (_, _) =>
            {
                if (suggestions.Length > 0)
                {
                    input.ShowDropDown();
                }
            };
            box.AddView(input);
            row.AddView(box, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
            var add = PrimaryButton("Add");
            var addLayout = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, Dp(56));
            addLayout.SetMargins(Dp(10), 0, 0, 0);
            row.AddView(add, addLayout);
            add.Click += async (_, _) => await AddLabelAsync(card, box, input, add);
            var rowLayout = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
            rowLayout.SetMargins(0, Dp(12), 0, 0);
            content.AddView(row, rowLayout);
        }
        shell.AddView(content);
        return shell;
    }

    private View CardActions(CardDetailsDto card)
    {
        var shell = SurfaceCard();
        var content = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        content.SetPadding(Dp(16), Dp(14), Dp(16), Dp(16));
        content.AddView(SectionTitle("ACTIONS"));
        content.AddView(Text("Save pending edits before moving this card.",
            12, Resource.Color.text_secondary));

        var columns = card.AvailableColumns;
        if (columns.Count > 0)
        {
            var columnNames = columns.Select(column => column.Name).ToArray();
            var selectedColumn = columns.FirstOrDefault(column => column.Id == card.ColumnId)?.Name
                ?? columnNames[0];
            var selector = LabeledSpinner("MOVE TO COLUMN", columnNames, selectedColumn, enabled: true);
            var selectorLayout = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
            selectorLayout.SetMargins(0, Dp(12), 0, Dp(6));
            content.AddView(selector.Container, selectorLayout);

            var move = PrimaryButton("Move card");
            content.AddView(move, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, Dp(52)));
            move.Click += async (_, _) =>
            {
                var index = Math.Clamp(selector.Spinner.SelectedItemPosition, 0, columns.Count - 1);
                await MoveFromDetailsAsync(card, columns[index], move);
            };
        }

        var transfer = SecondaryButton("Transfer to another board");
        var transferLayout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, Dp(50));
        transferLayout.SetMargins(0, Dp(10), 0, 0);
        content.AddView(transfer, transferLayout);
        transfer.Click += async (_, _) => await ShowTransferPickerAsync(transfer);
        shell.AddView(content);
        return shell;
    }

    private View LabelStrip(IReadOnlyList<CardLabelDto> labels, bool canEdit)
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
            var chip = new LinearLayout(this)
            {
                Orientation = global::Android.Widget.Orientation.Horizontal
            };
            chip.SetGravity(GravityFlags.CenterVertical);
            chip.SetPadding(Dp(12), Dp(7), canEdit ? Dp(6) : Dp(12), Dp(7));
            chip.Background = Rounded(ParseColor(label.Color, ColorOf(Resource.Color.brand_container)), 18);
            chip.AddView(Text(label.Name, 13, Resource.Color.text_primary, true));
            if (canEdit)
            {
                var remove = Text("×", 20, Resource.Color.text_primary, true);
                remove.Gravity = GravityFlags.Center;
                remove.ContentDescription = $"Remove {label.Name}";
                remove.SetPadding(Dp(9), 0, Dp(5), 0);
                remove.Clickable = true;
                remove.Click += async (_, _) => await RemoveLabelAsync(label, remove);
                chip.AddView(remove, new LinearLayout.LayoutParams(Dp(38), Dp(34)));
            }
            var layout = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);
            layout.SetMargins(0, 0, Dp(8), 0);
            row.AddView(chip, layout);
        }
        scroll.AddView(row);
        return scroll;
    }

    private bool TryReadRecurrence(
        ScheduleEditorState schedule,
        out int? interval,
        out string unit)
    {
        interval = null;
        unit = "None";
        schedule.IntervalInput.Error = null;
        if (!schedule.Recurring.Checked)
        {
            return true;
        }
        if (!schedule.DueDate.HasValue)
        {
            Snackbar.Make(_root, "Set a due date before enabling recurrence.", Snackbar.LengthLong).Show();
            return false;
        }
        if (!int.TryParse(schedule.IntervalInput.Text, out var parsed) || parsed is < 1 or > 365)
        {
            schedule.IntervalInput.Error = "Enter a value from 1 to 365";
            schedule.IntervalInput.RequestFocus();
            return false;
        }

        interval = parsed;
        unit = RecurrenceUnits[Math.Clamp(
            schedule.UnitSpinner.SelectedItemPosition, 0, RecurrenceUnits.Length - 1)];
        return true;
    }

    private void ShowDatePicker(DateTime? current, Action<DateTime> onSelected)
    {
        var initial = current?.Date ?? DateTime.UtcNow.Date;
        var picker = new DatePickerDialog(
            this,
            (_, args) => onSelected(DateTime.SpecifyKind(args.Date.Date, DateTimeKind.Utc)),
            initial.Year,
            initial.Month - 1,
            initial.Day);
        picker.Show();
    }

    private async Task AddLabelAsync(
        CardDetailsDto card,
        TextInputLayout box,
        AutoCompleteTextView input,
        MaterialButton button)
    {
        var name = input.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            box.Error = "Enter a label name";
            input.RequestFocus();
            return;
        }
        if (name.Length > 100)
        {
            box.Error = "Labels cannot exceed 100 characters";
            input.RequestFocus();
            return;
        }

        try
        {
            box.Error = null;
            button.Enabled = false;
            var response = await Api.AddCardLabelAsync(_cardId,
                new AddCardLabelRequest { Name = name });
            card.Labels.RemoveAll(label => label.Id == response.Label.Id ||
                string.Equals(label.Name, response.Label.Name, StringComparison.OrdinalIgnoreCase));
            card.Labels.Add(response.Label);
            card.Labels = card.Labels.OrderBy(label => label.Name).ToList();
            if (card.AvailableLabels.All(label => label.Id != response.Label.Id))
            {
                card.AvailableLabels.Add(response.Label);
            }
            Render();
            Snackbar.Make(_root, "Label added", Snackbar.LengthShort).Show();
        }
        catch (Exception exception)
        {
            button.Enabled = true;
            ShowError(exception, retry: false);
        }
    }

    private async Task RemoveLabelAsync(CardLabelDto label, View button)
    {
        try
        {
            button.Enabled = false;
            await Api.RemoveCardLabelAsync(_cardId, label.Id);
            _card?.Labels.RemoveAll(item => item.Id == label.Id);
            Render();
            Snackbar.Make(_root, "Label removed", Snackbar.LengthShort).Show();
        }
        catch (Exception exception)
        {
            button.Enabled = true;
            ShowError(exception, retry: false);
        }
    }

    private async Task MoveFromDetailsAsync(
        CardDetailsDto card,
        CardColumnOptionDto target,
        MaterialButton button)
    {
        if (target.Id == card.ColumnId)
        {
            Snackbar.Make(_root, "The card is already in this column.", Snackbar.LengthShort).Show();
            return;
        }

        try
        {
            button.Enabled = false;
            var result = await Api.MoveCardAsync(_cardId, new MoveCardRequest
            {
                TargetColumnId = target.Id,
                NewOrder = 0
            });
            _card = (await Api.GetCardDetailsAsync(_cardId)).Card;
            Render();
            Snackbar.Make(_root, result.Message ?? "Card moved", Snackbar.LengthLong).Show();
        }
        catch (Exception exception)
        {
            button.Enabled = true;
            ShowError(exception, retry: false);
        }
    }

    private async Task ShowTransferPickerAsync(MaterialButton button)
    {
        try
        {
            button.Enabled = false;
            var response = await Api.GetCardTransferTargetsAsync(_cardId);
            var destinations = response.Boards
                .SelectMany(board => board.Columns.Select(column =>
                    new TransferDestination(board, column)))
                .ToList();
            if (destinations.Count == 0)
            {
                Snackbar.Make(_root,
                    "No other editable board with columns is available.",
                    Snackbar.LengthLong).Show();
                return;
            }

            var container = new LinearLayout(this)
            {
                Orientation = global::Android.Widget.Orientation.Vertical
            };
            container.SetPadding(Dp(24), Dp(4), Dp(24), 0);
            container.AddView(Text(
                "The assignee is cleared and replies stay with the source card history.",
                13,
                Resource.Color.text_secondary));
            var spinner = new Spinner(this, SpinnerMode.Dialog);
            var adapter = new ArrayAdapter<string>(this,
                global::Android.Resource.Layout.SimpleSpinnerItem,
                destinations.Select(destination =>
                    $"{destination.Board.Name} / {destination.Column.Name}").ToArray());
            adapter.SetDropDownViewResource(global::Android.Resource.Layout.SimpleSpinnerDropDownItem);
            spinner.Adapter = adapter;
            var spinnerLayout = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, Dp(56));
            spinnerLayout.SetMargins(0, Dp(12), 0, 0);
            container.AddView(spinner, spinnerLayout);

            var builder = new MaterialAlertDialogBuilder(this);
            builder.SetTitle("Transfer card");
            builder.SetView(container);
            builder.SetNegativeButton("Cancel", (_, _) => { });
            builder.SetPositiveButton("Transfer", (dialog, args) =>
            {
                var index = Math.Clamp(spinner.SelectedItemPosition, 0, destinations.Count - 1);
                _ = TransferAsync(destinations[index]);
            });
            builder.Show();
        }
        catch (Exception exception)
        {
            ShowError(exception, retry: false);
        }
        finally
        {
            button.Enabled = true;
        }
    }

    private async Task TransferAsync(TransferDestination destination)
    {
        try
        {
            SetBusy(true);
            var result = await Api.TransferCardAsync(_cardId, new TransferCardRequest
            {
                TargetBoardId = destination.Board.Id,
                TargetColumnId = destination.Column.Id
            });
            _cardId = result.CardId;
            Intent?.PutExtra(CardIdExtra, _cardId);
            Session.SelectedBoardId = result.BoardId;
            _card = (await Api.GetCardDetailsAsync(_cardId)).Card;
            SetResult(Result.Ok);
            Render();
            Snackbar.Make(_root, "Card transferred", Snackbar.LengthLong).Show();
        }
        catch (Exception exception)
        {
            ShowError(exception, retry: false);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private View CommentAttachmentComposer()
    {
        var container = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        var controls = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        controls.SetGravity(GravityFlags.CenterVertical);
        var choose = SecondaryButton("Add images");
        choose.ContentDescription = "Choose images to attach to this reply";
        choose.Click += (_, _) => PickCommentImages();
        controls.AddView(choose, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, Dp(44)));
        _pendingImageSummary = Text(string.Empty, 12, Resource.Color.text_secondary);
        var summaryLayout = new LinearLayout.LayoutParams(
            0, ViewGroup.LayoutParams.WrapContent, 1);
        summaryLayout.SetMargins(Dp(8), 0, 0, 0);
        controls.AddView(_pendingImageSummary, summaryLayout);
        container.AddView(controls);

        var previewScroll = new HorizontalScrollView(this)
        {
            HorizontalScrollBarEnabled = false
        };
        _pendingImagePreviews = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        _pendingImagePreviews.SetPadding(0, Dp(4), 0, Dp(2));
        previewScroll.AddView(_pendingImagePreviews);
        container.AddView(previewScroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        RenderPendingImages(previewScroll, choose);
        return container;
    }

    private void RenderPendingImagePreviews()
    {
        if (_pendingImagePreviews?.Parent is not HorizontalScrollView previewScroll)
        {
            return;
        }
        var choose = (previewScroll.Parent as LinearLayout)?
            .GetChildAt(0) is LinearLayout controls
            ? controls.GetChildAt(0) as MaterialButton
            : null;
        RenderPendingImages(previewScroll, choose);
    }

    private void RenderPendingImages(HorizontalScrollView previewScroll, MaterialButton? choose)
    {
        if (_pendingImagePreviews == null || _pendingImageSummary == null)
        {
            return;
        }

        _pendingImagePreviews.RemoveAllViews();
        _pendingImageSummary.Text = _pendingImages.Count == 0
            ? "PNG, JPEG, GIF, WebP, or BMP · 10 MB each"
            : $"{_pendingImages.Count} of {MaxPendingImages} selected";
        previewScroll.Visibility = _pendingImages.Count == 0 ? ViewStates.Gone : ViewStates.Visible;
        if (choose != null)
        {
            choose.Enabled = _pendingImages.Count < MaxPendingImages;
        }

        foreach (var attachment in _pendingImages.ToArray())
        {
            var item = new LinearLayout(this)
            {
                Orientation = global::Android.Widget.Orientation.Vertical
            };
            var imageShell = new MaterialCardView(this)
            {
                Radius = Dp(10),
                CardElevation = 0
            };
            imageShell.SetCardBackgroundColor(GetColor(Resource.Color.surface_variant));
            imageShell.StrokeColor = GetColor(Resource.Color.outline);
            imageShell.StrokeWidth = Dp(1);
            var image = new ImageView(this)
            {
                ContentDescription = attachment.DisplayName
            };
            image.SetScaleType(ImageView.ScaleType.CenterCrop);
            imageShell.AddView(image, new ViewGroup.LayoutParams(Dp(96), Dp(78)));
            item.AddView(imageShell, new LinearLayout.LayoutParams(Dp(96), Dp(78)));

            var remove = Text("Remove", 12, Resource.Color.brand_primary, true);
            remove.Gravity = GravityFlags.Center;
            remove.ContentDescription = $"Remove {attachment.DisplayName}";
            remove.Clickable = true;
            remove.SetPadding(0, Dp(5), 0, Dp(5));
            remove.Click += (_, _) => RemovePendingImage(attachment);
            item.AddView(remove, new LinearLayout.LayoutParams(Dp(96), Dp(34)));
            var itemLayout = new LinearLayout.LayoutParams(Dp(96), ViewGroup.LayoutParams.WrapContent);
            itemLayout.SetMargins(0, 0, Dp(10), 0);
            _pendingImagePreviews.AddView(item, itemLayout);
            _ = LoadPendingImageThumbnailAsync(attachment, image);
        }
    }

    private void PickCommentImages()
    {
        if (_pendingImages.Count >= MaxPendingImages)
        {
            Snackbar.Make(_root, $"You can attach up to {MaxPendingImages} images.", Snackbar.LengthLong).Show();
            return;
        }

        var picker = new Intent(Intent.ActionOpenDocument);
        picker.AddCategory(Intent.CategoryOpenable);
        picker.SetType("image/*");
        picker.PutExtra(Intent.ExtraAllowMultiple, true);
        picker.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);
        try
        {
            StartActivityForResult(Intent.CreateChooser(picker, "Choose comment images"), ImagePickerRequestCode);
        }
        catch (ActivityNotFoundException)
        {
            Snackbar.Make(_root, "No image picker is available on this device.", Snackbar.LengthLong).Show();
        }
    }

    private PendingImageAttachment? ReadPendingImage(global::Android.Net.Uri uri)
    {
        var displayName = $"Image {_pendingImages.Count + 1}";
        long? size = null;
        try
        {
            using var cursor = ContentResolver?.Query(
                uri,
                [IOpenableColumns.DisplayName, IOpenableColumns.Size],
                null,
                null,
                null);
            if (cursor?.MoveToFirst() == true)
            {
                var nameIndex = cursor.GetColumnIndex(IOpenableColumns.DisplayName);
                if (nameIndex >= 0 && !cursor.IsNull(nameIndex))
                {
                    displayName = cursor.GetString(nameIndex) ?? displayName;
                }
                var sizeIndex = cursor.GetColumnIndex(IOpenableColumns.Size);
                if (sizeIndex >= 0 && !cursor.IsNull(sizeIndex))
                {
                    size = cursor.GetLong(sizeIndex);
                }
            }
        }
        catch
        {
            // Some document providers do not expose metadata. The upload endpoint validates the stream.
        }

        if (size > MaxImageBytes)
        {
            return null;
        }
        var contentType = ContentResolver?.GetType(uri)?.ToLowerInvariant();
        var extension = SupportedImageExtension(contentType, displayName);
        if (extension == null)
        {
            return null;
        }
        contentType = extension switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "bmp" => "image/bmp",
            _ => $"image/{extension}"
        };
        return new PendingImageAttachment(uri, displayName, contentType, extension);
    }

    private static string? SupportedImageExtension(string? contentType, string displayName)
    {
        var fromMime = contentType switch
        {
            "image/jpeg" or "image/jpg" => "jpg",
            "image/png" => "png",
            "image/gif" => "gif",
            "image/webp" => "webp",
            "image/bmp" or "image/x-ms-bmp" => "bmp",
            _ => null
        };
        if (fromMime != null)
        {
            return fromMime;
        }

        var extension = System.IO.Path.GetExtension(displayName).TrimStart('.').ToLowerInvariant();
        return extension is "bmp" or "gif" or "jpeg" or "jpg" or "png" or "webp"
            ? extension
            : null;
    }

    private void TryPersistReadPermission(global::Android.Net.Uri uri)
    {
        try
        {
            ContentResolver?.TakePersistableUriPermission(uri, ActivityFlags.GrantReadUriPermission);
        }
        catch
        {
            // The grant remains valid while the activity is open even if the provider is not persistable.
        }
    }

    private void RemovePendingImage(PendingImageAttachment attachment)
    {
        if (!_pendingImages.Remove(attachment))
        {
            return;
        }
        attachment.Thumbnail?.Dispose();
        attachment.Thumbnail = null;
        RenderPendingImagePreviews();
    }

    private async Task LoadPendingImageThumbnailAsync(
        PendingImageAttachment attachment,
        ImageView target)
    {
        try
        {
            attachment.ThumbnailLoad ??= Task.Run(
                () => DecodeLocalThumbnail(attachment.Uri),
                _imageLoadCancellation.Token);
            var bitmap = await attachment.ThumbnailLoad;
            if (bitmap == null)
            {
                return;
            }
            if (!_pendingImages.Contains(attachment))
            {
                bitmap.Dispose();
                return;
            }
            attachment.Thumbnail = bitmap;
            RunOnUiThread(() =>
            {
                if (!IsFinishing && !IsDestroyed && target.Parent != null)
                {
                    target.SetImageBitmap(bitmap);
                }
            });
        }
        catch (System.OperationCanceledException)
        {
            // Activity is closing.
        }
        catch
        {
            // A thumbnail is optional; the original stream can still be uploaded.
        }
    }

    private Bitmap? DecodeLocalThumbnail(global::Android.Net.Uri uri)
    {
        _imageLoadCancellation.Token.ThrowIfCancellationRequested();
        using var boundsStream = ContentResolver?.OpenInputStream(uri);
        if (boundsStream == null)
        {
            return null;
        }
        using var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeStream(boundsStream, null, bounds);
        var longestEdge = Math.Max(bounds.OutWidth, bounds.OutHeight);
        var sampleSize = 1;
        while (longestEdge / sampleSize > 320)
        {
            sampleSize *= 2;
        }

        _imageLoadCancellation.Token.ThrowIfCancellationRequested();
        using var imageStream = ContentResolver?.OpenInputStream(uri);
        if (imageStream == null)
        {
            return null;
        }
        using var options = new BitmapFactory.Options { InSampleSize = sampleSize };
        return BitmapFactory.DecodeStream(imageStream, null, options);
    }

    private View CommentCard(CardCommentDto comment)
    {
        var shell = SurfaceCard();
        var content = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        content.SetPadding(Dp(16), Dp(14), Dp(12), Dp(14));

        var header = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        header.SetGravity(GravityFlags.CenterVertical);
        var byline = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        byline.AddView(Text(comment.Author.DisplayName, 14, Resource.Color.text_primary, true));
        byline.AddView(Text(FormatDateTime(comment.CreationTime), 12, Resource.Color.text_secondary));
        header.AddView(byline, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        if (comment.CanDelete)
        {
            var delete = SecondaryButton("Delete");
            header.AddView(delete, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, Dp(40)));
            delete.Click += (_, _) => ConfirmDeleteComment(comment);
        }
        content.AddView(header);
        var body = Text(comment.Content, 15, Resource.Color.text_primary);
        body.SetTextIsSelectable(true);
        var bodyLayout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        bodyLayout.SetMargins(0, Dp(12), 0, 0);
        content.AddView(body, bodyLayout);
        var imageUrls = comment.Images
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (imageUrls.Length > 0)
        {
            var imagesLayout = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
            imagesLayout.SetMargins(0, Dp(10), 0, 0);
            content.AddView(CommentImages(imageUrls), imagesLayout);
        }
        shell.AddView(content);
        return shell;
    }

    private View CommentImages(IReadOnlyList<string> imageUrls)
    {
        var scroll = new HorizontalScrollView(this)
        {
            HorizontalScrollBarEnabled = false
        };
        var row = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        foreach (var imageUrl in imageUrls)
        {
            var shell = new MaterialCardView(this)
            {
                Radius = Dp(10),
                CardElevation = 0,
                Clickable = true,
                Focusable = true,
                ContentDescription = "Open comment image"
            };
            shell.SetCardBackgroundColor(GetColor(Resource.Color.surface_variant));
            shell.StrokeColor = GetColor(Resource.Color.outline);
            shell.StrokeWidth = Dp(1);
            var image = new ImageView(this)
            {
                ContentDescription = "Comment image"
            };
            image.SetScaleType(ImageView.ScaleType.CenterCrop);
            shell.AddView(image, new ViewGroup.LayoutParams(Dp(116), Dp(82)));
            shell.Click += async (_, _) => await ShowImagePreviewAsync(imageUrl);
            var shellLayout = new LinearLayout.LayoutParams(Dp(116), Dp(82));
            shellLayout.SetMargins(0, 0, Dp(10), 0);
            row.AddView(shell, shellLayout);
            _ = LoadRemoteImageAsync(imageUrl, 320, image);
        }
        scroll.AddView(row);
        return scroll;
    }

    private async Task LoadRemoteImageAsync(string imageUrl, int width, ImageView target)
    {
        try
        {
            var bitmap = await GetRemoteImageBitmapAsync(imageUrl, width);
            if (bitmap == null)
            {
                return;
            }
            RunOnUiThread(() =>
            {
                if (!IsFinishing && !IsDestroyed && target.Parent != null)
                {
                    target.SetImageBitmap(bitmap);
                }
            });
        }
        catch (System.OperationCanceledException)
        {
            // Activity is closing.
        }
        catch
        {
            RunOnUiThread(() => target.ContentDescription = "Comment image unavailable");
        }
    }

    private Task<Bitmap?> GetRemoteImageBitmapAsync(string imageUrl, int width)
    {
        var key = $"{width}:{imageUrl}";
        if (!_remoteImageLoads.TryGetValue(key, out var load))
        {
            load = DownloadRemoteImageBitmapAsync(imageUrl, width);
            _remoteImageLoads[key] = load;
        }
        return load;
    }

    private async Task<Bitmap?> DownloadRemoteImageBitmapAsync(string imageUrl, int width)
    {
        var bytes = await Api.DownloadCardImageThumbnailAsync(
            imageUrl,
            width,
            _imageLoadCancellation.Token);
        _imageLoadCancellation.Token.ThrowIfCancellationRequested();
        return BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
    }

    private async Task ShowImagePreviewAsync(string imageUrl)
    {
        try
        {
            var bitmap = await GetRemoteImageBitmapAsync(imageUrl, 1600);
            if (bitmap == null || IsFinishing || IsDestroyed)
            {
                return;
            }
            var preview = new ImageView(this)
            {
                ContentDescription = "Comment image preview"
            };
            preview.SetAdjustViewBounds(true);
            preview.SetScaleType(ImageView.ScaleType.FitCenter);
            preview.SetPadding(Dp(12), Dp(8), Dp(12), Dp(8));
            preview.SetImageBitmap(bitmap);
            var builder = new MaterialAlertDialogBuilder(this);
            builder.SetTitle("Comment image");
            builder.SetView(preview);
            builder.SetNegativeButton("Close", (_, _) => { });
            builder.SetPositiveButton("Open original", (_, _) => OpenImageExternally(imageUrl));
            builder.Show();
        }
        catch (System.OperationCanceledException)
        {
            // Activity is closing.
        }
        catch (Exception exception)
        {
            ShowError(exception, retry: false);
        }
    }

    private void OpenImageExternally(string imageUrl)
    {
        try
        {
            var absoluteUri = Uri.TryCreate(imageUrl, UriKind.Absolute, out var parsed) &&
                              parsed.Scheme is "http" or "https"
                ? parsed
                : new Uri(new Uri($"{Session.Endpoint.TrimEnd('/')}/"), imageUrl);
            var intent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(absoluteUri.ToString()));
            StartActivity(intent);
        }
        catch (Exception exception)
        {
            ShowError(exception, retry: false);
        }
    }

    private async Task SaveAsync(MaterialButton button, UpdateCardRequest request)
    {
        try
        {
            button.Enabled = false;
            var response = await Api.UpdateCardAsync(_cardId, request);
            _card = response.Card;
            Render();
            Snackbar.Make(_root, "Card updated", Snackbar.LengthShort).Show();
        }
        catch (Exception exception)
        {
            button.Enabled = true;
            ShowError(exception, retry: false);
        }
    }

    private async Task AddCommentAsync(
        TextInputLayout box,
        TextInputEditText input,
        MaterialButton button)
    {
        var content = input.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            box.Error = "Write a reply first";
            return;
        }
        if (content.Length > 2000)
        {
            box.Error = "Replies cannot exceed 2,000 characters";
            return;
        }

        try
        {
            box.Error = null;
            button.Enabled = false;
            var imageUrls = new List<string>();
            if (_pendingImages.Count > 0)
            {
                var grant = await Api.GetCardImageUploadGrantAsync();
                var attachments = _pendingImages.ToArray();
                for (var index = 0; index < attachments.Length; index++)
                {
                    button.Text = $"Uploading image {index + 1} of {attachments.Length}…";
                    var attachment = attachments[index];
                    await using var imageStream = ContentResolver?.OpenInputStream(attachment.Uri)
                        ?? throw new IOException($"Could not open {attachment.DisplayName}.");
                    var uploaded = await Api.UploadCardImageAsync(
                        grant,
                        imageStream,
                        $"android-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{index}.{attachment.Extension}",
                        attachment.ContentType);
                    imageUrls.Add(uploaded.InternetPath);
                }
            }

            var images = string.Join(';', imageUrls);
            if (images.Length > 2000)
            {
                throw new InvalidOperationException(
                    "The uploaded image links are too long for one reply. Attach fewer images.");
            }
            button.Text = "Sending…";
            var response = await Api.AddCardCommentAsync(_cardId,
                new AddCardCommentRequest
                {
                    Content = content,
                    Images = images
                });
            if (_card != null)
            {
                _card.Comments.Add(response.Comment);
                _card.IsSubscribed = true;
                _commentDraft = string.Empty;
                ClearPendingImages();
                Render();
                _scroll.Post(() => _scroll.FullScroll((int)FocusSearchDirection.Down));
            }
            Snackbar.Make(_root, "Reply added", Snackbar.LengthShort).Show();
        }
        catch (Exception exception)
        {
            button.Enabled = true;
            button.Text = "Reply";
            ShowError(exception, retry: false);
        }
    }

    private void ClearPendingImages()
    {
        foreach (var attachment in _pendingImages)
        {
            attachment.Thumbnail?.Dispose();
            attachment.Thumbnail = null;
        }
        _pendingImages.Clear();
    }

    private async Task ToggleSubscriptionAsync(MaterialButton button)
    {
        if (_card == null)
        {
            return;
        }
        var subscribe = !_card.IsSubscribed;
        try
        {
            button.Enabled = false;
            var response = await Api.SetCardSubscriptionAsync(_cardId, subscribe);
            _card.IsSubscribed = response.IsSubscribed;
            Render();
            Snackbar.Make(_root,
                response.IsSubscribed ? "Subscribed to updates" : "Unsubscribed from updates",
                Snackbar.LengthShort).Show();
        }
        catch (Exception exception)
        {
            button.Enabled = true;
            ShowError(exception, retry: false);
        }
    }

    private void ConfirmDeleteComment(CardCommentDto comment)
    {
        var builder = new MaterialAlertDialogBuilder(this);
        builder.SetTitle("Delete reply?");
        builder.SetMessage("This reply will be permanently removed.");
        builder.SetNegativeButton("Cancel", (_, _) => { });
        builder.SetPositiveButton("Delete", (_, _) => _ = DeleteCommentAsync(comment));
        builder.Show();
    }

    private async Task DeleteCommentAsync(CardCommentDto comment)
    {
        try
        {
            await Api.DeleteCardCommentAsync(_cardId, comment.Id);
            _card?.Comments.RemoveAll(item => item.Id == comment.Id);
            Render();
            Snackbar.Make(_root, "Reply deleted", Snackbar.LengthShort).Show();
        }
        catch (Exception exception)
        {
            ShowError(exception, retry: false);
        }
    }

    private void ConfirmDeleteCard()
    {
        var builder = new MaterialAlertDialogBuilder(this);
        builder.SetTitle("Delete card?");
        builder.SetMessage("The card and all of its replies will be permanently removed.");
        builder.SetNegativeButton("Cancel", (_, _) => { });
        builder.SetPositiveButton("Delete", (_, _) => _ = DeleteCardAsync());
        builder.Show();
    }

    private async Task DeleteCardAsync()
    {
        try
        {
            SetBusy(true);
            await Api.DeleteCardAsync(_cardId);
            SetResult(Result.Ok);
            Finish();
        }
        catch (Exception exception)
        {
            SetBusy(false);
            ShowError(exception, retry: false);
        }
    }

    private (LinearLayout Container, Spinner Spinner) LabeledSpinner(
        string label,
        IReadOnlyList<string> items,
        string selected,
        bool enabled)
    {
        var container = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        container.AddView(SectionTitle(label));
        var spinner = new Spinner(this, SpinnerMode.Dialog)
        {
            Enabled = enabled
        };
        var adapter = new ArrayAdapter<string>(this,
            global::Android.Resource.Layout.SimpleSpinnerItem,
            items.ToArray());
        adapter.SetDropDownViewResource(global::Android.Resource.Layout.SimpleSpinnerDropDownItem);
        spinner.Adapter = adapter;
        var selectedIndex = Math.Max(0,
            items.Select((value, index) => (value, index))
                .FirstOrDefault(item => string.Equals(item.value, selected, StringComparison.OrdinalIgnoreCase))
                .index);
        spinner.SetSelection(selectedIndex);
        container.AddView(spinner, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, Dp(52)));
        return (container, spinner);
    }

    private (TextInputLayout Box, TextInputEditText Input) Input(
        string hint,
        string value,
        bool multiline,
        int minLines = 1)
    {
        var box = new TextInputLayout(this)
        {
            Hint = hint,
            BoxBackgroundMode = TextInputLayout.BoxBackgroundOutline
        };
        box.SetBoxCornerRadii(Dp(14), Dp(14), Dp(14), Dp(14));
        var input = new TextInputEditText(this)
        {
            Text = value,
            Gravity = multiline ? GravityFlags.Top | GravityFlags.Start : GravityFlags.CenterVertical
        };
        input.SetMinLines(minLines);
        input.SetSingleLine(!multiline);
        input.InputType = multiline
            ? InputTypes.ClassText | InputTypes.TextFlagCapSentences | InputTypes.TextFlagMultiLine
            : InputTypes.ClassText | InputTypes.TextFlagCapSentences;
        box.AddView(input);
        return (box, input);
    }

    private View Metadata(string label, string value)
    {
        var row = new LinearLayout(this)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        row.SetPadding(0, Dp(8), 0, 0);
        row.AddView(Text(label, 13, Resource.Color.text_secondary),
            new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        var valueView = Text(value, 13, Resource.Color.text_primary, true);
        valueView.Gravity = GravityFlags.End;
        row.AddView(valueView, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));
        return row;
    }

    private TextView Badge(string value, int background, int foreground)
    {
        var badge = Text(value, 11, foreground, true);
        badge.Gravity = GravityFlags.Center;
        badge.SetPadding(Dp(12), Dp(7), Dp(12), Dp(7));
        badge.Background = Rounded(ColorOf(background), 18);
        return badge;
    }

    private TextView SectionTitle(string value)
    {
        var title = Text(value, 12, Resource.Color.text_secondary, true);
        title.LetterSpacing = 0.08f;
        return title;
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

    private MaterialButton PrimaryButton(string text)
    {
        var button = new MaterialButton(this)
        {
            Text = text,
            TextSize = 15,
            CornerRadius = Dp(14)
        };
        button.SetAllCaps(false);
        return button;
    }

    private MaterialButton SecondaryButton(string text)
    {
        var button = new MaterialButton(this, null, global::Android.Resource.Attribute.BorderlessButtonStyle)
        {
            Text = text,
            TextSize = 13,
            CornerRadius = Dp(14)
        };
        button.SetAllCaps(false);
        button.SetTextColor(ColorStateList.ValueOf(ColorOf(Resource.Color.brand_primary)));
        return button;
    }

    private MaterialButton DangerButton(string text)
    {
        var button = PrimaryButton(text);
        button.BackgroundTintList = ColorStateList.ValueOf(ColorOf(Resource.Color.danger_container));
        button.SetTextColor(ColorStateList.ValueOf(ColorOf(Resource.Color.on_danger_container)));
        return button;
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

    private void Add(View view, int top, int bottom, int? height = null)
    {
        var layout = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            height ?? ViewGroup.LayoutParams.WrapContent);
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

    private void ShowError(Exception exception, bool retry)
    {
        if (exception is KanbanAuthenticationRequiredException)
        {
            ReturnToLogin();
            return;
        }
        var bar = Snackbar.Make(_root, FriendlyMessage(exception), Snackbar.LengthLong);
        if (retry)
        {
            bar.SetAction("Retry", ignoredView => _ = LoadAsync());
        }
        bar.Show();
    }

    private void ReturnToLogin()
    {
        var intent = new Intent(this, typeof(LoginActivity));
        intent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.NewTask | ActivityFlags.ClearTask);
        StartActivity(intent);
        Finish();
    }

    private static string FormatDate(DateTime? value) =>
        value.HasValue ? value.Value.ToString("yyyy-MM-dd") : "Not set";

    private static string FormatDateTime(DateTime value) =>
        value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    private static string FriendlyMessage(Exception exception)
    {
        var message = exception.Message;
        return message.Length > 160 ? message[..160] : message;
    }

    private Color ColorOf(int colorResource) => new(GetColor(colorResource));

    private static Color ParseColor(string value, Color fallback)
    {
        try
        {
            return Color.ParseColor(value);
        }
        catch
        {
            return fallback;
        }
    }

    private GradientDrawable Rounded(Color color, int radius)
    {
        var drawable = new GradientDrawable();
        drawable.SetColor(color);
        drawable.SetCornerRadius(Dp(radius));
        return drawable;
    }

    private int Dp(int value) => (int)Math.Round(value * Resources!.DisplayMetrics!.Density);

    private sealed class ScheduleEditorState
    {
        public View Container { get; set; } = null!;
        public DateTime? PlannedStartTime { get; set; }
        public DateTime? DueDate { get; set; }
        public CheckBox Recurring { get; init; } = null!;
        public EditText IntervalInput { get; init; } = null!;
        public Spinner UnitSpinner { get; init; } = null!;
    }

    private sealed record TransferDestination(
        CardTransferBoardDto Board,
        CardColumnOptionDto Column);

    private sealed class PendingImageAttachment(
        global::Android.Net.Uri uri,
        string displayName,
        string contentType,
        string extension)
    {
        public global::Android.Net.Uri Uri { get; } = uri;
        public string DisplayName { get; } = displayName;
        public string ContentType { get; } = contentType;
        public string Extension { get; } = extension;
        public Bitmap? Thumbnail { get; set; }
        public Task<Bitmap?>? ThumbnailLoad { get; set; }
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
