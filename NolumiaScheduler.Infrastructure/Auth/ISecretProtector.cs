namespace NolumiaScheduler.Infrastructure.Auth;

/// <summary>Encrypts the secret kept on the PC (the refresh token). On Windows this is DPAPI (CurrentUser).</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] plain);

    byte[] Unprotect(byte[] protectedData);
}
