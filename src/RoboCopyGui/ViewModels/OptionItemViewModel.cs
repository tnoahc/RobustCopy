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
    public string Label => Id switch
    {
        "Mirror" => "Mirror",
        "Purge" => "Purge",
        "MoveTree" => "Move",
        "EmptySubdirectories" => "Include subfolders",
        "MultiThreaded" => "Multithreaded",
        "ExcludeOlder" => "Skip older files",
        "ExcludeNewer" => "Skip newer files",
        _ => Definition.Label
    };
    public string Flag => Definition.ArgumentStyle is OptionArgumentStyle.Switch ? Definition.Flag : Definition.Flag + ":";
    public string FlagText => Definition.ArgumentStyle switch
    {
        OptionArgumentStyle.Switch => Definition.Flag,
        OptionArgumentStyle.ColonValue => $"{Definition.Flag}:{Value}",
        OptionArgumentStyle.OptionalColonValue when !string.IsNullOrWhiteSpace(Value) => $"{Definition.Flag}:{Value}",
        OptionArgumentStyle.SeparateList when !string.IsNullOrWhiteSpace(Value) => $"{Definition.Flag} {Value}",
        _ => Definition.Flag
    };
    public string Description => Id switch
    {
        "Mirror" => "Destination matches source exactly, deleting extras.",
        "Purge" => "Removes destination files no longer in source.",
        "MoveTree" => "Moves files and deletes them from source.",
        "EmptySubdirectories" => "Copies all subfolders, including empty ones.",
        "Restartable" => "Resumes cleanly after a network interruption.",
        "MultiThreaded" => "Copies with multiple threads for faster transfers.",
        "ExcludeOlder" => "Only copies files newer than the destination.",
        "ExcludeNewer" => "Leaves newer destination files untouched.",
        "Retries" => "Attempts a failed file again before skipping it.",
        "Wait" => "Pause between retries on a failed file.",
        _ => Definition.Description
    };
    public string Category => Id switch
    {
        "Mirror" or "Purge" or "MoveTree" or "EmptySubdirectories" => "Copy mode",
        "Restartable" or "MultiThreaded" or "ExcludeOlder" or "ExcludeNewer" => "Reliability",
        "Retries" or "Wait" => "Retry behavior",
        _ => Definition.Category
    };
    public int DisplayOrder => Id switch
    {
        "Mirror" => 0,
        "Purge" => 1,
        "MoveTree" => 2,
        "EmptySubdirectories" => 3,
        "Restartable" => 4,
        "MultiThreaded" => 5,
        "ExcludeOlder" => 6,
        "ExcludeNewer" => 7,
        "Retries" => 8,
        "Wait" => 9,
        _ => 100
    };
    public bool HasValue => Definition.ValueKind != OptionValueKind.None;
    public bool IsDestructive => Definition.IsDestructive;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(FlagText));
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
                OnPropertyChanged(nameof(FlagText));
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
