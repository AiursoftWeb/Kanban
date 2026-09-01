using System.Security.Cryptography;
using System.Text.Json;
using Android.Content;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using CryptoCipherMode = Javax.Crypto.CipherMode;

namespace Aiursoft.Kanban.Android.Oidc;

public sealed class AndroidKeystoreTokenStore
{
    private const string AndroidKeyStore = "AndroidKeyStore";
    private const string KeyAlias = "aiursoft.kanban.oidc.tokens.v1";
    private const string CipherTransformation = "AES/GCM/NoPadding";
    private const string PreferencesName = "kanban.secure.tokens";
    private const string VersionKey = "version";
    private const string InitializationVectorKey = "iv";
    private const string CiphertextKey = "ciphertext";
    private const int CurrentVersion = 1;
    private const int AuthenticationTagBits = 128;

    private readonly ISharedPreferences _preferences;

    public AndroidKeystoreTokenStore(Context context)
    {
        _preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
    }

    public OidcTokenSet? Load()
    {
        var initializationVector = _preferences.GetString(InitializationVectorKey, null);
        var ciphertext = _preferences.GetString(CiphertextKey, null);
        if (_preferences.GetInt(VersionKey, 0) != CurrentVersion ||
            string.IsNullOrWhiteSpace(initializationVector) ||
            string.IsNullOrWhiteSpace(ciphertext))
        {
            return null;
        }

        try
        {
            using var keyStore = OpenKeyStore();
            var key = keyStore.GetKey(KeyAlias, null);
            if (key == null)
            {
                Clear();
                return null;
            }

            using var cipher = Cipher.GetInstance(CipherTransformation)!;
            var iv = Convert.FromBase64String(initializationVector);
            using var parameters = new GCMParameterSpec(AuthenticationTagBits, iv);
            cipher.Init(CryptoCipherMode.DecryptMode, key, parameters);
            var plaintext = cipher.DoFinal(Convert.FromBase64String(ciphertext))
                ?? throw new InvalidOperationException("Android Keystore returned no plaintext.");
            OidcTokenSet? tokens;
            try
            {
                tokens = JsonSerializer.Deserialize<OidcTokenSet>(plaintext);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
            if (tokens == null ||
                string.IsNullOrWhiteSpace(tokens.AccessToken) ||
                string.IsNullOrWhiteSpace(tokens.RefreshToken) ||
                string.IsNullOrWhiteSpace(tokens.TokenEndpoint) ||
                string.IsNullOrWhiteSpace(tokens.ClientId))
            {
                Clear();
                return null;
            }
            return tokens;
        }
        catch (Exception)
        {
            Clear();
            return null;
        }
    }

    public void Save(OidcTokenSet tokens)
    {
        if (string.IsNullOrWhiteSpace(tokens.RefreshToken))
        {
            Clear();
            return;
        }

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(tokens);
        byte[] initializationVector;
        byte[] ciphertext;
        try
        {
            try
            {
                (initializationVector, ciphertext) = Encrypt(plaintext);
            }
            catch (Exception)
            {
                DeleteKey();
                (initializationVector, ciphertext) = Encrypt(plaintext);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        var committed = _preferences.Edit()!
            .PutInt(VersionKey, CurrentVersion)!
            .PutString(InitializationVectorKey, Convert.ToBase64String(initializationVector))!
            .PutString(CiphertextKey, Convert.ToBase64String(ciphertext))!
            .Commit();
        if (!committed)
        {
            throw new InvalidOperationException("Could not persist the encrypted OIDC session.");
        }
    }

    public void Clear()
    {
        _preferences.Edit()!.Clear()!.Commit();
    }

    private static (byte[] InitializationVector, byte[] Ciphertext) Encrypt(byte[] plaintext)
    {
        using var keyStore = OpenKeyStore();
        var key = GetOrCreateKey(keyStore);
        using var cipher = Cipher.GetInstance(CipherTransformation)!;
        cipher.Init(CryptoCipherMode.EncryptMode, key);
        var ciphertext = cipher.DoFinal(plaintext)
            ?? throw new InvalidOperationException("Android Keystore returned no ciphertext.");
        var initializationVector = cipher.GetIV()
            ?? throw new InvalidOperationException("Android Keystore did not provide an initialization vector.");
        return (initializationVector, ciphertext);
    }

    private static IKey GetOrCreateKey(KeyStore keyStore)
    {
        var existing = keyStore.GetKey(KeyAlias, null);
        if (existing != null)
        {
            return existing;
        }

        using var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, AndroidKeyStore)!;
        using var specification = new KeyGenParameterSpec.Builder(
                KeyAlias,
                KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
            .SetKeySize(256)
            .Build();
        generator.Init(specification);
        return generator.GenerateKey()
            ?? throw new InvalidOperationException("Android Keystore could not generate an encryption key.");
    }

    private static KeyStore OpenKeyStore()
    {
        var keyStore = KeyStore.GetInstance(AndroidKeyStore)
            ?? throw new InvalidOperationException("Android Keystore is unavailable.");
        keyStore.Load(null);
        return keyStore;
    }

    private static void DeleteKey()
    {
        using var keyStore = OpenKeyStore();
        if (keyStore.ContainsAlias(KeyAlias))
        {
            keyStore.DeleteEntry(KeyAlias);
        }
    }
}
