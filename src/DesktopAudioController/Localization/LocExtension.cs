using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using Binding = System.Windows.Data.Binding;

namespace DesktopAudioController.Localization;

/// <summary>
/// XAML 에서 한국어 원문을 현재 언어 문구로 바꿉니다. 예: Text="{loc:Loc '저장'}"
/// 의존 속성에 쓰면 언어 변경을 따라가는 바인딩이 되고, 그 밖의 곳에서는 지금 언어 문구를 한 번 넣습니다.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension : MarkupExtension
{
    private static readonly LocTextConverter TextConverter = new();

    public LocExtension()
    {
    }

    public LocExtension(string text)
    {
        Text = text;
    }

    [ConstructorArgument("text")]
    public string Text { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget { TargetProperty: DependencyProperty })
        {
            var binding = new Binding(nameof(LocSource.Language))
            {
                Source = LocSource.Instance,
                Mode = BindingMode.OneWay,
                Converter = TextConverter,
                ConverterParameter = Text
            };
            return binding.ProvideValue(serviceProvider);
        }

        return Loc.T(Text);
    }

    private sealed class LocTextConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return Loc.T(parameter as string ?? string.Empty);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}

/// <summary>
/// 바인딩 값 하나를 "{0}" 이 든 한국어 원문 형식에 넣어 현재 언어로 보여 줍니다. 언어가 바뀌면 다시 그립니다.
/// 예: Text="{loc:LocFormat Path=Volume, Format='마스터 볼륨: {0}%'}"
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LocFormatExtension : MarkupExtension
{
    private static readonly LocFormatMultiConverter FormatConverter = new();

    public string Path { get; set; } = string.Empty;

    private string _format = "{0}";

    /// <summary>
    /// "{0}" 이 든 한국어 원문 형식입니다. XAML 관례대로 "{0}" 으로 시작하면 앞에 "{}" 를 붙여 적고, 여기서 떼어 냅니다.
    /// </summary>
    public string Format
    {
        get => _format;
        set => _format = value.StartsWith("{}", StringComparison.Ordinal) ? value[2..] : value;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var multiBinding = new MultiBinding
        {
            Mode = BindingMode.OneWay,
            Converter = FormatConverter,
            ConverterParameter = Format
        };
        multiBinding.Bindings.Add(new Binding(Path));
        multiBinding.Bindings.Add(new Binding(nameof(LocSource.Language)) { Source = LocSource.Instance });
        return multiBinding.ProvideValue(serviceProvider);
    }

    private sealed class LocFormatMultiConverter : IMultiValueConverter
    {
        public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
        {
            var value = values.Length > 0 && values[0] != DependencyProperty.UnsetValue ? values[0] : null;
            return Loc.F(parameter as string ?? "{0}", value);
        }

        public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
