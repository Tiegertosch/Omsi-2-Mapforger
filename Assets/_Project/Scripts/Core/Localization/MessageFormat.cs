using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OmsiMapEditor.Core.Localization
{
    public enum MessageArgumentKind
    {
        Plain,
        Number,
        Date,
        Time,
        Plural,
        Select
    }

    /// <summary>One argument a message uses: its name, how it is shown and, for plural and select, the cases the message gives.</summary>
    public sealed class MessageArgument
    {
        public MessageArgument(string name, MessageArgumentKind kind)
        {
            Name = name;
            Kind = kind;
        }

        public string Name { get; }
        public MessageArgumentKind Kind { get; }

        /// <summary>The case names of a plural or select, such as "one", "other", "=0".</summary>
        public List<string> Cases { get; } = new List<string>();

        public override string ToString()
        {
            return Name + ":" + Kind;
        }
    }

    /// <summary>
    /// A text with arguments in the ICU MessageFormat syntax, compiled once and formatted often: <c>{name}</c>,
    /// <c>{n, number}</c> (also <c>integer</c>, <c>percent</c> or a pattern such as <c>0.0</c>), <c>{when, date, short|medium|long|full}</c>,
    /// <c>{when, time, short|medium}</c>, <c>{n, plural, =0 {none} one {# item} other {# items}}</c> with an optional
    /// <c>offset:N</c> and <c>#</c> for the number, and <c>{kind, select, a {...} other {...}}</c>.
    /// Apostrophes follow ICU's lenient rule: one only quotes when it stands before <c>{</c>, <c>}</c> or, in a plural, <c>#</c>;
    /// <c>''</c> is an apostrophe. So "variant's" and "can't" need no care. Formatting never throws: an argument that is missing
    /// shows as <c>{name}</c>.
    /// </summary>
    public sealed class MessageFormat
    {
        private readonly List<Node> nodes;

        private MessageFormat(List<Node> nodes, List<MessageArgument> arguments)
        {
            this.nodes = nodes;
            Arguments = arguments;
            IsPlain = nodes.Count == 0 || nodes.Count == 1 && nodes[0] is LiteralNode;
            PlainText = nodes.Count == 0 ? string.Empty : IsPlain ? ((LiteralNode)nodes[0]).Text : null;
        }

        /// <summary>True when the text has no arguments, so it is the same whatever arguments are given.</summary>
        public bool IsPlain { get; }

        /// <summary>The text of a plain message (<see cref="IsPlain"/>), apostrophe quoting resolved; null otherwise.</summary>
        public string PlainText { get; }

        /// <summary>Every argument the text uses, once per name and kind.</summary>
        public List<MessageArgument> Arguments { get; }

        public static MessageFormat Parse(string template)
        {
            if (!TryParse(template, out var message, out var error))
                throw new FormatException(error);
            return message;
        }

        public static bool TryParse(string template, out MessageFormat message, out string error)
        {
            message = null;
            error = null;
            if (template == null)
            {
                error = "the text is missing";
                return false;
            }
            try
            {
                var parser = new Parser(template);
                var list = parser.ParseSequence(false, false);
                message = new MessageFormat(list, parser.Arguments);
                return true;
            }
            catch (FormatException exception)
            {
                error = exception.Message;
                return false;
            }
        }

        /// <summary>The text for the arguments, numbers and dates written the way <paramref name="culture"/> writes them.</summary>
        public string Format(CultureInfo culture, string language, IReadOnlyList<(string Name, object Value)> arguments)
        {
            if (IsPlain)
                return PlainText;
            var builder = new StringBuilder();
            var context = new Context
            {
                Culture = culture ?? CultureInfo.InvariantCulture, Language = language ?? string.Empty,
                Arguments = arguments ?? new (string Name, object Value)[0]
            };
            foreach (var node in nodes)
                node.Write(builder, context);
            return builder.ToString();
        }

        private sealed class Context
        {
            public CultureInfo Culture;
            public string Language;
            public IReadOnlyList<(string Name, object Value)> Arguments;
            public bool InPlural;
            public decimal PluralNumber;

            public bool TryGet(string name, out object value)
            {
                for (var index = 0; index < Arguments.Count; index++)
                    if (string.Equals(Arguments[index].Name, name, StringComparison.Ordinal))
                    {
                        value = Arguments[index].Value;
                        return true;
                    }
                value = null;
                return false;
            }
        }

        private abstract class Node
        {
            public abstract void Write(StringBuilder builder, Context context);
        }

        private sealed class LiteralNode : Node
        {
            public LiteralNode(string text)
            {
                Text = text;
            }

            public string Text { get; }

            public override void Write(StringBuilder builder, Context context)
            {
                builder.Append(Text);
            }
        }

        private sealed class PoundNode : Node
        {
            public override void Write(StringBuilder builder, Context context)
            {
                builder.Append(context.PluralNumber.ToString("#,##0.###", context.Culture));
            }
        }

        private sealed class ArgumentNode : Node
        {
            public ArgumentNode(string name, MessageArgumentKind kind, string style)
            {
                Name = name;
                Kind = kind;
                Style = style ?? string.Empty;
            }

            public string Name { get; }
            public MessageArgumentKind Kind { get; }
            public string Style { get; }

            public override void Write(StringBuilder builder, Context context)
            {
                if (!context.TryGet(Name, out var value))
                {
                    builder.Append('{').Append(Name).Append('}');
                    return;
                }
                builder.Append(Show(value, context.Culture));
            }

            private string Show(object value, CultureInfo culture)
            {
                if (value == null)
                    return string.Empty;
                try
                {
                    switch (Kind)
                    {
                        case MessageArgumentKind.Number:
                            return ShowNumber(value, culture);
                        case MessageArgumentKind.Date:
                        case MessageArgumentKind.Time:
                            return ShowDate(value, culture);
                        default:
                            return value is IFormattable formattable ? formattable.ToString(null, culture) : value.ToString();
                    }
                }
                catch (FormatException)
                {
                    return value.ToString();
                }
            }

            private string ShowNumber(object value, CultureInfo culture)
            {
                if (!(value is IFormattable formattable))
                    return value.ToString();
                switch (Style.ToLowerInvariant())
                {
                    case "":
                        return formattable.ToString("#,##0.###", culture);
                    case "integer":
                        return formattable.ToString("#,##0", culture);
                    case "percent":
                        return formattable.ToString("P0", culture);
                    default:
                        return formattable.ToString(Style, culture);
                }
            }

            /// <summary>The long date without the day of the week and with the month shortened: "Nov 7, 2026", "7. Nov 2026".</summary>
            private static string MediumDatePattern(CultureInfo culture)
            {
                var pattern = culture.DateTimeFormat.LongDatePattern;
                pattern = System.Text.RegularExpressions.Regex.Replace(pattern, @"\s*[,.]?\s*d{3,4}\s*[,.]?\s*", " ").Trim();
                return pattern.Replace("MMMM", "MMM");
            }

            private string ShowDate(object value, CultureInfo culture)
            {
                if (!(value is DateTime) && !(value is DateTimeOffset))
                    return value.ToString();
                var formattable = (IFormattable)value;
                var style = Style.ToLowerInvariant();
                if (Kind == MessageArgumentKind.Time)
                    return formattable.ToString(style == "medium" || style == "long" || style == "full" ? "T" : "t", culture);
                switch (style)
                {
                    case "":
                    case "short":
                        return formattable.ToString("d", culture);
                    case "medium":
                        return formattable.ToString(MediumDatePattern(culture), culture);
                    case "long":
                        return formattable.ToString("D", culture);
                    default:
                        return formattable.ToString("F", culture);
                }
            }
        }

        private sealed class CaseNode : Node
        {
            public CaseNode(string selector, List<Node> body)
            {
                Selector = selector;
                Body = body;
            }

            public string Selector { get; }
            public List<Node> Body { get; }

            public override void Write(StringBuilder builder, Context context)
            {
                foreach (var node in Body)
                    node.Write(builder, context);
            }
        }

        private sealed class PluralNode : Node
        {
            public PluralNode(string name, decimal offset, List<CaseNode> cases)
            {
                Name = name;
                Offset = offset;
                Cases = cases;
            }

            public string Name { get; }
            public decimal Offset { get; }
            public List<CaseNode> Cases { get; }

            public override void Write(StringBuilder builder, Context context)
            {
                if (!context.TryGet(Name, out var value))
                {
                    builder.Append('{').Append(Name).Append('}');
                    return;
                }
                if (!TryNumber(value, out var number))
                {
                    Cases.Find(candidate => candidate.Selector == "other")?.Write(builder, context);
                    return;
                }
                var shifted = number - Offset;
                var digits = (decimal.GetBits(shifted)[3] >> 16) & 0xFF;
                var exact = Cases.Find(candidate => candidate.Selector.Length > 1 && candidate.Selector[0] == '=' &&
                                                    decimal.TryParse(candidate.Selector.Substring(1), NumberStyles.Number, CultureInfo.InvariantCulture, out var wanted) &&
                                                    wanted == number);
                var category = exact == null ? PluralRules.Select(context.Language, shifted, digits) : null;
                var chosen = exact ?? Cases.Find(candidate => candidate.Selector == category) ?? Cases.Find(candidate => candidate.Selector == "other");
                if (chosen == null)
                    return;
                var outerPlural = context.InPlural;
                var outerNumber = context.PluralNumber;
                context.InPlural = true;
                context.PluralNumber = shifted;
                chosen.Write(builder, context);
                context.InPlural = outerPlural;
                context.PluralNumber = outerNumber;
            }

            private static bool TryNumber(object value, out decimal number)
            {
                number = 0m;
                try
                {
                    if (value is decimal exact)
                    {
                        number = exact;
                        return true;
                    }
                    if (value is double || value is float)
                    {
                        var real = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                        if (double.IsNaN(real) || double.IsInfinity(real) || Math.Abs(real) > 7.9e27)
                            return false;
                        number = Convert.ToDecimal(real);
                        return true;
                    }
                    if (value is IConvertible && !(value is string) && !(value is bool) && !(value is char) && !(value is DateTime))
                    {
                        number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                        return true;
                    }
                }
                catch (OverflowException)
                {
                }
                return false;
            }
        }

        private sealed class SelectNode : Node
        {
            public SelectNode(string name, List<CaseNode> cases)
            {
                Name = name;
                Cases = cases;
            }

            public string Name { get; }
            public List<CaseNode> Cases { get; }

            public override void Write(StringBuilder builder, Context context)
            {
                if (!context.TryGet(Name, out var value))
                {
                    builder.Append('{').Append(Name).Append('}');
                    return;
                }
                var key = value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value?.ToString() ?? string.Empty;
                var chosen = Cases.Find(candidate => candidate.Selector == key) ?? Cases.Find(candidate => candidate.Selector == "other");
                chosen?.Write(builder, context);
            }
        }

        private sealed class Parser
        {
            private static readonly string[] PluralCategories = { "zero", "one", "two", "few", "many", "other" };
            private readonly string text;
            private int position;

            public Parser(string text)
            {
                this.text = text;
            }

            public List<MessageArgument> Arguments { get; } = new List<MessageArgument>();

            /// <summary>Reads up to the end of the text, or in a nested part up to the closing brace, which stays unread.</summary>
            public List<Node> ParseSequence(bool nested, bool inPlural)
            {
                var result = new List<Node>();
                var literal = new StringBuilder();

                void Flush()
                {
                    if (literal.Length > 0)
                    {
                        result.Add(new LiteralNode(literal.ToString()));
                        literal.Clear();
                    }
                }

                while (position < text.Length)
                {
                    var c = text[position];
                    if (c == '\'')
                    {
                        ReadApostrophe(literal, inPlural);
                    }
                    else if (c == '{')
                    {
                        Flush();
                        result.Add(ReadArgument(inPlural));
                    }
                    else if (c == '}')
                    {
                        if (!nested)
                            throw Error("an unmatched '}'");
                        break;
                    }
                    else if (c == '#' && inPlural)
                    {
                        Flush();
                        result.Add(new PoundNode());
                        position++;
                    }
                    else
                    {
                        literal.Append(c);
                        position++;
                    }
                }
                Flush();
                return result;
            }

            private void ReadApostrophe(StringBuilder literal, bool inPlural)
            {
                var next = position + 1 < text.Length ? text[position + 1] : '\0';
                if (next == '\'')
                {
                    literal.Append('\'');
                    position += 2;
                    return;
                }
                if (next == '{' || next == '}' || inPlural && next == '#')
                {
                    // Quoted text up to the next single apostrophe; two in a row inside it are one apostrophe.
                    position++;
                    while (position < text.Length)
                    {
                        var c = text[position];
                        if (c == '\'')
                        {
                            if (position + 1 < text.Length && text[position + 1] == '\'')
                            {
                                literal.Append('\'');
                                position += 2;
                                continue;
                            }
                            position++;
                            return;
                        }
                        literal.Append(c);
                        position++;
                    }
                    return;
                }
                literal.Append('\'');
                position++;
            }

            private Node ReadArgument(bool inPlural)
            {
                var start = position;
                position++;
                var name = ReadUntil(",}").Trim();
                if (name.Length == 0 || !IsName(name))
                    throw Error("the argument name \"" + name + "\" is not valid (letters, digits and _ only)", start);
                if (Peek() == '}')
                {
                    position++;
                    Remember(name, MessageArgumentKind.Plain);
                    return new ArgumentNode(name, MessageArgumentKind.Plain, null);
                }
                if (Peek() != ',')
                    throw Error("the argument {" + name + " is not closed", start);
                position++;
                var type = ReadUntil(",}").Trim().ToLowerInvariant();
                switch (type)
                {
                    case "number":
                    case "date":
                    case "time":
                    {
                        string style = null;
                        if (Peek() == ',')
                        {
                            position++;
                            style = ReadUntil("}").Trim();
                        }
                        Expect('}', "the argument {" + name + ", " + type + " is not closed");
                        var kind = type == "number" ? MessageArgumentKind.Number : type == "date" ? MessageArgumentKind.Date : MessageArgumentKind.Time;
                        Remember(name, kind);
                        return new ArgumentNode(name, kind, style);
                    }
                    case "plural":
                    case "select":
                    {
                        Expect(',', "the " + type + " of {" + name + " needs its cases after a comma");
                        var plural = type == "plural";
                        var offset = 0m;
                        SkipSpace();
                        if (plural && string.CompareOrdinal(text, position, "offset:", 0, 7) == 0)
                        {
                            position += 7;
                            var number = ReadUntil(" \t\r\n{").Trim();
                            if (!decimal.TryParse(number, NumberStyles.Number, CultureInfo.InvariantCulture, out offset))
                                throw Error("the offset \"" + number + "\" of {" + name + " is not a number");
                        }
                        var argument = Remember(name, plural ? MessageArgumentKind.Plural : MessageArgumentKind.Select);
                        var cases = ReadCases(name, plural, argument);
                        return plural ? (Node)new PluralNode(name, offset, cases) : new SelectNode(name, cases);
                    }
                    default:
                        throw Error("the type \"" + type + "\" of {" + name + " is not supported (number, date, time, plural, select)", start);
                }
            }

            private List<CaseNode> ReadCases(string name, bool plural, MessageArgument argument)
            {
                var cases = new List<CaseNode>();
                while (true)
                {
                    SkipSpace();
                    if (Peek() == '}')
                    {
                        position++;
                        break;
                    }
                    if (position >= text.Length)
                        throw Error("the cases of {" + name + " are not closed");
                    var selector = ReadUntil(" \t\r\n{}");
                    if (selector.Length == 0)
                        throw Error("a case of {" + name + " has no name");
                    if (plural && !IsPluralSelector(selector))
                        throw Error("\"" + selector + "\" is not a plural case of {" + name + " (zero, one, two, few, many, other or =N)");
                    if (cases.Exists(candidate => candidate.Selector == selector))
                        throw Error("the case \"" + selector + "\" of {" + name + " stands twice");
                    SkipSpace();
                    Expect('{', "the case \"" + selector + "\" of {" + name + " needs its text in braces");
                    var body = ParseSequence(true, plural);
                    Expect('}', "the case \"" + selector + "\" of {" + name + " is not closed");
                    cases.Add(new CaseNode(selector, body));
                    argument.Cases.Add(selector);
                }
                if (!cases.Exists(candidate => candidate.Selector == "other"))
                    throw Error("{" + name + " has no \"other\" case");
                return cases;
            }

            private static bool IsPluralSelector(string selector)
            {
                if (Array.IndexOf(PluralCategories, selector) >= 0)
                    return true;
                return selector.Length > 1 && selector[0] == '=' && decimal.TryParse(selector.Substring(1), NumberStyles.Number, CultureInfo.InvariantCulture, out _);
            }

            private MessageArgument Remember(string name, MessageArgumentKind kind)
            {
                var known = Arguments.Find(candidate => candidate.Name == name && candidate.Kind == kind);
                if (known != null)
                    return known;
                var created = new MessageArgument(name, kind);
                Arguments.Add(created);
                return created;
            }

            private static bool IsName(string name)
            {
                foreach (var c in name)
                    if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_'))
                        return false;
                return true;
            }

            private char Peek()
            {
                return position < text.Length ? text[position] : '\0';
            }

            private void SkipSpace()
            {
                while (position < text.Length && char.IsWhiteSpace(text[position]))
                    position++;
            }

            private void Expect(char expected, string message)
            {
                if (Peek() != expected)
                    throw Error(message);
                position++;
            }

            private string ReadUntil(string stops)
            {
                var start = position;
                while (position < text.Length && stops.IndexOf(text[position]) < 0)
                    position++;
                return text.Substring(start, position - start);
            }

            private FormatException Error(string message)
            {
                return Error(message, position);
            }

            private FormatException Error(string message, int at)
            {
                return new FormatException(message + " (at character " + (at + 1) + ")");
            }
        }
    }
}
