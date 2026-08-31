# Android app

The Android client is a native **.NET for Android** application. It uses the shared
`Aiursoft.Kanban.SDK`, and every Kanban request is sent through `AiurProtocolClient`.
It supports listing boards, opening a board, creating boards and cards, and moving
cards between columns with Android drag and drop.

## Use the mobile workspace

Authentication is a dedicated screen. After sign-in, credentials and connection
controls are removed from the workspace and the app opens the last selected board.

- Open the left navigation drawer to switch between **My boards** and
  **Shared with me**. Each board shows whether the current account can edit it or
  can only view it.
- Tap **New card** for the quick-create bottom sheet, or use **Add card** in a
  specific column to preselect that destination.
- On an editable board, hold a card or its drag handle, then drop it on another
  column. The target column highlights while it can accept the card.
- Read-only shares hide all creation and drag controls. The API independently
  enforces the same effective board permission for every write.
- Server information and sign-out live in the drawer rather than occupying the
  board screen.

## Use a desktop development server over USB

The app also supports the server's built-in Local authentication mode. For a direct,
private development connection, keep the server bound to loopback and tunnel its port
through the connected Android device:

```bash
adb reverse tcp:5080 tcp:5080
dotnet run --project src/Aiursoft.Kanban -- --urls http://127.0.0.1:5080
```

For the USB workflow, enter `http://127.0.0.1:5080`. Sign in with an existing local
account, or create one when `AppSettings:Local:AllowRegister` is enabled. This cleartext
endpoint is allowed only for the USB tunnel; use HTTPS for LAN or internet access.
Local mobile access tokens expire after 12 hours and are protected by the server's
ASP.NET Core Data Protection key ring.

The workstation development build can alternatively expose HTTPS on port `5443` and
pin its local certificate in the APK. That lets the installed app use
`https://192.168.50.146:5443` on the same Wi-Fi without sending credentials or tokens
in cleartext. Regenerate and re-pin the certificate if the workstation address changes.

## OIDC deployment requirements

The web client and Android client are different OIDC client types:

- Keep the existing confidential web client and its `/signin-oidc` redirect URI.
- Register a public/native client with client ID `kanban-android` (or your chosen value).
- Enable Authorization Code flow, require PKCE with `S256`, and do not issue a client secret.
- Register `com.aiursoft.kanban:/oauth2redirect` as an exact redirect URI.
- Expose an API resource/scope such as `kanban-api`; its access-token audience must be
  `kanban-api`.
- Allow the native client to request `offline_access` so it can refresh expired access
  tokens without embedding or storing a password.
- Include `sub`, `preferred_username`, `name`, and `email` in the access token for a
  first-time mobile login. Users previously linked by web login only require `sub`.

Configure the server (environment-variable form shown):

```text
AppSettings__AuthProvider=OIDC
AppSettings__OIDC__Authority=https://identity.example.com/application/o/kanban
AppSettings__OIDC__RequireHttpsMetadata=true
AppSettings__OIDC__ClientId=kanban-web
AppSettings__OIDC__ClientSecret=replace-me
AppSettings__OIDC__MobileClientId=kanban-android
AppSettings__OIDC__ApiAudience=kanban-api
AppSettings__OIDC__ApiScope=kanban-api
AppSettings__OIDC__MobileRedirectUri=com.aiursoft.kanban:/oauth2redirect
```

The app first reads `/api/v1/config`, discovers the OIDC authorization and token
endpoints, then opens the system browser for Authorization Code + PKCE login. The API
validates issuer, signature, lifetime, and audience on every Bearer request.

## Build a sideloadable APK

Install the Android workload once. If the Android SDK/JDK are not already present,
let the .NET workload install its exact required versions into writable directories:

```bash
dotnet workload install android
mkdir -p .android-sdk .android-jdk
dotnet build src/Aiursoft.Kanban.Android/Aiursoft.Kanban.Android.csproj \
  -t:InstallAndroidDependencies -f net10.0-android \
  -p:AndroidSdkDirectory="$PWD/.android-sdk" \
  -p:JavaSdkDirectory="$PWD/.android-jdk" \
  -p:AcceptAndroidSDKLicenses=True
```

Then publish the self-contained, sideloadable APK:

```bash
dotnet publish src/Aiursoft.Kanban.Android/Aiursoft.Kanban.Android.csproj \
  -c Release -f net10.0-android -p:AndroidPackageFormat=apk \
  -p:AndroidSdkDirectory="$PWD/.android-sdk" \
  -p:JavaSdkDirectory="$PWD/.android-jdk"
```

The APK is emitted below `bin/Release/net10.0-android/publish/` and can be installed
with `adb install -r <apk-path>`. Google Play is not required.
