using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace PolarUninstallerWpf.Services;

public enum AuthenticodeStatus { Unsigned, Valid, Invalid }

public sealed record ExecutableVerification(bool IsValid, string? Error, AuthenticodeStatus Signature, string? Signer)
{
    public static ExecutableVerification Ok(AuthenticodeStatus signature, string? signer) => new(true, null, signature, signer);
    public static ExecutableVerification Fail(string error) => new(false, error, AuthenticodeStatus.Invalid, null);
}

public static class UpdateVerificationService
{
    /// <summary>
    /// Verifies that a staged file is a 64-bit portable executable, that its
    /// embedded version matches the release, and that its SHA-256 matches the
    /// published value. Authenticode is validated when the file is signed;
    /// unsigned builds are reported as unsigned so the caller can apply the
    /// independent GitHub release check.
    /// </summary>
    public static ExecutableVerification VerifyStandaloneExecutable(string path, string expectedVersion, string? expectedSha256 = null, long expectedSize = 0)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return ExecutableVerification.Fail("missing_file");
            if (expectedSize > 0 && info.Length != expectedSize) return ExecutableVerification.Fail("size_mismatch");
            if (!IsPortableExecutableX64(path)) return ExecutableVerification.Fail("invalid_pe");
            var fileVersion = FileVersionInfo.GetVersionInfo(path).FileVersion;
            if (!VersionMatches(fileVersion, expectedVersion)) return ExecutableVerification.Fail("version_mismatch");
            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                string actual = ComputeSha256(path);
                if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase)) return ExecutableVerification.Fail("checksum_mismatch");
            }
            var signature = VerifyAuthenticode(path);
            if (signature.Status == AuthenticodeStatus.Invalid) return ExecutableVerification.Fail("invalid_signature");
            return ExecutableVerification.Ok(signature.Status, signature.Signer);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException or NotSupportedException)
        {
            return ExecutableVerification.Fail("verify_error:" + ex.Message);
        }
    }

    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var hash = SHA256.Create();
        return Convert.ToHexString(hash.ComputeHash(stream)).ToLowerInvariant();
    }

    public static bool HashEquals(string path, string expectedHex)
    {
        if (string.IsNullOrWhiteSpace(expectedHex) || expectedHex.Length != 64 || !expectedHex.All(Uri.IsHexDigit)) return false;
        return string.Equals(ComputeSha256(path), expectedHex, StringComparison.OrdinalIgnoreCase);
    }

    public static bool VersionMatches(string? fileVersion, string expectedVersion)
    {
        if (!Version.TryParse(fileVersion, out var actual)) return false;
        if (!Version.TryParse(expectedVersion, out var expected)) return false;
        return actual.Major == expected.Major && actual.Minor == expected.Minor && actual.Build == expected.Build;
    }

    public static bool IsPortableExecutableX64(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 0x40) return false;
        if (reader.ReadUInt16() != 0x5A4D) return false;
        stream.Position = 0x3C;
        int peOffset = reader.ReadInt32();
        if (peOffset < 0x40 || peOffset + 6 > stream.Length) return false;
        stream.Position = peOffset;
        if (reader.ReadUInt32() != 0x00004550) return false;
        ushort machine = reader.ReadUInt16();
        return machine == 0x8664;
    }

    private static (AuthenticodeStatus Status, string? Signer) VerifyAuthenticode(string path)
    {
        try
        {
            uint result = WinVerifyTrustFile(path);
            if (result == 0) return (AuthenticodeStatus.Valid, null);
            // TRUST_E_NOSIGNATURE, TRUST_E_SUBJECT_FORM_UNKNOWN, TRUST_E_PROVIDER_UNKNOWN
            if (result is 0x800B0100 or 0x800B0003 or 0x800B0004) return (AuthenticodeStatus.Unsigned, null);
            WpfUpdateService.Log($"Authenticode verification failed: 0x{result:X8}");
            return (AuthenticodeStatus.Invalid, null);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            // If the platform cannot perform Authenticode validation, report the
            // file as unsigned rather than silently accepting a signature.
            return (AuthenticodeStatus.Unsigned, null);
        }
    }

    private static uint WinVerifyTrustFile(string path)
    {
        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = path,
            hFile = IntPtr.Zero,
            pgKnownSubject = IntPtr.Zero
        };
        IntPtr fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        IntPtr trustDataPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_DATA>());
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);
            var trustData = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                pPolicyCallbackData = IntPtr.Zero,
                pSIPClientData = IntPtr.Zero,
                dwUIChoice = 2,          // WTD_UI_NONE
                fdwRevocationChecks = 0, // WTD_REVOKE_NONE
                dwUnionChoice = 1,       // WTD_CHOICE_FILE
                pFile = fileInfoPtr,
                dwStateAction = 0,       // WTD_STATEACTION_IGNORE
                hWVTStateData = IntPtr.Zero,
                pwszURLReference = IntPtr.Zero,
                dwProvFlags = 0x00000010, // WTD_REVOCATION_CHECK_NONE
                dwUIContext = 0,
                pSignatureSettings = IntPtr.Zero
            };
            Marshal.StructureToPtr(trustData, trustDataPtr, false);
            return WinVerifyTrust(IntPtr.Zero, new[] { WinTrustActionGenericVerifyV2 }, trustDataPtr);
        }
        finally
        {
            Marshal.FreeHGlobal(fileInfoPtr);
            Marshal.FreeHGlobal(trustDataPtr);
        }
    }

    private static readonly Guid WinTrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPArray)] Guid[] actionId, IntPtr data);
}
