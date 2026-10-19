namespace NolumiaScheduler.Infrastructure.Auth;

/// <summary>PC に置く秘密（更新トークン）を暗号化する。Windows では DPAPI（CurrentUser）。</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] plain);

    byte[] Unprotect(byte[] protectedData);
}
