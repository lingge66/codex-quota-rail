using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CodexQuotaRail.Windows.Interop;
using Microsoft.Win32.SafeHandles;

namespace CodexQuotaRail.Windows.Windows;

internal static class WindowProcessIdentityReader
{
    private static readonly Guid VerifyAction =
        new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public static WindowProcessIdentity? Read(uint processId)
    {
        using var process = NativeMethods.OpenProcess(
            NativeMethods.ProcessQueryLimitedInformation,
            inheritHandle: false,
            processId);
        if (process.IsInvalid)
        {
            return null;
        }

        var executablePath = ReadExecutablePath(process);
        if (executablePath is null)
        {
            return null;
        }

        var packageFullName = ReadPackageFullName(process);
        var signerSubject = CodexExecutableIdentityPolicy.RequiresSignerLookup(
                                executablePath,
                                packageFullName) &&
                            HasTrustedSignature(executablePath)
            ? ReadSignerSubject(executablePath)
            : null;
        return new WindowProcessIdentity(
            executablePath,
            packageFullName,
            signerSubject);
    }

    private static string? ReadExecutablePath(SafeProcessHandle process)
    {
        var buffer = new char[32 * 1024];
        var size = (uint)buffer.Length;
        return NativeMethods.QueryFullProcessImageName(process, 0, buffer, ref size)
            ? new string(buffer, 0, checked((int)size))
            : null;
    }

    private static string? ReadPackageFullName(SafeProcessHandle process)
    {
        uint length = 0;
        var result = NativeMethods.GetPackageFullName(process, ref length, null);
        if (result == NativeMethods.AppModelErrorNoPackage)
        {
            return null;
        }

        if (result != NativeMethods.ErrorInsufficientBuffer || length == 0 || length > 32 * 1024)
        {
            return null;
        }

        var buffer = new char[length];
        result = NativeMethods.GetPackageFullName(process, ref length, buffer);
        return result == 0
            ? new string(buffer, 0, Math.Max(0, checked((int)length) - 1))
            : null;
    }

    private static bool HasTrustedSignature(string path)
    {
        var fileInfo = new NativeMethods.WinTrustFileInfo
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.WinTrustFileInfo>(),
            FilePath = path,
        };
        var fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf(fileInfo));
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, fDeleteOld: false);
            var trustData = new NativeMethods.WinTrustData
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.WinTrustData>(),
                UiChoice = 2,
                RevocationChecks = 0,
                UnionChoice = 1,
                FileInfo = fileInfoPointer,
                ProviderFlags = 0x00001000,
            };
            var action = VerifyAction;
            return NativeMethods.WinVerifyTrust(new nint(-1), ref action, ref trustData) == 0;
        }
        finally
        {
            Marshal.DestroyStructure<NativeMethods.WinTrustFileInfo>(fileInfoPointer);
            Marshal.FreeHGlobal(fileInfoPointer);
        }
    }

    private static string? ReadSignerSubject(string path)
    {
        try
        {
            using var certificate = X509Certificate.CreateFromSignedFile(path);
            return certificate.Subject;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
