using RoboCopyGui.Core;

namespace RoboCopyGui.ViewModels;

public sealed class OptionItemViewModel : ObservableObject
{
    private readonly Action<OptionItemViewModel> _changed;
    private bool _isSelected;
    private string _value;
    private bool _isAvailable = true;
    private string _conflictMessage = string.Empty;

    public OptionItemViewModel(RobocopyOptionDefinition definition, Action<OptionItemViewModel> changed)
    {
        Definition = definition;
        _changed = changed;
        _isSelected = definition.DefaultSelected;
        _value = definition.DefaultValue;
    }

    public RobocopyOptionDefinition Definition { get; }
    public string Id => Definition.Id;
    public string Label => Definition.Label;
    public string Flag => Definition.ArgumentStyle is OptionArgumentStyle.Switch ? Definition.Flag : Definition.Flag + ":";
    public string Description => Definition.Description;
    public string Category => Definition.Category;
    public bool HasValue => Definition.ValueKind != OptionValueKind.None;
    public bool IsDestructive => Definition.IsDestructive;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                _changed(this);
            }
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value))
            {
                _changed(this);
            }
        }
    }

    public bool IsAvailable
    {
        get => _isAvailable;
        set => SetProperty(ref _isAvailable, value);
    }

    public string ConflictMessage
    {
        get => _conflictMessage;
        set
        {
            if (SetProperty(ref _conflictMessage, value))
            {
                OnPropertyChanged(nameof(DisplayDescription));
            }
        }
    }

    public string DisplayDescription => string.IsNullOrEmpty(ConflictMessage) ? Description : $"{Description}  {ConflictMessage}";

    public CopyOptionValue ToSelection() => new(Id, HasValue ? Value : null);
}
