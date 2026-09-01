using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using AndroidX.AppCompat.App;
using Google.Android.Material.Button;
using Google.Android.Material.ProgressIndicator;
using Google.Android.Material.TextField;
using Uri = System.Uri;

namespace Aiursoft.Kanban.Android;

[Activity(
    Label = "Kanban",
    MainLauncher = true,
    Exported = true,
    Theme = "@style/AppTheme")]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "com.aiursoft.kanban", DataPath = "/oauth2redirect")]
public sealed class LoginActivity : AppCompatActivity
{
    private TextInputEditText _server = null!;
    private TextInputLayout _serverContainer = null!;
    private MaterialButton _connect = null!;
    private CircularProgressIndicator _progress = null!;
    private LinearLayout _authPanel = null!;
    private TextView _authMode = null!;
    private LinearLayout _localPanel = null!;
    private TextInputEditText _identity = null!;
    private TextInputLayout _passwordContainer = null!;
    private TextInputEditText _password = null!;
    private MaterialButton _localSignIn = null!;
    private MaterialButton _register = null!;
    private MaterialButton _oidcSignIn = null!;
    private TextView _status = null!;
    private bool _registerMode;

    private AppSession Session => ((KanbanApplication)Application!).Session;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_login);
        BindViews();
        WireEvents();

        var root = FindViewById<View>(Resource.Id.login_root)!;
        root.SetOnApplyWindowInsetsListener(new SystemBarInsetListener(0, 0, 0, 0, true, true));
        _server.Text = Session.Endpoint;

        var callback = Intent?.DataString;
        if (!string.IsNullOrWhiteSpace(callback))
        {
            _ = CompleteOidcAsync(new Uri(callback));
        }
        else if (Session.IsAuthenticated)
        {
            OpenWorkspace();
        }
        else
        {
            _ = ConnectAsync();
        }
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        var callback = intent?.DataString;
        if (!string.IsNullOrWhiteSpace(callback))
        {
            _ = CompleteOidcAsync(new Uri(callback));
        }
    }

    private void BindViews()
    {
        _server = FindViewById<TextInputEditText>(Resource.Id.server_input)!;
        _serverContainer = FindViewById<TextInputLayout>(Resource.Id.server_container)!;
        _connect = FindViewById<MaterialButton>(Resource.Id.connect_button)!;
        _progress = FindViewById<CircularProgressIndicator>(Resource.Id.login_progress)!;
        _authPanel = FindViewById<LinearLayout>(Resource.Id.auth_panel)!;
        _authMode = FindViewById<TextView>(Resource.Id.auth_mode_label)!;
        _localPanel = FindViewById<LinearLayout>(Resource.Id.local_auth_panel)!;
        _identity = FindViewById<TextInputEditText>(Resource.Id.identity_input)!;
        _passwordContainer = FindViewById<TextInputLayout>(Resource.Id.password_container)!;
        _password = FindViewById<TextInputEditText>(Resource.Id.password_input)!;
        _localSignIn = FindViewById<MaterialButton>(Resource.Id.local_sign_in_button)!;
        _register = FindViewById<MaterialButton>(Resource.Id.register_button)!;
        _oidcSignIn = FindViewById<MaterialButton>(Resource.Id.oidc_sign_in_button)!;
        _status = FindViewById<TextView>(Resource.Id.login_status)!;
    }

    private void WireEvents()
    {
        _connect.Click += async (_, _) => await ConnectAsync();
        _localSignIn.Click += async (_, _) => await AuthenticateLocalAsync();
        _register.Click += (_, _) => ToggleRegistrationMode();
        _oidcSignIn.Click += (_, _) => BeginOidc();
        _password.EditorAction += async (_, args) =>
        {
            if (args.ActionId == ImeAction.Done)
            {
                await AuthenticateLocalAsync();
            }
        };
    }

    private async Task ConnectAsync()
    {
        try
        {
            SetBusy(true, "Connecting securely…");
            _serverContainer.Error = null;
            var configuration = await Session.ConnectAsync(_server.Text ?? string.Empty);
            if (Session.IsAuthenticated)
            {
                OpenWorkspace();
                return;
            }
            _authMode.Text = configuration.AuthenticationMode.Equals("OIDC", StringComparison.OrdinalIgnoreCase)
                ? "ORGANIZATION SIGN-IN"
                : "LOCAL ACCOUNT";
            _authPanel.Visibility = ViewStates.Visible;
            _localPanel.Visibility = configuration.AuthenticationMode.Equals("Local", StringComparison.OrdinalIgnoreCase)
                ? ViewStates.Visible
                : ViewStates.Gone;
            _oidcSignIn.Visibility = configuration.AuthenticationMode.Equals("OIDC", StringComparison.OrdinalIgnoreCase)
                ? ViewStates.Visible
                : ViewStates.Gone;
            _register.Visibility = configuration.AllowRegistration ? ViewStates.Visible : ViewStates.Gone;
            SetBusy(false, "Connected. Sign in to continue.");
            _identity.RequestFocus();
        }
        catch (Exception exception)
        {
            _authPanel.Visibility = ViewStates.Gone;
            _serverContainer.Error = FriendlyMessage(exception);
            SetBusy(false, "Check the server address and network connection.");
        }
    }

    private async Task AuthenticateLocalAsync()
    {
        var identity = _identity.Text?.Trim() ?? string.Empty;
        var password = _password.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(identity))
        {
            _identity.Error = _registerMode ? "Enter your email" : "Enter your email or username";
            return;
        }
        if (password.Length < 6)
        {
            _passwordContainer.Error = "Password must contain at least 6 characters";
            return;
        }

        try
        {
            HideKeyboard();
            _identity.Error = null;
            _passwordContainer.Error = null;
            SetBusy(true, _registerMode ? "Creating your account…" : "Signing you in…");
            if (_registerMode)
            {
                await Session.RegisterLocalAsync(identity, password);
            }
            else
            {
                await Session.LoginLocalAsync(identity, password);
            }
            OpenWorkspace();
        }
        catch (Exception exception)
        {
            _passwordContainer.Error = FriendlyMessage(exception);
            SetBusy(false, _registerMode ? "Could not create the account." : "Sign-in failed.");
        }
    }

    private void ToggleRegistrationMode()
    {
        _registerMode = !_registerMode;
        _identity.Hint = _registerMode ? "Email" : "Email or username";
        _localSignIn.Text = _registerMode ? "Create account" : "Sign in";
        _register.Text = _registerMode ? "Already have an account? Sign in" : "Create an account";
        _passwordContainer.Error = null;
    }

    private void BeginOidc()
    {
        try
        {
            var uri = Session.CreateAuthorizationUri();
            StartActivity(new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(uri.ToString())));
        }
        catch (Exception exception)
        {
            SetBusy(false, FriendlyMessage(exception));
        }
    }

    private async Task CompleteOidcAsync(Uri callback)
    {
        try
        {
            SetBusy(true, "Completing secure sign-in…");
            await Session.CompleteAuthorizationAsync(callback);
            OpenWorkspace();
        }
        catch (Exception exception)
        {
            SetBusy(false, FriendlyMessage(exception));
        }
    }

    private void OpenWorkspace()
    {
        StartActivity(new Intent(this, typeof(MainActivity)));
        Finish();
    }

    private void SetBusy(bool busy, string message)
    {
        _connect.Enabled = !busy;
        _localSignIn.Enabled = !busy;
        _register.Enabled = !busy;
        _oidcSignIn.Enabled = !busy;
        _progress.Visibility = busy ? ViewStates.Visible : ViewStates.Gone;
        _status.Text = message;
        _status.Visibility = ViewStates.Visible;
    }

    private void HideKeyboard()
    {
        var manager = (InputMethodManager?)GetSystemService(InputMethodService);
        manager?.HideSoftInputFromWindow(_password.WindowToken, HideSoftInputFlags.None);
    }

    private static string FriendlyMessage(Exception exception)
    {
        var message = exception.Message;
        if (message.Contains("net_http", StringComparison.OrdinalIgnoreCase))
        {
            return "The server could not be reached securely.";
        }
        return message.Length > 160 ? message[..160] : message;
    }
}
