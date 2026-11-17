namespace Cccc.Web.Features.Data;

public sealed record Row(Guid Id, string Name, string? Colour, string Detail)
{
    public static Row Of(Guid id, string name, string? colour, string detail) => new(id, string.IsNullOrWhiteSpace(name) ? "(no name)" : name, colour, detail);
}
