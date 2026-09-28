using Foundrmind.Data;
using Foundrmind.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Foundrmind.Components.Shared;

/// <summary>Base for pages under /app/p/{Id}: loads the project and enforces that the current user owns it.</summary>
public abstract class ProjectBase : ComponentBase
{
    [Parameter] public int Id { get; set; }
    [CascadingParameter] public Task<AuthenticationState>? AuthState { get; set; }
    [Inject] public IDbContextFactory<AppDb> DbFactory { get; set; } = default!;
    [Inject] public NavigationManager Nav { get; set; } = default!;

    protected Project? Project;
    protected int UserId;
    protected bool NotFound;
    /// <summary>True for the business owner; team members can use everything except deleting it, billing and social connections.</summary>
    protected bool IsOwner => Project != null && Project.UserId == UserId;

    protected override async Task OnParametersSetAsync()
    {
        var user = AuthState == null ? null : (await AuthState).User;
        var uid = user.UserId();
        if (uid == null) { NotFound = true; return; }
        UserId = uid.Value;
        await using var db = await DbFactory.CreateDbContextAsync();
        Project = await Access.Projects(db, UserId).FirstOrDefaultAsync(p => p.Id == Id);
        NotFound = Project == null;
        if (!NotFound) await OnProjectLoadedAsync();
    }

    protected virtual Task OnProjectLoadedAsync() => Task.CompletedTask;
}
