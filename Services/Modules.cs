using Foundrmind.Data;

namespace Foundrmind.Services;

public record ModuleField(string Key, string Label, string Placeholder = "", string Type = "text", string[]? Options = null, string Default = "");

public enum OutputKind { Markdown, Html }

public record ModuleDef(
    string Key,
    string Name,
    string Icon,
    string Tagline,
    string Saves,
    ModuleField[] Fields,
    OutputKind Output,
    bool WebSearch,
    Func<Project, IReadOnlyDictionary<string, string>, string> Task);

public static class Modules
{
    public static readonly ModuleDef[] All =
    {
        new("niche", "Niche Profiler", "🧭",
            "Live market research: customers, competitors, positioning and pricing.",
            "~40 hours of research",
            new[]
            {
                new ModuleField("market", "Target market / region", "e.g. US & UK, English-speaking"),
                new ModuleField("focus", "Anything specific to investigate?", "e.g. is there room for a premium tier?", "textarea"),
            },
            OutputKind.Markdown, WebSearch: true,
            (p, i) => $"""
                Produce a niche profile for this business. Research the market on the web first: real competitors (with URLs), their pricing, and demand signals. Cite sources inline as markdown links.

                Target market: {Or(i, "market", "not specified — pick the most promising one and say why")}
                Specific question: {Or(i, "focus", "none")}

                Structure:
                ## Verdict (3 sentences: is this worth pursuing, and the sharpest angle)
                ## Ideal customer profiles (2–3 personas: who, trigger moment, pains in their words, where they hang out)
                ## Competitor landscape (table: name, offer, price, weakness we can exploit)
                ## Positioning (one-line positioning statement + 3 differentiators)
                ## Pricing recommendation (entry / core / premium with reasoning)
                ## 3 offers to test first
                ## Risks and how to de-risk them in the next 30 days
                """),

        new("funnel", "Funnel Builder", "🚀",
            "A complete, conversion-focused landing page you can publish in one click. Leads land in your CRM.",
            "Launch in minutes, not weeks",
            new[]
            {
                new ModuleField("offer", "What are you offering on this page?", "e.g. Free 7-day email course on …", "textarea"),
                new ModuleField("cta", "Main call to action", "e.g. Join the waitlist"),
                new ModuleField("style", "Visual style", Type: "select", Options: new[] { "Vibrant gradient", "Dark & premium", "Clean light", "Editorial" }, Default: "Vibrant gradient"),
            },
            OutputKind.Html, WebSearch: false,
            (p, i) => $"""
                Build a single-file, production-quality landing page (HTML + inline CSS, optional small inline JS) for this offer.

                Offer: {Or(i, "offer", "the core offer of the business")}
                Call to action: {Or(i, "cta", "Get started")}
                Visual style: {Or(i, "style", "Vibrant gradient")}

                Requirements:
                - Sections: hero (headline + subhead + CTA), problem, solution/benefits, how it works, social proof placeholder that is clearly marked as placeholder (never invent testimonials or numbers), FAQ, final CTA.
                - Mobile-first, responsive, accessible (contrast, labels, semantic HTML). Use a Google Font via <link>. No external JS libraries, no external images (use CSS gradients / inline SVG).
                - Include exactly one lead capture form with the attribute data-foundrmind-lead, containing inputs name="name", name="email" (type=email, required) and optionally a textarea name="message". Do NOT set action or method; the platform wires it up.
                - Copy must be specific to this business and audience, not generic.
                - Output ONLY the HTML document, starting with <!DOCTYPE html>. No markdown fences, no commentary.
                """),

        new("content", "Content Reactor", "⚡",
            "A ready-to-post content calendar across your channels.",
            "30 days of content in minutes",
            new[]
            {
                new ModuleField("channels", "Channels", "LinkedIn, X, Instagram", Default: "LinkedIn, X, Instagram"),
                new ModuleField("days", "How many days?", Type: "select", Options: new[] { "7", "14", "30" }, Default: "14"),
                new ModuleField("themes", "Themes or launches to cover", "e.g. launch of the waitlist on day 5", "textarea"),
            },
            OutputKind.Markdown, WebSearch: false,
            (p, i) => $"""
                Create a {Or(i, "days", "14")}-day content calendar for these channels: {Or(i, "channels", "LinkedIn, X")}.
                Themes / events to include: {Or(i, "themes", "none — build a natural awareness → trust → offer arc")}

                First a short strategy (content pillars, posting rhythm, the one metric to watch).
                Then for each day a "### Day N — <pillar>" heading with, per channel, the complete ready-to-post copy (not an outline), hashtags where they fit that channel, and a one-line visual idea.
                Mix formats: stories, contrarian takes, how-tos, behind the scenes, soft offers (max 1 in 5 posts sells).
                """),

        new("youtube", "YouTube Studio", "🎬",
            "Titles, hook, full script, thumbnail concepts, description and chapters.",
            "10× your video output",
            new[]
            {
                new ModuleField("topic", "Video topic", "e.g. 5 mistakes first-time course creators make"),
                new ModuleField("length", "Target length", Type: "select", Options: new[] { "Short (<60s)", "5–8 min", "10–15 min", "20+ min" }, Default: "5–8 min"),
            },
            OutputKind.Markdown, WebSearch: false,
            (p, i) => $"""
                Plan and write a YouTube video.
                Topic: {Or(i, "topic", "the most searchable topic for this audience — choose it and say why")}
                Length: {Or(i, "length", "5–8 min")}

                ## 5 title options (curiosity + clarity, < 60 chars) — mark the best one
                ## Hook (first 15 seconds, word for word)
                ## Full script with [timestamps] and [B-ROLL: …] / [ON SCREEN: …] cues
                ## 3 thumbnail concepts (composition, text overlay ≤ 4 words, emotion)
                ## Description (SEO-friendly, with a CTA to the business offer)
                ## Chapters and 15 tags
                """),

        new("product", "Product Creator", "📦",
            "Design a digital product or course: outline, pricing ladder and launch plan.",
            "Launch products in days",
            new[]
            {
                new ModuleField("type", "Product type", Type: "select", Options: new[] { "Online course", "Ebook / guide", "Templates / toolkit", "Cohort / coaching", "Membership" }, Default: "Online course"),
                new ModuleField("price", "Price point in mind", "e.g. $49–$99"),
                new ModuleField("notes", "Constraints or ideas", "e.g. must be finishable in a weekend", "textarea"),
            },
            OutputKind.Markdown, WebSearch: false,
            (p, i) => $"""
                Design a digital product for this business.
                Type: {Or(i, "type", "Online course")}
                Price in mind: {Or(i, "price", "recommend one")}
                Constraints / ideas: {Or(i, "notes", "none")}

                ## Product name options (3) and the promise (transformation in one sentence)
                ## Full outline (modules → lessons, each lesson with its outcome and format)
                ## Bonuses that remove objections
                ## Pricing ladder (lead magnet → core → premium) with reasoning
                ## Sales page bullets (10) and guarantee
                ## 14-day launch plan (day by day)
                ## Write the complete content of lesson 1 (or chapter 1) so the creator can start today
                """),

        new("email", "Email Sequences", "✉️",
            "Welcome, nurture and launch email sequences written in your voice.",
            "Weeks of copywriting",
            new[]
            {
                new ModuleField("kind", "Sequence", Type: "select", Options: new[] { "Welcome / onboarding", "Nurture (value)", "Product launch", "Re-engagement", "Abandoned cart" }, Default: "Welcome / onboarding"),
                new ModuleField("count", "Number of emails", Type: "select", Options: new[] { "3", "5", "7" }, Default: "5"),
                new ModuleField("goal", "Goal of the sequence", "e.g. book a discovery call"),
            },
            OutputKind.Markdown, WebSearch: false,
            (p, i) => $"""
                Write a {Or(i, "count", "5")}-email "{Or(i, "kind", "Welcome")}" sequence.
                Goal: {Or(i, "goal", "move the subscriber to the core offer")}

                For each email: "### Email N — send on day X", subject line + 2 alternatives, preview text, then the full body (plain, personal, one idea per email, one clear CTA). Short paragraphs; no hype.
                End with a note on which metric to watch per email.
                """),
    };

