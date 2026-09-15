using Avalonia.Media;
using NBA.App.Converters;
using Xunit;

namespace NBA.App.Tests;

public class MarkerStyleToBrushConverterTests
{
    private static readonly MarkerStyleToBrushConverter Converter = MarkerStyleToBrushConverter.Instance;

    [Theory]
    [InlineData(null)]
    [InlineData("landmark")]
    public void UnrecognizedOrNullStyleKey_ResolvesToExistingKeypointColor(string? styleKey)
    {
        var brush = Assert.IsType<SolidColorBrush>(Converter.Convert(styleKey, typeof(IBrush), null, null!));

        Assert.Equal(Color.Parse("#40A0FF"), brush.Color);
    }

    [Fact]
    public void PlayerStyleKey_ResolvesToDistinctColor()
    {
        var brush = Assert.IsType<SolidColorBrush>(Converter.Convert("player", typeof(IBrush), null, null!));

        Assert.Equal(Color.Parse("#F2F2F2"), brush.Color);
        Assert.NotEqual(Color.Parse("#40A0FF"), brush.Color);
    }
}
