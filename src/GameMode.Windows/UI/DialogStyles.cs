using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;

namespace GameMode.Windows.UI;

internal static class DialogStyles
{
    public static void Apply(Window window)
    {
        if (System.Windows.Application.Current?.MainWindow is MainWindow main)
            foreach (var type in new[] { typeof(Button), typeof(CheckBox), typeof(TextBox), typeof(System.Windows.Controls.Primitives.ScrollBar) })
                window.Resources[type] = main.FindResource(type);
        window.Resources[typeof(ListBoxItem)] = (Style)XamlReader.Parse("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListBoxItem">
                <Setter Property="Background" Value="#101728"/>
                <Setter Property="Foreground" Value="#F5F6FF"/>
                <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
                <Setter Property="Margin" Value="3,2"/>
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="ListBoxItem">
                            <Border Name="Surface" Background="{TemplateBinding Background}" BorderBrush="#34435F" BorderThickness="1" CornerRadius="9">
                                <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"/>
                            </Border>
                            <ControlTemplate.Triggers>
                                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Surface" Property="Background" Value="#243452"/></Trigger>
                                <Trigger Property="IsSelected" Value="True"><Setter TargetName="Surface" Property="Background" Value="#2E416D"/><Setter TargetName="Surface" Property="BorderBrush" Value="#7E9CEF"/></Trigger>
                                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Surface" Property="BorderBrush" Value="#BCC9FF"/><Setter TargetName="Surface" Property="BorderThickness" Value="2"/></Trigger>
                                <Trigger Property="IsEnabled" Value="False"><Setter TargetName="Surface" Property="Opacity" Value="0.5"/></Trigger>
                            </ControlTemplate.Triggers>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
            """);
    }
}
