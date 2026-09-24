using System.Text.Json;
using MilkTeaWeb.ViewModels.Cart;

namespace MilkTeaWeb.Services;

public sealed class GuestCartSessionStore(
    IHttpContextAccessor httpContextAccessor,
    ILogger<GuestCartSessionStore> logger)
{
    public const string SessionKey = "MilkTeaWeb.GuestCart";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public GuestCartSessionModel Get()
    {
        var session = GetSession();
        var payload = session.GetString(SessionKey);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return new GuestCartSessionModel();
        }

        try
        {
            var cart = JsonSerializer.Deserialize<GuestCartSessionModel>(payload, SerializerOptions)
                ?? new GuestCartSessionModel();
            cart.Items ??= [];
            return cart;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Guest cart session data could not be read and was cleared.");
            session.Remove(SessionKey);
            return new GuestCartSessionModel();
        }
    }

    public void Save(GuestCartSessionModel cart)
    {
        cart.Items ??= [];
        GetSession().SetString(SessionKey, JsonSerializer.Serialize(cart, SerializerOptions));
    }

    public void Clear() => GetSession().Remove(SessionKey);

    private ISession GetSession() => httpContextAccessor.HttpContext?.Session
        ?? throw new InvalidOperationException("Guest cart session is unavailable for this request.");
}
