using VYaml.Annotations;
using VYaml.Serialization;

namespace TgSupportBot.Data;

[YamlObject(NamingConvention.SnakeCase)]
public partial record struct Option
{
    public string Text;
    public string? Hint;
    public Option[]? Options;
}