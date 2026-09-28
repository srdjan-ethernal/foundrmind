using Foundrmind.Data;
using Microsoft.EntityFrameworkCore;

namespace Foundrmind.Services;

/// <summary>Who can open which business: its owner, plus members of the owner's team while the owner is on Scale.</summary>
public static class Access
{
    public static IQueryable<Project> Projects(AppDb db, int userId) =>
        db.Projects.Where(p => p.UserId == userId
            || db.TeamMembers.Any(t => t.OwnerUserId == p.UserId && t.MemberUserId == userId
                && db.Users.Any(o => o.Id == p.UserId && o.Plan == Plans.Scale)));

    /// <summary>Links pending invites for this email to the user (called on signup and login).</summary>
    public static async Task AcceptInvitesAsync(AppDb db, User user)
    {
        var pending = await db.TeamMembers.Where(t => t.Email == user.Email && t.MemberUserId == null && t.OwnerUserId != user.Id).ToListAsync();
        foreach (var t in pending) { t.MemberUserId = user.Id; t.AcceptedAt = DateTime.UtcNow; }
        if (pending.Count > 0) await db.SaveChangesAsync();
    }
}
