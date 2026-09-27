namespace Foundrmind.Services;

/// <summary>The free, no-signup demo on the landing page: a short "niche snapshot".</summary>
public static class Demo
{
    public const string System = """
        You are Foundrmind, an AI co-founder. A visitor typed a business idea on our homepage.
        Give a fast, genuinely useful snapshot that makes them think "this understood my idea better than I did".
        Never invent statistics or testimonials. Plain text with simple markdown headings (##) and short bullets. Max ~300 words.
        """;

    public static string Task(string idea) => $"""
        Business idea: {idea}

        ## The sharpest angle
        (1–2 sentences: who exactly to serve first and why)
        ## Your first customer
        (persona: who, the trigger moment, the pain in their own words)
        ## Offer to test this week
        (name, what's included, price point, and why it's easy to say yes to)
        ## 3 moves for the next 7 days
        ## Biggest risk
        """;

    public static string Sample(string idea) => $"""
        ## The sharpest angle
        Demo mode (AI key not configured yet) — this is an example snapshot. For "{Short(idea)}", start with one narrow segment that feels the pain weekly and already pays for workarounds.

        ## Your first customer
        - Who: a busy owner-operator who has tried DIY tools and quit
        - Trigger: a missed deadline or lost client that made the problem expensive
        - In their words: "I know what to do, I just never have the time to do it properly."

        ## Offer to test this week
        **Done-with-you Sprint** — a 7-day setup with a clear deliverable, priced as a no-brainer pilot, with a simple guarantee.

        ## 3 moves for the next 7 days
        - Interview 5 people from the segment and collect their exact phrases
        - Publish a one-page offer with a waitlist form
        - Post three stories from the interviews on the channel they use most

        ## Biggest risk
        Building before selling. Pre-sell 3 pilots before you build anything big.
        """;

    static string Short(string s) => s.Length > 60 ? s[..60] + "…" : s;
}
