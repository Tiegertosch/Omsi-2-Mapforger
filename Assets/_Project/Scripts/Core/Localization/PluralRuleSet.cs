using System;
using System.Collections.Generic;
using System.Globalization;

namespace OmsiMapEditor.Core.Localization
{
    /// <summary>
    /// The plural rules of one language, written as data in the notation of the Unicode CLDR (<c>i = 1 and v = 0</c>): for each category
    /// zero, one, two, few and many a condition over the operands of a number; a number that meets none is "other". The rules come from
    /// <c>languages.json</c>, so a language is added without a change in the code.
    /// <para>
    /// Operands: <c>n</c> the number without its sign, <c>i</c> its integer part, <c>v</c> the number of decimals shown, <c>w</c> the same
    /// without trailing zeros, <c>f</c> the decimals shown as an integer, <c>t</c> the same without trailing zeros, <c>e</c> and <c>c</c> the
    /// exponent (always 0 here). Operators: <c>=</c>, <c>!=</c>, <c>%</c>, <c>and</c>, <c>or</c>, ranges <c>2..4</c> and lists <c>0,1</c>.
    /// A range holds whole numbers only, as in the CLDR. Text after <c>@</c> (the examples of the CLDR) is ignored.
    /// </para>
    /// </summary>
    public sealed class PluralRuleSet
    {
        /// <summary>The categories a rule set can name besides "other", in the order they are tried.</summary>
        public static readonly string[] CategoryNames = { "zero", "one", "two", "few", "many" };

        private sealed class Relation
        {
            public char Operand;
            public decimal? Modulus;
            public bool Negated;
            public List<KeyValuePair<decimal, decimal>> Ranges = new List<KeyValuePair<decimal, decimal>>();
        }

        // A condition is an or of ands of relations.
        private readonly List<KeyValuePair<string, List<List<Relation>>>> rules = new List<KeyValuePair<string, List<List<Relation>>>>();

        private PluralRuleSet()
        {
        }

        /// <summary>The categories this set names, in the order they are tried, followed by "other".</summary>
        public IReadOnlyList<string> Categories
        {
            get
            {
                var result = new List<string>();
                foreach (var rule in rules)
                    result.Add(rule.Key);
                result.Add("other");
                return result;
            }
        }

        /// <summary>
        /// Reads rules given as category to condition. False with the reason in <paramref name="error"/> when a category is unknown or a
        /// condition cannot be read.
        /// </summary>
        public static bool TryParse(IEnumerable<KeyValuePair<string, string>> source, out PluralRuleSet set, out string error)
        {
            set = null;
            error = null;
            var given = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in source)
                given[pair.Key] = pair.Value ?? string.Empty;
            foreach (var name in given.Keys)
                if (Array.IndexOf(CategoryNames, name) < 0)
                {
                    error = "\"" + name + "\" is not a plural category (zero, one, two, few, many; \"other\" needs no rule)";
                    return false;
                }
            var result = new PluralRuleSet();
            foreach (var name in CategoryNames)
            {
                if (!given.TryGetValue(name, out var text))
                    continue;
                if (!TryParseCondition(text, out var condition, out var reason))
                {
                    error = "the rule for " + name + " cannot be read: " + reason;
                    return false;
                }
                result.rules.Add(new KeyValuePair<string, List<List<Relation>>>(name, condition));
            }
            set = result;
            return true;
        }

        /// <summary>The category of a number; <paramref name="visibleFractionDigits"/> is how many decimals it is shown with.</summary>
        public string Select(decimal value, int visibleFractionDigits)
        {
            var n = Math.Abs(value);
            var integer = Math.Truncate(n);
            var digits = Math.Max(0, visibleFractionDigits);
            var scale = Pow10(digits);
            var f = Math.Round((n - integer) * scale);
            var t = f;
            var w = digits;
            while (t != 0 && t % 10 == 0)
            {
                t /= 10;
                w--;
            }
            if (t == 0)
                w = 0;
            foreach (var rule in rules)
                if (Matches(rule.Value, n, integer, digits, f, t, w))
                    return rule.Key;
            return "other";
        }

        private static decimal Pow10(int digits)
        {
            var result = 1m;
            for (var index = 0; index < digits && index < 20; index++)
                result *= 10m;
            return result;
        }

