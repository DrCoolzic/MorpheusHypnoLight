// Ignore Spelling: Html

using System.Globalization;
using HtmlAgilityPack;
using Microsoft.Maui.Controls;

namespace MPHEditor.Converters;

/// <summary>
/// Converts a simple HTML string (as found in sequence metadata summary/detail fields)
/// into a <see cref="FormattedString"/> that can be displayed by a <see cref="Label"/>.
/// Supports basic inline tags: &lt;strong&gt;/&lt;b&gt; (bold), &lt;em&gt;/&lt;i&gt; (italic),
/// &lt;br&gt; and &lt;p&gt; (line breaks). Any other tag is ignored, only its text content is kept.
/// </summary>
public class HtmlToFormattedStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var formatted = new FormattedString();

        if (value is not string html || string.IsNullOrWhiteSpace(html))
            return formatted;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        AppendChildren(doc.DocumentNode, formatted, isBold: false, isItalic: false);
        return formatted;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException($"{nameof(HtmlToFormattedStringConverter)} does not support converting back.");
    }

    private static void AppendChildren(HtmlNode node, FormattedString formatted, bool isBold, bool isItalic)
    {
        foreach (var child in node.ChildNodes)
        {
            switch (child.NodeType)
            {
                case HtmlNodeType.Text:
                    var text = HtmlEntity.DeEntitize(child.InnerText);
                    if (!string.IsNullOrEmpty(text))
                    {
                        formatted.Spans.Add(new Span
                        {
                            Text = text,
                            FontAttributes = ToFontAttributes(isBold, isItalic)
                        });
                    }
                    break;

                case HtmlNodeType.Element:
                    switch (child.Name.ToLowerInvariant())
                    {
                        case "strong":
                        case "b":
                            AppendChildren(child, formatted, isBold: true, isItalic);
                            break;
                        case "em":
                        case "i":
                            AppendChildren(child, formatted, isBold, isItalic: true);
                            break;
                        case "br":
                            formatted.Spans.Add(new Span { Text = "\n" });
                            break;
                        case "p":
                            AppendChildren(child, formatted, isBold, isItalic);
                            formatted.Spans.Add(new Span { Text = "\n\n" });
                            break;
                        default:
                            AppendChildren(child, formatted, isBold, isItalic);
                            break;
                    }
                    break;
            }
        }
    }

    private static FontAttributes ToFontAttributes(bool isBold, bool isItalic)
    {
        var attributes = FontAttributes.None;
        if (isBold)
            attributes |= FontAttributes.Bold;
        if (isItalic)
            attributes |= FontAttributes.Italic;
        return attributes;
    }
}
