using Foundrmind.Data;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;

namespace Foundrmind.Services;

/// <summary>
/// Stripe subscriptions: Checkout to subscribe, Customer Portal to manage, webhooks as the source of truth for the plan.
/// Webhook handling uses only the event payload, so it needs no extra API calls.
/// </summary>
public class Billing(IConfiguration cfg, IDbContextFactory<AppDb> dbf, ILogger<Billing> log)
{
    string? SecretKey => cfg["STRIPE_SECRET_KEY"];
    string? WebhookSecret => cfg["STRIPE_WEBHOOK_SECRET"];
    string? PricePro => cfg["STRIPE_PRICE_PRO"];
    string? PriceScale => cfg["STRIPE_PRICE_SCALE"];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SecretKey) && !string.IsNullOrWhiteSpace(PricePro) && !string.IsNullOrWhiteSpace(PriceScale);

    StripeClient Client => new(SecretKey);

    public string? PriceFor(string plan) => plan switch { Plans.Pro => PricePro, Plans.Scale => PriceScale, _ => null };

    public string PlanForPrice(string? priceId) =>
        priceId != null && priceId == PriceScale ? Plans.Scale :
        priceId != null && priceId == PricePro ? Plans.Pro : Plans.Free;

    public async Task<string> CreateCheckoutAsync(Data.User user, string plan, string baseUrl)
    {
        var price = PriceFor(plan) ?? throw new ArgumentException("Unknown plan", nameof(plan));
        var options = new SessionCreateOptions
        {
            Mode = "subscription",
            LineItems = new List<SessionLineItemOptions> { new() { Price = price, Quantity = 1 } },
            ClientReferenceId = user.Id.ToString(),
            SuccessUrl = $"{baseUrl}/app/billing?success=1",
            CancelUrl = $"{baseUrl}/app/billing?canceled=1",
            AllowPromotionCodes = true,
            Metadata = new Dictionary<string, string> { ["userId"] = user.Id.ToString(), ["plan"] = plan },
            SubscriptionData = new SessionSubscriptionDataOptions
            {
                Metadata = new Dictionary<string, string> { ["userId"] = user.Id.ToString() },
            },
        };
        if (user.StripeCustomerId.Length > 0) options.Customer = user.StripeCustomerId;
        else options.CustomerEmail = user.Email;

        var session = await new SessionService(Client).CreateAsync(options);
        return session.Url;
    }

    public async Task<string> CreatePortalAsync(Data.User user, string baseUrl)
    {
        var session = await new Stripe.BillingPortal.SessionService(Client).CreateAsync(new Stripe.BillingPortal.SessionCreateOptions
        {
            Customer = user.StripeCustomerId,
            ReturnUrl = $"{baseUrl}/app/billing",
        });
        return session.Url;
    }

    /// <summary>Verifies the signature and applies the event. Returns false for an invalid signature.</summary>
    public async Task<bool> HandleWebhookAsync(string json, string signature)
    {
        Event ev;
        try
        {
            ev = EventUtility.ConstructEvent(json, signature, WebhookSecret, throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            log.LogWarning("Rejected Stripe webhook: {Message}", ex.Message);
            return false;
        }

        switch (ev.Data.Object)
        {
            case Session s when ev.Type == EventTypes.CheckoutSessionCompleted:
                await OnCheckoutCompleted(s);
                break;
            case Subscription sub when ev.Type is EventTypes.CustomerSubscriptionCreated or EventTypes.CustomerSubscriptionUpdated or EventTypes.CustomerSubscriptionDeleted:
                await OnSubscriptionChanged(sub, deleted: ev.Type == EventTypes.CustomerSubscriptionDeleted);
                break;
            default:
                log.LogDebug("Ignoring Stripe event {Type}", ev.Type);
                break;
        }
        return true;
    }

    async Task OnCheckoutCompleted(Session s)
    {
        if (!int.TryParse(s.ClientReferenceId ?? s.Metadata?.GetValueOrDefault("userId"), out var userId)) return;
        await using var db = await dbf.CreateDbContextAsync();
        var user = await db.Users.FindAsync(userId);
        if (user == null) return;
        user.StripeCustomerId = s.CustomerId ?? user.StripeCustomerId;
        user.StripeSubscriptionId = s.SubscriptionId ?? user.StripeSubscriptionId;
        // Grant the plan right away; the subscription events that follow keep it in sync.
        if (s.Metadata?.GetValueOrDefault("plan") is Plans.Pro or Plans.Scale && s.PaymentStatus is "paid" or "no_payment_required")
        {
            user.Plan = s.Metadata["plan"];
            user.SubscriptionStatus = "active";
        }
        await db.SaveChangesAsync();
        log.LogInformation("Checkout completed for user {UserId} → {Plan}", userId, user.Plan);
    }

    async Task OnSubscriptionChanged(Subscription sub, bool deleted)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.StripeSubscriptionId == sub.Id)
            ?? await db.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == sub.CustomerId && u.StripeCustomerId != "");
        if (user == null && int.TryParse(sub.Metadata?.GetValueOrDefault("userId"), out var uid))
            user = await db.Users.FindAsync(uid);
        if (user == null) { log.LogWarning("Subscription {Sub} matches no user", sub.Id); return; }

        var item = sub.Items?.Data?.FirstOrDefault();
        var active = !deleted && sub.Status is "active" or "trialing" or "past_due";
        user.StripeCustomerId = sub.CustomerId ?? user.StripeCustomerId;
        user.StripeSubscriptionId = deleted ? "" : sub.Id;
        user.SubscriptionStatus = deleted ? "canceled" : sub.Status;
        user.Plan = active ? PlanForPrice(item?.Price?.Id) : Plans.Free;
        user.PlanRenewsAt = active && item != null ? item.CurrentPeriodEnd : null;
        user.CancelAtPeriodEnd = !deleted && sub.CancelAtPeriodEnd;
        await db.SaveChangesAsync();
        log.LogInformation("Subscription {Sub} for user {UserId}: {Status} → {Plan}", sub.Id, user.Id, sub.Status, user.Plan);
    }
}
