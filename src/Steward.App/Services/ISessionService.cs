namespace Steward.App.Services;

public interface ISessionService
{
    bool TryRestoreSession();

    Task SignInAsync(CancellationToken cancellationToken);

    void ClearSession();
}
