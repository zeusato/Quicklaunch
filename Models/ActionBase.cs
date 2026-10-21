using System.ComponentModel;
using System.Text.Json.Serialization;

namespace QuickStartLauncher.Models
{
    public enum ActionType
    {
        App,
        Command
    }

    [JsonDerivedType(typeof(AppAction), typeDiscriminator: "app")]
    [JsonDerivedType(typeof(CommandAction), typeDiscriminator: "command")]
    public abstract class ActionBase : INotifyPropertyChanged
    {
        private ActionType _actionType;
        [JsonPropertyName("type")]
        public ActionType ActionType
        {
            get => _actionType;
            set
            {
                if (_actionType != value)
                {
                    var oldType = _actionType;
                    _actionType = value;
                    OnPropertyChanged(nameof(ActionType));
                    ActionTypeChanged?.Invoke(this, new ActionTypeChangedEventArgs(oldType, value));
                }
            }
        }

        [JsonIgnore]
        public abstract string Description { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public event EventHandler<ActionTypeChangedEventArgs>? ActionTypeChanged;

        protected ActionBase(ActionType actionType)
        {
            _actionType = actionType;
        }
    }
    public class ActionTypeChangedEventArgs : EventArgs
    {
        public ActionType OldType { get; }
        public ActionType NewType { get; }

        public ActionTypeChangedEventArgs(ActionType oldType, ActionType newType)
        {
            OldType = oldType;
            NewType = newType;
        }
    }
}