namespace Foundrmind.Data;

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Plan { get; set; } = Plans.Free;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<Project> Projects { get; set; } = new();
}

public static class Plans
{
    public const string Free = "free";
    public const string Pro = "pro";
    public const string Scale = "scale";

    public static int MonthlyRuns(string plan) => plan switch
    {
        Pro => 500,
        Scale => 2000,
        _ => 30,
    };
}

/// <summary>One business. Every module reads this as shared context.</summary>
public class Project
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public string Name { get; set; } = "";
    public string Idea { get; set; } = "";
    public string Audience { get; set; } = "";
    public string Goals { get; set; } = "";
    public string Tone { get; set; } = "Friendly and expert";
    public string Stage { get; set; } = "Idea";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<ModuleRun> Runs { get; set; } = new();
    public List<Lead> Leads { get; set; } = new();
    public List<PublishedPage> Pages { get; set; } = new();
}

public class ModuleRun
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public string ModuleKey { get; set; } = "";
    public string InputJson { get; set; } = "{}";
    public string Output { get; set; } = "";
    public string Status { get; set; } = "done"; // done | error | refused | manual
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class LeadStages
{
    public static readonly string[] All = { "New", "Contacted", "Qualified", "Proposal", "Won", "Lost" };
}

public class Lead
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Company { get; set; } = "";
    public string Source { get; set; } = "Manual";
    public string Stage { get; set; } = "New";
    public decimal Value { get; set; }
    public string Notes { get; set; } = "";
    public string AiDraft { get; set; } = "";
    public DateTime? NextFollowUp { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A landing page produced by the Funnel Builder, served at /p/{slug}.</summary>
public class PublishedPage
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Html { get; set; } = "";
    public bool IsPublished { get; set; }
    public int Views { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