        private static bool Matches(List<List<Relation>> condition, decimal n, decimal i, int v, decimal f, decimal t, int w)
        {
            foreach (var and in condition)
            {
                var all = true;
                foreach (var relation in and)
                {
                    decimal operand;
                    switch (relation.Operand)
                    {
                        case 'n': operand = n; break;
                        case 'i': operand = i; break;
                        case 'v': operand = v; break;
                        case 'w': operand = w; break;
                        case 'f': operand = f; break;
                        case 't': operand = t; break;
                        default: operand = 0m; break;
                    }
                    if (relation.Modulus.HasValue)
                        operand = operand - Math.Floor(operand / relation.Modulus.Value) * relation.Modulus.Value;
                    var inside = false;
                    foreach (var range in relation.Ranges)
                    {
                        var single = range.Key == range.Value;
                        if (single ? operand == range.Key : operand == Math.Truncate(operand) && operand >= range.Key && operand <= range.Value)
                        {
                            inside = true;
                            break;
                        }
                    }
                    if (inside == relation.Negated)
                    {
                        all = false;
                        break;
                    }
                }
                if (all)
                    return true;
            }
            return false;
        }

        private static bool TryParseCondition(string text, out List<List<Relation>> condition, out string error)
        {
            condition = new List<List<Relation>>();
            error = null;
            var at = text.IndexOf('@');
            if (at >= 0)
                text = text.Substring(0, at);
            text = text.Trim();
            if (text.Length == 0)
            {
                error = "the rule is empty";
                return false;
            }
            foreach (var orPart in SplitWord(text, "or"))
            {
                var and = new List<Relation>();
                foreach (var part in SplitWord(orPart, "and"))
                {
                    if (!TryParseRelation(part.Trim(), out var relation, out error))
                        return false;
                    and.Add(relation);
                }
                condition.Add(and);
            }
            return true;
        }

        private static List<string> SplitWord(string text, string word)
        {
            var parts = new List<string>();
            var padded = " " + word + " ";
            var start = 0;
            while (true)
            {
                var at = text.IndexOf(padded, start, StringComparison.Ordinal);
                if (at < 0)
                {
                    parts.Add(text.Substring(start));
                    return parts;
                }
                parts.Add(text.Substring(start, at - start));
                start = at + padded.Length;
            }
        }

        private static bool TryParseRelation(string text, out Relation relation, out string error)
        {
            relation = new Relation();
            error = null;
            var negated = false;
            var cut = text.IndexOf("!=", StringComparison.Ordinal);
            var operatorLength = 2;
            if (cut >= 0)
                negated = true;
            else
            {
                cut = text.IndexOf('=');
                operatorLength = 1;
            }
            if (cut < 0)
            {
                error = "\"" + text + "\" has no = or !=";
                return false;
            }
            relation.Negated = negated;
            var left = text.Substring(0, cut).Trim();
            var right = text.Substring(cut + operatorLength).Trim();
            var percent = left.IndexOf('%');
            var operandText = percent >= 0 ? left.Substring(0, percent).Trim() : left;
            if (operandText.Length != 1 || "nivwftec".IndexOf(operandText[0]) < 0)
            {
                error = "\"" + operandText + "\" is not an operand (n, i, v, w, f, t, e)";
                return false;
            }
            relation.Operand = operandText[0] == 'c' ? 'e' : operandText[0];
            if (percent >= 0)
            {
                if (!decimal.TryParse(left.Substring(percent + 1).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var modulus) || modulus <= 0)
                {
                    error = "the number after % in \"" + text + "\" is not a positive whole number";
                    return false;
                }
                relation.Modulus = modulus;
            }
            foreach (var item in right.Split(','))
            {
                var range = item.Trim();
                var dots = range.IndexOf("..", StringComparison.Ordinal);
                decimal low;
                decimal high;
                bool ok;
                if (dots < 0)
                {
                    ok = decimal.TryParse(range, NumberStyles.None, CultureInfo.InvariantCulture, out low);
                    high = low;
                }
                else
                {
                    ok = decimal.TryParse(range.Substring(0, dots), NumberStyles.None, CultureInfo.InvariantCulture, out low);
                    ok &= decimal.TryParse(range.Substring(dots + 2), NumberStyles.None, CultureInfo.InvariantCulture, out high);
                }
                if (!ok)
                {
                    error = "\"" + range + "\" is not a number or a range such as 2..4";
                    return false;
                }
                if (high < low)
                {
                    error = "the range \"" + range + "\" ends before it starts";
                    return false;
                }
                relation.Ranges.Add(new KeyValuePair<decimal, decimal>(low, high));
            }
            return true;
        }
    }
}
