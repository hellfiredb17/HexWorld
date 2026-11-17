using HawkNet.CoreUx.Common.Infrastructure;

namespace HawkNet.CoreUx.Common.LayerSelection;

internal sealed class LayerAttributeItemViewModel : ObservableObject
{
    private string _type;
    private string _value;

    public LayerAttributeItemViewModel(string type, string value)
    {
        _type = LayerSelectionViewModel.NormalizeType(type);
        _value = value ?? string.Empty;
    }

    public IReadOnlyList<string> AvailableTypes => LayerSelectionViewModel.AttributeTypes;

    public string Type
    {
        get => _type;
        set => SetProperty(ref _type, LayerSelectionViewModel.NormalizeType(value));
    }

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value ?? string.Empty);
    }

    public LayerAttributeDefinition ToDefinition() => new(Type, Value);
}
