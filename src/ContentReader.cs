namespace SelfDestructChannel;

public class ContentReader
{
    private readonly string _text;
    private int _position = 0;

    public int Length => _text.Length - _position;

    public ReadOnlySpan<char> Span => _text.AsSpan()[_position..];

    public ContentReader(string message, int position = 0)
    {
        _text = message;
        _position = position;
    }

    public ContentReader TrimStart()
    {
        var trimmed = Span.TrimStart();
        int trim_length = Length - trimmed.Length;
        _position += trim_length;
        return this;
    }

    public bool TryReadWhitespace()
    {
        var trimmed = Span.TrimStart();
        int trim_length = Length - trimmed.Length;
        _position += trim_length;
        return trim_length > 0;
    }

    public bool TryReadMentionId(Mention mention, out ulong mentionId)
    {
        var content = Span;
        if (content.StartsWith(mention.Prefix))
        {
            var digits = content[mention.Prefix.Length..(content.IndexOf('>'))];
            bool result = ulong.TryParse(digits, out mentionId);
            if (result)
                _position += (mention.Prefix.Length + digits.Length + 1);
            return result;
        }
        mentionId = 0;
        return false;
    }

    public bool TryReadMentionId(Mention option1, Mention option2, out ulong mentionId)
    {
        var content = Span;
        var (longer, shorter) = option1.Prefix.Length > option2.Prefix.Length ? (option1, option2) : (option2, option1);
        var mention = content.StartsWith(longer.Prefix) ? longer : shorter;
        if (content.StartsWith(mention.Prefix))
        {
            var digits = content[mention.Prefix.Length..(content.IndexOf('>'))];
            bool result = ulong.TryParse(digits, out mentionId);
            if (result)
                _position += (mention.Prefix.Length + digits.Length + 1);
            return result;
        }
        mentionId = 0;
        return false;
    }

    public bool TryReadU64(out ulong mentionId)
    {
        var content = Span;
        var digits = TakeDigits(content);
        var result = ulong.TryParse(digits, out mentionId);
        if (result)
            _position += digits.Length;
        return result;
    }

    public bool TryReadText(ReadOnlySpan<char> text, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        var content = Span;
        var result = content.StartsWith(text, comparison);
        if (result)
            _position += text.Length;
        return result;
    }

    public ContentReader Copy() => new(_text, _position);

    private static ReadOnlySpan<char> TakeDigits(ReadOnlySpan<char> text)
    {
        int i = 0;
        while (i < text.Length && char.IsDigit(text[i]))
            i++;
        return text[..i];
    }
}

public readonly struct Mention
{
    public readonly string Prefix;

    public static Mention USER = new("<@");
    public static Mention NICKNAME = new("<@!");
    public static Mention ROLE = new("<@&");
    public static Mention CHANNEL = new("<#");

    private Mention(string prefix) => Prefix = prefix;

    public string Resolve(ulong id) => $"{Prefix}{id}>";
}