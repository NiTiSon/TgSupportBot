using VYaml.Annotations;
using VYaml.Serialization;

namespace SSSR.Data;

[YamlObject(NamingConvention.SnakeCase)]
public partial record struct Config
{
    public Option[] Options;

    public static Config Value { get; internal set; } 
}

[YamlObject(NamingConvention.SnakeCase)]
public partial record struct Option
{
    public string Text;
    public string? Hint;
    public Option[]? Options;
}