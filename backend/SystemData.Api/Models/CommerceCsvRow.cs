namespace SystemData.Api.Models;

public sealed record CommerceCsvRow(DateOnly ProcessDate, string? MerchantName, string? DocumentNumber);
public sealed record ProcessCommerceRequest(DateOnly ProcessDate);
public sealed record QuarantinedCommerce(long Id, DateOnly? ProcessDate, string? MerchantName,
    string? DocumentNumber, string Reason, DateTime QuarantinedAt);
