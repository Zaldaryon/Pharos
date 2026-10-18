namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// A client setting every test of the class runs with. The scenario applies it once the client
/// has booted and puts the previous value back when the test ends.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class ClientSettingAttribute(string key, object value) : Attribute
{
    /// <summary>The setting's key in <c>clientsettings.json</c>.</summary>
    public string Key { get; } = key;

    /// <summary>The value.</summary>
    public object Value { get; } = value;
}
