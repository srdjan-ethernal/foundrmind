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

                First a short "## Strategy" section (content pillars, posting rhythm, the one metric to watch).
                Then a "## Calendar" section using EXACTLY this structure, because the posts are imported into a scheduler automatically:

                ### Day 1 — <pillar>
                #### <Channel name, exactly as listed above>
                <the complete ready-to-post copy, not an outline; hashtags only where they fit that channel>
                Visual: <one-line visual idea>

                Repeat "#### <Channel>" blocks for every channel on every day, then continue with "### Day 2 — …".
                Posts for X must be 280 characters or fewer including hashtags. Don't wrap post copy in quotes or code blocks.
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

        new("ads", "Ads Studio", "📣",
            "Complete paid-ad campaigns: structure, audiences, ad copy variations and a test plan.",
            "An agency retainer",
            new[]
            {
                new ModuleField("platform", "Platform", Type: "select", Options: new[] { "Meta (Facebook & Instagram)", "Google Search", "LinkedIn", "TikTok", "YouTube" }, Default: "Meta (Facebook & Instagram)"),
                new ModuleField("goal", "Campaign goal", Type: "select", Options: new[] { "Leads / waitlist sign-ups", "Sales", "Traffic to content", "Brand awareness", "Retargeting" }, Default: "Leads / waitlist sign-ups"),
                new ModuleField("budget", "Monthly budget", "e.g. $500"),
                new ModuleField("offer", "What are you advertising?", "e.g. the free 7-day meal plan", "textarea"),
            },
            OutputKind.Markdown, WebSearch: false,
            (p, i) => $"""
                Plan a paid campaign.
                Platform: {Or(i, "platform", "Meta")}
                Goal: {Or(i, "goal", "Leads")}
                Monthly budget: {Or(i, "budget", "not given — recommend a sensible test budget")}
                Offer being advertised: {Or(i, "offer", "the core offer of the business")}

                ## Campaign structure (campaign → ad sets/ad groups, objective, bidding, budget split, schedule)
                ## Audiences / keywords (for search: keyword groups with match types and negatives; for social: 3 audiences with targeting details and why)
                ## Ad copy: 10 variations as a table (angle, headline, primary text/description, CTA). Respect the platform's character limits and state them.
                ## Creative concepts (5: format, what's on screen, hook in the first 2 seconds)
                ## Landing page checklist for this campaign
                ## 14-day test plan (what to test first, kill/scale rules with concrete thresholds, KPIs to watch)
                Do not invent benchmark numbers; if you give typical ranges, label them as rough estimates.
                """),

        new("seo", "SEO Blog Writer", "✍️",
            "Search-optimised articles researched against what already ranks.",
            "A day of writing per article",
            new[]
            {
                new ModuleField("keyword", "Target keyword or topic", "e.g. healthy meals for night shift workers"),
                new ModuleField("intent", "Reader intent", Type: "select", Options: new[] { "Informational (how-to / guide)", "Commercial (best / vs / review)", "Transactional (buy / pricing)" }, Default: "Informational (how-to / guide)"),
                new ModuleField("length", "Length", Type: "select", Options: new[] { "~1,000 words", "~1,800 words", "~2,500 words" }, Default: "~1,800 words"),
            },
            OutputKind.Markdown, WebSearch: true,
            (p, i) => $"""
                Write an SEO article.
                Target keyword/topic: {Or(i, "keyword", "the highest-intent topic for this audience — choose it and say why")}
                Intent: {Or(i, "intent", "Informational")}
                Length: {Or(i, "length", "~1,800 words")}

                First search the web for this keyword: look at what currently ranks and what those pages miss. Then:
                ## SEO brief (primary keyword, 5–8 secondary keywords, search intent, the gap we fill vs current top results with links)
                ## Meta title (≤ 60 chars) and meta description (≤ 155 chars), 2 options each
                ## URL slug
                Then the complete article in markdown: H1, a hook intro, H2/H3 structure, practical specifics, a natural mention of the business's offer with a CTA, and an FAQ section (4–6 questions) at the end.
                Close with: internal link suggestions and one image idea per H2.
                """),

        new("brand", "Brand Kit", "🎨",
            "Positioning, voice, taglines, colour palette, fonts and logo concepts.",
            "A branding workshop",
            new[]
            {
                new ModuleField("feel", "How should the brand feel?", "e.g. calm, trustworthy, a bit playful"),
                new ModuleField("avoid", "Anything to avoid?", "e.g. clichéd health greens, corporate blue"),
            },
            OutputKind.Markdown, WebSearch: false,
            (p, i) => $"""
                Create a brand kit.
                Desired feel: {Or(i, "feel", "derive it from the audience and tone")}
                Avoid: {Or(i, "avoid", "nothing specific")}

                ## Brand core (purpose, promise, personality in 3 words, the enemy we stand against)
                ## Positioning statement and elevator pitch (10 seconds and 30 seconds)
                ## 8 tagline options (mark the top 2)
                ## Voice & tone guide (we are / we are not table, words we use / avoid, 3 before→after rewrites)
                ## Colour palette: 5–6 colours as a table (name, hex, role: primary/accent/background/text, and why). Check that text/background pairs meet WCAG AA contrast and say which pairs to use.
                ## Typography: 2 Google Font pairings (headings + body) with the reasoning
                ## Logo concepts (4 directions: idea, mark description, which palette colours)
                ## Imagery & social templates guidance
                """),

        new("proposal", "Proposal Writer", "📝",
            "Client proposals that sell: scope, timeline, three pricing options and terms.",
            "Hours per proposal",
            new[]
            {
                new ModuleField("client", "Client", "e.g. St. Mary's Hospital nursing staff wellness program"),
                new ModuleField("need", "What do they need?", "Their problem, context, anything from the call", "textarea"),
                new ModuleField("budget", "Budget signals", "e.g. around $5k, decision by end of month"),
            },
            OutputKind.Markdown, WebSearch: false,
            (p, i) => $"""
                Write a client proposal.
                Client: {Or(i, "client", "a typical ideal client — make it clear this is a template")}
                Their need / context: {Or(i, "need", "infer the most common need of the target audience")}
                Budget signals: {Or(i, "budget", "unknown")}

                ## Executive summary (their situation, the outcome, why us — in their language)
                ## Understanding of the problem
                ## Proposed approach and deliverables
                ## Timeline (table: phase, what happens, duration)
                ## Investment: three options (Good / Better / Best) as a table with what's included; anchor on the middle option
                ## Why us (specific, no invented credentials or client names — leave clearly marked placeholders for real case studies)
                ## Next steps and terms (validity, payment schedule, what we need from them)
                Keep it scannable and persuasive; no fluff.
                """),

        new("sales", "Sales Scripts", "📞",
            "Discovery-call script, objection handling and DM/email openers that don't feel salesy.",
            "Weeks of trial and error",
            new[]
            {
                new ModuleField("channel", "Main sales channel", Type: "select", Options: new[] { "Discovery calls", "LinkedIn DMs", "Cold email", "Instagram DMs", "In person" }, Default: "Discovery calls"),
                new ModuleField("objections", "Objections you hear", "e.g. too expensive, no time, I'll do it myself", "textarea"),
            },
            OutputKind.Markdown, WebSearch: false,
            (p, i) => $"""
                Build a sales playbook for this business.
                Main channel: {Or(i, "channel", "Discovery calls")}
                Objections heard so far: {Or(i, "objections", "none recorded — anticipate the 6 most likely ones")}

                ## Discovery call script (opening, 8–10 diagnostic questions in order, how to summarise their pain back, transition to the offer, close/next step). Include what to listen for after each question.
                ## Objection handling (table: objection, what's really behind it, response, follow-up question)
                ## 5 openers for the main channel that are about the prospect, not us (personalised hooks, short, one low-friction ask)
                ## Follow-up cadence (day-by-day for 14 days, with the message for each touch)
                ## Qualification checklist (when to walk away)
                """),

        new("podcast", "Podcast Studio", "🎙️",
            "Episode plans, interview questions, intro script, show notes and clip ideas.",
            "Hours of prep per episode",
            new[]
            {
                new ModuleField("topic", "Episode topic", "e.g. how nurses meal-prep on a 3-shift rotation"),
                new ModuleField("format", "Format", Type: "select", Options: new[] { "Solo", "Interview", "Co-hosted", "Q&A from listeners" }, Default: "Interview"),
                new ModuleField("guest", "Guest (if any)", "Name and why they're interesting"),
            },
            OutputKind.Markdown, WebSearch: false,
            (p, i) => $"""
                Plan a podcast episode.
                Topic: {Or(i, "topic", "the most compelling topic for this audience — choose it and say why")}
                Format: {Or(i, "format", "Interview")}
                Guest: {Or(i, "guest", "none")}

                ## 5 episode title options (mark the best)
                ## Cold open + intro script (word for word, under 60 seconds)
                ## Run of show with timestamps (segments, what each achieves)
                ## Questions / talking points (15 for interviews, in order, with follow-up prompts; for solo: a full outline with key lines)
                ## Mid-roll mention of the business's offer (natural, 20 seconds)
                ## Outro with a single call to action
                ## Show notes (SEO-friendly summary, key takeaways, links placeholders) and 5 short clip ideas for social with the hook line for each
                """),
    };

    /// <summary>Modules on the guided launch path, in order; the rest form the growth toolkit.</summary>
    public static readonly string[] LaunchKeys = { "niche", "product", "funnel", "content", "email", "youtube" };

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