    public static ModuleDef? Get(string key) => All.FirstOrDefault(m => m.Key == key);

    public static string System(Project p) => $"""
        You are Foundrmind, an expert AI co-founder: a sharp strategist, marketer and copywriter who produces finished, usable work — not advice about doing the work.

        The business you are working on:
        - Name: {p.Name}
        - Idea: {p.Idea}
        - Target audience: {Fallback(p.Audience)}
        - Goals: {Fallback(p.Goals)}
        - Brand tone: {Fallback(p.Tone)}
        - Stage: {p.Stage}

        Rules:
        - Be specific to this business and audience. Generic output is a failure.
        - Never invent statistics, testimonials, customer names or results. When a number is an estimate, say so; when you used a web source, link it.
        - Write in English unless the business details clearly call for another language.
        - Use clean markdown (headings, short paragraphs, tables where useful) unless the task asks for HTML.
        """;

    static string Or(IReadOnlyDictionary<string, string> i, string key, string fallback)
        => i.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : fallback;

    static string Fallback(string s) => string.IsNullOrWhiteSpace(s) ? "(not specified — infer sensibly)" : s;

    public static string FollowUpTask(Lead l) => $"""
        Draft a follow-up message to this lead.
        Name: {l.Name}; company: {(l.Company == "" ? "unknown" : l.Company)}; source: {l.Source}; pipeline stage: {l.Stage}
        Notes so far: {(l.Notes == "" ? "none" : l.Notes)}

        Principles: it is about them, not us; reference something specific from the notes; one clear, low-friction next step; under 120 words; no pushy sales language.
        Output: a subject line on the first line ("Subject: …"), a blank line, then the message. Nothing else.
        """;
}
