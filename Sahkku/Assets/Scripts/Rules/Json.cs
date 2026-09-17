using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Sahkku.Rules
{
    /// <summary>Thrown when a ruleset cannot be parsed, is incomplete, or is inconsistent.</summary>
    public sealed class RuleSetException : Exception
    {
        public RuleSetException(string message) : base(message) { }
    }

    public enum JsonValueKind
    {
        Null,
        Bool,
        Number,
        String,
        Array,
        Object
    }

    /// <summary>A parsed JSON value. Deliberately tiny so the ruleset has no third-party or Unity dependency.</summary>
    public sealed class JsonValue
    {
        public JsonValueKind Kind { get; private set; }

        string _stringValue;
        double _numberValue;
        bool _boolValue;
        Dictionary<string, JsonValue> _objectValue;
        List<JsonValue> _arrayValue;

        JsonValue() { }

        public bool IsNull { get { return Kind == JsonValueKind.Null; } }

        public bool Has(string key)
        {
            return _objectValue != null && _objectValue.ContainsKey(key);
        }

        public JsonValue Get(string key)
        {
            if (_objectValue == null) throw new RuleSetException("Expected a JSON object while reading '" + key + "'.");
            JsonValue value;
            return _objectValue.TryGetValue(key, out value) ? value : null;
        }

        public JsonValue Require(string key)
        {
            JsonValue value = Get(key);
            if (value == null) throw new RuleSetException("Missing required ruleset key '" + key + "'.");
            return value;
        }

        public string AsString()
        {
            if (Kind != JsonValueKind.String) throw new RuleSetException("Expected a string but found " + Kind + ".");
            return _stringValue;
        }

        public string AsStringOrNull()
        {
            return Kind == JsonValueKind.String ? _stringValue : null;
        }

        public bool AsBool()
        {
            if (Kind != JsonValueKind.Bool) throw new RuleSetException("Expected a boolean but found " + Kind + ".");
            return _boolValue;
        }

        public int AsInt()
        {
            return (int)AsDouble();
        }

        public double AsDouble()
        {
            if (Kind != JsonValueKind.Number) throw new RuleSetException("Expected a number but found " + Kind + ".");
            return _numberValue;
        }

        public IReadOnlyList<JsonValue> Items()
        {
            return _arrayValue ?? (IReadOnlyList<JsonValue>)Array.Empty<JsonValue>();
        }

        public static JsonValue NewNull() { return new JsonValue { Kind = JsonValueKind.Null }; }
        public static JsonValue NewString(string value) { return new JsonValue { Kind = JsonValueKind.String, _stringValue = value }; }
        public static JsonValue NewNumber(double value) { return new JsonValue { Kind = JsonValueKind.Number, _numberValue = value }; }
        public static JsonValue NewBool(bool value) { return new JsonValue { Kind = JsonValueKind.Bool, _boolValue = value }; }
        internal static JsonValue NewArray(List<JsonValue> items) { return new JsonValue { Kind = JsonValueKind.Array, _arrayValue = items }; }
        internal static JsonValue NewObject(Dictionary<string, JsonValue> fields) { return new JsonValue { Kind = JsonValueKind.Object, _objectValue = fields }; }
    }

    /// <summary>Minimal recursive-descent JSON reader (no reflection, IL2CPP/WebGL safe).</summary>
    public static class JsonParser
    {
        public static JsonValue Parse(string text)
        {
            var parser = new Parser(text);
            JsonValue value = parser.ReadValue();
            parser.SkipWhitespace();
            if (!parser.AtEnd) throw new RuleSetException("Unexpected trailing content at position " + parser.Position + ".");
            return value;
        }

        sealed class Parser
        {
            readonly string _text;
            int _index;

            public Parser(string text)
            {
                if (text == null) throw new RuleSetException("Ruleset JSON is null.");
                _text = text;
            }

            public bool AtEnd { get { return _index >= _text.Length; } }
            public int Position { get { return _index; } }

            public void SkipWhitespace()
            {
                while (_index < _text.Length && char.IsWhiteSpace(_text[_index])) _index++;
            }

            public JsonValue ReadValue()
            {
                SkipWhitespace();
                if (AtEnd) throw new RuleSetException("Unexpected end of JSON.");
                char c = _text[_index];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return JsonValue.NewString(ReadString());
                    case 't': Expect("true"); return JsonValue.NewBool(true);
                    case 'f': Expect("false"); return JsonValue.NewBool(false);
                    case 'n': Expect("null"); return JsonValue.NewNull();
                    default: return JsonValue.NewNumber(ReadNumber());
                }
            }

            JsonValue ReadObject()
            {
                _index++;
                var fields = new Dictionary<string, JsonValue>();
                SkipWhitespace();
                if (!AtEnd && _text[_index] == '}') { _index++; return JsonValue.NewObject(fields); }

                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _text[_index] != '"') throw new RuleSetException("Expected an object key at position " + _index + ".");
                    string key = ReadString();
                    SkipWhitespace();
                    if (AtEnd || _text[_index] != ':') throw new RuleSetException("Expected ':' at position " + _index + ".");
                    _index++;
                    fields[key] = ReadValue();
                    SkipWhitespace();
                    if (AtEnd) throw new RuleSetException("Unterminated JSON object.");
                    if (_text[_index] == ',') { _index++; continue; }
                    if (_text[_index] == '}') { _index++; return JsonValue.NewObject(fields); }
                    throw new RuleSetException("Expected ',' or '}' at position " + _index + ".");
                }
            }

            JsonValue ReadArray()
            {
                _index++;
                var items = new List<JsonValue>();
                SkipWhitespace();
                if (!AtEnd && _text[_index] == ']') { _index++; return JsonValue.NewArray(items); }

                while (true)
                {
                    items.Add(ReadValue());
                    SkipWhitespace();
                    if (AtEnd) throw new RuleSetException("Unterminated JSON array.");
                    if (_text[_index] == ',') { _index++; continue; }
                    if (_text[_index] == ']') { _index++; return JsonValue.NewArray(items); }
                    throw new RuleSetException("Expected ',' or ']' at position " + _index + ".");
                }
            }

            string ReadString()
            {
                _index++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw new RuleSetException("Unterminated JSON string.");
                    char c = _text[_index++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }

                    if (AtEnd) throw new RuleSetException("Unterminated JSON escape.");
                    char escape = _text[_index++];
                    switch (escape)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_index + 4 > _text.Length) throw new RuleSetException("Invalid unicode escape in JSON string.");
                            sb.Append((char)ushort.Parse(_text.Substring(_index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _index += 4;
                            break;
                        default: throw new RuleSetException("Invalid JSON escape '\\" + escape + "'.");
                    }
                }
            }

            double ReadNumber()
            {
                int start = _index;
                while (!AtEnd && (char.IsDigit(_text[_index]) || _text[_index] == '-' || _text[_index] == '+'
                    || _text[_index] == '.' || _text[_index] == 'e' || _text[_index] == 'E'))
                {
                    _index++;
                }

                string token = _text.Substring(start, _index - start);
                double value;
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    throw new RuleSetException("Invalid JSON number '" + token + "'.");
                return value;
            }

            void Expect(string literal)
            {
                if (_index + literal.Length > _text.Length || string.CompareOrdinal(_text, _index, literal, 0, literal.Length) != 0)
                    throw new RuleSetException("Invalid JSON token at position " + _index + ".");
                _index += literal.Length;
            }
        }
    }

    /// <summary>Maps the ruleset JSON onto <see cref="RuleSet"/> (explicit, reflection-free).</summary>
    public static class RuleSetJson
    {
        public static RuleSet FromJson(string json)
        {
            JsonValue root = JsonParser.Parse(json);
            var ruleset = new RuleSet
            {
                id = root.Get("id") == null ? null : root.Get("id").AsStringOrNull(),
                version = root.Get("version") == null ? 0 : root.Get("version").AsInt(),
                board = ParseBoard(root.Require("board")),
                track = ParseTrack(root.Require("track")),
                pieces = ParsePieces(root.Require("pieces")),
                dice = ParseDice(root.Require("dice")),
                activation = ParseActivation(root.Require("activation")),
                inactive = ParseInactive(root.Require("inactive")),
                start = ParseStart(root.Require("start")),
                setup = ParseSetup(root.Require("setup")),
                variants = ParseVariants(root.Require("variants")),
                win = ParseWin(root.Require("win"))
            };
            ruleset.Validate();
            return ruleset;
        }

        static BoardRules ParseBoard(JsonValue value)
        {
            return new BoardRules
            {
                width = value.Require("width").AsInt(),
                height = value.Require("height").AsInt(),
                layout = value.Require("layout").AsString()
            };
        }

        static TrackRules ParseTrack(JsonValue value)
        {
            JsonValue legs = value.Require("legs");
            var list = new List<TrackLegRules>();
            foreach (JsonValue leg in legs.Items())
            {
                list.Add(new TrackLegRules
                {
                    row = leg.Require("row").AsInt(),
                    direction = leg.Require("direction").AsString()
                });
            }
            return new TrackRules { legs = list.ToArray() };
        }

        static PieceSetRules ParsePieces(JsonValue value)
        {
            return new PieceSetRules
            {
                soldier = ParsePiece(value.Require("soldier")),
                king = ParsePiece(value.Require("king")),
                queen = ParsePiece(value.Require("queen"))
            };
        }

        static PieceRules ParsePiece(JsonValue value)
        {
            return new PieceRules
            {
                moves = ParseStringArray(value.Require("moves")),
                movesScaleWithDie = value.Require("movesScaleWithDie").AsBool(),
                startActivatable = value.Require("startActivatable").AsBool(),
                queuesNextOnActivation = value.Require("queuesNextOnActivation").AsBool(),
                blocksOwnLanding = value.Require("blocksOwnLanding").AsBool(),
                cannotLandOnOwnUnits = value.Require("cannotLandOnOwnUnits").AsBool(),
                capturable = value.Require("capturable").AsBool(),
                recruitedWhenLanded = value.Require("recruitedWhenLanded").AsBool(),
                landingEndsGame = value.Require("landingEndsGame").AsBool(),
                recruitsKingOnEnemyHomeRow = value.Require("recruitsKingOnEnemyHomeRow").AsBool()
            };
        }

        static DiceRules ParseDice(JsonValue value)
        {
            JsonValue faces = value.Require("faces");
            var list = new List<DieFaceRules>();
            foreach (JsonValue face in faces.Items())
            {
                list.Add(new DieFaceRules
                {
                    id = face.Require("id").AsString(),
                    steps = face.Require("steps").AsInt()
                });
            }
            return new DiceRules
            {
                count = value.Require("count").AsInt(),
                activateFace = value.Require("activateFace").AsString(),
                useOrder = ParseStringArray(value.Require("useOrder")),
                reroll = ParseReroll(value.Require("reroll")),
                faces = list.ToArray()
            };
        }

        static RerollRules ParseReroll(JsonValue value)
        {
            return new RerollRules
            {
                faces = ParseStringArray(value.Require("faces")),
                beforeUsingAnyDie = value.Require("beforeUsingAnyDie").AsBool()
            };
        }

        static ActivationRules ParseActivation(JsonValue value)
        {
            JsonValue rule = value.Require("onMoveInactivePiece");
            return new ActivationRules
            {
                onMoveInactivePiece = new SoldierActivationRule
                {
                    unlockOffset = rule.Require("unlockOffset").AsInt()
                }
            };
        }

        static InactiveRules ParseInactive(JsonValue value)
        {
            return new InactiveRules { enterable = value.Require("enterable").AsBool() };
        }

        static StartRules ParseStart(JsonValue value)
        {
            return new StartRules { mode = value.Require("mode").AsString() };
        }

        static SetupRules ParseSetup(JsonValue value)
        {
            JsonValue soldiers = value.Require("soldiers");
            JsonValue queens = value.Require("queens");
            JsonValue king = value.Require("king");
            return new SetupRules
            {
                soldiers = new PerOwnerRow
                {
                    P1 = ParseRow(soldiers.Require("P1")),
                    P2 = ParseRow(soldiers.Require("P2"))
                },
                queens = new PerOwnerPlacement
                {
                    P1 = ParsePlacement(queens.Require("P1")),
                    P2 = ParsePlacement(queens.Require("P2"))
                },
                king = new KingsPlacement
                {
                    row = king.Require("row").AsInt(),
                    x = king.Require("x").AsInt(),
                    owner = king.Require("owner").AsString()
                }
            };
        }

        static RowPlacement ParseRow(JsonValue value)
        {
            return new RowPlacement
            {
                row = value.Require("row").AsInt(),
                placement = value.Require("placement").AsString()
            };
        }

        static Placement ParsePlacement(JsonValue value)
        {
            return new Placement
            {
                row = value.Require("row").AsInt(),
                x = value.Require("x").AsInt()
            };
        }

        static VariantSet ParseVariants(JsonValue value)
        {
            return new VariantSet
            {
                standard = ParseVariant(value.Require("standard")),
                evenOdds = ParseVariant(value.Require("evenOdds"))
            };
        }

        static VariantRules ParseVariant(JsonValue value)
        {
            return new VariantRules
            {
                soldiersActive = value.Require("soldiersActive").AsInt()
            };
        }

        static WinRules ParseWin(JsonValue value)
        {
            return new WinRules
            {
                opponentSoldiersExhausted = value.Require("opponentSoldiersExhausted").AsBool()
            };
        }

        static string[] ParseStringArray(JsonValue value)
        {
            var list = new List<string>();
            foreach (JsonValue item in value.Items()) list.Add(item.AsString());
            return list.ToArray();
        }
    }
}
