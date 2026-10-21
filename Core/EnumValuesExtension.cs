using System;
using System.Linq;
using System.Windows.Markup;

namespace QuickStartLauncher.Core
{
    public class EnumValuesExtension : MarkupExtension
    {
        private readonly Type _enumType;

        public EnumValuesExtension(Type enumType)
        {
            if (enumType == null || !enumType.IsEnum)
            {
                throw new ArgumentException("enumType must be an enum type");
            }
            _enumType = enumType;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return Enum.GetValues(_enumType);
        }
    }
}