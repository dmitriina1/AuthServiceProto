namespace AuthServiceProto;

// Имена вынесены в одно место, чтобы конфигурация OpenIddict и проверки API не расходились.
public static class AuthConstants
{
    public const string SecurityStampClaim = "urn:authservice:security_stamp";

    public const string OrdersScope = "orders_api";
    public const string OrdersResource = "orders-api";
    public const string OrdersPolicy = "orders-api";

    public const string ProfileScope = "profile_api";
    public const string ProfileResource = "profile-api";
    public const string ProfilePolicy = "profile-api";
}
