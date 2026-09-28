using Foundrmind.Data;
using Microsoft.EntityFrameworkCore;

namespace Foundrmind.Services;

/// <summary>Development only: gives the signed-in user a sample business with a page, leads and traffic.</summary>
public static class DevSeed
{
    public static async Task<int> RunAsync(AppDb db, int userId)
    {
        var rnd = new Random(42);
        var project = new Project
        {
            UserId = userId, Name = "NightShift Meals", Stage = "Validating",
            Idea = "Weekly meal-prep plans and grocery lists for night-shift nurses who want to stop living on vending machines.",
            Audience = "Night-shift nurses in the US, 25–45", Goals = "200 waitlist sign-ups and 20 paying customers at $49/month",
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var slug = "nightshift-meals-" + project.Id;
        var page = new PublishedPage
        {
            ProjectId = project.Id, Slug = slug, Title = "NightShift Meals: eat well after 12h shifts", IsPublished = true,
            Html = """<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><title>NightShift Meals</title></head><body><h1>Stop living on vending machines</h1><form data-foundrmind-lead><input name="name"><input name="email" type="email" required><button>Join</button></form></body></html>""",
        };
        db.Pages.Add(page);
        await db.SaveChangesAsync();

        string[] refs = { "", "", "linkedin.com", "t.co", "google.com", "instagram.com" };
        var now = DateTime.UtcNow;
        for (var d = 29; d >= 0; d--)
        {
            var n = 8 + rnd.Next(12) + (29 - d) / 2;
            for (var i = 0; i < n; i++)
                db.PageViews.Add(new PageView
                {
                    PageId = page.Id, ProjectId = project.Id, At = now.AddDays(-d).AddMinutes(-rnd.Next(1400)),
                    Referrer = refs[rnd.Next(refs.Length)], UtmSource = rnd.Next(8) == 0 ? "newsletter" : "",
                });
            page.Views += n;
        }

        string[] names = { "Ana Kovač", "Maria Lopez", "Jess Carter", "Priya Nair", "Tom Reed", "Olivia Park", "Sam Hughes", "Lena Fischer", "Chris Diaz", "Nora Ali" };
        for (var i = 0; i < names.Length; i++)
            db.Leads.Add(new Lead
            {
                ProjectId = project.Id, Name = names[i], Email = $"lead{i}@example.test",
                Source = i < 8 ? $"Page /p/{slug}" : "Manual",
                Stage = LeadStages.All[Math.Min(i / 2, LeadStages.All.Length - 1)],
                Value = i >= 4 ? 49 * 12 : 0,
                CreatedAt = now.AddDays(-rnd.Next(28)), NextFollowUp = now.Date.AddDays(rnd.Next(-2, 4)),
            });
        await db.SaveChangesAsync();
        return project.Id;
    }
}
